// Replaces Hypertable::Schema / AccessGroupSpec / ColumnFamilySpec from ht4w,
// minus expat and boost.
//
// The rendered XML is what ends up in the sys_db record, so both the parser and
// the writer are held to the ht4w format: the writer emits the same elements,
// attributes, indentation and ordering render_xml produced, and the parser
// accepts everything ht4w accepted, including the legacy attribute spellings
// (blksz, inMemory, bloomFilter, ...). A database written by ht4n opens here and
// the other way round.

#pragma once

#include "Support.h"

#include <cstdint>
#include <memory>
#include <string>
#include <string_view>
#include <utility>
#include <unordered_map>
#include <vector>

namespace hyperlite
{
/// Hypertable caps a table at 255 column families, ids are 1 based.
constexpr std::int32_t k_max_column_id = 255;

/// Tri state option holder: every field carries an explicit "is set" bit,
/// because only set options are rendered and only unset options inherit from a
/// defaults block.
class access_group_options final
{
public:
    bool is_set_replication() const noexcept { return isset_ & bit_replication; }
    bool is_set_blocksize() const noexcept { return isset_ & bit_blocksize; }
    bool is_set_compressor() const noexcept { return isset_ & bit_compressor; }
    bool is_set_bloom_filter() const noexcept { return isset_ & bit_bloom_filter; }
    bool is_set_in_memory() const noexcept { return isset_ & bit_in_memory; }
    bool any_set() const noexcept { return isset_ != 0; }

    std::int16_t get_replication() const noexcept { return replication_; }
    std::int32_t get_blocksize() const noexcept { return blocksize_; }
    const std::string& get_compressor() const noexcept { return compressor_; }
    const std::string& get_bloom_filter() const noexcept { return bloom_filter_; }
    bool get_in_memory() const noexcept { return in_memory_; }

    void set_replication(std::int16_t v);
    void set_blocksize(std::int32_t v);
    void set_compressor(std::string v);
    void set_bloom_filter(std::string v);
    void set_in_memory(bool v);

    /// Adopts every option of other that is not set here.
    void merge(const access_group_options& other);

    std::string render_xml(std::string_view line_prefix) const;

private:
    enum : std::uint32_t
    {
        bit_replication = 1u << 0,
        bit_blocksize = 1u << 1,
        bit_compressor = 1u << 2,
        bit_bloom_filter = 1u << 3,
        bit_in_memory = 1u << 4
    };

    std::uint32_t isset_ = 0;
    std::int16_t replication_ = 0;
    std::int32_t blocksize_ = 0;
    std::string compressor_;
    std::string bloom_filter_;
    bool in_memory_ = false;
};

class column_family_options final
{
public:
    bool is_set_max_versions() const noexcept { return isset_ & bit_max_versions; }
    bool is_set_ttl() const noexcept { return isset_ & bit_ttl; }
    bool is_set_time_order_desc() const noexcept { return isset_ & bit_time_order_desc; }
    bool any_set() const noexcept { return isset_ != 0; }

    std::int32_t get_max_versions() const noexcept { return max_versions_; }
    std::int32_t get_ttl() const noexcept { return ttl_; }
    bool get_time_order_desc() const noexcept { return time_order_desc_; }

    /// Throws BAD_VALUE on a negative value and INCOMPATIBLE_OPTIONS when the
    /// combination is one ht4w rejected, so a bad schema fails the same way.
    void set_max_versions(std::int32_t v);
    void set_ttl(std::int32_t v);
    void set_time_order_desc(bool v);

    void merge(const column_family_options& other);

    std::string render_xml(std::string_view line_prefix) const;

private:
    enum : std::uint32_t
    {
        bit_max_versions = 1u << 0,
        bit_ttl = 1u << 1,
        bit_time_order_desc = 1u << 2
    };

    std::uint32_t isset_ = 0;
    std::int32_t max_versions_ = 0;
    std::int32_t ttl_ = 0;
    bool time_order_desc_ = false;
};

class column_family_spec final
{
public:
    column_family_spec() = default;
    explicit column_family_spec(std::string name) : name_(std::move(name)) {}

    const std::string& get_name() const noexcept { return name_; }
    const std::string& get_access_group() const noexcept { return ag_; }
    std::int32_t get_id() const noexcept { return id_; }
    std::int64_t get_generation() const noexcept { return generation_; }
    bool get_deleted() const noexcept { return deleted_; }
    bool get_value_index() const noexcept { return value_index_; }
    bool get_qualifier_index() const noexcept { return qualifier_index_; }

    void set_name(std::string v) { name_ = std::move(v); }
    void set_access_group(std::string v) { ag_ = std::move(v); }
    void set_id(std::int32_t v) { id_ = v; }
    void set_generation(std::int64_t v) { generation_ = v; }
    void set_deleted(bool v) { deleted_ = v; }
    void set_value_index(bool v);
    void set_qualifier_index(bool v);

    const column_family_options& options() const noexcept { return options_; }
    column_family_options& options() noexcept { return options_; }

    void set_option_max_versions(std::int32_t v) { options_.set_max_versions(v); }
    void set_option_ttl(std::int32_t v) { options_.set_ttl(v); }
    void set_option_time_order_desc(bool v) { options_.set_time_order_desc(v); }

