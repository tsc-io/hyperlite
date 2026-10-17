// The value types this layer passes around: Key, Cell, Cells and the string arena
// that owns their storage.
//
// They hold const char* pointers into an arena that outlives every reader, which is
// what lets the scanner and mutator code pass cells around without copying. The
// arena is a block arena for that reason and its lifetime is the contract.

#pragma once

#include "Support.h"

#include <cstdint>
#include <deque>
#include <string_view>
#include <vector>

namespace hyperlite
{
/// Bump allocator handing out stable, nul terminated copies of strings. Stands in
/// for Hypertable::CharArena / PageArena. Pointers stay valid until the arena is
/// destroyed or cleared, never on a subsequent allocation, which is exactly what
/// the ported code relies on.
class char_arena final
{
public:
    explicit char_arena(std::size_t page_size = 4096) : page_size_(page_size) {}

    char_arena(const char_arena&) = delete;
    char_arena& operator=(const char_arena&) = delete;

    /// Copies s plus a terminating nul. A null pointer stays null, which keeps the
    /// "not specified" and "empty" cases distinguishable, as hypertable needs.
    const char* dup(const char* s) { return s ? dup(std::string_view(s)) : nullptr; }

    const char* dup(std::string_view s)
    {
        char* at = allocate(s.size() + 1);
        if (!s.empty())
        {
            std::memcpy(at, s.data(), s.size());
        }
        at[s.size()] = '\0';
        return at;
    }

    /// Raw allocation, the returned block is uninitialised.
    char* allocate(std::size_t bytes)
    {
        if (bytes > page_size_)
        {
            // oversized entries get a page of their own rather than forcing the
            // page size up for everyone
            pages_.emplace_back(bytes);
            auto& page = pages_.back();
            page.used = bytes;
            return page.data.get();
        }
        if (pages_.empty() || pages_.back().remaining() < bytes)
        {
            pages_.emplace_back(page_size_);
        }
        auto& page = pages_.back();
        char* at = page.data.get() + page.used;
        page.used += bytes;
        return at;
    }

    void clear() { pages_.clear(); }

private:
    struct page
    {
        explicit page(std::size_t n) : data(std::make_unique<char[]>(n)), size(n) {}
        std::size_t remaining() const noexcept { return size - used; }

        std::unique_ptr<char[]> data;
        std::size_t size;
        std::size_t used = 0;
    };

    std::size_t page_size_;
    std::deque<page> pages_;
};

/// The subset of Hypertable::Key the sqlite provider uses. The serialised
/// on-the-wire form the original also modelled is irrelevant here, sqlite stores
/// the parts in their own columns.
struct key
{
    const char* row = nullptr;
    const char* column_qualifier = nullptr;
    std::uint32_t row_len = 0;
    std::uint32_t column_qualifier_len = 0;
    std::int64_t timestamp = AUTO_ASSIGN;
    std::int64_t revision = AUTO_ASSIGN;
    std::uint8_t column_family_code = 0;
    std::uint8_t flag = FLAG_INSERT;

    void clear() { *this = key(); }
};

/// Mirrors Hypertable::KeySpec. Borrowed pointers, the caller owns the storage
/// for the duration of the call.
struct key_spec
{
    const void* row = nullptr;
    const char* column_family = nullptr;
    const char* column_qualifier = nullptr;
    std::size_t row_len = 0;
    std::size_t column_qualifier_len = 0;
    std::int64_t timestamp = AUTO_ASSIGN;
    std::int64_t revision = AUTO_ASSIGN;
    std::uint8_t flag = FLAG_INSERT;
};

/// Mirrors Hypertable::Cell. Borrowed pointers throughout; a scanner hands out
/// cells pointing into its own storage, valid until the next step.
struct cell
{
    const char* row_key = nullptr;
    const char* column_family = nullptr;
    const char* column_qualifier = nullptr;
    const std::uint8_t* value = nullptr;
    std::uint32_t value_len = 0;
    std::int64_t timestamp = AUTO_ASSIGN;
    std::int64_t revision = AUTO_ASSIGN;
    std::uint8_t flag = FLAG_INSERT;

    // Lengths of the three strings above, so the abi layer does not have to measure them
    // again. Every producer already knows them - sqlite reports the column width and the
    // schema owns the family name - and the consumer needs them for the managed decode,
    // so measuring in between would be three strlen per scanned cell for nothing.
    //
    // A producer that does not set them leaves them at -1, meaning "nul terminated,
    // measure it".
    std::int32_t row_key_len = -1;
    std::int32_t column_family_len = -1;
    std::int32_t column_qualifier_len = -1;

    void clear() { *this = cell(); }
};

/// Max cell size, as ht4c::Common::Cell::MaxSize.
constexpr std::uint32_t k_max_cell_size = 2000000000;

using cells = std::vector<cell>;
} // namespace hyperlite
