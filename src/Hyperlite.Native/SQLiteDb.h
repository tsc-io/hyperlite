// Client, namespace, table, mutator and the four scanner readers.
//
// The storage layout below is part of the on-disk contract and must not change:
//  - sys_db holds one row per namespace and per table. The key is the classifier
//    character '0' followed by the full path, nul terminated. A namespace has a
//    NULL value, a table's value is its schema xml (with ids) nul terminated,
//    optionally followed by a nul terminated option string of {UR} and {NCR}.
//  - Cells live in t<sys_db rowid> (r TEXT, cf INTEGER, cq TEXT, ts INTEGER,
//    v BLOB). cf is the schema column family id.
//  - ts is stored bitwise complemented for a time-order-asc family, so ascending
//    sql order yields newest first. Do not change this without a migration.

#pragma once

#include "ScanContext.h"
#include "ScanSpec.h"
#include "SQLiteEnv.h"
#include "Schema.h"
#include "Support.h"
#include "Types.h"

#include <exception>
#include <memory>
#include <string>
#include <vector>

namespace hyperlite::sqlite::db
{
class client;
class name_space;
class table;
class mutator;
class scanner;

using client_ptr = std::shared_ptr<client>;
using namespace_ptr = std::shared_ptr<name_space>;
using table_ptr = std::shared_ptr<table>;

/// One entry of a namespace listing.
struct namespace_listing
{
    std::string name;
    bool is_namespace = false;
    std::vector<namespace_listing> sub_entries;
};

namespace key_classifiers
{
constexpr int length = 1;
constexpr char namespace_listing_prefix = '0';
} // namespace key_classifiers

class client final : public std::enable_shared_from_this<client>
{
public:
    explicit client(env_ptr e);

    client(const client&) = delete;
    client& operator=(const client&) = delete;

    env* get_env() const noexcept { return env_.get(); }

    void create_namespace(const std::string& name, bool create_intermediate);
    namespace_ptr open_namespace(const std::string& name);
    bool namespace_exists(const char* name);
    bool namespace_exists(const std::string& name) { return namespace_exists(name.c_str()); }
    void drop_namespace(const std::string& name, bool if_exists);

    /// Used by name_space::drop while it is already inside a transaction.
    void drop_namespace_in_tx(const std::string& name, bool if_exists);

private:
    void create_one_namespace(const std::string& name);

    env_ptr env_;
    ::sqlite3* db_;
};

class name_space final : public std::enable_shared_from_this<name_space>
{
public:
    name_space(client_ptr c, const std::string& name);

    name_space(const name_space&) = delete;
    name_space& operator=(const name_space&) = delete;

    env* get_env() const noexcept { return env_; }

    /// The path without the classifier character.
    const char* get_name() const noexcept { return key_name_.c_str() + key_classifiers::length; }

    void get_namespace_listing(bool deep, std::vector<namespace_listing>& listing);
    void drop(const std::string& ns_name, const std::vector<namespace_listing>& listing,
              bool if_exists, bool drop_tables);

    void create_table(const std::string& name, const std::string& schema_xml);
    table_ptr open_table(const std::string& name);
    void alter_table(const std::string& name, const std::string& schema_xml);
    bool table_exists(const std::string& name);
    void get_table_id(const std::string& name, std::string& id);
    void get_table_schema(const std::string& name, bool with_ids, std::string& schema_xml);
    void rename_table(const std::string& name, const std::string& new_name);
    void drop_table(const std::string& name, bool if_exists);

    /// True when the path exists at all. isTable tells a table from a namespace:
    /// only a table row carries a value.
    bool name_exists(bool& is_table, std::int64_t* rowid = nullptr);

    /// Hands back the sys_db key, including its terminating nul.
    int to_key(const char*& psz) const;

private:
    /// Refuses a table name being given that has an empty segment, or names a namespace that
    /// does not exist.
    void check_new_name(const std::string& name);

