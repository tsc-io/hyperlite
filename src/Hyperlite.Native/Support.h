// The few pieces this layer needs that the standard library does not provide:
// error codes, string and buffer helpers, time, and the base85 key encoding, as a
// self contained C++20 equivalent of the ht4w headers they replace (Common/Compat.h,
// Common/Error.h, Common/String.h, Common/DynamicBuffer.h, Common/Time.h). The error
// codes and the base85 encoding are value compatible with ht4w on purpose: the codes
// drive which managed exception Hyperlite raises, and the keys end up on disk.

#pragma once

#include <climits>
#include <cstdarg>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <exception>
#include <memory>
#include <string>
#include <string_view>
#include <utility>

namespace hyperlite
{
namespace error
{
// clang-format off
enum : int32_t
{
    EXTERNAL                         = -2,
    FAILED_EXPECTATION               = -1,
    OK                               = 0,
    REQUEST_TIMEOUT                  = 4,
    BAD_SCHEMA                       = 7,
    BAD_KEY                          = 9,
    TABLE_NOT_FOUND                  = 22,
    TOO_MANY_COLUMNS                 = 24,
    BAD_MEMORY_ALLOCATION            = 29,
    BAD_SCAN_SPEC                    = 30,
    NOT_IMPLEMENTED                  = 31,
    SCHEMA_PARSE_ERROR               = 34,
    SYNTAX_ERROR                     = 35,
    NAME_ALREADY_IN_USE              = 39,
    NAMESPACE_DOES_NOT_EXIST         = 40,
    NAMESPACE_EXISTS                 = 42,
    CLOSED                           = 50,
    INCOMPATIBLE_OPTIONS             = 61,
    BAD_VALUE                        = 62,

    HYPERSPACE_FILE_NOT_FOUND        = 0x00030003,
    HYPERSPACE_BAD_PATHNAME          = 0x00030006,
    HYPERSPACE_DIR_NOT_EMPTY         = 0x00030013,

    MASTER_TABLE_EXISTS              = 0x00040001,
    MASTER_BAD_SCHEMA                = 0x00040002,

    RANGESERVER_GENERATION_MISMATCH  = 0x00050001,
    RANGESERVER_SCHEMA_INVALID_CFID  = 0x00050009,
    RANGESERVER_INVALID_COLUMNFAMILY = 0x0005000A
};
// clang-format on
} // namespace error

/// The only exception type thrown inside Hyperlite.Native. It carries the same
/// payload ht4c::Common::HypertableException did, so Hyperlite can rebuild the exact
/// managed exception, including the inner chain.
class exception : public std::exception
{
public:
    exception(int32_t code, std::string msg, const char* func, const char* file, int32_t line)
        : code_(code), line_(line), msg_(std::move(msg)), func_(func ? func : ""),
          file_(file ? file : "")
    {
    }

    exception(int32_t code, std::string msg, const char* func, const char* file, int32_t line,
              const exception& inner)
        : code_(code), line_(line), msg_(std::move(msg)), func_(func ? func : ""),
          file_(file ? file : ""), inner_(std::make_shared<exception>(inner))
    {
    }

    const char* what() const noexcept override { return msg_.c_str(); }

    int32_t code() const noexcept { return code_; }
    int32_t line() const noexcept { return line_; }
    const std::string& message() const noexcept { return msg_; }
    const std::string& func() const noexcept { return func_; }
    const std::string& file() const noexcept { return file_; }
    const exception* inner() const noexcept { return inner_.get(); }

