#include "pch.h"
#include "Schema.h"
#include "Xml.h"

#include <algorithm>
#include <map>
#include <set>

namespace hyperlite
{
namespace
{
bool iequals(std::string_view a, std::string_view b) noexcept
{
    if (a.size() != b.size())
    {
        return false;
    }
    auto lower = [](char c)
    { return (c >= 'A' && c <= 'Z') ? static_cast<char>(c - 'A' + 'a') : c; };
    for (std::size_t i = 0; i < a.size(); ++i)
    {
        if (lower(a[i]) != lower(b[i]))
        {
            return false;
        }
    }
    return true;
}

//
// parse helpers, one per ht4w XmlParser subclass
//

void parse_access_group_options(const xml::element& node, access_group_options& out)
{
    for (const auto& child : node.children)
    {
        const std::string& n = child->name;
        if (iequals(n, "Replication"))
        {
            out.set_replication(xml::content_to_i16(n, child->text));
        }
        else if (iequals(n, "BlockSize"))
        {
            out.set_blocksize(xml::content_to_i32(n, child->text));
        }
        else if (iequals(n, "Compressor"))
        {
            out.set_compressor(child->text);
        }
        else if (iequals(n, "BloomFilter"))
        {
            out.set_bloom_filter(child->text);
        }
        else if (iequals(n, "InMemory"))
        {
            out.set_in_memory(xml::content_to_bool(n, child->text));
        }
        else
        {
            HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR,
                             "Unrecognized AccessGroupOptions element (%s)", n.c_str());
        }
    }
}

void parse_column_family_options(const xml::element& node, column_family_options& out)
{
    for (const auto& child : node.children)
    {
        const std::string& n = child->name;
        if (iequals(n, "MaxVersions"))
        {
            out.set_max_versions(xml::content_to_i32(n, child->text));
        }
        else if (iequals(n, "TTL"))
        {
            out.set_ttl(xml::content_to_i32(n, child->text));
        }
        else if (iequals(n, "TimeOrder"))
        {
            out.set_time_order_desc(xml::content_to_text(n, child->text, {"asc", "desc"}) ==
                                    "desc");
        }
        else if (iequals(n, "Counter"))
        {
            // accepted and ignored: counter columns are gone, but a schema written
            // before they were removed still lives in somebody's database, and this
            // parser throws on an element it does not recognise
        }
        else
        {
            HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR,
                             "Unrecognized ColumnFamilyOptions element (%s)", n.c_str());
        }
    }
}

std::unique_ptr<column_family_spec> parse_column_family(const xml::element& node)
{
    auto cf = std::make_unique<column_family_spec>();

    for (const auto& attr : node.attributes)
    {
        if (iequals(attr.name, "id"))
        {
            cf->set_id(xml::content_to_i32("id", attr.value));
        }
        else
        {
            HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR,
                             "Unrecognized attribute (%s) in ColumnFamily element",
                             attr.name.c_str());
        }
    }

    // ht4w reads Generation into a local and only commits it when the element
    // closes, which amounts to "last one wins". Same here.
    std::int64_t generation = 0;

    for (const auto& child : node.children)
    {
        const std::string& n = child->name;
        if (iequals(n, "Name"))
        {
            cf->set_name(child->text);
        }
        else if (iequals(n, "AccessGroup"))
        {
            cf->set_access_group(child->text);
        }
        else if (iequals(n, "Generation"))
        {
            generation = xml::content_to_i64(n, child->text);
        }
        else if (iequals(n, "Deleted"))
        {
            cf->set_deleted(xml::content_to_bool(n, child->text));
        }
        else if (iequals(n, "Index"))
        {
            cf->set_value_index(xml::content_to_bool(n, child->text));
        }
        else if (iequals(n, "QualifierIndex"))
        {
            cf->set_qualifier_index(xml::content_to_bool(n, child->text));
        }
        else if (iequals(n, "Options"))
        {
            column_family_options options;
            parse_column_family_options(*child, options);
            cf->merge_options(options);
        }
        // backward compatibility: options used to sit directly under ColumnFamily
        else if (iequals(n, "MaxVersions"))
        {
            cf->set_option_max_versions(xml::content_to_i32(n, child->text));
        }
        else if (iequals(n, "TTL"))
        {
            cf->set_option_ttl(xml::content_to_i32(n, child->text));
        }
        else if (iequals(n, "TimeOrder"))
        {
            cf->set_option_time_order_desc(xml::content_to_text(n, child->text, {"asc", "desc"}) ==
                                           "desc");
        }
        else if (iequals(n, "Renamed") || iequals(n, "NewName") || iequals(n, "Counter"))
        {
            // accepted and ignored: Renamed and NewName as ht4w did, Counter because
            // counter columns are gone and an older schema may still name one
        }
        else
        {
            HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR, "Unrecognized ColumnFamily element (%s)",
                             n.c_str());
        }
    }

    cf->set_generation(generation);

    if (cf->get_name().empty())
    {
        HYPERLITE_THROW(error::BAD_SCHEMA, "ColumnFamily element without a Name");
    }
    return cf;
}

