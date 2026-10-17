// Replaces Hypertable::ScanSpec / ScanSpecBuilder and ht4c::Common::ScanSpec.
//
// The two layers of the original collapse into one here: ht4c::Common::ScanSpec
// was a thin setter facade over ScanSpecBuilder, and the builder existed only to
// own the strings. This type owns its strings in a char_arena and exposes the
// fields the scanner reads directly, which removes a copy per scan without
// changing any observable behaviour.

#pragma once

#include "Support.h"
#include "Types.h"

#include <cstdint>
#include <string>
#include <vector>

namespace hyperlite
{
/// Mirrors Hypertable::ColumnPredicate.
struct column_predicate
{
    enum : std::uint32_t
    {
        NO_OPERATION = 0x0000,
        EXACT_MATCH = 0x0001,
        PREFIX_MATCH = 0x0002,
        REGEX_MATCH = 0x0004,
        VALUE_MATCH = 0x0007,
        QUALIFIER_EXACT_MATCH = 0x0100,
        QUALIFIER_PREFIX_MATCH = 0x0200,
        QUALIFIER_REGEX_MATCH = 0x0400,
        QUALIFIER_MATCH = 0x0700
    };

    const char* column_family = nullptr;
    const char* column_qualifier = nullptr;
    const char* value = nullptr;
    std::uint32_t column_qualifier_len = 0;
    std::uint32_t value_len = 0;
    std::uint32_t operation = NO_OPERATION;
};

/// Mirrors Hypertable::RowInterval.
struct row_interval
{
    const char* start = nullptr;
    const char* end = nullptr;
    bool start_inclusive = true;
    bool end_inclusive = true;
};

/// Mirrors Hypertable::CellInterval.
struct cell_interval
{
    const char* start_row = nullptr;
    const char* start_column = nullptr;
    const char* end_row = nullptr;
    const char* end_column = nullptr;
    bool start_inclusive = true;
    bool end_inclusive = true;
};

class scan_spec final
{
public:
    scan_spec() = default;

    /// Deep copy: every string is re-duplicated into this instance's arena, so the
    /// copy is independent of the original's lifetime. A scanner takes such a copy
    /// because it mutates the intervals (filling in the open bounds) and its
    /// readers hold pointers into them for the duration of the scan.
    scan_spec(const scan_spec& other);

    scan_spec& operator=(const scan_spec&) = delete;

    //
    // scalars, names kept as in Hypertable::ScanSpec
    //

    std::int32_t row_limit = 0;
    std::int32_t cell_limit = 0;
    std::int32_t cell_limit_per_family = 0;
    std::int32_t row_offset = 0;
    std::int32_t cell_offset = 0;
    std::uint32_t max_versions = 0;

    std::pair<std::int64_t, std::int64_t> time_interval{TIMESTAMP_MIN, TIMESTAMP_MAX};

    const char* row_regexp = nullptr;
    const char* value_regexp = nullptr;

    bool return_deletes = false;
    bool keys_only = false;
    bool scan_and_filter_rows = false;
    bool do_not_cache = false;
    bool and_column_predicates = false;

    std::vector<const char*> columns;
    std::vector<row_interval> row_intervals;
    std::vector<cell_interval> cell_intervals;
    std::vector<column_predicate> column_predicates;

    //
    // builders, the ht4c::Common::ScanSpec surface
    //

    void clear();

    void set_max_rows(std::int32_t v) { row_limit = v; }
    void set_max_versions(std::int32_t v) { max_versions = static_cast<std::uint32_t>(v); }
    void set_max_cells(std::int32_t v) { cell_limit = v; }
    void set_max_cells_column_family(std::int32_t v) { cell_limit_per_family = v; }
    /// Rejects the combination with cell_offset, as Hypertable::ScanSpecBuilder did.
    void set_row_offset(std::int32_t v);
    void set_cell_offset(std::int32_t v);

    void set_keys_only(bool v) { keys_only = v; }
    void set_not_use_query_cache(bool v) { do_not_cache = v; }
    void set_scan_and_filter(bool v) { scan_and_filter_rows = v; }
    void set_column_predicate_and(bool v) { and_column_predicates = v; }

    void set_start_timestamp(std::uint64_t v)
    {
        time_interval.first = static_cast<std::int64_t>(v);
    }
    /// The stored interval end is exclusive while the caller's is inclusive, hence
    /// the + 1. ht4c::Common::ScanSpec::endTimestamp did the same adjustment.
    void set_end_timestamp(std::uint64_t v)
    {
        time_interval.second = static_cast<std::int64_t>(v) + 1;
    }

    void set_row_regex(const char* v, int len);
    void set_value_regex(const char* v, int len);

    void reserve_rows(std::int32_t count)
    {
        row_intervals.reserve(static_cast<std::size_t>(count));
    }
    void reserve_columns(std::int32_t count) { columns.reserve(static_cast<std::size_t>(count)); }
    void reserve_cells(std::int32_t count)
    {
        cell_intervals.reserve(static_cast<std::size_t>(count));
    }

    /// An empty row is ignored, as ht4c::Common::ScanSpec did.
    void add_row(const char* row, int len);
    void add_column(const char* column, int len);
    /// A cell without a column degrades to a row, matching ht4c.
    void add_cell(const char* row, int row_len, const char* column, int column_len);
    void add_row_interval(const char* from, int from_len, bool include_from, const char* to,
                          int to_len, bool include_to);
    void add_cell_interval(const char* from_row, int from_row_len, const char* from_column,
                           int from_column_len, bool include_from, const char* to_row,
                           int to_row_len, const char* to_column, int to_column_len,
                           bool include_to);
    /// Ignored when the column family is empty, as ht4c did. A null value becomes
    /// "", because hypertable cannot tell null from empty in a predicate.
    void add_column_predicate(const char* cf, int cf_len, const char* cq, int cq_len,
                              std::uint32_t operation, const void* value, int value_len);

    /// Owns every string the members above point at.
    char_arena& arena() noexcept { return arena_; }

private:
    /// A null pointer stays null. Negative len means "nul terminated".
    const char* dup(const char* text, int len);

    char_arena arena_{4096};
};
} // namespace hyperlite