    void drop_in_tx(const std::string& ns_name, const std::vector<namespace_listing>& listing,
                    bool if_exists, bool drop_tables);
    void drop_table_in_tx(const std::string& name, bool if_exists);

    client_ptr client_;
    env* env_;
    ::sqlite3* db_;
    std::string key_name_;
};

class table final : public std::enable_shared_from_this<table>
{
public:
    table(namespace_ptr ns, const std::string& name);
    table(namespace_ptr ns, const std::string& name, const std::string& schema_xml,
          std::int64_t id);
    /// Rename: same schema and id under a new name.
    table(namespace_ptr ns, const std::string& name, const table& other);
    /// Alter: a new schema for an existing table, keeping its id and options.
    table(namespace_ptr ns, const table& other, const std::string& schema_xml);
    ~table();

    table(const table&) = delete;
    table& operator=(const table&) = delete;

    bool unique_rows() const noexcept { return unique_rows_; }
    bool no_cell_revisions() const noexcept { return no_cell_revisions_; }

    env* get_env() const noexcept { return ns_->get_env(); }
    const namespace_ptr& get_namespace() const noexcept { return ns_; }
    const std::string& get_name() const noexcept { return name_; }
    const char* get_full_name() const noexcept
    {
        return key_name_.c_str() + key_classifiers::length;
    }
    const std::string& get_schema_spec() const noexcept { return schema_spec_; }
    std::int64_t get_id() const noexcept { return id_; }

    void get_table_schema(bool with_ids, std::string& schema_xml);

    std::shared_ptr<mutator> create_mutator();
    std::shared_ptr<scanner> create_scanner(const scan_spec& spec);

    /// Parses schema_spec_ on first use and caches it.
    const schema& get_schema();

    /// The parsed schema, shared: a mutator or scanner keeps the one it started from alive
    /// when an alter replaces the table's.
    std::shared_ptr<const schema> share_schema();

    /// The parsed schema, null once an alter has replaced it and it is yet to be parsed again.
    const schema* current_schema() const noexcept { return schema_.get(); }

    bool name_exists(bool& is_table, std::int64_t* rowid = nullptr);

    int to_key(const char*& psz) const;
    void to_record(dynamic_buffer& buf) const;
    void from_record(dynamic_buffer& buf);

    void open();
    /// Takes up the name a rename gave the table.
    void rename(const std::string& name) noexcept;

    /// Takes up the schema an alter stored.
    void refresh(const std::string& schema_xml, const std::shared_ptr<const schema>& sch) noexcept;
    void dispose();

private:
    void init();
    std::string to_key_name(const std::string& name) const;

    namespace_ptr ns_;
    std::string name_;
    std::string schema_spec_;
    std::shared_ptr<const schema> schema_;
    std::string key_name_;
    std::int64_t id_ = 0;
    ::sqlite3* db_ = nullptr;
    env* env_ = nullptr;
    bool unique_rows_ = false;
    bool no_cell_revisions_ = false;
};

class mutator final
{
public:
    explicit mutator(table_ptr t);
    ~mutator();

    mutator(const mutator&) = delete;
    mutator& operator=(const mutator&) = delete;

    env* get_env() const noexcept { return table_->get_env(); }

    void set(key_spec& ks, const void* value, std::uint32_t value_len);
    void set(const cells& cs);
    void del(key_spec& ks);
    void flush();

private:
    void insert(const key& k, const void* value, std::uint32_t value_len);
    void set(const key& k, const void* value, std::uint32_t value_len);
    void del(const key& k);

    /// Takes the table's schema up again once an alter has replaced it.
    void sync_schema();
    void adopt_schema(std::shared_ptr<const schema> sch);

    /// Acquires the statement for the sql specified from the environment pool, unless it has
    /// been acquired already. Mutators are created per unit of work, and most of them only
    /// ever insert - preparing all five statements up front costs more than the writes they
    /// carry. The sql has to be a format string taking the table id.
    void stmt_acquire(::sqlite3_stmt*& stmt, const char* sql);