    void set_inner(const exception& inner) { inner_ = std::make_shared<exception>(inner); }

private:
    int32_t code_;
    int32_t line_;
    std::string msg_;
    std::string func_;
    std::string file_;
    std::shared_ptr<exception> inner_;
};

/// printf style formatting into a std::string. Stands in for Hypertable::format,
/// which the ported code calls in ~60 places, so the format strings stay as they
/// were rather than being rewritten to std::format.
[[nodiscard]] std::string format(const char* fmt, ...);
[[nodiscard]] std::string vformat(const char* fmt, std::va_list ap);

#if defined(_MSC_VER)
#define HYPERLITE_FUNC __FUNCTION__
#else
#define HYPERLITE_FUNC __PRETTY_FUNCTION__
#endif

#define HYPERLITE_THROW(code, msg)                                                                 \
    throw ::hyperlite::exception((code), (msg), HYPERLITE_FUNC, __FILE__, __LINE__)

#define HYPERLITE_THROWF(code, ...)                                                                \
    throw ::hyperlite::exception((code), ::hyperlite::format(__VA_ARGS__), HYPERLITE_FUNC,         \
                                 __FILE__, __LINE__)

/// Stand in for Hypertable::DynamicBuffer. Same member names (base, ptr, fill,
/// add, reserve, clear) so the ported call sites read unchanged, but it owns its
/// storage through a unique_ptr and grows geometrically.
class dynamic_buffer final
{
public:
    dynamic_buffer() = default;
    explicit dynamic_buffer(std::size_t initial) { reserve(initial); }

    dynamic_buffer(const dynamic_buffer&) = delete;
    dynamic_buffer& operator=(const dynamic_buffer&) = delete;

    dynamic_buffer(dynamic_buffer&& other) noexcept
        : base(other.base), ptr(other.ptr), own_(std::move(other.own_)), end_(other.end_)
    {
        other.base = other.ptr = other.end_ = nullptr;
    }

    dynamic_buffer& operator=(dynamic_buffer&& other) noexcept
    {
        if (this != &other)
        {
            own_ = std::move(other.own_);
            base = other.base;
            ptr = other.ptr;
            end_ = other.end_;
            other.base = other.ptr = other.end_ = nullptr;
        }
        return *this;
    }

    std::size_t fill() const noexcept { return static_cast<std::size_t>(ptr - base); }
    std::size_t size() const noexcept { return static_cast<std::size_t>(end_ - base); }
    std::size_t remaining() const noexcept { return static_cast<std::size_t>(end_ - ptr); }
    bool empty() const noexcept { return ptr == base; }

    void clear() noexcept { ptr = base; }

    void reserve(std::size_t need)
    {
        if (remaining() >= need)
        {
            return;
        }
        grow(fill() + need);
    }

    /// Appends len bytes. Grows as needed, so callers do not have to pre-reserve.
    void add(const void* data, std::size_t len)
    {
        reserve(len);
        if (len)
        {
            std::memcpy(ptr, data, len);
            ptr += len;
        }
    }

    /// Appends the string including its terminating nul, the shape the sys_db
    /// records use.
    void add_cstr(std::string_view s)
    {
        reserve(s.size() + 1);
        std::memcpy(ptr, s.data(), s.size());
        ptr += s.size();
        *ptr++ = '\0';
    }

    /// Replaces the contents with len bytes. Hypertable::DynamicBuffer::set.
    void set(const void* data, std::size_t len)
    {
        clear();
        add(data, len);
    }

    /// Reserves len bytes and hands back the write position, advancing fill.
    std::uint8_t* add_unchecked(std::size_t len)
    {
        reserve(len);
        std::uint8_t* at = ptr;
        ptr += len;
        return at;
    }

    std::uint8_t* base = nullptr;
    std::uint8_t* ptr = nullptr;

private:
    void grow(std::size_t need);