std::unique_ptr<access_group_spec> parse_access_group(const xml::element& node)
{
    auto ag = std::make_unique<access_group_spec>();

    for (const auto& attr : node.attributes)
    {
        if (iequals(attr.name, "name"))
        {
            ag->set_name(attr.value);
        }
        // backward compatibility: options used to be attributes
        else if (iequals(attr.name, "inMemory"))
        {
            ag->options().set_in_memory(xml::content_to_bool(attr.name, attr.value));
        }
        else if (iequals(attr.name, "counter"))
        {
            // accepted and ignored, see the Counter element
        }
        else if (iequals(attr.name, "replication"))
        {
            ag->options().set_replication(xml::content_to_i16(attr.name, attr.value));
        }
        else if (iequals(attr.name, "blksz"))
        {
            ag->options().set_blocksize(xml::content_to_i32(attr.name, attr.value));
        }
        else if (iequals(attr.name, "compressor"))
        {
            ag->options().set_compressor(attr.value);
        }
        else if (iequals(attr.name, "bloomFilter"))
        {
            ag->options().set_bloom_filter(attr.value);
        }
        else
        {
            HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR,
                             "Unrecognized attribute (%s) in AccessGroup element",
                             attr.name.c_str());
        }
    }

    // Options and ColumnFamilyDefaults have to be folded in before the columns
    // are added, otherwise a column would not inherit them
    for (const auto& child : node.children)
    {
        if (iequals(child->name, "Options"))
        {
            access_group_options options;
            parse_access_group_options(*child, options);
            ag->merge_options(options);
        }
        else if (iequals(child->name, "ColumnFamilyDefaults"))
        {
            column_family_options defaults;
            parse_column_family_options(*child, defaults);
            ag->merge_defaults(defaults);
        }
        else if (iequals(child->name, "Generation"))
        {
            ag->set_generation(xml::content_to_i64(child->name, child->text));
        }
        else if (!iequals(child->name, "ColumnFamily"))
        {
            HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR, "Unrecognized AccessGroup element (%s)",
                             child->name.c_str());
        }
    }

    for (const auto& child : node.children)
    {
        if (iequals(child->name, "ColumnFamily"))
        {
            ag->add_column(parse_column_family(*child));
        }
    }

    return ag;
}
} // namespace

void access_group_options::set_replication(std::int16_t v)
{
    isset_ |= bit_replication;
    replication_ = v;
}

void access_group_options::set_blocksize(std::int32_t v)
{
    isset_ |= bit_blocksize;
    blocksize_ = v;
}

void access_group_options::set_compressor(std::string v)
{
    isset_ |= bit_compressor;
    compressor_ = std::move(v);
}

void access_group_options::set_bloom_filter(std::string v)
{
    isset_ |= bit_bloom_filter;
    bloom_filter_ = std::move(v);
}

void access_group_options::set_in_memory(bool v)
{
    isset_ |= bit_in_memory;
    in_memory_ = v;
}

