#include "pch.h"
#include "SQLiteDb.h"

#include <map>
#include <utility>

namespace hyperlite::sqlite::db
{
namespace
{
/// Doubles every single quote, for the qualifier literals that go into a
/// predicate. Replaces the boost::replace_all ht4c used.
std::string escape(std::string_view s)
{
    std::string out;
    out.reserve(s.size());
    for (const char c : s)
    {
        if (c == '\'')
        {
            out += "''";
        }
        else
        {
            out.push_back(c);
        }
    }
    return out;
}

/// True when key carries the classifier and the remainder starts with prefix.
bool key_starts_with(const void* key, char classifier, std::string_view prefix)
{
    const char* k = static_cast<const char*>(key);
    if (!k || *k != classifier)
    {
        return false;
    }
    k += key_classifiers::length;
    return std::strncmp(k, prefix.data(), prefix.size()) == 0;
}

/// Skips the classifier character, returning null when it does not match.
const char* key_to_string(const void* key, char classifier)
{
    const char* k = static_cast<const char*>(key);
    if (!k || *k != classifier)
    {
        return nullptr;
    }
    return k + key_classifiers::length;
}

/// Column qualifier as bound, never null.
const char* bind_cq(const char* cq) noexcept
{
    return cq ? cq : "";
}

/// The stored timestamps of the cells at or older than timestamp, as the bounds of a BETWEEN.
/// A time-order-asc family stores the complement, which turns older into greater.
std::pair<std::int64_t, std::int64_t> stored_through(bool time_order_asc,
                                                     std::int64_t timestamp) noexcept
{
    return time_order_asc ? std::pair{~timestamp, TIMESTAMP_MAX}
                          : std::pair{TIMESTAMP_MIN, timestamp};
}

/// Binds the bounds stored_through gives to the parameters at index and index + 1.
int bind_through(::sqlite3_stmt* stmt, int index, bool time_order_asc, std::int64_t timestamp)
{
    const auto [lower, upper] = stored_through(time_order_asc, timestamp);
    const int st = ::sqlite3_bind_int64(stmt, index, lower);
    return st != SQLITE_OK ? st : ::sqlite3_bind_int64(stmt, index + 1, upper);
}
} // namespace

//
// client
//

client::client(env_ptr e) : env_(std::move(e)), db_(env_->handle()) {}

void client::create_namespace(const std::string& name, bool create_intermediate)
{
    if (name.empty())
    { // root "/" always exists
        HYPERLITE_THROW(error::NAMESPACE_EXISTS, "Root namespace '/' always exists");
    }

    // the name whose creation failed, needed for the constraint diagnosis below
    std::string failed_name = name;

    try
    {
        util::tx tx(env_.get());

        if (create_intermediate)
        {
            std::string path = name;
            int created = 0;
            std::size_t at = 0;
            while ((at = path.find('/', at)) != std::string::npos)
            {
                const std::string parent = path.substr(0, at);
                if (created > 0 || !namespace_exists(parent))
                {
                    failed_name = parent;
                    create_one_namespace(parent);
                    ++created;
                }
                ++at;
            }
            if (created > 0 || !namespace_exists(path))
            {
                failed_name = path;
                create_one_namespace(path);
            }
        }
        else
        {
            const std::size_t at = name.rfind('/');
            if (at != std::string::npos)
            {
                const std::string parent = name.substr(0, at);
                if (!namespace_exists(parent))
                {
                    HYPERLITE_THROWF(error::NAMESPACE_DOES_NOT_EXIST,
                                     "Namespace '%s' does not exist", parent.c_str());
                }
            }
            create_one_namespace(name);
        }

        tx.commit();
    }
    catch (const exception& e)
    {
        // a UNIQUE(k) violation means the path is taken, by a namespace or a table.
        // ht4c distinguished the two so that the managed side raises
        // NamespaceExistsException or NameAlreadyInUseException accordingly.
        if (e.code() == error::EXTERNAL &&
            e.message().find("UNIQUE constraint failed") != std::string::npos)
        {
            name_space ns(shared_from_this(), failed_name);
            bool is_table = false;
            ns.name_exists(is_table);
            if (is_table)
            {
                HYPERLITE_THROWF(error::NAME_ALREADY_IN_USE, "Name '%s' already in use",
                                 failed_name.c_str());
            }
            HYPERLITE_THROWF(error::NAMESPACE_EXISTS, "Namespace '%s' already exists",
                             failed_name.c_str());
        }
        throw;
    }
}

namespace_ptr client::open_namespace(const std::string& name)
{
    auto ns = std::make_shared<name_space>(shared_from_this(), name);
    if (name.empty())
    { // root "/" always exists
        return ns;
    }
    if (!namespace_exists(name))
    {
        HYPERLITE_THROWF(error::NAMESPACE_DOES_NOT_EXIST, "Namespace '%s' does not exist",
                         name.c_str());
    }
    return ns;
}

bool client::namespace_exists(const char* name)
{
    if (!name || !*name)
    { // root "/" always exists
        return true;
    }
    name_space ns(shared_from_this(), name);
    bool is_table = false;
    return ns.name_exists(is_table) && !is_table;
}

void client::drop_namespace(const std::string& name, bool if_exists)
{
    util::tx tx(env_.get());
    drop_namespace_in_tx(name, if_exists);
    tx.commit();
}

void client::drop_namespace_in_tx(const std::string& name, bool if_exists)
{
    if (name.empty())
    { // root "/" always exists
        HYPERLITE_THROW(error::HYPERSPACE_DIR_NOT_EMPTY, "Cannot drop root namespace '/'");
    }

    name_space ns(shared_from_this(), name);

    {
        // the first key from the sub-tree's prefix on tells whether the sub-tree is empty, see
        // get_namespace_listing
        const std::string prefix = key_classifiers::namespace_listing_prefix + name + "/";
        ::sqlite3_stmt* stmt =
            env_->acquire_stmt("SELECT k FROM sys_db WHERE k>=? ORDER BY k LIMIT 1;");
        util::stmt_release release(env_.get(), stmt);

        int st =
            ::sqlite3_bind_text(stmt, 1, prefix.c_str(), static_cast<int>(prefix.size()), nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_step(stmt);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
        if (st == SQLITE_ROW)
        {
            const char* found = reinterpret_cast<const char*>(::sqlite3_column_text(stmt, 0));
            if (key_starts_with(found, key_classifiers::namespace_listing_prefix, name + "/"))
            {
                HYPERLITE_THROWF(error::HYPERSPACE_DIR_NOT_EMPTY, "Namespace '%s' is not empty",
                                 name.c_str());
            }
        }
    }

    const char* key = nullptr;
    const int len = ns.to_key(key);
    const bool dropped = env_->sys_delete(key, len);
    if (!if_exists && !dropped)
    {
        HYPERLITE_THROWF(error::NAMESPACE_DOES_NOT_EXIST, "Namespace '%s' does not exist",
                         name.c_str());
    }
}

void client::create_one_namespace(const std::string& name)
{
    name_space ns(shared_from_this(), name);
    const char* key = nullptr;
    const int len = ns.to_key(key);
    // a namespace row carries no value, which is what tells it from a table
    env_->sys_insert(key, len, nullptr, 0);
}

//
// namespace
//

name_space::name_space(client_ptr c, const std::string& name)
    : client_(std::move(c)), env_(client_->get_env()), db_(client_->get_env()->handle())
{
    key_name_.reserve(name.size() + 2);
    key_name_ += key_classifiers::namespace_listing_prefix;
    key_name_ += name;
}

int name_space::to_key(const char*& psz) const
{
    psz = key_name_.c_str();
    return static_cast<int>(key_name_.size()) + 1;
}

bool name_space::name_exists(bool& is_table, std::int64_t* rowid)
{
    const char* key = nullptr;
    const int len = to_key(key);
    dynamic_buffer buf;
    const bool exists = env_->sys_read(key, len, buf, rowid);
    is_table = buf.fill() > 0;
    return exists;
}

void name_space::get_namespace_listing(bool deep, std::vector<namespace_listing>& listing)
{
    listing.clear();

    // The keys are '0' + path and the query returns them ordered, so the whole sub-tree arrives
    // in one contiguous run - not depth first, "c-d" sorts between "c" and "c/d" - and the
    // entries are grouped by their parent's path below, so one pass is enough.
    //
    // The run starts at the sub-tree's prefix, '0' + path + '/', not after the namespace's own
    // key: a sibling whose name carries on with a character sorting below '/' - "app-test"
    // beside "app" - lies in between, and ended the walk before the sub-tree began. The root
    // has no key of its own and holds no table, so its prefix is '0' alone.
    ::sqlite3_stmt* stmt = env_->acquire_stmt("SELECT k, v FROM sys_db WHERE k>=? ORDER BY k;");
    util::stmt_release release(env_, stmt);

    std::string prefix(get_name());
    if (!prefix.empty())
    {
        prefix += "/";
    }
    const std::size_t prefix_len = prefix.size();

    const std::string lower = key_classifiers::namespace_listing_prefix + prefix;
    int st = ::sqlite3_bind_text(stmt, 1, lower.c_str(), static_cast<int>(lower.size()), nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    // relative path of the parent -> its entries
    std::map<std::string, std::vector<namespace_listing>> children;

    while ((st = ::sqlite3_step(stmt)) == SQLITE_ROW)
    {
        const char* name = key_to_string(::sqlite3_column_text(stmt, 0),
                                         key_classifiers::namespace_listing_prefix);
        if (!name || std::strncmp(name, prefix.c_str(), prefix_len) != 0)
        {
            break; // left the sub-tree
        }

        const char* rel = name + prefix_len;
        const char* sep = std::strrchr(rel, '/');
        if (sep && !deep)
        {
            continue; // a descendant, not a direct child
        }

        namespace_listing nsl;
        nsl.name = sep ? sep + 1 : rel;
        // a namespace row has no value, a table row has its schema
        nsl.is_namespace = ::sqlite3_column_type(stmt, 1) == SQLITE_NULL;
        children[sep ? std::string(rel, static_cast<std::size_t>(sep - rel)) : std::string()]
            .push_back(std::move(nsl));
    }
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    // a path sorts before any path it is a prefix of, so walking the map backwards
    // visits an entry only after its own children have been assembled
    for (auto it = children.rbegin(); it != children.rend(); ++it)
    {
        const std::string& parent = it->first;
        for (auto& entry : it->second)
        {
            const auto sub = children.find(parent.empty() ? entry.name : parent + "/" + entry.name);
            if (sub != children.end())
            {
                entry.sub_entries.swap(sub->second);
            }
        }
    }

    const auto root = children.find(std::string());
    if (root != children.end())
    {
        listing.swap(root->second);
    }
}

void name_space::drop(const std::string& ns_name, const std::vector<namespace_listing>& listing,
                      bool if_exists, bool drop_tables)
{
    util::tx tx(env_);
    drop_in_tx(ns_name, listing, if_exists, drop_tables);
    tx.commit();
}

void name_space::drop_in_tx(const std::string& ns_name,
                            const std::vector<namespace_listing>& listing, bool if_exists,
                            bool drop_tables)
{
    for (const auto& entry : listing)
    {
        if (entry.is_namespace)
        {
            const std::string sub_name = ns_name + "/" + entry.name;
            namespace_ptr sub = client_->open_namespace(sub_name);
            sub->drop_in_tx(sub_name, entry.sub_entries, if_exists, drop_tables);
            client_->drop_namespace_in_tx(sub_name, if_exists);
        }
        else if (drop_tables)
        {
            drop_table_in_tx(entry.name, true);
        }
    }
}

void name_space::create_table(const std::string& name, const std::string& schema_xml)
{
    if (name.empty())
    {
        HYPERLITE_THROW(error::HYPERSPACE_BAD_PATHNAME, "Empty table name");
    }

    util::tx tx(env_);

    // table already exists?
    {
        table probe(shared_from_this(), name);
        bool is_table = false;
        if (probe.name_exists(is_table))
        {
            if (is_table)
            {
                HYPERLITE_THROWF(error::MASTER_TABLE_EXISTS, "Table '%s' already exists",
                                 name.c_str());
            }
            HYPERLITE_THROWF(error::NAME_ALREADY_IN_USE, "Name '%s' already in use", name.c_str());
        }
    }

    // namespace exists?
    {
        bool is_table = false;
        if (!name_exists(is_table) || is_table)
        {
            HYPERLITE_THROWF(error::NAMESPACE_DOES_NOT_EXIST, "Namespace '%s' does not exist",
                             get_name());
        }
    }
    check_new_name(name);

    auto sch = schema::parse(schema_xml);
    sch->update_generation(static_cast<std::int64_t>(get_ts64()));

    const std::string final_schema = sch->render_xml(true);

    table new_table(shared_from_this(), name, final_schema, 0);
    const char* key = nullptr;
    const int len = new_table.to_key(key);
    dynamic_buffer buf;
    new_table.to_record(buf);
    std::int64_t id = 0;
    env_->sys_create_table(key, len, buf.base, static_cast<int>(buf.fill()), id);

    tx.commit();
}

table_ptr name_space::open_table(const std::string& name)
{
    util::tx tx(env_);

    auto t = std::make_shared<table>(shared_from_this(), name);
    t->open();

    tx.commit();
    return t;
}

void name_space::alter_table(const std::string& name, const std::string& schema_xml)
{
    if (name.empty())
    {
        HYPERLITE_THROW(error::HYPERSPACE_BAD_PATHNAME, "Empty table name");
    }

    util::tx tx(env_);

    // the table as stored: its schema, and the options its cell table was created with
    table current(shared_from_this(), name);
    std::int64_t rowid = 0;
    {
        const char* key = nullptr;
        const int len = current.to_key(key);
        dynamic_buffer record;
        if (!env_->sys_read(key, len, record, &rowid) || !record.fill())
        {
            HYPERLITE_THROWF(error::TABLE_NOT_FOUND, "Table '%s' does not exist", name.c_str());
        }
        current.from_record(record);
    }

    // carry the existing ids over, so a column family keeps its id across an alter
    const schema& existing = current.get_schema();
    auto sch = schema::parse(schema_xml);

    for (const column_family_spec* existing_cf : existing.get_column_families())
    {
        if (column_family_spec* cf = sch->get_column_family(existing_cf->get_name()))
        {
            cf->set_id(existing_cf->get_id());
        }
    }
    sch->update_generation(static_cast<std::int64_t>(get_ts64()));

    const std::string final_schema = sch->render_xml(true);

    // validated again, with the ids as they will be stored: carrying an id over can give a new
    // family's explicit id a second owner
    const std::shared_ptr<const schema> altered_schema = schema::parse(final_schema);

    const auto exec = [this](const std::string& sql)
    {
        char* errmsg = nullptr;
        const int st = ::sqlite3_exec(env_->handle(), sql.c_str(), nullptr, nullptr, &errmsg);
        HYPERLITE_SQLITE_VERIFY(st, env_->handle(), errmsg);
    };

    for (const column_family_spec* existing_cf : existing.get_column_families())
    {
        const int id = existing_cf->get_id();
        const column_family_spec* kept = altered_schema->get_column_family(id);
        if (!kept || kept->get_name() != existing_cf->get_name())
        {
            // A family the alter removes takes its cells with it. Left behind they could not
            // be read, and a family given the freed id later, explicitly or as the next one,
            // would return them as its own.
            exec(format("DELETE FROM t%lld WHERE cf=%d;", static_cast<long long>(rowid), id));
        }
        else if (kept->get_option_time_order_desc() != existing_cf->get_option_time_order_desc())
        {
            // A time-order-asc family stores the complement of its timestamps, so a family
            // changing its time order has its cells converted. By way of an id no family can
            // have: converted in place, a cell could meet one yet to be converted on the
            // table's unique key.
            const int parked = id + k_max_column_id + 1;
            exec(format("UPDATE t%lld SET cf=%d, ts=~ts WHERE cf=%d;",
                        static_cast<long long>(rowid), parked, id));
            exec(format("UPDATE t%lld SET cf=%d WHERE cf=%d;", static_cast<long long>(rowid), id,
                        parked));
        }
    }

    table altered(shared_from_this(), current, final_schema);
    dynamic_buffer buf;
    altered.to_record(buf);
    env_->sys_update_value(rowid, buf.base, static_cast<int>(buf.fill()));

    tx.commit();

    // every open handle on this table has to pick the new schema up, once it is the stored one
    env_->sys_refresh_table(rowid, final_schema, altered_schema);
}

void name_space::check_new_name(const std::string& name)
{
    // Only for a name being given, not for one being looked up: a table an older database holds
    // under such a name stays reachable to be opened, renamed or dropped.
    //
    // A qualified name has no empty segment: "a//t" was stored as such, then listed as "t",
    // which could not be opened.
    if (name.front() == '/' || name.find("//") != std::string::npos)
    {
        HYPERLITE_THROW(error::SYNTAX_ERROR, "Invalid table name");
    }

    // And it puts the table in a sub-namespace, which has to exist: without it the table was
    // never listed - nothing above it was - and the namespace holding it could never be
    // dropped, as it was never empty.
    const auto at = name.rfind('/');
    if (at == std::string::npos)
    {
        return;
    }

    std::string parent(get_name());
    if (!parent.empty())
    {
        parent += "/";
    }
    parent.append(name, 0, at);
    if (!client_->namespace_exists(parent))
    {
        HYPERLITE_THROWF(error::NAMESPACE_DOES_NOT_EXIST, "Namespace '%s' does not exist",
                         parent.c_str());
    }
}

bool name_space::table_exists(const std::string& name)
{
    if (name.empty())
    {
        return true;
    }
    table probe(shared_from_this(), name);
    bool is_table = false;
    return probe.name_exists(is_table) && is_table;
}

void name_space::get_table_id(const std::string& name, std::string& id)
{
    table probe(shared_from_this(), name);
    bool is_table = false;
    std::int64_t rowid = 0;
    if (!probe.name_exists(is_table, &rowid) || !is_table)
    {
        HYPERLITE_THROWF(error::TABLE_NOT_FOUND, "Table '%s' does not exist", name.c_str());
    }
    id = format("%lld", static_cast<long long>(rowid));
}

void name_space::get_table_schema(const std::string& name, bool with_ids, std::string& schema_xml)
{
    schema_xml.clear();

    table probe(shared_from_this(), name);
    const char* key = nullptr;
    const int len = probe.to_key(key);
    dynamic_buffer buf;
    if (!env_->sys_read(key, len, buf))
    {
        HYPERLITE_THROWF(error::TABLE_NOT_FOUND, "Table '%s' does not exist", name.c_str());
    }

    probe.from_record(buf);

    if (with_ids)
    {
        schema_xml = probe.get_schema_spec();
    }
    else
    {
        schema_xml = probe.get_schema().render_xml(false);
    }
}

void name_space::rename_table(const std::string& name, const std::string& new_name)
{
    if (name.empty())
    {
        HYPERLITE_THROW(error::HYPERSPACE_BAD_PATHNAME, "Empty table name");
    }
    if (new_name.empty())
    {
        HYPERLITE_THROW(error::HYPERSPACE_BAD_PATHNAME, "Empty new table name");
    }

    util::tx tx(env_);

    {
        table probe(shared_from_this(), new_name);
        bool is_table = false;
        if (probe.name_exists(is_table))
        {
            if (is_table)
            {
                HYPERLITE_THROWF(error::MASTER_TABLE_EXISTS, "Table '%s' already exists",
                                 new_name.c_str());
            }
            HYPERLITE_THROWF(error::NAME_ALREADY_IN_USE, "Name '%s' already in use",
                             new_name.c_str());
        }
    }

    table existing(shared_from_this(), name);
    bool is_table = false;
    std::int64_t rowid = 0;
    if (!existing.name_exists(is_table, &rowid) || !is_table)
    {
        HYPERLITE_THROWF(error::TABLE_NOT_FOUND, "Table '%s' does not exist", name.c_str());
    }
    check_new_name(new_name);

    table renamed(shared_from_this(), new_name, existing);
    const char* key = nullptr;
    const int len = renamed.to_key(key);
    env_->sys_update_key(rowid, key, len);

    tx.commit();

    // a handle open on the table looks it up by name, for its schema among others
    env_->sys_rename_table(rowid, new_name);
}

void name_space::drop_table(const std::string& name, bool if_exists)
{
    util::tx tx(env_);
    drop_table_in_tx(name, if_exists);
    tx.commit();
}

void name_space::drop_table_in_tx(const std::string& name, bool if_exists)
{
    if (name.empty())
    {
        HYPERLITE_THROW(error::HYPERSPACE_BAD_PATHNAME, "Empty table name");
    }

    table probe(shared_from_this(), name);
    const char* key = nullptr;
    const int len = probe.to_key(key);
    if (!env_->sys_delete_table(key, len) && !if_exists)
    {
        HYPERLITE_THROWF(error::HYPERSPACE_FILE_NOT_FOUND, "Table '%s' does not exist",
                         name.c_str());
    }
}

//
// table
//

table::table(namespace_ptr ns, const std::string& name)
    : ns_(std::move(ns)), name_(name), env_(ns_->get_env())
{
    init();
}

table::table(namespace_ptr ns, const std::string& name, const std::string& schema_xml,
             std::int64_t id)
    : ns_(std::move(ns)), name_(name), schema_spec_(schema_xml), id_(id), env_(ns_->get_env())
{
    init();
}

table::table(namespace_ptr ns, const std::string& name, const table& other)
    : ns_(std::move(ns)), name_(name), schema_spec_(other.schema_spec_), id_(other.id_),
      env_(ns_->get_env())
{
    init();
}

table::table(namespace_ptr ns, const table& other, const std::string& schema_xml)
    : ns_(std::move(ns)), name_(other.name_), schema_spec_(schema_xml), id_(other.id_),
      env_(ns_->get_env())
{
    // the options are the ones the cell table was created with, not the session's
    init();
    unique_rows_ = other.unique_rows_;
    no_cell_revisions_ = other.no_cell_revisions_;
}

void table::init()
{
    key_name_ = to_key_name(name_);

    unique_rows_ = env_->unique_rows();
    no_cell_revisions_ = env_->no_cell_revisions();
}

std::string table::to_key_name(const std::string& name) const
{
    std::string key_name;
    key_name.reserve(std::strlen(ns_->get_name()) + name.size() + 3);
    key_name += key_classifiers::namespace_listing_prefix;
    key_name += ns_->get_name();
    key_name += "/";
    key_name += name;
    return key_name;
}

void table::rename(const std::string& name) noexcept
{
    // composed aside and swapped in, so running out of memory leaves the old name whole
    try
    {
        std::string key_name = to_key_name(name);
        std::string new_name = name;
        name_.swap(new_name);
        key_name_.swap(key_name);
    }
    catch (...)
    {
    }
}

table::~table()
{
    try
    {
        dispose();
    }
    catch (...)
    {
    }
}

int table::to_key(const char*& psz) const
{
    psz = key_name_.c_str();
    return static_cast<int>(key_name_.size()) + 1;
}

void table::to_record(dynamic_buffer& buf) const
{
    if (schema_spec_.empty())
    {
        HYPERLITE_THROWF(error::MASTER_BAD_SCHEMA, "Undefined schema for table '%s'",
                         get_full_name());
    }

    std::string options;
    if (unique_rows_)
    {
        options += "{UR}";
    }
    if (no_cell_revisions_)
    {
        options += "{NCR}";
    }

    buf.clear();
    buf.reserve(schema_spec_.size() + 1 + (options.empty() ? 0 : options.size() + 1));
    buf.add_cstr(schema_spec_);
    if (!options.empty())
    {
        buf.add_cstr(options);
    }
}

void table::from_record(dynamic_buffer& buf)
{
    if (buf.fill())
    {
        schema_spec_ = reinterpret_cast<const char*>(buf.base);
        schema_.reset();

        // the options are the table's own: a record without them is a table created with
        // neither, whatever the session has set
        unique_rows_ = false;
        no_cell_revisions_ = false;
        if (buf.fill() > schema_spec_.size() + 1)
        {
            const std::string options(
                reinterpret_cast<const char*>(buf.base + schema_spec_.size() + 1));
            unique_rows_ = options.find("{UR}") != std::string::npos;
            no_cell_revisions_ = options.find("{NCR}") != std::string::npos;
        }
    }
    else
    {
        schema_spec_.clear();
        schema_.reset();
        dispose();
    }
}

void table::get_table_schema(bool with_ids, std::string& schema_xml)
{
    ns_->get_table_schema(name_, with_ids, schema_xml);
}

const schema& table::get_schema()
{
    if (!schema_)
    {
        schema_ = schema::parse(schema_spec_);
    }
    return *schema_;
}

std::shared_ptr<const schema> table::share_schema()
{
    get_schema();
    return schema_;
}

bool table::name_exists(bool& is_table, std::int64_t* rowid)
{
    const char* key = nullptr;
    const int len = to_key(key);
    dynamic_buffer buf;
    const bool exists = env_->sys_read(key, len, buf, rowid);
    is_table = buf.fill() > 0;
    return exists;
}

void table::open()
{
    dispose();
    if (!env_->sys_open_table(this, id_))
    {
        HYPERLITE_THROWF(error::HYPERSPACE_FILE_NOT_FOUND, "Table '%s' does not exist",
                         name_.c_str());
    }
    db_ = env_->handle();
}

void table::refresh(const std::string& schema_xml,
                    const std::shared_ptr<const schema>& sch) noexcept
{
    // Handed the schema rather than looking it up by name: a handle keeps the name it was opened
    // with, which a rename may have given up since. It cannot fail either, the alter it follows
    // has been committed.
    if (db_)
    {
        schema_ = sch;
        try
        {
            schema_spec_ = schema_xml;
        }
        catch (...)
        {
            // the parsed schema is what the handle reads
        }
    }
}

void table::dispose()
{
    if (db_)
    {
        env_->sys_dispose_table(id_, this);
        db_ = nullptr;
    }
    id_ = 0;
}

std::shared_ptr<mutator> table::create_mutator()
{
    if (!id_)
    {
        HYPERLITE_THROWF(error::TABLE_NOT_FOUND, "Invalid identifier for table '%s'",
                         get_full_name());
    }
    if (!db_)
    {
        HYPERLITE_THROWF(error::TABLE_NOT_FOUND, "Table '%s' already disposed", get_full_name());
    }
    return std::make_shared<mutator>(shared_from_this());
}

std::shared_ptr<scanner> table::create_scanner(const scan_spec& spec)
{
    if (!id_)
    {
        HYPERLITE_THROWF(error::TABLE_NOT_FOUND, "Invalid identifier for table '%s'",
                         get_full_name());
    }
    if (!db_)
    {
        HYPERLITE_THROWF(error::TABLE_NOT_FOUND, "Table '%s' already disposed", get_full_name());
    }
    return std::make_shared<scanner>(shared_from_this(), spec);
}

//
// mutator
//

mutator::mutator(table_ptr t)
    : table_(std::move(t)), db_(table_->get_env()->handle()), env_(table_->get_env())
{
    adopt_schema(table_->share_schema());

    // the statements are acquired on first use, see stmt_acquire
}

void mutator::sync_schema()
{
    // An alter replaced the table's schema. The mutator follows it, as a Hypertable mutator
    // does on a generation mismatch: a family it removed is refused from here on, and one
    // whose time order it changed is written the new way.
    if (table_->current_schema() != schema_.get())
    {
        adopt_schema(table_->share_schema());
    }
}

void mutator::adopt_schema(std::shared_ptr<const schema> sch)
{
    schema_ = std::move(sch);
    time_order_asc_.fill(true);
    std::string desc;
    for (const column_family_spec* cf : schema_->get_column_families())
    {
        time_order_asc_[static_cast<std::size_t>(cf->get_id())] = !cf->get_option_time_order_desc();
        if (cf->get_option_time_order_desc())
        {
            desc += format("%s%d", desc.empty() ? "" : ",", cf->get_id());
        }
    }

    // A row spans the families, and older is a different range of stored timestamps in a
    // time-order-desc family. The statement names those, so it goes with the schema.
    delete_row_sql_ = "DELETE FROM t%lld WHERE r=?1 AND (cf IN (" + desc +
                      ") AND ts BETWEEN ?4 AND ?5 OR cf NOT IN (" + desc +
                      ") AND ts BETWEEN ?2 AND ?3);";
    env_->release_stmt(stmt_delete_row_);
}

void mutator::stmt_acquire(::sqlite3_stmt*& stmt, const char* sql)
{
    if (!stmt)
    {
        stmt = env_->acquire_stmt(format(sql, static_cast<long long>(table_->get_id())));
    }
}

mutator::~mutator()
{
    try
    {
        // back to the pool rather than finalised, the next mutator on this table reuses them
        env_->release_stmt(stmt_insert_);
        env_->release_stmt(stmt_delete_row_);
        env_->release_stmt(stmt_delete_cf_);
        env_->release_stmt(stmt_delete_cell_);
        env_->release_stmt(stmt_delete_cell_version_);

        // a single huge value can leave sqlite holding a page cache far larger than
        // the configured one, so hand it back
        if (db_release_memory_)
        {
            ::sqlite3_db_release_memory(db_);
        }
    }
    catch (...)
    {
    }
}

void mutator::to_key(const char* row, int row_len, const char* cf, const char* cq, int cq_len,
                     std::int64_t timestamp, std::int64_t revision, std::uint8_t flag,
                     key& full_key) const
{
    if (flag > FLAG_DELETE_ROW)
    {
        if (!cf)
        {
            HYPERLITE_THROW(error::BAD_KEY, "Column family not specified");
        }
        const column_family_spec* spec = schema_->get_column_family(cf);
        if (!spec)
        {
            HYPERLITE_THROWF(error::BAD_KEY, "Bad column family '%s'", cf);
        }
        full_key.column_family_code = static_cast<std::uint8_t>(spec->get_id());
    }
    else
    {
        full_key.column_family_code = 0;
    }

    if (timestamp == AUTO_ASSIGN)
    {
        timestamp = static_cast<std::int64_t>(get_ts64());
    }

    full_key.row = row;
    full_key.row_len = static_cast<std::uint32_t>(row_len);
    full_key.column_qualifier = cq;
    full_key.column_qualifier_len = static_cast<std::uint32_t>(cq_len);
    full_key.timestamp = timestamp;
    full_key.revision = revision;
    full_key.flag = flag;
}

void mutator::set(key_spec& ks, const void* value, std::uint32_t value_len)
{
    if (!ks.row)
    {
        HYPERLITE_THROW(error::BAD_KEY, "Invalid row key - not specified");
    }
    if (ks.row_len == 0)
    {
        HYPERLITE_THROW(error::BAD_KEY, "Invalid row key - empty");
    }

    sync_schema();
    key k;
    to_key(ks, k);
    set(k, value, value_len);
}

void mutator::set(const cells& cs)
{
    sync_schema();
    for (const cell& c : cs)
    {
        if (!c.row_key || !*c.row_key)
        {
            HYPERLITE_THROW(error::BAD_KEY, "Invalid row key - not specified");
        }
        key k;
        to_key(c, k);
        set(k, c.value, c.value_len);
    }
}

void mutator::del(key_spec& ks)
{
    if (!ks.row)
    {
        HYPERLITE_THROW(error::BAD_KEY, "Invalid row key - not specified");
    }
    if (ks.row_len == 0)
    {
        HYPERLITE_THROW(error::BAD_KEY, "Invalid row key - empty");
    }

    sync_schema();
    key k;
    to_key(ks, k);

    if (k.flag == FLAG_INSERT)
    {
        HYPERLITE_THROWF(error::BAD_KEY, "Invalid delete flag '%d'", static_cast<int>(k.flag));
    }
    del(k);
}

void mutator::flush()
{
    env_->tx_flush();
}

void mutator::set(const key& k, const void* value, std::uint32_t value_len)
{
    if (k.flag == FLAG_INSERT)
    {
        insert(k, value, value_len);
    }
    else
    {
        del(k);
    }
}

void mutator::insert(const key& k, const void* value, std::uint32_t value_len)
{
    env_->tx_ensure();

    stmt_acquire(stmt_insert_,
                 "INSERT OR REPLACE INTO t%lld (r, cf, cq, ts, v) VALUES(?, ?, ?, ?, ?);");

    util::stmt_reset reset(stmt_insert_);

    int st = ::sqlite3_bind_text(stmt_insert_, 1, k.row, static_cast<int>(k.row_len), nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_bind_int(stmt_insert_, 2, k.column_family_code);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_bind_text(stmt_insert_, 3, bind_cq(k.column_qualifier),
                             static_cast<int>(k.column_qualifier_len), nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    // a time-order-asc family stores the complement, so ascending sql order gives
    // newest first
    st = ::sqlite3_bind_int64(stmt_insert_, 4,
                              time_order_asc_[k.column_family_code] ? ~k.timestamp : k.timestamp);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_bind_blob(stmt_insert_, 5, value, static_cast<int>(value_len), nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_step(stmt_insert_);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
    env_->tx_wrote();

    if (!db_release_memory_)
    {
        db_release_memory_ = value_len >= k_huge_value_length;
    }
}

void mutator::del(const key& k)
{
    // a delete takes the cells at or older than its timestamp, a cell version delete the one at it
    const bool time_order_asc = time_order_asc_[k.column_family_code];

    env_->tx_ensure();

    int st = SQLITE_OK;
    switch (k.flag)
    {
    case FLAG_DELETE_ROW:
    {
        stmt_acquire(stmt_delete_row_, delete_row_sql_.c_str());

        util::stmt_reset reset(stmt_delete_row_);

        st = ::sqlite3_bind_text(stmt_delete_row_, 1, k.row, static_cast<int>(k.row_len), nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = bind_through(stmt_delete_row_, 2, true, k.timestamp);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = bind_through(stmt_delete_row_, 4, false, k.timestamp);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_step(stmt_delete_row_);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
        break;
    }

    case FLAG_DELETE_COLUMN_FAMILY:
    {
        stmt_acquire(stmt_delete_cf_,
                     "DELETE FROM t%lld WHERE r=? AND cf=? AND ts BETWEEN ? AND ?;");

        util::stmt_reset reset(stmt_delete_cf_);

        st = ::sqlite3_bind_text(stmt_delete_cf_, 1, k.row, static_cast<int>(k.row_len), nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_bind_int(stmt_delete_cf_, 2, k.column_family_code);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = bind_through(stmt_delete_cf_, 3, time_order_asc, k.timestamp);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_step(stmt_delete_cf_);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
        break;
    }

    case FLAG_DELETE_CELL:
    {
        stmt_acquire(stmt_delete_cell_,
                     "DELETE FROM t%lld WHERE r=? AND cf=? AND cq=? AND ts BETWEEN ? AND ?;");

        util::stmt_reset reset(stmt_delete_cell_);

        st = ::sqlite3_bind_text(stmt_delete_cell_, 1, k.row, static_cast<int>(k.row_len), nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_bind_int(stmt_delete_cell_, 2, k.column_family_code);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_bind_text(stmt_delete_cell_, 3, bind_cq(k.column_qualifier),
                                 static_cast<int>(k.column_qualifier_len), nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = bind_through(stmt_delete_cell_, 4, time_order_asc, k.timestamp);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_step(stmt_delete_cell_);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
        break;
    }

    case FLAG_DELETE_CELL_VERSION:
    {
        stmt_acquire(stmt_delete_cell_version_,
                     "DELETE FROM t%lld WHERE r=? AND cf=? AND cq=? AND ts=?;");

        util::stmt_reset reset(stmt_delete_cell_version_);

        st = ::sqlite3_bind_text(stmt_delete_cell_version_, 1, k.row, static_cast<int>(k.row_len),
                                 nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_bind_int(stmt_delete_cell_version_, 2, k.column_family_code);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_bind_text(stmt_delete_cell_version_, 3, bind_cq(k.column_qualifier),
                                 static_cast<int>(k.column_qualifier_len), nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_bind_int64(stmt_delete_cell_version_, 4,
                                  time_order_asc ? ~k.timestamp : k.timestamp);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_step(stmt_delete_cell_version_);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
        break;
    }

    default:
        return;
    }

    // each case is one statement, and reaching here means it succeeded
    env_->tx_wrote();
}

//
// scanner
//

void scanner::sql_scan_context::initialize()
{
    scan_context::initialize();

    // The stored timestamp is complemented for a time-order-asc family and plain
    // for a desc one, so a single sql bound can only be expressed when the table
    // uses one order throughout. With both present the bound is dropped here and
    // filter_cell does the work per cell, exactly as ht4c did.
    bool has_asc = false;
    bool has_desc = false;
    for (const column_family_spec* cf : sch.get_column_families())
    {
        if (!cf->get_option_time_order_desc())
        {
            has_asc = true;
        }
        else
        {
            has_desc = true;
        }
        if (has_asc && has_desc)
        {
            break;
        }
    }

    std::string predicate_timestamp;
    if (time_interval.first > TIMESTAMP_MIN && has_asc != has_desc)
    {
        predicate_timestamp = has_asc
                                  ? format("ts<=%lld", static_cast<long long>(~time_interval.first))
                                  : format("ts>=%lld", static_cast<long long>(time_interval.first));
    }

    if (time_interval.second < TIMESTAMP_MAX && has_asc != has_desc)
    {
        const char* sep = predicate_timestamp.empty() ? "" : " AND ";
        predicate_timestamp +=
            has_asc ? format("%sts>%lld", sep, static_cast<long long>(~time_interval.second))
                    : format("%sts<%lld", sep, static_cast<long long>(time_interval.second));
    }

    if (!cf_predicate.empty())
    {
        predicate = q_predicate.empty()
                        ? format("cf IN (%s)", cf_predicate.c_str())
                        : format("cf IN (%s) OR %s", cf_predicate.c_str(), q_predicate.c_str());
    }
    else
    {
        predicate = q_predicate;
    }

    if (!predicate_timestamp.empty())
    {
        predicate = predicate.empty()
                        ? predicate_timestamp
                        : format("%s AND (%s)", predicate_timestamp.c_str(), predicate.c_str());
    }

    columns = "r, cf, cq, ts";
    if (!keys_only)
    {
        columns += ", v";
    }
}

void scanner::sql_scan_context::initial_column(const column_family_spec& cf, bool has_qualifier,
                                               bool is_regexp, bool is_prefix,
                                               const std::string& qualifier)
{
    if (!has_qualifier || is_regexp)
    {
        // a regexp qualifier cannot be pushed down, the whole family is read
        cf_predicate += format("%s'%d'", cf_predicate.empty() ? "" : ",", cf.get_id());
    }
    else if (is_prefix)
    {
        // Deliberate deviation: ht4c emitted cq>=%s without quotes, so "d:^1"
        // became cq>=1, comparing text against an integer. Under sqlite's type
        // ordering text always sorts above an integer, which made the bound
        // vacuously true and left all the work to qualifier_matches. Quoting it
        // turns it into a real lower bound. cq >= 'p' is a necessary condition for
        // "starts with p", so the result set is unchanged and fewer rows are read.
        q_predicate += format("%s(cf=%d AND cq>='%s')", q_predicate.empty() ? "" : " OR ",
                              cf.get_id(), escape(qualifier).c_str());
    }
    else
    {
        q_predicate += format("%s(cf=%d AND cq='%s')", q_predicate.empty() ? "" : " OR ",
                              cf.get_id(), escape(qualifier).c_str());
    }
}

//
// reader
//

scanner::reader::reader(table& t, const schema& sch, ::sqlite3* db, const scan_spec& spec)
    : env_(t.get_env()), db_(db), table_id_(t.get_id()),
      ctx_(std::make_unique<sql_scan_context>(spec, sch)), no_cell_revisions_(t.no_cell_revisions())
{
    ctx_->initialize();

    time_order_asc_.fill(true);
    bool has_ttl = false;
    for (const column_family_spec* cf : ctx_->sch.get_column_families())
    {
        time_order_asc_[static_cast<std::size_t>(cf->get_id())] = !cf->get_option_time_order_desc();
        if (cf->get_option_ttl() != 0)
        {
            has_ttl = true;
        }
    }

    // Expired cells get removed as the scan walks over them, unless the database is read only:
    // there the delete would fail the scan, so they are only skipped.
    if (has_ttl && !env_->read_only())
    {
        const int st = ::sqlite3_prepare_v2(
            db_,
            format("DELETE FROM t%lld WHERE r=? AND cf=? AND ts BETWEEN ? AND ?;",
                   static_cast<long long>(table_id_))
                .c_str(),
            -1, &stmt_delete_cf_, nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
    }
}

scanner::reader::~reader()
{
    try
    {
        env_->release_stmt(stmt_query_);
        if (stmt_delete_cf_)
        {
            util::stmt_finalize(db_, &stmt_delete_cf_);
        }
    }
    catch (...)
    {
    }
}

std::string scanner::reader::select_sql(const char* row_constraint) const
{
    std::string sql("SELECT ");
    sql += ctx_->columns;
    sql += format(" FROM t%lld", static_cast<long long>(table_id_));

    if (row_constraint && *row_constraint)
    {
        sql += " WHERE (";
        sql += row_constraint;
        sql += ")";
        if (!ctx_->predicate.empty())
        {
            sql += " AND (";
            sql += ctx_->predicate;
            sql += ")";
        }
    }
    else if (!ctx_->predicate.empty())
    {
        sql += " WHERE ";
        sql += ctx_->predicate;
    }

    sql += no_cell_revisions_ ? " ORDER BY r, cf, cq;" : " ORDER BY r, cf, cq, ts;";
    return sql;
}

void scanner::reader::stmt_acquire(const std::string& sql)
{
    env_->release_stmt(stmt_query_);
    stmt_query_ = env_->acquire_stmt(sql);
}

void scanner::reader::stmt_prepare()
{
    stmt_acquire(select_sql(nullptr));
}

bool scanner::reader::move_next()
{
    const int st = ::sqlite3_step(stmt_query_);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
    return st == SQLITE_ROW;
}

bool scanner::reader::next_cell(cell& c)
{
    key k;
    while (!eos_)
    {
        if (!move_next())
        {
            // Exhausted, and it has to stay exhausted. Stepping a statement which already
            // returned SQLITE_DONE runs its query a second time and would hand out the same
            // cells again, and the interval readers would read through an iterator sitting
            // at end().
            eos_ = true;
            break;
        }

        k.row = reinterpret_cast<const char*>(::sqlite3_column_text(stmt_query_, 0));
        k.row_len = static_cast<std::uint32_t>(::sqlite3_column_bytes(stmt_query_, 0));
        if (!filter_row(k.row))
        {
            continue;
        }

        k.column_family_code = static_cast<std::uint8_t>(::sqlite3_column_int(stmt_query_, 1));
        k.column_qualifier = reinterpret_cast<const char*>(::sqlite3_column_text(stmt_query_, 2));
        k.column_qualifier_len = static_cast<std::uint32_t>(::sqlite3_column_bytes(stmt_query_, 2));
        const std::int64_t stored = ::sqlite3_column_int64(stmt_query_, 3);
        k.timestamp = time_order_asc_[k.column_family_code] ? ~stored : stored;

        if (const column_family_spec* cf = filter_cell(k))
        {
            if (get_cell(k, *cf, c))
            {
                return true;
            }
        }
    }
    return false;
}

bool scanner::reader::filter_row(const char* row)
{
    // requested row set, consumed in order as the scan advances
    if (!ctx_->rowset.empty())
    {
        int cmp = 1;
        while (!ctx_->rowset.empty() && (cmp = std::strcmp(ctx_->rowset.begin()->c_str(), row)) < 0)
        {
            ctx_->rowset.erase(ctx_->rowset.begin());
        }
        if (ctx_->rowset.empty() || cmp > 0)
        {
            return false;
        }
    }

    if (ctx_->row_regexp)
    {
        bool cached = false;
        bool match = false;
        regexp_cache_.check_row(row, &cached, &match);
        if (!cached)
        {
            match = ctx_->row_regexp->matches(row, std::strlen(row));
            regexp_cache_.set_row(row, match);
        }
        if (!match)
        {
            return false;
        }
    }

    return true;
}

const column_family_spec* scanner::reader::filter_cell(const key& k)
{
    if (!ctx_->family_mask[k.column_family_code])
    {
        return nullptr;
    }

    cell_filter_info& cfi = ctx_->family_info(k.column_family_code);

    // ttl cutoff: the cell has expired, drop it from the table as we pass
    if (k.timestamp < cfi.cutoff_time)
    {
        if (stmt_delete_cf_)
        {
            util::stmt_reset reset(stmt_delete_cf_);

            int st = ::sqlite3_bind_text(stmt_delete_cf_, 1, k.row, static_cast<int>(k.row_len),
                                         nullptr);
            HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

            st = ::sqlite3_bind_int(stmt_delete_cf_, 2, k.column_family_code);
            HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

            st = bind_through(stmt_delete_cf_, 3, time_order_asc_[k.column_family_code],
                              k.timestamp);
            HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

            st = ::sqlite3_step(stmt_delete_cf_);
            HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
        }
        return nullptr;
    }

    if (k.timestamp < ctx_->time_interval.first || k.timestamp >= ctx_->time_interval.second)
    {
        return nullptr;
    }

    // revision, row offset and row limit accounting
    if (ctx_->row_offset || ctx_->row_limit || cfi.max_versions)
    {
        curr_key_.clear();
        curr_key_.add(k.row, k.row_len + 1);
        curr_key_.add(&k.column_family_code, sizeof(k.column_family_code));
        curr_key_.add(bind_cq(k.column_qualifier), k.column_qualifier_len + 1);

        if (!prev_key_.fill() ||
            std::strcmp(reinterpret_cast<const char*>(prev_key_.base), k.row) != 0)
        {
            // row changed
            prev_key_.set(curr_key_.base, curr_key_.fill());
            revs_count_ = 0;
            revs_limit_ = cfi.max_versions;
            cell_per_family_count_ = 0;
            ++row_count_;
        }
        else if (prev_key_.fill() != curr_key_.fill() ||
                 std::memcmp(curr_key_.base, prev_key_.base, curr_key_.fill()) != 0)
        {
            // cell changed within the same row
            prev_key_.set(curr_key_.base, curr_key_.fill());
            revs_count_ = 0;
            revs_limit_ = cfi.max_versions;
        }

        if (ctx_->row_offset && row_count_ <= ctx_->row_offset)
        {
            return nullptr;
        }
        if (ctx_->row_limit && row_count_ > ctx_->row_limit)
        {
            limit_reached();
            return nullptr;
        }

        ++revs_count_;
        if (revs_limit_ && revs_count_ > revs_limit_)
        {
            return nullptr;
        }
    }

    if (cfi.has_qualifier_regexp_filter())
    {
        bool cached = false;
        bool match = false;
        regexp_cache_.check_column(k.column_family_code, bind_cq(k.column_qualifier), &cached,
                                   &match);
        if (!cached)
        {
            match = cfi.qualifier_matches(k.column_qualifier, k.column_qualifier_len);
            regexp_cache_.set_column(k.column_family_code, bind_cq(k.column_qualifier), match);
        }
        if (!match)
        {
            return nullptr;
        }
    }
    else if (!cfi.qualifier_matches(k.column_qualifier, k.column_qualifier_len))
    {
        return nullptr;
    }

    return ctx_->column_families[k.column_family_code];
}

bool scanner::reader::get_cell(const key& k, const column_family_spec& cf, cell& c)
{
    // With a value regexp the cell limits must only be charged once the value has
    // passed, otherwise a filtered out cell would consume a slot. Without one the
    // limits are charged up front, which lets a keys-only scan skip reading the
    // blob entirely.
    if (!ctx_->value_regexp && !check_cell_limits(k))
    {
        return false;
    }

    cell_filter_info& cfi = ctx_->family_info(k.column_family_code);

    c.row_key = k.row;
    c.row_key_len = static_cast<std::int32_t>(k.row_len);
    c.column_family = cf.get_name().c_str();
    c.column_family_len = static_cast<std::int32_t>(cf.get_name().size());
    c.column_qualifier = k.column_qualifier;
    c.column_qualifier_len = static_cast<std::int32_t>(k.column_qualifier_len);
    c.timestamp = k.timestamp;
    c.revision = k.revision;
    c.flag = k.flag;
    c.value = nullptr;
    c.value_len = 0;

    if (!ctx_->keys_only || ctx_->value_regexp)
    {
        const void* v = ::sqlite3_column_blob(stmt_query_, 4);
        const std::uint32_t len =
            static_cast<std::uint32_t>(::sqlite3_column_bytes(stmt_query_, 4));

        if (cfi.has_column_predicate_filter() && !cfi.column_predicate_matches(v, len))
        {
            return false;
        }

        if (ctx_->value_regexp)
        {
            if (!ctx_->value_regexp->matches(static_cast<const char*>(v), len))
            {
                return false;
            }
            if (!check_cell_limits(k))
            {
                return false;
            }
        }

        if (!ctx_->keys_only)
        {
            c.value = static_cast<const std::uint8_t*>(v);
            c.value_len = len;
        }
    }

    return true;
}

bool scanner::reader::check_cell_limits(const key& k)
{
    ++cell_count_;

    if (prev_column_family_code_ != k.column_family_code)
    {
        prev_column_family_code_ = k.column_family_code;
        cell_per_family_count_ = 1;
    }
    else
    {
        ++cell_per_family_count_;
    }

    if (ctx_->cell_offset && cell_count_ <= ctx_->cell_offset)
    {
        return false;
    }
    if (ctx_->cell_limit && cell_count_ > ctx_->cell_limit)
    {
        limit_reached();
        return false;
    }
    if (ctx_->cell_limit_per_family && cell_per_family_count_ > ctx_->cell_limit_per_family)
    {
        return false;
    }
    return true;
}

//
// reader_scan_and_filter
//

scanner::reader_scan_and_filter::reader_scan_and_filter(table& t, const schema& sch, ::sqlite3* db,
                                                        const scan_spec& spec)
    : reader(t, sch, db, spec)
{
    // The row set is sorted, so seeking the rows one by one yields them in the
    // very same order a 'r>=min AND r<=max' range scan did, without reading
    // everything in between only to discard it in filter_row.
    rows_.reserve(ctx_->rowset.size());
    for (const std::string& row : ctx_->rowset)
    {
        rows_.push_back(row.c_str());
    }
}

void scanner::reader_scan_and_filter::stmt_prepare()
{
    stmt_acquire(select_sql("r=?"));
}

bool scanner::reader_scan_and_filter::move_next()
{
    for (;;)
    {
        if (row_done_)
        {
            if (rows_index_ >= rows_.size())
            {
                return false;
            }

            // the statement is reused for every row, hence the explicit reset
            ::sqlite3_reset(stmt_query_);

            const int st = ::sqlite3_bind_text(stmt_query_, 1, rows_[rows_index_++], -1, nullptr);
            HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
            row_done_ = false;
        }

        const int st = ::sqlite3_step(stmt_query_);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        if (st != SQLITE_DONE)
        {
            return st == SQLITE_ROW;
        }
        row_done_ = true;
    }
}

//
// reader_row_intervals
//

scanner::reader_row_intervals::reader_row_intervals(table& t, const schema& sch, ::sqlite3* db,
                                                    const scan_spec& spec)
    : reader(t, sch, db, spec), spec_(spec), it_(spec.row_intervals.begin())
{
}

bool scanner::reader_row_intervals::move_next()
{
    // iterative: one stack frame per exhausted interval used to be one recursion
    for (;;)
    {
        if (row_interval_done_)
        {
            row_interval_done_ = false;
            if (it_ == spec_.row_intervals.end())
            {
                return false;
            }

            // the limits are per interval
            row_count_ = 0;
            cell_count_ = 0;
            cell_per_family_count_ = 0;

            int st = SQLITE_OK;
            if (std::strcmp(it_->start, it_->end) != 0)
            {
                if (*it_->start && *it_->end)
                {
                    stmt_acquire(
                        select_sql(it_->start_inclusive
                                       ? (it_->end_inclusive ? "r>=? AND r<=?" : "r>=? AND r<?")
                                       : (it_->end_inclusive ? "r>? AND r<=?" : "r>? AND r<?")));

                    st = ::sqlite3_bind_text(stmt_query_, 1, it_->start, -1, nullptr);
                    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

                    st = ::sqlite3_bind_text(stmt_query_, 2, it_->end, -1, nullptr);
                    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
                }
                else if (*it_->end)
                {
                    stmt_acquire(select_sql(it_->end_inclusive ? "r<=?" : "r<?"));

                    st = ::sqlite3_bind_text(stmt_query_, 1, it_->end, -1, nullptr);
                    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
                }
                else
                {
                    stmt_acquire(select_sql(it_->start_inclusive ? "r>=?" : "r>?"));

                    st = ::sqlite3_bind_text(stmt_query_, 1, it_->start, -1, nullptr);
                    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
                }
            }
            else
            {
                stmt_acquire(select_sql("r=?"));

                st = ::sqlite3_bind_text(stmt_query_, 1, it_->start, -1, nullptr);
                HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
            }
        }

        const int st = ::sqlite3_step(stmt_query_);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        if (st != SQLITE_DONE)
        {
            return st == SQLITE_ROW;
        }

        ++it_;
        row_interval_done_ = true;
    }
}

//
// reader_cell_intervals
//

scanner::reader_cell_intervals::reader_cell_intervals(table& t, const schema& sch, ::sqlite3* db,
                                                      const scan_spec& spec)
    : reader(t, sch, db, spec), spec_(spec), it_(spec.cell_intervals.begin())
{
}

bool scanner::reader_cell_intervals::move_next()
{
    for (;;)
    {
        if (cell_interval_done_)
        {
            cell_interval_done_ = false;
            if (it_ == spec_.cell_intervals.end())
            {
                return false;
            }

            row_count_ = 0;
            cell_count_ = 0;
            cell_per_family_count_ = 0;

            cmp_start_ = it_->start_inclusive ? 0 : 1;
            cmp_end_ = it_->end_inclusive ? 0 : -1;

            {
                // assigned even when empty, or the previous interval's qualifier would stay
                const parsed_column parsed = parse_column(it_->start_column);
                start_column_qualifier_buf_ = parsed.qualifier;
                const column_family_spec* cf = ctx_->sch.get_column_family(parsed.family);
                if (!cf)
                {
                    HYPERLITE_THROWF(error::BAD_SCAN_SPEC, "Column family '%s' does not exist",
                                     parsed.family.c_str());
                }
                start_column_family_code_ = static_cast<std::uint8_t>(cf->get_id());
                start_column_qualifier_ = parsed.has_qualifier && !parsed.is_regexp
                                              ? start_column_qualifier_buf_.c_str()
                                              : nullptr;
            }

            {
                const parsed_column parsed = parse_column(it_->end_column);
                end_column_qualifier_buf_ = parsed.qualifier;
                const column_family_spec* cf = ctx_->sch.get_column_family(parsed.family);
                if (!cf)
                {
                    HYPERLITE_THROWF(error::BAD_SCAN_SPEC, "Column family '%s' does not exist",
                                     parsed.family.c_str());
                }
                end_column_family_code_ = static_cast<std::uint8_t>(cf->get_id());
                end_column_qualifier_ = parsed.has_qualifier && !parsed.is_regexp
                                            ? end_column_qualifier_buf_.c_str()
                                            : nullptr;
            }

            int st = SQLITE_OK;
            if (std::strcmp(it_->start_row, it_->end_row) != 0)
            {
                stmt_acquire(select_sql("r>=? AND r<=?"));

                st = ::sqlite3_bind_text(stmt_query_, 1, it_->start_row, -1, nullptr);
                HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

                st = ::sqlite3_bind_text(stmt_query_, 2, it_->end_row, -1, nullptr);
                HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
            }
            else if (start_column_family_code_ != end_column_family_code_)
            {
                stmt_acquire(select_sql("r=? AND cf>=? AND cf<=?"));

                st = ::sqlite3_bind_text(stmt_query_, 1, it_->start_row, -1, nullptr);
                HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

                st = ::sqlite3_bind_int(stmt_query_, 2, start_column_family_code_);
                HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

                st = ::sqlite3_bind_int(stmt_query_, 3, end_column_family_code_);
                HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
            }
            else
            {
                stmt_acquire(select_sql("r=? AND cf=?"));

                st = ::sqlite3_bind_text(stmt_query_, 1, it_->start_row, -1, nullptr);
                HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

                st = ::sqlite3_bind_int(stmt_query_, 2, start_column_family_code_);
                HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
            }
        }

        const int st = ::sqlite3_step(stmt_query_);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        if (st != SQLITE_DONE)
        {
            return st == SQLITE_ROW;
        }

        ++it_;
        cell_interval_done_ = true;
    }
}

bool scanner::reader_cell_intervals::filter_row(const char* row)
{
    // the row bounds are always compared inclusively here, the exclusive part of
    // the interval is a column matter and is settled in filter_cell
    if ((cmp_start_row_ = std::strcmp(row, it_->start_row)) >= 0)
    {
        if ((cmp_end_row_ = std::strcmp(row, it_->end_row)) <= 0)
        {
            return reader::filter_row(row);
        }
        ++it_;
        cell_interval_done_ = true;
    }
    return false;
}

const column_family_spec* scanner::reader_cell_intervals::filter_cell(const key& k)
{
    int cmp_start_cf = 0;
    int cmp_end_cf = 1;
    int cmp_end_cq = 1;

    const bool after_start =
        cmp_start_row_ > 0 ||
        ((cmp_start_cf = k.column_family_code - start_column_family_code_) >=
             (start_column_qualifier_ ? 0 : cmp_start_) &&
         (cmp_start_cf > 0 || !start_column_qualifier_ ||
          std::strcmp(bind_cq(k.column_qualifier), start_column_qualifier_) >= cmp_start_));

    const bool before_end =
        cmp_end_row_ < 0 || ((cmp_end_cf = k.column_family_code - end_column_family_code_) <=
                                 (end_column_qualifier_ ? 0 : cmp_end_) &&
                             (cmp_end_cf < 0 || !end_column_qualifier_ ||
                              (cmp_end_cq = std::strcmp(bind_cq(k.column_qualifier),
                                                        end_column_qualifier_)) <= cmp_end_));

    if (after_start && before_end)
    {
        // sitting exactly on the interval's upper bound, so this interval is done
        if (!cmp_end_cf && !cmp_end_cq)
        {
            ++it_;
            cell_interval_done_ = true;
        }
        return reader::filter_cell(k);
    }
    return nullptr;
}

//
// scanner
//

scanner::scanner(table_ptr t, const scan_spec& spec)
    : table_(std::move(t)), db_(table_->get_env()->handle()), schema_(table_->share_schema()),
      spec_(std::make_unique<scan_spec>(spec))
{
    try
    {
        // reader selection, in the order ht4c used: no row intervals means either a
        // full scan or a cell interval scan, otherwise scan-and-filter wins over
        // the row interval reader
        if (spec_->row_intervals.empty())
        {
            if (spec_->cell_intervals.empty())
            {
                reader_ = std::make_unique<reader>(*table_, *schema_, db_, *spec_);
            }
            else
            {
                for (auto& ci : spec_->cell_intervals)
                {
                    if (!ci.start_row)
                    {
                        ci.start_row = "";
                    }
                    if (!ci.end_row || !*ci.end_row)
                    {
                        ci.end_row = END_ROW_MARKER;
                    }
                }
                reader_ = std::make_unique<reader_cell_intervals>(*table_, *schema_, db_, *spec_);
            }
        }
        else if (spec_->scan_and_filter_rows)
        {
            reader_ = std::make_unique<reader_scan_and_filter>(*table_, *schema_, db_, *spec_);
        }
        else
        {
            for (auto& ri : spec_->row_intervals)
            {
                if (!ri.start)
                {
                    ri.start = "";
                }
                if (!ri.end || !*ri.end)
                {
                    ri.end = END_ROW_MARKER;
                }
            }
            reader_ = std::make_unique<reader_row_intervals>(*table_, *schema_, db_, *spec_);
        }

        reader_->stmt_prepare();

        // Without a transaction sqlite runs an implicit one per statement
        // execution - shared lock, hot journal probe, change counter check, unlock -
        // which costs more than the seek it guards. It pays off as soon as the
        // reader executes its statement more than once, once per requested row or
        // interval. For a single execution the two extra statements would be a net
        // loss, so those scans keep the implicit transaction. A read transaction, not a
        // scope: a mutator's flush must still commit what it wrote while the scan is open.
        if (reader_->multi_statement())
        {
            read_tx_ = get_env()->tx_read_begin();
        }
    }
    catch (...)
    {
        // ended, not rolled back: a mutation may have joined it, and rolling back would
        // discard what the mutator has buffered
        end_read_tx();
        reader_.reset();
        throw;
    }
}

scanner::~scanner()
{
    reader_.reset();
    end_read_tx();
    db_ = nullptr;
}

void scanner::end_read_tx() noexcept
{
    const std::uint64_t token = read_tx_;
    read_tx_ = 0;
    try
    {
        get_env()->tx_read_end(token);
    }
    catch (...)
    {
    }
}

bool scanner::next_cell(cell& c)
{
    // a scan that failed part way - a regex that timed out - stays failed: the cell it failed
    // on has been stepped past, so carrying on would return a result with cells missing
    if (failure_)
    {
        std::rethrow_exception(failure_);
    }

    // a scan that has ended stays ended, whatever has happened to the table since
    if (exhausted_)
    {
        return false;
    }

    try
    {
        // An alter replaced the schema the scan was composed from, so its family ids and time
        // orders need not be the table's any longer. Unlike a mutator a scan cannot follow:
        // what it has returned already was read the old way.
        if (table_->current_schema() != schema_.get())
        {
            HYPERLITE_THROWF(error::RANGESERVER_GENERATION_MISMATCH,
                             "Table '%s' was altered while being scanned", table_->get_full_name());
        }
        exhausted_ = !reader_->next_cell(c);

        // nothing is left to read, so the read transaction need not wait for the dispose
        if (exhausted_)
        {
            end_read_tx();
        }
        return !exhausted_;
    }
    catch (...)
    {
        failure_ = std::current_exception();
        throw;
    }
}
} // namespace hyperlite::sqlite::db
