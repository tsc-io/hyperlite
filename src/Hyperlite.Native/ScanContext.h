// Replaces ht4c::Common::CellFilterInfo / RegexpCache / ScanContext, which is the
// per scan filter state: which column families are visible, which qualifiers and
// values pass, the TTL cutoff and the per family version limit.
//
// The filter decisions are the part of a scan that has to stay bug for bug
// identical, so the logic is carried over as it stood. What changed is the
// plumbing: RE2 became hyperlite::regex, the raw new/delete containers became owning
// ones, and the exact qualifier set no longer keeps a parallel set of pointers
// into another set's strings.

#pragma once

#include "Regex.h"
#include "ScanSpec.h"
#include "Schema.h"
#include "Support.h"
#include "Types.h"

#include <array>
#include <memory>
#include <set>
#include <string>
#include <vector>

namespace hyperlite
{
/// Column family ids are one byte, so 256 slots cover every possible family.
constexpr int k_max_cf = 256;

/// Per column family filter. A default constructed instance passes everything.
class cell_filter_info final
{
public:
    cell_filter_info() = default;

    cell_filter_info(const cell_filter_info&) = delete;
    cell_filter_info& operator=(const cell_filter_info&) = delete;

    /// True when no qualifier filter is configured, otherwise whether the
    /// qualifier satisfies at least one of them. Exact first, then prefix, then
    /// regex, the order ht4c used.
    bool qualifier_matches(const char* qualifier, std::size_t qualifier_len) const;

    /// Throws BAD_SCAN_SPEC when isRegexp and the pattern does not compile.
    void add_qualifier(const std::string& qualifier, bool is_regexp, bool is_prefix);

    bool has_qualifier_filter() const noexcept
    {
        return filter_by_exact_qualifier_ || filter_by_regexp_qualifier_ ||
               filter_by_prefix_qualifier_;
    }
    bool has_qualifier_regexp_filter() const noexcept { return filter_by_regexp_qualifier_; }
    bool has_qualifier_prefix_filter() const noexcept { return filter_by_prefix_qualifier_; }

    /// True when any predicate matches. Only called when there is at least one.
    bool column_predicate_matches(const void* value, std::uint32_t value_len) const;

    void add_column_predicate(const column_predicate& cp);

    bool has_column_predicate_filter() const noexcept { return !column_predicates_.empty(); }

    std::int64_t cutoff_time = TIMESTAMP_MIN;
    std::uint32_t max_versions = 0;

private:
    std::vector<std::unique_ptr<regex>> regexp_qualifiers_;
    /// Ordered, so the prefix scan can stop early exactly as ht4c's did.
    std::set<std::string> exact_qualifiers_;
    std::set<std::string> prefix_qualifiers_;

    std::vector<column_predicate> column_predicates_;
    /// Parallel to column_predicates_, an entry is null unless that predicate is a
    /// REGEX_MATCH one.
    std::vector<std::unique_ptr<regex>> regexp_value_predicates_;

    bool filter_by_exact_qualifier_ = false;
    bool filter_by_regexp_qualifier_ = false;
    bool filter_by_prefix_qualifier_ = false;
};

/// Memoises the last row and the last column match. A scan walks a row's cells
/// together, so the hit rate is high and it saves a callback per cell.
class regexp_cache final
{
public:
    void check_row(const char* row, bool* cached, bool* match) const
    {
        *match = last_row_match_;
        *cached = has_row_ && last_row_ == row;
    }

    void check_column(int family, const char* qualifier, bool* cached, bool* match) const
    {
        *match = last_column_match_;
        *cached = last_family_ == family && last_qualifier_ == qualifier;
    }

    void set_row(const char* row, bool match)
    {
        last_row_ = row;
        last_row_match_ = match;
        has_row_ = true;
    }

    void set_column(int family, const char* qualifier, bool match)
    {
        last_family_ = family;
        last_qualifier_ = qualifier;
        last_column_match_ = match;
    }

private:
    std::string last_row_;
    std::string last_qualifier_;
    int last_family_ = -1;
    bool last_row_match_ = false;
    bool last_column_match_ = false;
    // ht4c compared against an empty std::string before any row was seen, so a row
    // key of "" reported a cached miss-result. An explicit flag removes that.
    bool has_row_ = false;
};

class scan_context
{
public:
    scan_context(const scan_spec& spec, const schema& sch);
    virtual ~scan_context() = default;

    scan_context(const scan_context&) = delete;
    scan_context& operator=(const scan_context&) = delete;

    /// Resolves the scan spec against the schema: fills the family mask, builds
    /// the per family filters and compiles the row and value patterns. Throws
    /// RANGESERVER_INVALID_COLUMNFAMILY, RANGESERVER_SCHEMA_INVALID_CFID or
    /// BAD_SCAN_SPEC, the same codes ht4c raised.
    virtual void initialize();

    /// Lazily materialised per family filter. A scan usually touches a couple of
    /// families out of 256 and each filter owns several containers, so they are
    /// created on demand.
    cell_filter_info& family_info(int cf);
    const cell_filter_info* family_info_if_present(int cf) const
    {
        return family_info_[static_cast<std::size_t>(cf)].get();
    }

    const schema& sch;
    const scan_spec& spec;

    std::pair<std::int64_t, std::int64_t> time_interval;

    std::array<bool, k_max_cf> family_mask{};
    std::array<const column_family_spec*, k_max_cf> column_families{};

    std::unique_ptr<regex> row_regexp;
    std::unique_ptr<regex> value_regexp;

    /// Only populated for a scan_and_filter_rows scan.
    std::set<std::string> rowset;

    bool keys_only;
    std::int32_t row_offset;
    std::int32_t cell_offset;
    std::int32_t row_limit;
    std::int32_t cell_limit;
    std::int32_t cell_limit_per_family;

protected:
    /// Hook the sqlite scan context uses to accumulate its SQL predicates while
    /// the columns are being walked.
    virtual void initial_column(const column_family_spec& cf, bool has_qualifier, bool is_regexp,
                                bool is_prefix, const std::string& qualifier)
    {
        (void)cf;
        (void)has_qualifier;
        (void)is_regexp;
        (void)is_prefix;
        (void)qualifier;
    }

private:
    std::array<std::unique_ptr<cell_filter_info>, k_max_cf> family_info_{};
};

struct parsed_column
{
    std::string family;
    std::string qualifier;
    bool has_qualifier = false;
    bool is_regexp = false;
    bool is_prefix = false;
};

/// Splits "cf", "cf:cq", "cf:/regex/", "cf:^prefix", "cf:*" and strips enclosing
/// quotes, matching Hypertable::ScanSpec::parse_column.
parsed_column parse_column(const char* column_str);
} // namespace hyperlite