    /// Resolves the column family to its id and settles the timestamp.
    void to_key(const char* row, int row_len, const char* cf, const char* cq, int cq_len,
                std::int64_t timestamp, std::int64_t revision, std::uint8_t flag,
                key& full_key) const;

    void to_key(const key_spec& ks, key& full_key) const
    {
        to_key(static_cast<const char*>(ks.row), static_cast<int>(ks.row_len), ks.column_family,
               ks.column_qualifier, static_cast<int>(ks.column_qualifier_len), ks.timestamp,
               ks.revision, ks.flag, full_key);
    }

    void to_key(const cell& c, key& full_key) const
    {
        to_key(c.row_key, c.row_key ? static_cast<int>(std::strlen(c.row_key)) : 0, c.column_family,
               c.column_qualifier,
               c.column_qualifier ? static_cast<int>(std::strlen(c.column_qualifier)) : 0,
               c.timestamp, c.revision, c.flag, full_key);
    }

    static constexpr std::uint32_t k_huge_value_length = 8 * 1024 * 1024;

    table_ptr table_;
    ::sqlite3* db_;
    env* env_;
    std::shared_ptr<const schema> schema_;
    /// The row delete, which names the schema's time-order-desc families.
    std::string delete_row_sql_;
    bool db_release_memory_ = false;
    /// Per column family, whether it stores complemented timestamps.
    std::array<bool, k_max_cf> time_order_asc_{};

    ::sqlite3_stmt* stmt_insert_ = nullptr;
    ::sqlite3_stmt* stmt_delete_row_ = nullptr;
    ::sqlite3_stmt* stmt_delete_cf_ = nullptr;
    ::sqlite3_stmt* stmt_delete_cell_ = nullptr;
    ::sqlite3_stmt* stmt_delete_cell_version_ = nullptr;
};

class scanner final
{
public:
    scanner(table_ptr t, const scan_spec& spec);
    ~scanner();

    scanner(const scanner&) = delete;
    scanner& operator=(const scanner&) = delete;

    env* get_env() const noexcept { return table_->get_env(); }

    /// False once the scan is exhausted. The cell points into scanner owned
    /// storage and stays valid until the next call.
    bool next_cell(cell& c);

private:
    /// Gives up the read transaction share, if the scanner holds one. Best effort: the
    /// transaction holds no mutator's writes, only expired cells deleted on the way, which
    /// the next scan deletes again.
    void end_read_tx() noexcept;

    /// Scan context that also composes the sql predicates while the requested
    /// columns are walked.
    class sql_scan_context final : public scan_context
    {
    public:
        sql_scan_context(const scan_spec& spec, const schema& sch) : scan_context(spec, sch) {}

        void initialize() override;

        std::string predicate;
        std::string cf_predicate;
        std::string q_predicate;
        std::string columns;

    protected:
        void initial_column(const column_family_spec& cf, bool has_qualifier, bool is_regexp,
                            bool is_prefix, const std::string& qualifier) override;
    };

    /// Base reader: one select over the whole table, plus all the per cell
    /// filtering, revision tracking and limit accounting.
    class reader
    {
    public:
        reader(table& t, const schema& sch, ::sqlite3* db, const scan_spec& spec);
        virtual ~reader();

        reader(const reader&) = delete;
        reader& operator=(const reader&) = delete;

        virtual void stmt_prepare();
        bool next_cell(cell& c);

        /// Whether the reader executes its query more than once, which is what
        /// makes a read transaction around the scan worthwhile.
        virtual bool multi_statement() const { return false; }

    protected:
        virtual bool move_next();
        virtual bool filter_row(const char* row);
        virtual const column_family_spec* filter_cell(const key& k);
        virtual void limit_reached() { eos_ = true; }

        bool get_cell(const key& k, const column_family_spec& cf, cell& c);

        /// Composes the select. The row constraint has to be parameterised
        /// (r=?, r>=? AND r<=? ..), otherwise the statement pool cannot recycle it.
        std::string select_sql(const char* row_constraint) const;

