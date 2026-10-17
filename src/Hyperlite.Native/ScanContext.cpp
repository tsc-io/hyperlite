#include "pch.h"
#include "ScanContext.h"

namespace hyperlite
{
bool cell_filter_info::qualifier_matches(const char* qualifier, std::size_t qualifier_len) const
{
    if (!filter_by_exact_qualifier_ && !filter_by_regexp_qualifier_ && !filter_by_prefix_qualifier_)
    {
        return true;
    }

    const char* q = qualifier ? qualifier : "";

    if (filter_by_exact_qualifier_ &&
        exact_qualifiers_.find(std::string(q, qualifier_len)) != exact_qualifiers_.end())
    {
        return true;
    }

    if (filter_by_prefix_qualifier_)
    {
        // the set is ordered, so once a candidate sorts above the qualifier no
        // later one can be a prefix of it
        for (const auto& candidate : prefix_qualifiers_)
        {
            if (candidate.size() > qualifier_len)
            {
                continue;
            }
            const int cmp = std::memcmp(candidate.c_str(), q, candidate.size());
            if (cmp == 0)
            {
                return true;
            }
            if (cmp > 0)
            {
                break;
            }
        }
    }

    for (const auto& re : regexp_qualifiers_)
    {
        if (re->matches(q, qualifier_len))
        {
            return true;
        }
    }
    return false;
}

void cell_filter_info::add_qualifier(const std::string& qualifier, bool is_regexp, bool is_prefix)
{
    if (is_regexp)
    {
        regexp_qualifiers_.push_back(std::make_unique<regex>(qualifier, "qualifier"));
        filter_by_regexp_qualifier_ = true;
    }
    else if (is_prefix)
    {
        prefix_qualifiers_.insert(qualifier);
        filter_by_prefix_qualifier_ = true;
    }
    else
    {
        exact_qualifiers_.insert(qualifier);
        filter_by_exact_qualifier_ = true;
    }
}

bool cell_filter_info::column_predicate_matches(const void* value, std::uint32_t value_len) const
{
    // hypertable cannot tell a null value from an empty one, ht4c substituted ""
    const char* v = value ? static_cast<const char*>(value) : "";

    std::size_t n = 0;
    for (const auto& cp : column_predicates_)
    {
        if (cp.value)
        {
            switch (cp.operation)
            {
            case column_predicate::EXACT_MATCH:
                if (cp.value_len == value_len && std::memcmp(cp.value, v, cp.value_len) == 0)
                {
                    return true;
                }
                break;
            case column_predicate::PREFIX_MATCH:
                if (cp.value_len <= value_len && std::memcmp(cp.value, v, cp.value_len) == 0)
                {
                    return true;
                }
                break;
            case column_predicate::REGEX_MATCH:
                if (n < regexp_value_predicates_.size() && regexp_value_predicates_[n] &&
                    regexp_value_predicates_[n]->matches(v, value_len))
                {
                    return true;
                }
                break;
            default:
                break;
            }
        }
        ++n;
    }
    return false;
}

void cell_filter_info::add_column_predicate(const column_predicate& cp)
{
    column_predicates_.push_back(cp);

    if (cp.operation == column_predicate::REGEX_MATCH)
    {
        regexp_value_predicates_.push_back(std::make_unique<regex>(
            std::string_view(cp.value ? cp.value : "", cp.value_len), "value regexp"));
    }
    else
    {
        regexp_value_predicates_.push_back(nullptr);
    }
}

scan_context::scan_context(const scan_spec& s, const schema& sc)
    : sch(sc), spec(s), time_interval(s.time_interval), keys_only(s.keys_only),
      row_offset(s.row_offset), cell_offset(s.cell_offset), row_limit(s.row_limit),
      cell_limit(s.cell_limit), cell_limit_per_family(s.cell_limit_per_family)
{
}

cell_filter_info& scan_context::family_info(int cf)
{
    auto& slot = family_info_[static_cast<std::size_t>(cf)];
    if (!slot)
    {
        slot = std::make_unique<cell_filter_info>();
    }
    return *slot;
}

namespace
{
/// The per family TTL cutoff and version limit, factored out because the two
/// branches of initialize() computed it identically.
void apply_family_limits(cell_filter_info& cfi, const column_family_spec& cf, std::int64_t now,
                         std::int32_t scan_max_versions)
{
    if (cf.get_option_ttl() == 0)
    {
        cfi.cutoff_time = TIMESTAMP_MIN;
    }
    else
    {
        cfi.cutoff_time = now - static_cast<std::int64_t>(cf.get_option_ttl()) * 1000000000LL;
    }

    const std::int32_t cf_max = cf.get_option_max_versions();
    if (scan_max_versions == 0)
    {
        cfi.max_versions = static_cast<std::uint32_t>(cf_max);
    }
    else if (cf_max == 0)
    {
        cfi.max_versions = static_cast<std::uint32_t>(scan_max_versions);
    }
    else
    {
        cfi.max_versions = static_cast<std::uint32_t>(std::min(scan_max_versions, cf_max));
    }
}
} // namespace

void scan_context::initialize()
{
    family_mask.fill(false);
    column_families.fill(nullptr);

    const std::int64_t now = static_cast<std::int64_t>(get_ts64());
    const std::int32_t max_versions = static_cast<std::int32_t>(spec.max_versions);

    if (!spec.columns.empty())
    {
        for (const char* cfstr : spec.columns)
        {
            const parsed_column parsed = parse_column(cfstr);

            const column_family_spec* cf = sch.get_column_family(parsed.family);
            if (!cf)
            {
                HYPERLITE_THROWF(error::RANGESERVER_INVALID_COLUMNFAMILY,
                                 "Invalid column family '%s'", cfstr);
            }
            const std::uint8_t id = static_cast<std::uint8_t>(cf->get_id());
            if (id == 0)
            {
                HYPERLITE_THROWF(error::RANGESERVER_SCHEMA_INVALID_CFID,
                                 "Bad id for column family '%s'", cf->get_name().c_str());
            }
            column_families[id] = cf;
            family_mask[id] = true;

            cell_filter_info& cfi = family_info(id);
            if (parsed.has_qualifier)
            {
                cfi.add_qualifier(parsed.qualifier, parsed.is_regexp, parsed.is_prefix);
            }
            apply_family_limits(cfi, *cf, now, max_versions);

            initial_column(*cf, parsed.has_qualifier, parsed.is_regexp, parsed.is_prefix,
                           parsed.qualifier);
        }
    }
    else
    {
        // a ROW_DELETE record carries column family 0, letting it through keeps
        // deletes visible to a return_deletes scan
        family_mask[0] = true;

        for (const auto& ag : sch.get_access_groups())
        {
            for (const auto& cf : ag->columns())
            {
                const std::uint8_t id = static_cast<std::uint8_t>(cf->get_id());
                if (id == 0)
                {
                    HYPERLITE_THROWF(error::RANGESERVER_SCHEMA_INVALID_CFID,
                                     "Bad id for column family '%s'", cf->get_name().c_str());
                }
                if (cf->get_deleted())
                {
                    family_mask[id] = false;
                    continue;
                }
                column_families[id] = cf.get();
                family_mask[id] = true;

                apply_family_limits(family_info(id), *cf, now, max_versions);
            }
        }
    }

    if (spec.scan_and_filter_rows)
    {
        for (const auto& ri : spec.row_intervals)
        {
            if (ri.start && *ri.start)
            {
                rowset.insert(ri.start);
            }
            if (ri.end && *ri.end && ri.start != ri.end)
            {
                rowset.insert(ri.end);
            }
        }
    }

    for (const auto& cp : spec.column_predicates)
    {
        if (!cp.column_family || !*cp.column_family)
        {
            continue;
        }

        const column_family_spec* cf = sch.get_column_family(cp.column_family);
        if (!cf)
        {
            HYPERLITE_THROWF(error::RANGESERVER_INVALID_COLUMNFAMILY, "Invalid column family '%s'",
                             cp.column_family);
        }
        const std::uint8_t id = static_cast<std::uint8_t>(cf->get_id());
        if (id == 0)
        {
            HYPERLITE_THROWF(error::RANGESERVER_SCHEMA_INVALID_CFID,
                             "Bad id for column family '%s'", cf->get_name().c_str());
        }
        cell_filter_info& cfi = family_info(id);

        if (cp.operation & column_predicate::VALUE_MATCH)
        {
            cfi.add_column_predicate(cp);
        }
        else if (cp.operation & column_predicate::QUALIFIER_MATCH)
        {
            const std::string qualifier(cp.column_qualifier ? cp.column_qualifier : "",
                                        cp.column_qualifier_len);
            cfi.add_qualifier(qualifier,
                              (cp.operation & column_predicate::QUALIFIER_REGEX_MATCH) != 0,
                              (cp.operation & column_predicate::QUALIFIER_PREFIX_MATCH) != 0);
        }
    }

    if (spec.row_regexp && *spec.row_regexp)
    {
        row_regexp = std::make_unique<regex>(spec.row_regexp, "row regexp");
    }
    if (spec.value_regexp && *spec.value_regexp)
    {
        value_regexp = std::make_unique<regex>(spec.value_regexp, "value regexp");
    }
}

namespace
{
/// Hypertable::strip_enclosing_quotes.
std::string_view strip_enclosing_quotes(std::string_view in)
{
    if (in.size() < 2)
    {
        return in;
    }
    const char first = in.front();
    const char last = in.back();
    if ((first == '\'' && last == '\'') || (first == '"' && last == '"'))
    {
        return in.substr(1, in.size() - 2);
    }
    return in;
}
} // namespace

parsed_column parse_column(const char* column_str)
{
    parsed_column out;
    if (!column_str)
    {
        return out;
    }

    const std::string_view all(column_str);
    const std::size_t colon = all.find(':');

    if (colon == std::string_view::npos)
    {
        out.family = std::string(all);
        return out;
    }

    out.has_qualifier = true;
    out.family = std::string(all.substr(0, colon));

    const std::string_view raw = all.substr(colon + 1);
    if (raw.empty())
    {
        return out;
    }

    if (raw.size() > 2 && raw.front() == '/' && raw.back() == '/')
    {
        out.is_regexp = true;
        out.qualifier = std::string(raw.substr(1, raw.size() - 2));
    }
    else if (raw.front() == '*')
    {
        // "cf:*" means any qualifier, so no qualifier value is recorded
        out.is_prefix = true;
    }
    else if (raw.front() == '^')
    {
        out.is_prefix = true;
        out.qualifier = std::string(strip_enclosing_quotes(raw.substr(1)));
    }
    else
    {
        out.qualifier = std::string(strip_enclosing_quotes(raw));
    }
    return out;
}
} // namespace hyperlite