void access_group_options::merge(const access_group_options& other)
{
    if (!is_set_replication() && other.is_set_replication())
    {
        set_replication(other.get_replication());
    }
    if (!is_set_blocksize() && other.is_set_blocksize())
    {
        set_blocksize(other.get_blocksize());
    }
    if (!is_set_compressor() && other.is_set_compressor())
    {
        set_compressor(other.get_compressor());
    }
    if (!is_set_bloom_filter() && other.is_set_bloom_filter())
    {
        set_bloom_filter(other.get_bloom_filter());
    }
    if (!is_set_in_memory() && other.is_set_in_memory())
    {
        set_in_memory(other.get_in_memory());
    }
}

std::string access_group_options::render_xml(std::string_view line_prefix) const
{
    std::string out;
    const std::string prefix(line_prefix);
    if (is_set_replication())
    {
        out += format("%s<Replication>%d</Replication>\n", prefix.c_str(),
                      static_cast<int>(replication_));
    }
    if (is_set_blocksize())
    {
        out +=
            format("%s<BlockSize>%d</BlockSize>\n", prefix.c_str(), static_cast<int>(blocksize_));
    }
    if (is_set_compressor())
    {
        out += format("%s<Compressor>%s</Compressor>\n", prefix.c_str(),
                      xml::escape(compressor_).c_str());
    }
    if (is_set_bloom_filter())
    {
        out += format("%s<BloomFilter>%s</BloomFilter>\n", prefix.c_str(),
                      xml::escape(bloom_filter_).c_str());
    }
    if (is_set_in_memory())
    {
        out += format("%s<InMemory>%s</InMemory>\n", prefix.c_str(), in_memory_ ? "true" : "false");
    }
    return out;
}

void column_family_options::set_max_versions(std::int32_t v)
{
    if (v < 0)
    {
        HYPERLITE_THROWF(error::BAD_VALUE,
                         "Invalid value for MAX VERSIONS (%d), must be non-negative", v);
    }
    isset_ |= bit_max_versions;
    max_versions_ = v;
}

void column_family_options::set_ttl(std::int32_t v)
{
    if (v < 0)
    {
        HYPERLITE_THROWF(error::BAD_VALUE, "Invalid value for TTL (%d), must be non-negative", v);
    }
    isset_ |= bit_ttl;
    ttl_ = v;
}

void column_family_options::set_time_order_desc(bool v)
{
    isset_ |= bit_time_order_desc;
    time_order_desc_ = v;
}

void column_family_options::merge(const column_family_options& other)
{
    if (!is_set_max_versions() && other.is_set_max_versions())
    {
        set_max_versions(other.get_max_versions());
    }
    if (!is_set_ttl() && other.is_set_ttl())
    {
        set_ttl(other.get_ttl());
    }
    if (!is_set_time_order_desc() && other.is_set_time_order_desc())
    {
        set_time_order_desc(other.get_time_order_desc());
    }
}

std::string column_family_options::render_xml(std::string_view line_prefix) const
{
    std::string out;
    const std::string prefix(line_prefix);
    if (is_set_max_versions())
    {
        out += format("%s<MaxVersions>%d</MaxVersions>\n", prefix.c_str(),
                      static_cast<int>(max_versions_));
    }
    if (is_set_ttl())
    {
        out += format("%s<TTL>%d</TTL>\n", prefix.c_str(), static_cast<int>(ttl_));
    }
    if (is_set_time_order_desc())
    {
        out += format("%s<TimeOrder>%s</TimeOrder>\n", prefix.c_str(),
                      time_order_desc_ ? "desc" : "asc");
    }
    return out;
}

void column_family_spec::set_value_index(bool v)
{
    value_index_ = v;
}

void column_family_spec::set_qualifier_index(bool v)
{
    qualifier_index_ = v;
}