        /// Releases the current query statement, then acquires the one for sql.
        void stmt_acquire(const std::string& sql);

        env* env_;
        ::sqlite3* db_;
        std::int64_t table_id_;
        ::sqlite3_stmt* stmt_query_ = nullptr;
        std::unique_ptr<sql_scan_context> ctx_;
        std::int32_t row_count_ = 0;
        std::int32_t cell_count_ = 0;
        std::int32_t cell_per_family_count_ = 0;
        std::array<bool, k_max_cf> time_order_asc_{};
        bool no_cell_revisions_;

    private:
        bool check_cell_limits(const key& k);

        ::sqlite3_stmt* stmt_delete_cf_ = nullptr;
        dynamic_buffer curr_key_{64};
        dynamic_buffer prev_key_{64};
        int prev_column_family_code_ = -1;
        regexp_cache regexp_cache_;
        std::uint32_t revs_limit_ = 0;
        std::uint32_t revs_count_ = 0;
        bool eos_ = false;
    };

    /// Seeks the requested rows one at a time instead of scanning the range
    /// between them.
    class reader_scan_and_filter final : public reader
    {
    public:
        reader_scan_and_filter(table& t, const schema& sch, ::sqlite3* db, const scan_spec& spec);

        void stmt_prepare() override;
        bool multi_statement() const override { return rows_.size() > 1; }

    protected:
        bool move_next() override;

    private:
        /// The requested rows in ascending order. Points into the scan context's
        /// rowset, which outlives the reader.
        std::vector<const char*> rows_;
        std::size_t rows_index_ = 0;
        bool row_done_ = true;
    };

    class reader_row_intervals final : public reader
    {
    public:
        reader_row_intervals(table& t, const schema& sch, ::sqlite3* db, const scan_spec& spec);

        void stmt_prepare() override {} // one statement per interval, built in move_next
        bool multi_statement() const override { return spec_.row_intervals.size() > 1; }

    protected:
        bool move_next() override;
        void limit_reached() override
        {
            ++it_;
            row_interval_done_ = true;
        }

    private:
        const scan_spec& spec_;
        std::vector<row_interval>::const_iterator it_;
        bool row_interval_done_ = true;
    };

    class reader_cell_intervals final : public reader
    {
    public:
        reader_cell_intervals(table& t, const schema& sch, ::sqlite3* db, const scan_spec& spec);

        void stmt_prepare() override {}
        bool multi_statement() const override { return spec_.cell_intervals.size() > 1; }

    protected:
        bool move_next() override;
        bool filter_row(const char* row) override;
        const column_family_spec* filter_cell(const key& k) override;
        void limit_reached() override
        {
            ++it_;
            cell_interval_done_ = true;
        }

    private:
        const scan_spec& spec_;
        std::vector<cell_interval>::const_iterator it_;
        std::uint8_t start_column_family_code_ = 0;
        std::string start_column_qualifier_buf_;
        const char* start_column_qualifier_ = nullptr;
        std::uint8_t end_column_family_code_ = 0;
        std::string end_column_qualifier_buf_;
        const char* end_column_qualifier_ = nullptr;
        int cmp_start_ = 0;
        int cmp_end_ = 0;
        int cmp_start_row_ = 0;
        int cmp_end_row_ = 0;
        bool cell_interval_done_ = true;
    };

    table_ptr table_;
    ::sqlite3* db_;
    /// The schema the readers were composed from, alive for as long as they are.
    std::shared_ptr<const schema> schema_;
    std::unique_ptr<reader> reader_;
    /// Owned copy of the caller's scan spec. The readers keep pointers into it.
    std::unique_ptr<scan_spec> spec_;
    /// The share of a read transaction the scanner holds, see env::tx_read_begin.
    std::uint64_t read_tx_ = 0;
    /// Set once the scan has returned its last cell.
    bool exhausted_ = false;
    /// What the scan failed with, thrown again by every later next_cell.
    std::exception_ptr failure_;
};
} // namespace hyperlite::sqlite::db
