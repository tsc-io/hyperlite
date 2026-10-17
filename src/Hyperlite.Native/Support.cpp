#include "pch.h"
#include "Support.h"

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <random>

#if defined(_WIN32)
#include <windows.h>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")
#else
#include <sys/random.h>
#endif

namespace hyperlite
{
std::string vformat(const char* fmt, std::va_list ap)
{
    if (!fmt)
    {
        return std::string();
    }

    // one shot into a stack buffer covers every format string in the ported code,
    // the heap path is there for pathological table names and schemas
    char stack[512];

    std::va_list probe;
    va_copy(probe, ap);
    const int need = std::vsnprintf(stack, sizeof(stack), fmt, probe);
    va_end(probe);

    if (need < 0)
    {
        return std::string();
    }
    if (static_cast<std::size_t>(need) < sizeof(stack))
    {
        return std::string(stack, static_cast<std::size_t>(need));
    }

    std::string out(static_cast<std::size_t>(need), '\0');
    std::vsnprintf(out.data(), static_cast<std::size_t>(need) + 1, fmt, ap);
    return out;
}

std::string format(const char* fmt, ...)
{
    std::va_list ap;
    va_start(ap, fmt);
    std::string out = vformat(fmt, ap);
    va_end(ap);
    return out;
}

std::string canonicalize_namespace(std::string_view path)
{
    std::string out;
    out.reserve(path.size());

    std::size_t at = 0;
    while (at < path.size())
    {
        const std::size_t next = path.find('/', at);
        const std::string_view segment =
            path.substr(at, next == std::string_view::npos ? std::string_view::npos : next - at);

        if (!segment.empty())
        {
            out.append(segment);
            out.push_back('/');
        }

        if (next == std::string_view::npos)
        {
            break;
        }
        at = next + 1;
    }

    // strip the trailing separator the loop leaves, plus any stray spaces
    std::size_t from = 0;
    std::size_t to = out.size();
    while (from < to && (out[from] == '/' || out[from] == ' '))
    {
        ++from;
    }
    while (to > from && (out[to - 1] == '/' || out[to - 1] == ' '))
    {
        --to;
    }

    return out.substr(from, to - from);
}

void validate_table_name(const char* name)
{
    if (name && *name)
    {
        const char* basename = std::strrchr(name, '/');
        basename = basename ? basename + 1 : name;
        if (*basename && *basename != '^')
        {
            return;
        }
    }

    HYPERLITE_THROW(error::SYNTAX_ERROR, "Invalid table name");
}

void dynamic_buffer::grow(std::size_t need)
{
    const std::size_t filled = fill();
    std::size_t want = size() ? size() * 2 : 64;
    while (want < need)
    {
        want *= 2;
    }

    auto fresh = std::make_unique<std::uint8_t[]>(want);
    if (filled)
    {
        std::memcpy(fresh.get(), base, filled);
    }

    own_ = std::move(fresh);
    base = own_.get();
    ptr = base + filled;
    end_ = base + want;
}

std::uint64_t get_ts64() noexcept
{
    using namespace std::chrono;
    return static_cast<std::uint64_t>(
        duration_cast<nanoseconds>(system_clock::now().time_since_epoch()).count());
}

namespace
{
// base85 alphabet, carried over verbatim from ht4c::Common::KeyBuilder so that
// generated row keys keep the exact same character set and ordering. Changing it
// would invalidate every key already stored.
//
// Printable ascii from '!' upwards, skipping ';' '&' '\' '"' ',' as the
// postscript / cookie safe variant does.
struct base85_tables
{
    constexpr base85_tables()
    {
        for (int i = 0; i < 256; ++i)
        {
            to_int[i] = 99;
        }
        int j = 0;
        for (int i = '!'; j < 85 && i < 127; ++i)
        {
            if (i == ';' || i == '&' || i == '\\' || i == '"' || i == ',')
            {
                continue;
            }
            to_char[j] = static_cast<char>(i);
            to_int[i] = static_cast<std::uint32_t>(j);
            ++j;
        }
    }