std::string column_family_spec::render_xml(std::string_view line_prefix, bool with_ids) const
{
    const std::string p0(line_prefix);
    const std::string p1 = p0 + "  ";
    const std::string p2 = p1 + "  ";

    std::string out = p0 + "<ColumnFamily";
    if (with_ids)
    {
        out += format(" id=\"%u\"", static_cast<unsigned>(id_));
        out += ">\n";
        out += p1 + format("<Generation>%lld</Generation>\n", static_cast<long long>(generation_));
    }
    else
    {
        out += ">\n";
    }
    out += p1 + format("<Name>%s</Name>\n", xml::escape(name_).c_str());
    out += p1 + format("<AccessGroup>%s</AccessGroup>\n", xml::escape(ag_).c_str());
    out += p1 + format("<Deleted>%s</Deleted>\n", deleted_ ? "true" : "false");
    if (value_index_)
    {
        out += p1 + "<Index>true</Index>\n";
    }
    if (qualifier_index_)
    {
        out += p1 + "<QualifierIndex>true</QualifierIndex>\n";
    }
    out += p1 + "<Options>\n";
    out += options_.render_xml(p2);
    out += p1 + "</Options>\n";
    out += p0 + "</ColumnFamily>\n";
    return out;
}

void access_group_spec::add_column(std::unique_ptr<column_family_spec> cf)
{
    cf->merge_options(defaults_);
    if (cf->get_access_group().empty())
    {
        cf->set_access_group(name_);
    }
    else if (cf->get_access_group() != name_)
    {
        HYPERLITE_THROWF(error::BAD_SCHEMA,
                         "Column family '%s' names access group '%s' but sits in '%s'",
                         cf->get_name().c_str(), cf->get_access_group().c_str(), name_.c_str());
    }
    columns_.push_back(std::move(cf));
}

std::string access_group_spec::render_xml(std::string_view line_prefix, bool with_ids) const
{
    const std::string p0(line_prefix);
    const std::string p1 = p0 + "  ";
    const std::string p2 = p1 + "  ";

    std::string out = p0 + format("<AccessGroup name=\"%s\">\n", xml::escape(name_).c_str());
    if (with_ids)
    {
        out += p1 + format("<Generation>%lld</Generation>\n", static_cast<long long>(generation_));
    }
    out += p1 + "<Options>\n";
    out += options_.render_xml(p2);
    out += p1 + "</Options>\n";
    out += p1 + "<ColumnFamilyDefaults>\n";
    out += defaults_.render_xml(p2);
    out += p1 + "</ColumnFamilyDefaults>\n";
    for (const auto& cf : columns_)
    {
        out += cf->render_xml(p1, with_ids);
    }
    out += p0 + "</AccessGroup>\n";
    return out;
}

std::unique_ptr<schema> schema::parse(std::string_view text)
{
    auto root = xml::parse(text);
    if (!iequals(root->name, "Schema"))
    {
        HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR, "Expected a Schema element, found '%s'",
                         root->name.c_str());
    }

    auto out = std::make_unique<schema>();

    for (const auto& attr : root->attributes)
    {
        if (iequals(attr.name, "generation"))
        {
            out->set_generation(xml::content_to_i64(attr.name, attr.value));
        }
        else if (iequals(attr.name, "version"))
        {
            out->set_version(xml::content_to_i32(attr.name, attr.value));
        }
        else if (iequals(attr.name, "group_commit_interval"))
        {
            out->set_group_commit_interval(xml::content_to_i32(attr.name, attr.value));
        }
        else if (iequals(attr.name, "compressor"))
        {
            out->access_group_defaults().set_compressor(attr.value);
        }
        else
        {
            HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR,
                             "Unrecognized attribute (%s) in Schema element", attr.name.c_str());
        }
    }

    // the defaults blocks have to be in place before the access groups are added,
    // add_access_group merges them downwards
    for (const auto& child : root->children)
    {
        const std::string& n = child->name;
        if (iequals(n, "Generation"))
        {
            out->set_generation(xml::content_to_i64(n, child->text));
        }
        else if (iequals(n, "GroupCommitInterval"))
        {
            out->set_group_commit_interval(xml::content_to_i32(n, child->text));
        }
        else if (iequals(n, "AccessGroupDefaults"))
        {
            access_group_options defaults;
            parse_access_group_options(*child, defaults);
            out->set_access_group_defaults(defaults);
        }
        else if (iequals(n, "ColumnFamilyDefaults"))
        {
            column_family_options defaults;
            parse_column_family_options(*child, defaults);
            out->set_column_family_defaults(defaults);
        }
        else if (!iequals(n, "AccessGroup"))
        {
            HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR, "Unrecognized Schema element (%s)",
                             n.c_str());
        }
    }

    for (const auto& child : root->children)
    {
        if (iequals(child->name, "AccessGroup"))
        {
            out->add_access_group(parse_access_group(*child));
        }
    }

    out->validate();
    return out;
}