    std::unique_ptr<std::uint8_t[]> own_;
    std::uint8_t* end_ = nullptr;
};

/// Nanoseconds since 1970-01-01 00:00:00 UTC, the hypertable timestamp unit.
/// Equivalent to Hypertable::get_ts64.
std::uint64_t get_ts64() noexcept;

// Timestamp sentinels, values taken from ht4w Hypertable/Lib/KeySpec.h. They are
// declared int64_t there and the ported code compares them against uint64_t
// timestamps, so both spellings are kept and must stay in sync.
constexpr std::int64_t TIMESTAMP_MIN = INT64_MIN;
constexpr std::int64_t TIMESTAMP_MAX = INT64_MAX;
constexpr std::int64_t TIMESTAMP_NULL = INT64_MIN + 1;
constexpr std::int64_t AUTO_ASSIGN = INT64_MIN + 2;

constexpr std::uint64_t U_TIMESTAMP_MIN = static_cast<std::uint64_t>(TIMESTAMP_MIN);
constexpr std::uint64_t U_TIMESTAMP_MAX = static_cast<std::uint64_t>(TIMESTAMP_MAX);
constexpr std::uint64_t U_TIMESTAMP_NULL = static_cast<std::uint64_t>(TIMESTAMP_NULL);
constexpr std::uint64_t U_AUTO_ASSIGN = static_cast<std::uint64_t>(AUTO_ASSIGN);

// Hypertable::Key::END_ROW_MARKER. Two 0xff bytes, which sort above every valid
// UTF-8 sequence under sqlite's default BINARY collation, so an open ended row
// interval can be expressed as a plain r <= ? bound. Kept byte identical to ht4w
// because it reaches the query parameters.
inline constexpr char k_end_row_marker[3] = {'\xff', '\xff', '\0'};
constexpr const char* END_ROW_MARKER = k_end_row_marker;

/// Cell flags, values taken from ht4w Hypertable/Lib/KeySpec.h.
enum : std::uint8_t
{
    FLAG_DELETE_ROW = 0x00,
    FLAG_DELETE_COLUMN_FAMILY = 0x01,
    FLAG_DELETE_CELL = 0x02,
    FLAG_DELETE_CELL_VERSION = 0x03,
    FLAG_INSERT = 0xFF
};

namespace key_builder
{
enum : std::size_t
{
    size_guid = 16,
    size_key = 20
};

/// Generates a fresh random key. out receives size_key characters plus a nul.
void generate(char out[size_key + 1]) noexcept;

/// Encodes a 16 byte guid. Bit for bit the ht4c encoding, so keys round trip
/// through Hyperlite.Key on the managed side and match what is already on disk.
void encode(const std::uint8_t guid[size_guid], char out[size_key + 1]) noexcept;

/// Reverse of encode.
void decode(const char in[size_key], std::uint8_t guid[size_guid]) noexcept;
} // namespace key_builder

/// Canonicalizes a namespace path: drops empty segments and strips leading and
/// trailing '/' and ' '. Equivalent to Hypertable::Canonicalize::trim_namespace_path.
///
/// So "/test" and "test/" and "//test//" all become "test", and "/" becomes the
/// empty string, which is how the root namespace is spelled internally.
[[nodiscard]] std::string canonicalize_namespace(std::string_view path);

/// Rejects a table name whose last path segment is empty or starts with '^'.
/// Throws SYNTAX_ERROR, as ht4c::Common::Namespace::validateTableName did. The '^'
/// prefix is reserved for the index tables hypertable maintains itself.
void validate_table_name(const char* name);

/// Column family, or null when unspecified. Note that an empty string collapses
/// to null here, which is what the FLAG helpers below rely on.
inline const char* CF(const char* cf) noexcept
{
    return cf && *cf ? cf : nullptr;
}

/// Column qualifier, or empty, never null. Used where a bind needs a string.
inline const char* CQ(const char* cq) noexcept
{
    return cq ? cq : "";
}

/// Narrows a delete flag to the coarsest one the column family / qualifier
/// actually supports, so that del("row", nullptr, nullptr) deletes the row.
inline std::uint8_t FLAG(const char* cf, const char* cq, std::uint8_t flag) noexcept
{
    if (flag != FLAG_INSERT)
    {
        if (flag >= FLAG_DELETE_COLUMN_FAMILY)
        {
            if (!CF(cf))
            {
                return FLAG_DELETE_ROW;
            }
            if (flag >= FLAG_DELETE_CELL && !cq)
            {
                return FLAG_DELETE_COLUMN_FAMILY;
            }
        }
    }
    return flag;
}

/// The delete flag implied by the given column family / qualifier.
inline std::uint8_t FLAG_DELETE(const char* cf, const char* cq) noexcept
{
    return !CF(cf) ? FLAG_DELETE_ROW : cq ? FLAG_DELETE_CELL : FLAG_DELETE_COLUMN_FAMILY;
}

/// Zero means auto assign. A delete takes the cells at or older than its timestamp, which the
/// mutator compares inclusively; ht4c added one here for deletes and compared exclusively,
/// which overflowed at the greatest timestamp.
inline std::uint64_t TIMESTAMP(std::uint64_t timestamp) noexcept
{
    return timestamp == 0 ? U_AUTO_ASSIGN : timestamp;
}
} // namespace hyperlite