    char to_char[85]{};
    std::uint32_t to_int[256]{};
};

// constexpr, so there is no lazy initialisation to race on
constexpr base85_tables k_b85{};

constexpr std::uint32_t bswap32(std::uint32_t v) noexcept
{
    return ((v & 0x000000FFu) << 24) | ((v & 0x0000FF00u) << 8) | ((v & 0x00FF0000u) >> 8) |
           ((v & 0xFF000000u) >> 24);
}

/// Big endian load / store, standing in for the htonl / ntohl the ht4c code used.
constexpr std::uint32_t to_be(std::uint32_t v) noexcept
{
    if constexpr (std::endian::native == std::endian::big)
    {
        return v;
    }
    else
    {
        return bswap32(v);
    }
}

// 16 random bytes per key, drawn from a per thread pool so the system RNG is
// asked once per refill rather than once per key.
constexpr std::size_t k_pool_size = 64 * key_builder::size_guid;

thread_local std::uint8_t t_pool[k_pool_size];
thread_local std::size_t t_offset = k_pool_size;

bool system_random(std::uint8_t* out, std::size_t len) noexcept
{
#if defined(_WIN32)
    return BCRYPT_SUCCESS(
        ::BCryptGenRandom(nullptr, out, static_cast<ULONG>(len), BCRYPT_USE_SYSTEM_PREFERRED_RNG));
#else
    std::size_t done = 0;
    while (done < len)
    {
        const ssize_t got = ::getrandom(out + done, len - done, 0);
        if (got <= 0)
        {
            return false;
        }
        done += static_cast<std::size_t>(got);
    }
    return true;
#endif
}

bool next_random(std::uint8_t guid[key_builder::size_guid]) noexcept
{
    if (t_offset + key_builder::size_guid > k_pool_size)
    {
        if (!system_random(t_pool, k_pool_size))
        {
            return false;
        }
        t_offset = 0;
    }
    std::memcpy(guid, t_pool + t_offset, key_builder::size_guid);
    t_offset += key_builder::size_guid;
    return true;
}

/// Last resort source, only reached if the system RNG refuses. Still better than
/// handing out a key that might repeat.
void fallback_random(std::uint8_t guid[key_builder::size_guid]) noexcept
{
    static std::atomic<std::uint64_t> counter{0};
    std::random_device rd;
    std::uint64_t hi = (static_cast<std::uint64_t>(rd()) << 32) ^ rd();
    std::uint64_t lo =
        get_ts64() ^ (counter.fetch_add(1, std::memory_order_relaxed) * 0x9E3779B97F4A7C15ULL);
    std::memcpy(guid, &hi, 8);
    std::memcpy(guid + 8, &lo, 8);
}
} // namespace

namespace key_builder
{
void encode(const std::uint8_t guid[size_guid], char out[size_key + 1]) noexcept
{
    char* cp = out;
    for (std::size_t i = 0; i < size_guid / 4; ++i)
    {
        std::uint32_t raw;
        std::memcpy(&raw, guid + i * 4, 4);
        std::uint32_t v = to_be(raw);

        cp[4] = k_b85.to_char[v % 85];
        v /= 85;
        cp[3] = k_b85.to_char[v % 85];
        v /= 85;
        cp[2] = k_b85.to_char[v % 85];
        v /= 85;
        cp[1] = k_b85.to_char[v % 85];
        v /= 85;
        cp[0] = k_b85.to_char[v];
        cp += 5;
    }
    *cp = '\0';
}

void decode(const char in[size_key], std::uint8_t guid[size_guid]) noexcept
{
    const char* cp = in;
    for (std::size_t i = 0; i < size_guid / 4; ++i)
    {
        std::uint32_t v = 0;
        for (int j = 0; j < 5; ++j)
        {
            const std::uint32_t digit = k_b85.to_int[static_cast<std::uint8_t>(*cp++)];
            v = v * 85 + (digit == 99 ? 0 : digit);
        }
        const std::uint32_t be = to_be(v);
        std::memcpy(guid + i * 4, &be, 4);
    }
}

void generate(char out[size_key + 1]) noexcept
{
    std::uint8_t guid[size_guid];
    if (!next_random(guid))
    {
        fallback_random(guid);
    }

    // the shape a random uuid has: version 4, variant 10. ht4c did the same, so
    // decoded keys still look like a v4 guid to callers
    guid[7] = static_cast<std::uint8_t>((guid[7] & 0x0F) | 0x40);
    guid[8] = static_cast<std::uint8_t>((guid[8] & 0x3F) | 0x80);

    encode(guid, out);
}
} // namespace key_builder
} // namespace hyperlite