void schema::add_access_group(std::unique_ptr<access_group_spec> ag)
{
    for (const auto& existing : access_groups_)
    {
        if (existing->get_name() == ag->get_name())
        {
            HYPERLITE_THROWF(error::BAD_SCHEMA,
                             "Attempt to add access group '%s' which already exists",
                             ag->get_name().c_str());
        }
    }

    std::set<std::string> incoming;
    for (const auto& cf : ag->columns())
    {
        incoming.insert(cf->get_name());
    }

    std::size_t column_count = 0;
    for (const auto& existing : access_groups_)
    {
        column_count += existing->columns().size();
        for (const auto& cf : existing->columns())
        {
            if (incoming.count(cf->get_name()))
            {
                HYPERLITE_THROWF(error::BAD_SCHEMA, "Column family '%s' already defined",
                                 cf->get_name().c_str());
            }
        }
    }

    if (column_count + ag->columns().size() > static_cast<std::size_t>(k_max_column_id))
    {
        HYPERLITE_THROWF(error::TOO_MANY_COLUMNS, "Attempt to add > %d column families to table",
                         k_max_column_id);
    }

    access_group_spec& added = *ag;
    access_groups_.push_back(std::move(ag));

    merge_table_defaults(added);
    for (const auto& cf : added.columns())
    {
        merge_table_defaults(*cf);
    }
}

access_group_spec* schema::get_access_group(std::string_view name) const noexcept
{
    for (const auto& ag : access_groups_)
    {
        if (ag->get_name() == name)
        {
            return ag.get();
        }
    }
    return nullptr;
}

column_family_spec* schema::get_column_family(std::string_view name) const noexcept
{
    const auto it =
        std::lower_bound(column_family_by_name_.begin(), column_family_by_name_.end(), name,
                         [](const auto& entry, std::string_view key) { return entry.first < key; });
    return it != column_family_by_name_.end() && it->first == name ? it->second : nullptr;
}

column_family_spec* schema::get_column_family(std::int32_t id) const noexcept
{
    const auto it = column_family_id_map_.find(id);
    return it == column_family_id_map_.end() ? nullptr : it->second;
}

std::int32_t schema::get_max_column_family_id() const noexcept
{
    std::int32_t max = 0;
    for (const auto& ag : access_groups_)
    {
        for (const auto& cf : ag->columns())
        {
            if (cf->get_id() > max)
            {
                max = cf->get_id();
            }
        }
    }
    return max;
}

void schema::update_generation(std::int64_t generation)
{
    std::int32_t max_id = get_max_column_family_id();
    for (const auto& ag : access_groups_)
    {
        for (const auto& cf : ag->columns())
        {
            if (cf->get_generation() == 0)
            {
                if (cf->get_id() == 0)
                {
                    if (max_id >= k_max_column_id)
                    {
                        HYPERLITE_THROWF(error::TOO_MANY_COLUMNS,
                                         "Attempt to add > %d column families to table",
                                         k_max_column_id);
                    }
                    cf->set_id(++max_id);
                }
                cf->set_generation(generation);
                ag->set_generation(generation);
                generation_ = generation;
            }
            else if (cf->get_id() == 0)
            {
                HYPERLITE_THROWF(error::BAD_SCHEMA,
                                 "Column family '%s' carries a generation but no id",
                                 cf->get_name().c_str());
            }
        }
        if (ag->get_generation() == 0)
        {
            ag->set_generation(generation);
        }
    }
    validate();
}

