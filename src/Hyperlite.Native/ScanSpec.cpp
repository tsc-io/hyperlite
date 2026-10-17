#include "pch.h"
#include "ScanSpec.h"

namespace hyperlite
{
namespace
{
/// ht4c fed a null string to hypertable as "", both in add_row_interval and in
/// add_cell_interval. Kept, because it decides whether an interval bound is open.
const char* to_string(const char* s) noexcept
{
    return s ? s : "";
}

/// Hypertable caps the predicate count, the limit comes from the bitset width in
/// its CellPredicate.
constexpr std::size_t k_max_column_predicates = 32;
} // namespace

scan_spec::scan_spec(const scan_spec& other)
    : row_limit(other.row_limit), cell_limit(other.cell_limit),
      cell_limit_per_family(other.cell_limit_per_family), row_offset(other.row_offset),
      cell_offset(other.cell_offset), max_versions(other.max_versions),
      time_interval(other.time_interval), return_deletes(other.return_deletes),
      keys_only(other.keys_only), scan_and_filter_rows(other.scan_and_filter_rows),
      do_not_cache(other.do_not_cache), and_column_predicates(other.and_column_predicates)
{
    // END_ROW_MARKER is a static sentinel and is compared by pointer in places,
    // so it is carried over as the same pointer rather than duplicated
    auto carry = [this](const char* s) -> const char*
    { return s == END_ROW_MARKER ? END_ROW_MARKER : dup(s, -1); };

    row_regexp = dup(other.row_regexp, -1);
    value_regexp = dup(other.value_regexp, -1);

    columns.reserve(other.columns.size());
    for (const char* c : other.columns)
    {
        columns.push_back(dup(c, -1));
    }

    row_intervals.reserve(other.row_intervals.size());
    for (const auto& ri : other.row_intervals)
    {
        row_interval copy;
        // start and end often alias each other (add_row sets both), keep that
        copy.start = carry(ri.start);
        copy.end = ri.end == ri.start ? copy.start : carry(ri.end);
        copy.start_inclusive = ri.start_inclusive;
        copy.end_inclusive = ri.end_inclusive;
        row_intervals.push_back(copy);
    }

    cell_intervals.reserve(other.cell_intervals.size());
    for (const auto& ci : other.cell_intervals)
    {
        cell_interval copy;
        copy.start_row = carry(ci.start_row);
        copy.end_row = ci.end_row == ci.start_row ? copy.start_row : carry(ci.end_row);
        copy.start_column = carry(ci.start_column);
        copy.end_column =
            ci.end_column == ci.start_column ? copy.start_column : carry(ci.end_column);
        copy.start_inclusive = ci.start_inclusive;
        copy.end_inclusive = ci.end_inclusive;
        cell_intervals.push_back(copy);
    }

    column_predicates.reserve(other.column_predicates.size());
    for (const auto& cp : other.column_predicates)
    {
        column_predicate copy;
        copy.column_family = dup(cp.column_family, -1);
        // by its length, which is what the scan reads it by
        copy.column_qualifier = dup(cp.column_qualifier, static_cast<int>(cp.column_qualifier_len));
        copy.column_qualifier_len = cp.column_qualifier_len;
        copy.operation = cp.operation;
        copy.value = cp.value ? arena_.dup(std::string_view(cp.value, cp.value_len)) : nullptr;
        copy.value_len = cp.value_len;
        column_predicates.push_back(copy);
    }
}

const char* scan_spec::dup(const char* text, int len)
{
    if (!text)
    {
        return nullptr;
    }
    return len < 0 ? arena_.dup(text)
                   : arena_.dup(std::string_view(text, static_cast<std::size_t>(len)));
}

void scan_spec::clear()
{
    row_limit = 0;
    cell_limit = 0;
    cell_limit_per_family = 0;
    row_offset = 0;
    cell_offset = 0;
    max_versions = 0;
    time_interval = {TIMESTAMP_MIN, TIMESTAMP_MAX};
    row_regexp = nullptr;
    value_regexp = nullptr;
    return_deletes = false;
    keys_only = false;
    scan_and_filter_rows = false;
    do_not_cache = false;
    and_column_predicates = false;
    columns.clear();
    row_intervals.clear();
    cell_intervals.clear();
    column_predicates.clear();
    arena_.clear();
}

void scan_spec::set_row_offset(std::int32_t v)
{
    if (v && cell_offset)
    {
        HYPERLITE_THROW(error::BAD_SCAN_SPEC,
                        "predicate row_offset not allowed in combination with cell_offset");
    }
    row_offset = v;
}

void scan_spec::set_cell_offset(std::int32_t v)
{
    if (v && row_offset)
    {
        HYPERLITE_THROW(error::BAD_SCAN_SPEC,
                        "predicate cell_offset not allowed in combination with row_offset");
    }
    cell_offset = v;
}

void scan_spec::set_row_regex(const char* v, int len)
{
    if (row_regexp)
    {
        HYPERLITE_THROWF(error::BAD_SCAN_SPEC, "row_regexp already set to '%s'", row_regexp);
    }
    row_regexp = dup(v, len);
}

void scan_spec::set_value_regex(const char* v, int len)
{
    if (value_regexp)
    {
        HYPERLITE_THROWF(error::BAD_SCAN_SPEC, "value_regexp already set to '%s'", value_regexp);
    }
    value_regexp = dup(v, len);
}

void scan_spec::add_row(const char* row, int len)
{
    if (!row || !*row)
    {
        return; // ht4c dropped an empty row silently
    }
    if (!cell_intervals.empty())
    {
        HYPERLITE_THROW(error::BAD_SCAN_SPEC, "cell spec excludes rows");
    }

    row_interval ri;
    ri.start = ri.end = dup(row, len);
    ri.start_inclusive = ri.end_inclusive = true;
    row_intervals.push_back(ri);
}

void scan_spec::add_column(const char* column, int len)
{
    if (!column || !*column)
    {
        return;
    }
    columns.push_back(dup(column, len));
}

void scan_spec::add_cell(const char* row, int row_len, const char* column, int column_len)
{
    if (!row || !*row)
    {
        return;
    }
    if (!column || !*column)
    {
        // a cell without a column is just a row, as ht4c::Common::ScanSpec had it
        add_row(row, row_len);
        return;
    }
    if (!row_intervals.empty())
    {
        HYPERLITE_THROW(error::BAD_SCAN_SPEC, "row spec excludes cells");
    }

    cell_interval ci;
    ci.start_row = ci.end_row = dup(row, row_len);
    ci.start_column = ci.end_column = dup(column, column_len);
    ci.start_inclusive = ci.end_inclusive = true;
    cell_intervals.push_back(ci);
}

void scan_spec::add_row_interval(const char* from, int from_len, bool include_from, const char* to,
                                 int to_len, bool include_to)
{
    if (!cell_intervals.empty())
    {
        HYPERLITE_THROW(error::BAD_SCAN_SPEC, "cell spec excludes rows");
    }

    row_interval ri;
    ri.start = dup(to_string(from), from ? from_len : 0);
    ri.start_inclusive = include_from;
    ri.end = (to && *to) ? dup(to, to_len) : END_ROW_MARKER;
    ri.end_inclusive = include_to;
    row_intervals.push_back(ri);
}

void scan_spec::add_cell_interval(const char* from_row, int from_row_len, const char* from_column,
                                  int from_column_len, bool include_from, const char* to_row,
                                  int to_row_len, const char* to_column, int to_column_len,
                                  bool include_to)
{
    // ht4c required both columns, an interval without them was dropped
    if (!from_column || !*from_column || !to_column || !*to_column)
    {
        return;
    }
    if (!row_intervals.empty())
    {
        HYPERLITE_THROW(error::BAD_SCAN_SPEC, "row spec excludes cells");
    }

    cell_interval ci;
    ci.start_row = dup(to_string(from_row), from_row ? from_row_len : 0);
    ci.start_column = dup(from_column, from_column_len);
    ci.start_inclusive = include_from;
    ci.end_row = (to_row && *to_row) ? dup(to_row, to_row_len) : END_ROW_MARKER;
    ci.end_column = dup(to_column, to_column_len);
    ci.end_inclusive = include_to;
    cell_intervals.push_back(ci);
}

void scan_spec::add_column_predicate(const char* cf, int cf_len, const char* cq, int cq_len,
                                     std::uint32_t operation, const void* value, int value_len)
{
    if (!cf || !*cf)
    {
        return; // ht4c dropped a predicate without a column family
    }
    if (column_predicates.size() == k_max_column_predicates)
    {
        HYPERLITE_THROWF(error::FAILED_EXPECTATION,
                         "Column predicate limit of %zu has been exceeded!",
                         k_max_column_predicates);
    }

    column_predicate cp;
    cp.column_family = dup(cf, cf_len);
    cp.column_qualifier = dup(cq, cq_len);
    cp.column_qualifier_len =
        cq ? static_cast<std::uint32_t>(cq_len < 0 ? std::strlen(cq) : cq_len) : 0;
    cp.operation = operation;

    // hypertable cannot tell a null predicate value from an empty one, so ht4c
    // substituted "". Same here, and the substituted value is what gets matched.
    const char* v = value ? static_cast<const char*>(value) : "";
    const std::size_t n = !value           ? 0
                          : value_len >= 0 ? static_cast<std::size_t>(value_len)
                                           : std::strlen(v);
    cp.value = arena_.dup(std::string_view(v, n));
    cp.value_len = static_cast<std::uint32_t>(n);

    column_predicates.push_back(cp);
}
} // namespace hyperlite