    bool get_option_time_order_desc() const noexcept
    {
        return options_.is_set_time_order_desc() && options_.get_time_order_desc();
    }
    std::int32_t get_option_max_versions() const noexcept
    {
        return options_.is_set_max_versions() ? options_.get_max_versions() : 0;
    }
    std::int32_t get_option_ttl() const noexcept
    {
        return options_.is_set_ttl() ? options_.get_ttl() : 0;
    }

    void merge_options(const column_family_options& other) { options_.merge(other); }

    std::string render_xml(std::string_view line_prefix, bool with_ids) const;

private:
    std::string name_;
    std::string ag_;
    std::int32_t id_ = 0;
    std::int64_t generation_ = 0;
    bool deleted_ = false;
    bool value_index_ = false;
    bool qualifier_index_ = false;
    column_family_options options_;
};

class access_group_spec final
{
public:
    access_group_spec() = default;
    explicit access_group_spec(std::string name) : name_(std::move(name)) {}

    const std::string& get_name() const noexcept { return name_; }
    void set_name(std::string v) { name_ = std::move(v); }

    std::int64_t get_generation() const noexcept { return generation_; }
    void set_generation(std::int64_t v) { generation_ = v; }

    const access_group_options& options() const noexcept { return options_; }
    access_group_options& options() noexcept { return options_; }
    const column_family_options& defaults() const noexcept { return defaults_; }
    column_family_options& defaults() noexcept { return defaults_; }

    void merge_options(const access_group_options& other) { options_.merge(other); }
    void merge_defaults(const column_family_options& other) { defaults_.merge(other); }

    /// Takes ownership. Applies the group defaults and, when the column does not
    /// name a group, assigns this one.
    void add_column(std::unique_ptr<column_family_spec> cf);

    const std::vector<std::unique_ptr<column_family_spec>>& columns() const noexcept
    {
        return columns_;
    }
    std::vector<std::unique_ptr<column_family_spec>>& columns() noexcept { return columns_; }

    std::string render_xml(std::string_view line_prefix, bool with_ids) const;

private:
    std::string name_;
    std::int64_t generation_ = 0;
    access_group_options options_;
    column_family_options defaults_;
    std::vector<std::unique_ptr<column_family_spec>> columns_;
};

class schema final
{
public:
    schema() = default;

    /// Parses and validates. Throws SCHEMA_PARSE_ERROR or BAD_SCHEMA on failure,
    /// same codes ht4w used, so Hyperlite raises the same managed exception.
    static std::unique_ptr<schema> parse(std::string_view xml);

    std::int64_t get_generation() const noexcept { return generation_; }
    void set_generation(std::int64_t v) { generation_ = v; }
    std::int32_t get_version() const noexcept { return version_; }
    void set_version(std::int32_t v) { version_ = v; }
    std::int32_t get_group_commit_interval() const noexcept { return group_commit_interval_; }
    void set_group_commit_interval(std::int32_t v) { group_commit_interval_ = v; }

    access_group_options& access_group_defaults() noexcept { return ag_defaults_; }
    column_family_options& column_family_defaults() noexcept { return cf_defaults_; }
    void set_access_group_defaults(const access_group_options& v) { ag_defaults_ = v; }
    void set_column_family_defaults(const column_family_options& v) { cf_defaults_ = v; }

    /// Takes ownership, merges the table level defaults down, rejects a duplicate
    /// group or column family name.
    void add_access_group(std::unique_ptr<access_group_spec> ag);

    const std::vector<std::unique_ptr<access_group_spec>>& get_access_groups() const noexcept
    {
        return access_groups_;
    }

    access_group_spec* get_access_group(std::string_view name) const noexcept;
    column_family_spec* get_column_family(std::string_view name) const noexcept;
    column_family_spec* get_column_family(std::int32_t id) const noexcept;

    /// Every column family, in access group then declaration order.
    const std::vector<column_family_spec*>& get_column_families() const noexcept
    {
        return column_families_;
    }

    /// Assigns ids and the given generation to every column family that has none
    /// yet, exactly as Schema::update_generation did. Called before a schema is
    /// persisted.
    void update_generation(std::int64_t generation);

    /// Rebuilds the lookup maps and rejects a column family assigned to two
    /// groups or a table with more than k_max_column_id families.
    void validate();

    std::string render_xml(bool with_ids) const;

private:
    std::int64_t generation_ = 0;
    std::int32_t version_ = 0;
    std::int32_t group_commit_interval_ = 0;

    access_group_options ag_defaults_;
    column_family_options cf_defaults_;

    std::vector<std::unique_ptr<access_group_spec>> access_groups_;

    // rebuilt by validate(), never owning
    std::vector<column_family_spec*> column_families_;

    // Lookup by name is on the mutator's per cell path, so it must not allocate. An
    // unordered_map keyed on std::string would: find() needs a std::string, and building
    // one from the caller's string_view is a heap allocation per cell. A vector sorted by
    // name compares string_views directly against the names the specs already own, and a
    // schema has a handful of families, so the binary search is a couple of compares.
    std::vector<std::pair<std::string_view, column_family_spec*>> column_family_by_name_;
    std::unordered_map<std::int32_t, column_family_spec*> column_family_id_map_;

    std::int32_t get_max_column_family_id() const noexcept;
    void merge_table_defaults(access_group_spec& ag);
    void merge_table_defaults(column_family_spec& cf);
};
} // namespace hyperlite