void schema::validate()
{
    column_families_.clear();
    column_family_by_name_.clear();
    column_family_id_map_.clear();

    std::size_t column_count = 0;
    std::map<std::string, std::string> column_to_ag;

    for (const auto& ag : access_groups_)
    {
        // each column family repeats its group's name as element text, which XML reads back
        // trimmed: a name starting or ending with white space would not match itself, and the
        // table could not be opened again
        const std::string& ag_name = ag->get_name();
        const auto edge_space = [](char c)
        { return c == ' ' || c == '\t' || c == '\n' || c == '\r'; };
        if (!ag_name.empty() && (edge_space(ag_name.front()) || edge_space(ag_name.back())))
        {
            HYPERLITE_THROWF(error::BAD_SCHEMA,
                             "Access group name '%s' starts or ends with white space",
                             ag_name.c_str());
        }

        column_count += ag->columns().size();
        if (column_count > static_cast<std::size_t>(k_max_column_id))
        {
            HYPERLITE_THROWF(error::TOO_MANY_COLUMNS,
                             "Attempt to add > %d column families to table", k_max_column_id);
        }
        for (const auto& cf : ag->columns())
        {
            if (cf->get_access_group().empty())
            {
                cf->set_access_group(ag->get_name());
            }
            const auto it = column_to_ag.find(cf->get_name());
            if (it != column_to_ag.end())
            {
                HYPERLITE_THROWF(
                    error::BAD_SCHEMA, "Column '%s' assigned to two access groups (%s & %s)",
                    cf->get_name().c_str(), it->second.c_str(), ag->get_name().c_str());
            }
            column_to_ag.emplace(cf->get_name(), ag->get_name());

            column_families_.push_back(cf.get());
            column_family_by_name_.emplace_back(cf->get_name(), cf.get());
            // an id is the column family's key in every stored cell and an index into the
            // scanner's and the mutator's per-family tables: one outside 1..k_max_column_id
            // wrote past those, and one that two families shared gave the cells of one to the
            // other. 0 is a family whose id is yet to be assigned.
            const std::int32_t id = cf->get_id();
            if (id < 0 || id > k_max_column_id)
            {
                HYPERLITE_THROWF(error::BAD_SCHEMA, "Column family '%s' has id %d, outside 1..%d",
                                 cf->get_name().c_str(), id, k_max_column_id);
            }
            if (id)
            {
                const auto taken = column_family_id_map_.find(id);
                if (taken != column_family_id_map_.end())
                {
                    HYPERLITE_THROWF(error::BAD_SCHEMA, "Column families '%s' and '%s' share id %d",
                                     taken->second->get_name().c_str(), cf->get_name().c_str(), id);
                }
                column_family_id_map_.emplace(id, cf.get());
            }
        }
    }

    // The views point into the names the specs own, which outlive this vector and are
    // not mutated afterwards - set_name() goes through the spec and is only reachable
    // before validate().
    std::sort(column_family_by_name_.begin(), column_family_by_name_.end(),
              [](const auto& l, const auto& r) { return l.first < r.first; });
}

void schema::merge_table_defaults(access_group_spec& ag)
{
    ag.merge_options(ag_defaults_);
    ag.merge_defaults(cf_defaults_);
}

void schema::merge_table_defaults(column_family_spec& cf)
{
    try
    {
        cf.merge_options(cf_defaults_);
    }
    catch (const exception& e)
    {
        HYPERLITE_THROWF(e.code(), "Merging column '%s' options with table defaults: %s",
                         cf.get_name().c_str(), e.what());
    }
}

std::string schema::render_xml(bool with_ids) const
{
    std::string out = "<Schema";
    if (version_ != 0)
    {
        out += format(" version=\"%d\"", static_cast<int>(version_));
    }
    out += ">\n";

    if (with_ids)
    {
        out += format("  <Generation>%lld</Generation>\n", static_cast<long long>(generation_));
    }
    if (group_commit_interval_ > 0)
    {
        out += format("  <GroupCommitInterval>%u</GroupCommitInterval>\n",
                      static_cast<unsigned>(group_commit_interval_));
    }

    out += "  <AccessGroupDefaults>\n";
    out += ag_defaults_.render_xml("    ");
    out += "  </AccessGroupDefaults>\n";

    out += "  <ColumnFamilyDefaults>\n";
    out += cf_defaults_.render_xml("    ");
    out += "  </ColumnFamilyDefaults>\n";

    for (const auto& ag : access_groups_)
    {
        out += ag->render_xml("  ", with_ids);
    }

    out += "</Schema>\n";
    return out;
}
} // namespace hyperlite
