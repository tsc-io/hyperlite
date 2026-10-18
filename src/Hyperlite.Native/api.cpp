// The ABI surface. Every entry point here is noexcept: an hyperlite::exception is
// captured into the calling thread's error slot and reported as its hypertable
// error code, anything else becomes error::EXTERNAL. Nothing unwinds into managed
// code.
//
// See abi.h for the contract.

#include "pch.h"
#include "abi.h"

#include "Regex.h"
#include "SQLiteDb.h"
#include "SQLiteEnv.h"
#include "ScanSpec.h"
#include "Support.h"
#include "Types.h"

#include <filesystem>

namespace
{
using namespace hyperlite;
using namespace hyperlite::abi;

/// One frame of the captured failure. Strings are owned so the borrowed pointers
/// handed to the managed side stay valid until the next call on this thread.
struct error_frame
{
    std::int32_t code = 0;
    std::int32_t line = 0;
    std::string text;
    std::string func;
    std::string file;
};

thread_local std::vector<error_frame> t_frames;

void clear_error()
{
    t_frames.clear();
}

void capture(const exception& e)
{
    t_frames.clear();
    for (const exception* at = &e; at != nullptr; at = at->inner())
    {
        error_frame frame;
        frame.code = at->code();
        frame.line = at->line();
        frame.text = at->message();
        frame.func = at->func();
        frame.file = at->file();
        t_frames.push_back(std::move(frame));
    }
}

void capture(std::int32_t code, std::string text)
{
    t_frames.clear();
    error_frame frame;
    frame.code = code;
    frame.text = std::move(text);
    t_frames.push_back(std::move(frame));
}

// Wraps a body so that no exception escapes. Returns 0 on success, otherwise the
// hypertable error code, which is never 0 for a real failure.
template <typename F>
std::int32_t guard(F&& body) noexcept
{
    try
    {
        clear_error();
        body();
        return 0;
    }
    catch (const exception& e)
    {
        try
        {
            capture(e);
        }
        catch (...)
        {
        }
        return e.code() != 0 ? e.code() : error::EXTERNAL;
    }
    catch (const std::bad_alloc&)
    {
        try
        {
            capture(error::BAD_MEMORY_ALLOCATION, "Out of memory");
        }
        catch (...)
        {
        }
        return error::BAD_MEMORY_ALLOCATION;
    }
    catch (const std::exception& e)
    {
        try
        {
            capture(error::EXTERNAL, e.what() ? e.what() : "Unknown failure");
        }
        catch (...)
        {
        }
        return error::EXTERNAL;
    }
    catch (...)
    {
        try
        {
            capture(error::EXTERNAL, "Unknown failure");
        }
        catch (...)
        {
        }
        return error::EXTERNAL;
    }
}

// Every handle carries a magic word, so a stale or foreign pointer is rejected
// rather than dereferenced. The managed side is the only caller and is expected
// to be correct; this is a backstop against a use-after-dispose turning into
// silent corruption.
constexpr std::uint32_t k_magic_env = 0x6E45324EU; // "N2En"
constexpr std::uint32_t k_magic_cli = 0x6C43324EU;
constexpr std::uint32_t k_magic_ns = 0x734E324EU;
constexpr std::uint32_t k_magic_tbl = 0x6254324EU;
constexpr std::uint32_t k_magic_mut = 0x754D324EU;
constexpr std::uint32_t k_magic_scn = 0x6353324EU;
constexpr std::uint32_t k_magic_ss = 0x7353324EU;
constexpr std::uint32_t k_magic_buf = 0x7542324EU;

// One environment - one sqlite connection - per database file, as ht4c's
// Context::getSQLiteEnv did. Two connections to one file inside a single process contend on
// the file locks, and worse, neither sees the other's uncommitted work: a mutator batch or a
// scope open on one is invisible to the other, which surfaces as 'database is locked' and as
// namespaces or rows that were just written and are not there.
//
// The int is the number of open handles. The last close drops the entry, so a later open
// builds a fresh connection - an environment still kept alive by a leaked mutator or scanner
// is deliberately not handed out again.
std::mutex g_envs_mutex;
std::map<std::string, std::pair<sqlite::env_ptr, int>> g_envs;

// The key is the resolved path rather than the string the caller passed: "db.sqlite",
// ".\db.sqlite" and an absolute path all name one file, and keying on the spelling would
// hand out one connection per spelling. Windows paths are compared case insensitively, Linux
// paths as they are.
std::string env_key(const std::string& filename)
{
    if (filename == "memory")
    {
        // :memory: is private to its connection, so sharing the environment is what makes an
        // in memory database reachable from a second context at all
        return filename;
    }

    try
    {
        // absolute + lexically_normal, deliberately not weakly_canonical: the canonical form
        // stats every component of the path, which measured 90us per open on this box -
        // more than the rest of opening a context. The lexical form still folds "db.sqlite",
        // ".\db.sqlite", "sub\..\db.sqlite" and an absolute path into one key; what it
        // cannot see through is a symlink or a junction, and two spellings across one of
        // those get a connection each, as they did before there was a registry at all.
        std::string key = std::filesystem::absolute(filename).lexically_normal().string();
#if defined(_WIN32)
        std::transform(key.begin(), key.end(), key.begin(),
                       [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
#endif
        return key;
    }
    catch (...)
    {
        // an unresolvable path is still a usable key, sqlite will report whatever is wrong
        return filename;
    }
}

template <std::uint32_t Magic, typename T>
struct handle
{
    std::uint32_t magic = Magic;
    T payload;

    template <typename... Args>
    explicit handle(Args&&... args) : payload(std::forward<Args>(args)...)
    {
    }
};

/// A scanner plus the staging area scn_next_many copies into. A cell handed out
/// by the scanner points into sqlite's current row, which the next step
/// invalidates, so batching has to copy.
struct scanner_state
{
    explicit scanner_state(std::shared_ptr<sqlite::db::scanner> s) : scanner(std::move(s)) {}

    std::shared_ptr<sqlite::db::scanner> scanner;
    std::string staging;
};

using env_handle = handle<k_magic_env, sqlite::env_ptr>;
using cli_handle = handle<k_magic_cli, sqlite::db::client_ptr>;
using ns_handle = handle<k_magic_ns, sqlite::db::namespace_ptr>;
using tbl_handle = handle<k_magic_tbl, sqlite::db::table_ptr>;
using mut_handle = handle<k_magic_mut, std::shared_ptr<sqlite::db::mutator>>;
using scn_handle = handle<k_magic_scn, std::unique_ptr<scanner_state>>;
using ss_handle = handle<k_magic_ss, std::unique_ptr<scan_spec>>;
using buf_handle = handle<k_magic_buf, std::string>;

/// Checks the magic word before handing back the typed handle.
template <std::uint32_t Magic, typename T>
handle<Magic, T>& checked(void* h, const char* what)
{
    if (!h)
    {
        HYPERLITE_THROWF(error::CLOSED, "Null %s handle", what);
    }
    auto* typed = static_cast<handle<Magic, T>*>(h);
    if (typed->magic != Magic)
    {
        HYPERLITE_THROWF(error::CLOSED, "Invalid %s handle", what);
    }
    return *typed;
}

sqlite::env& as_env(void* h)
{
    auto& handle = checked<k_magic_env, sqlite::env_ptr>(h, "environment");
    if (!handle.payload)
    {
        HYPERLITE_THROW(error::CLOSED, "Environment already closed");
    }
    return *handle.payload;
}

sqlite::db::client& as_client(void* h)
{
    auto& handle = checked<k_magic_cli, sqlite::db::client_ptr>(h, "client");
    if (!handle.payload)
    {
        HYPERLITE_THROW(error::CLOSED, "Client already released");
    }
    return *handle.payload;
}

sqlite::db::name_space& as_namespace(void* h)
{
    auto& handle = checked<k_magic_ns, sqlite::db::namespace_ptr>(h, "namespace");
    if (!handle.payload)
    {
        HYPERLITE_THROW(error::CLOSED, "Namespace already released");
    }
    return *handle.payload;
}

sqlite::db::table& as_table(void* h)
{
    auto& handle = checked<k_magic_tbl, sqlite::db::table_ptr>(h, "table");
    if (!handle.payload)
    {
        HYPERLITE_THROW(error::CLOSED, "Table already released");
    }
    return *handle.payload;
}

sqlite::db::mutator& as_mutator(void* h)
{
    auto& handle = checked<k_magic_mut, std::shared_ptr<sqlite::db::mutator>>(h, "mutator");
    if (!handle.payload)
    {
        HYPERLITE_THROW(error::CLOSED, "Mutator already released");
    }
    return *handle.payload;
}

scanner_state& as_scanner(void* h)
{
    auto& handle = checked<k_magic_scn, std::unique_ptr<scanner_state>>(h, "scanner");
    if (!handle.payload || !handle.payload->scanner)
    {
        HYPERLITE_THROW(error::CLOSED, "Scanner already released");
    }
    return *handle.payload;
}

scan_spec& as_scan_spec(void* h)
{
    auto& handle = checked<k_magic_ss, std::unique_ptr<scan_spec>>(h, "scan spec");
    if (!handle.payload)
    {
        HYPERLITE_THROW(error::CLOSED, "Scan spec already released");
    }
    return *handle.payload;
}

/// Publishes a string as a t_buf the managed side releases with buf_release.
void publish(t_buf* out, std::string value)
{
    auto* h = new buf_handle(std::move(value));
    out->h = h;
    out->p = h->payload.data();
    out->n = static_cast<std::int32_t>(h->payload.size());
    out->_pad = 0;
}

/// Nul terminated slice, or nullptr. A negative length means nul terminated
/// already, so no copy is needed.
std::string to_string(const char* text, std::int32_t length)
{
    if (!text)
    {
        return std::string();
    }
    return length < 0 ? std::string(text) : std::string(text, static_cast<std::size_t>(length));
}

// A row, a column family or a qualifier is text to sqlite and to the scan spec, which take it
// up to its first NUL. One with a NUL inside would be written at its full length and looked up
// at a shorter one - another row's cells, or every row's - so it is refused where it enters.
void refuse_nul(const char* text, std::int32_t len, std::int32_t code, const char* what)
{
    if (text && len > 0 && std::memchr(text, 0, static_cast<std::size_t>(len)))
    {
        HYPERLITE_THROWF(code, "%s contains a NUL character", what);
    }
}

// A name is the same: stored at its full length, but checked, listed and looked up as text up
// to its first NUL - a table "t\0x" was listed as "t", which could not be opened.
std::string checked_table_name(const char* name, std::int32_t name_n)
{
    refuse_nul(name, name_n, error::SYNTAX_ERROR, "Table name");
    std::string out = to_string(name, name_n);
    validate_table_name(out.c_str());
    return out;
}

// Flat little endian stream, so the managed side reads it without a call per
// entry:
//   list  := int32 count, entry * count
//   entry := int32 nameLen, byte * nameLen, byte isNamespace, list subEntries
void write_i32(std::string& out, std::int32_t v)
{
    const auto u = static_cast<std::uint32_t>(v);
    out.push_back(static_cast<char>(u & 0xFF));
    out.push_back(static_cast<char>((u >> 8) & 0xFF));
    out.push_back(static_cast<char>((u >> 16) & 0xFF));
    out.push_back(static_cast<char>((u >> 24) & 0xFF));
}

void write_listing(std::string& out, const std::vector<sqlite::db::namespace_listing>& list)
{
    write_i32(out, static_cast<std::int32_t>(list.size()));
    for (const auto& entry : list)
    {
        write_i32(out, static_cast<std::int32_t>(entry.name.size()));
        out.append(entry.name);
        out.push_back(entry.is_namespace ? '\1' : '\0');
        write_listing(out, entry.sub_entries);
    }
}

std::int32_t HYPERLITE_CALL fn_abi_revision()
{
    return k_abi_revision;
}

std::int32_t HYPERLITE_CALL fn_set_callbacks(const t_callbacks* cb)
{
    return guard(
        [&]
        {
            if (!cb)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null callback table");
            }
            set_regex_callbacks(*cb);
        });
}

void HYPERLITE_CALL fn_cfg_defaults(t_cfg* cfg)
{
    if (!cfg)
    {
        return;
    }
    const sqlite::env_config defaults;
    cfg->cache_size_mb = defaults.cache_size_mb;
    cfg->page_size_kb = defaults.page_size_kb;
    cfg->write_ahead_log = defaults.write_ahead_log ? 1 : 0;
    cfg->synchronous = defaults.synchronous ? 1 : 0;
    cfg->auto_vacuum = defaults.auto_vacuum;
    cfg->unique_rows = defaults.unique_rows ? 1 : 0;
    cfg->no_cell_revisions = defaults.no_cell_revisions ? 1 : 0;
    cfg->index_column = defaults.index_column ? 1 : 0;
    cfg->index_column_family = defaults.index_column_family ? 1 : 0;
    cfg->index_column_qualifier = defaults.index_column_qualifier ? 1 : 0;
    cfg->index_timestamp = defaults.index_timestamp ? 1 : 0;
    cfg->_pad = 0;
}

const char* HYPERLITE_CALL fn_sqlite_version()
{
    return ::sqlite3_libversion();
}

std::int32_t HYPERLITE_CALL fn_err_get(std::int32_t depth, t_error* out)
{
    if (!out || depth < 0 || static_cast<std::size_t>(depth) >= t_frames.size())
    {
        return 0;
    }
    const error_frame& frame = t_frames[static_cast<std::size_t>(depth)];
    out->code = frame.code;
    out->line = frame.line;
    out->text = frame.text.c_str();
    out->text_n = static_cast<std::int32_t>(frame.text.size());
    out->_pad = 0;
    out->func = frame.func.empty() ? nullptr : frame.func.c_str();
    out->func_n = static_cast<std::int32_t>(frame.func.size());
    out->_pad2 = 0;
    out->file = frame.file.empty() ? nullptr : frame.file.c_str();
    out->file_n = static_cast<std::int32_t>(frame.file.size());
    out->_pad3 = 0;
    return 1;
}

void HYPERLITE_CALL fn_buf_release(t_buf* buf)
{
    if (!buf || !buf->h)
    {
        return;
    }
    auto* h = static_cast<buf_handle*>(buf->h);
    if (h->magic == k_magic_buf)
    {
        h->magic = 0;
        delete h;
    }
    buf->h = nullptr;
    buf->p = nullptr;
    buf->n = 0;
}

std::int32_t HYPERLITE_CALL fn_env_get_or_create(const char* filename, std::int32_t filename_n,
                                                 const t_cfg* cfg, void** out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            *out = nullptr;

            sqlite::env_config config;
            if (cfg)
            {
                config.cache_size_mb = cfg->cache_size_mb;
                config.page_size_kb = cfg->page_size_kb;
                config.write_ahead_log = cfg->write_ahead_log != 0;
                config.synchronous = cfg->synchronous != 0;
                config.auto_vacuum = cfg->auto_vacuum;
                config.unique_rows = cfg->unique_rows != 0;
                config.no_cell_revisions = cfg->no_cell_revisions != 0;
                config.index_column = cfg->index_column != 0;
                config.index_column_family = cfg->index_column_family != 0;
                config.index_column_qualifier = cfg->index_column_qualifier != 0;
                config.index_timestamp = cfg->index_timestamp != 0;
            }

            const std::string name = to_string(filename, filename_n);
            const std::string key = env_key(name);

            std::lock_guard<std::mutex> guard(g_envs_mutex);

            const auto it = g_envs.find(key);
            if (it != g_envs.end())
            {
                // the handle is built first, a throw must not leave the count raised
                auto* shared = new env_handle(it->second.first);
                ++it->second.second;
                *out = shared;
                return;
            }

            auto e = std::make_shared<sqlite::env>(name, config);
            auto* created = new env_handle(e);
            try
            {
                g_envs.emplace(key, std::make_pair(std::move(e), 1));
            }
            catch (...)
            {
                delete created;
                throw;
            }
            *out = created;
        });
}

std::int32_t HYPERLITE_CALL fn_env_close(void* h)
{
    return guard(
        [&]
        {
            auto& handle = checked<k_magic_env, sqlite::env_ptr>(h, "environment");

            {
                std::lock_guard<std::mutex> guard(g_envs_mutex);
                for (auto it = g_envs.begin(); it != g_envs.end(); ++it)
                {
                    if (it->second.first == handle.payload)
                    {
                        if (--it->second.second == 0)
                        {
                            g_envs.erase(it);
                        }
                        break;
                    }
                }
            }

            // outside the registry lock: this may be the last reference, and closing the
            // connection is none of the registry's business
            handle.magic = 0;
            delete &handle;
        });
}

std::int32_t HYPERLITE_CALL fn_env_optimize(void* h)
{
    return guard(
        [&]
        {
            sqlite::env& e = as_env(h);
            auto guard_lock = e.lock();
            // optimize is a client level operation in ht4c, but it only ever touched the
            // connection, so it is served here without a client handle. VACUUM cannot run
            // inside a transaction, so what the mutators hold is committed first, as a
            // flush would have; a loss is left for the mutator's flush to report
            e.tx_commit_ambient();
            char* errmsg = nullptr;
            const int st = ::sqlite3_exec(e.handle(), "VACUUM", nullptr, nullptr, &errmsg);
            HYPERLITE_SQLITE_VERIFY(st, e.handle(), errmsg);
        });
}

std::int32_t HYPERLITE_CALL fn_cli_create(void* env_h, void** out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            *out = nullptr;

            auto& handle = checked<k_magic_env, sqlite::env_ptr>(env_h, "environment");
            if (!handle.payload)
            {
                HYPERLITE_THROW(error::CLOSED, "Environment already closed");
            }
            auto lock = handle.payload->lock();
            auto c = std::make_shared<sqlite::db::client>(handle.payload);
            *out = new cli_handle(std::move(c));
        });
}

std::int32_t HYPERLITE_CALL fn_cli_release(void* h)
{
    return guard(
        [&]
        {
            auto& handle = checked<k_magic_cli, sqlite::db::client_ptr>(h, "client");
            handle.magic = 0;
            delete &handle;
        });
}

// Resolves an optional base namespace into the canonical path.
//
// Canonicalization is not cosmetic: "/" has to collapse to the empty string,
// because that is how the root namespace is spelled internally and what makes
// exists("/") answer true, and "/test/" has to come out as "test" so that a
// namespace's reported name matches whatever spelling the caller used to open it.
// ht4c did this in SQLiteClient::getNamespace, on exactly this boundary.
std::string qualify(const char* name, std::int32_t name_n, void* ns_base)
{
    std::string out;
    if (ns_base)
    {
        out = as_namespace(ns_base).get_name();
    }

    refuse_nul(name, name_n, error::SYNTAX_ERROR, "Namespace name");
    const std::string leaf = to_string(name, name_n);
    if (!leaf.empty())
    {
        if (!out.empty())
        {
            out += "/";
        }
        out += leaf;
    }

    return canonicalize_namespace(out);
}

std::int32_t HYPERLITE_CALL fn_cli_ns_create(void* h, const char* name, std::int32_t name_n,
                                             void* ns_base, std::int32_t flags)
{
    return guard(
        [&]
        {
            sqlite::db::client& c = as_client(h);
            auto lock = c.get_env()->lock();

            const std::string path = qualify(name, name_n, ns_base);
            const bool if_not_exists = (flags & fl_if_not_exists) != 0;

            if (if_not_exists && c.namespace_exists(path))
            {
                return;
            }
            c.create_namespace(path, (flags & fl_create_intermediate) != 0);
        });
}

std::int32_t HYPERLITE_CALL fn_cli_ns_open(void* h, const char* name, std::int32_t name_n,
                                           void* ns_base, void** out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            *out = nullptr;

            sqlite::db::client& c = as_client(h);
            auto lock = c.get_env()->lock();
            auto ns = c.open_namespace(qualify(name, name_n, ns_base));
            *out = new ns_handle(std::move(ns));
        });
}

std::int32_t HYPERLITE_CALL fn_cli_ns_drop(void* h, const char* name, std::int32_t name_n,
                                           void* ns_base, std::int32_t flags)
{
    return guard(
        [&]
        {
            sqlite::db::client& c = as_client(h);
            auto lock = c.get_env()->lock();

            const std::string path = qualify(name, name_n, ns_base);
            const bool if_exists = (flags & fl_if_exists) != 0;

            const bool deep = (flags & fl_deep) != 0;
            const bool drop_tables = (flags & fl_drop_tables) != 0;

            // one transaction, so a drop that fails part way leaves the sub-tree whole
            sqlite::util::tx tx(c.get_env());

            if (drop_tables || deep)
            {
                if (!c.namespace_exists(path))
                {
                    if (if_exists)
                    {
                        return;
                    }
                    HYPERLITE_THROWF(error::NAMESPACE_DOES_NOT_EXIST,
                                     "Namespace '%s' does not exist", path.c_str());
                }

                auto ns = c.open_namespace(path);
                std::vector<sqlite::db::namespace_listing> listing;
                ns->get_namespace_listing(deep, listing);

                if (deep)
                {
                    ns->drop(path, listing, if_exists, drop_tables);
                }
                else
                {
                    // without deep, only the namespace's own tables go: a sub-namespace is
                    // left alone, so the drop below fails on a non-empty namespace exactly
                    // as it should
                    for (const auto& entry : listing)
                    {
                        if (!entry.is_namespace)
                        {
                            ns->drop_table(entry.name, true);
                        }
                    }
                }
            }

            c.drop_namespace(path, if_exists);
            tx.commit();
        });
}

std::int32_t HYPERLITE_CALL fn_cli_ns_exists(void* h, const char* name, std::int32_t name_n,
                                             void* ns_base, std::int32_t* exists)
{
    return guard(
        [&]
        {
            if (!exists)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            sqlite::db::client& c = as_client(h);
            auto lock = c.get_env()->lock();
            *exists = c.namespace_exists(qualify(name, name_n, ns_base)) ? 1 : 0;
        });
}

std::int32_t HYPERLITE_CALL fn_ns_release(void* h)
{
    return guard(
        [&]
        {
            auto& handle = checked<k_magic_ns, sqlite::db::namespace_ptr>(h, "namespace");
            handle.magic = 0;
            delete &handle;
        });
}

std::int32_t HYPERLITE_CALL fn_ns_name(void* h, t_buf* out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            publish(out, as_namespace(h).get_name());
        });
}

std::int32_t HYPERLITE_CALL fn_ns_tbl_create(void* h, const char* name, std::int32_t name_n,
                                             const char* schema, std::int32_t schema_n)
{
    return guard(
        [&]
        {
            auto& ns = as_namespace(h);
            const std::string table_name = checked_table_name(name, name_n);

            auto lock = ns.get_env()->lock();
            refuse_nul(schema, schema_n, error::SCHEMA_PARSE_ERROR, "Schema");
            ns.create_table(table_name, to_string(schema, schema_n));
        });
}

std::int32_t HYPERLITE_CALL fn_ns_tbl_create_like(void* h, const char* name, std::int32_t name_n,
                                                  const char* like, std::int32_t like_n)
{
    return guard(
        [&]
        {
            auto& ns = as_namespace(h);
            auto lock = ns.get_env()->lock();

            // create-like reads the source schema with ids and hands it straight over,
            // a table created "like" another takes the other's schema, not its rows
            std::string schema;
            const std::string table_name = checked_table_name(name, name_n);
            ns.get_table_schema(checked_table_name(like, like_n), true, schema);
            ns.create_table(table_name, schema);
        });
}

std::int32_t HYPERLITE_CALL fn_ns_tbl_alter(void* h, const char* name, std::int32_t name_n,
                                            const char* schema, std::int32_t schema_n)
{
    return guard(
        [&]
        {
            auto& ns = as_namespace(h);
            const std::string table_name = checked_table_name(name, name_n);

            auto lock = ns.get_env()->lock();
            refuse_nul(schema, schema_n, error::SCHEMA_PARSE_ERROR, "Schema");
            ns.alter_table(table_name, to_string(schema, schema_n));
        });
}

std::int32_t HYPERLITE_CALL fn_ns_tbl_rename(void* h, const char* from, std::int32_t from_n,
                                             const char* to, std::int32_t to_n)
{
    return guard(
        [&]
        {
            auto& ns = as_namespace(h);
            const std::string from_name = checked_table_name(from, from_n);
            const std::string to_name = checked_table_name(to, to_n);

            auto lock = ns.get_env()->lock();
            ns.rename_table(from_name, to_name);
        });
}

std::int32_t HYPERLITE_CALL fn_ns_tbl_open(void* h, const char* name, std::int32_t name_n,
                                           std::int32_t flags, void** out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            *out = nullptr;
            (void)flags; // force has no meaning for the sqlite provider

            auto& ns = as_namespace(h);
            const std::string table_name = checked_table_name(name, name_n);

            auto lock = ns.get_env()->lock();

            // Probed first so that a missing table is reported as TABLE_NOT_FOUND rather
            // than the HYPERSPACE_FILE_NOT_FOUND the storage layer raises - the managed
            // side maps the former to TableNotFoundException, which is what callers catch.
            if (!ns.table_exists(table_name))
            {
                HYPERLITE_THROWF(error::TABLE_NOT_FOUND, "%s", table_name.c_str());
            }

            auto t = ns.open_table(table_name);
            *out = new tbl_handle(std::move(t));
        });
}

std::int32_t HYPERLITE_CALL fn_ns_tbl_drop(void* h, const char* name, std::int32_t name_n,
                                           std::int32_t flags)
{
    return guard(
        [&]
        {
            auto& ns = as_namespace(h);
            const std::string table_name = checked_table_name(name, name_n);

            auto lock = ns.get_env()->lock();
            ns.drop_table(table_name, (flags & fl_if_exists) != 0);
        });
}

std::int32_t HYPERLITE_CALL fn_ns_tbl_exists(void* h, const char* name, std::int32_t name_n,
                                             std::int32_t* exists)
{
    return guard(
        [&]
        {
            if (!exists)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            auto& ns = as_namespace(h);
            auto lock = ns.get_env()->lock();
            refuse_nul(name, name_n, error::SYNTAX_ERROR, "Table name");
            *exists = ns.table_exists(to_string(name, name_n)) ? 1 : 0;
        });
}

std::int32_t HYPERLITE_CALL fn_ns_tbl_schema(void* h, const char* name, std::int32_t name_n,
                                             std::int32_t flags, t_buf* out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            auto& ns = as_namespace(h);
            auto lock = ns.get_env()->lock();
            std::string schema;
            refuse_nul(name, name_n, error::SYNTAX_ERROR, "Table name");
            ns.get_table_schema(to_string(name, name_n), (flags & fl_with_ids) != 0, schema);
            publish(out, std::move(schema));
        });
}

std::int32_t HYPERLITE_CALL fn_ns_listing(void* h, std::int32_t flags, t_buf* out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            auto& ns = as_namespace(h);
            auto lock = ns.get_env()->lock();

            std::vector<sqlite::db::namespace_listing> listing;
            ns.get_namespace_listing((flags & fl_deep) != 0, listing);

            std::string encoded;
            write_listing(encoded, listing);
            publish(out, std::move(encoded));
        });
}

std::int32_t HYPERLITE_CALL fn_tbl_release(void* h)
{
    return guard(
        [&]
        {
            auto& handle = checked<k_magic_tbl, sqlite::db::table_ptr>(h, "table");
            if (handle.payload)
            {
                // Dropping the last reference disposes the table, which runs statements on the
                // connection - hence the lock. The environment gets pinned as well: this may be
                // the very reference which owns it, and destroying it would take the mutex the
                // lock still holds down with it.
                sqlite::env_ptr pin = handle.payload->get_env()->shared_from_this();
                auto lock = pin->lock();
                handle.payload.reset();
            }
            handle.magic = 0;
            delete &handle;
        });
}

std::int32_t HYPERLITE_CALL fn_tbl_name(void* h, t_buf* out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            // the full path, namespace/table, which is what ht4c's getName returned, and what
            // callers compare against Namespace.Name + "/" + name. Under the lock, as a rename
            // changes it.
            auto& t = as_table(h);
            auto lock = t.get_env()->lock();
            publish(out, t.get_full_name());
        });
}

std::int32_t HYPERLITE_CALL fn_tbl_schema(void* h, std::int32_t flags, t_buf* out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            auto& t = as_table(h);
            auto lock = t.get_env()->lock();
            std::string schema;
            t.get_table_schema((flags & fl_with_ids) != 0, schema);
            publish(out, std::move(schema));
        });
}

std::int32_t HYPERLITE_CALL fn_tbl_mut_create(void* h, std::int32_t timeout_ms, std::int32_t flags,
                                              std::int32_t flush_interval_ms, void** out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            *out = nullptr;
            // Hypertable's settings, which the managed docs say change nothing here: the sqlite
            // provider is local, so there is nothing to time out, it has no commit log to sync
            // and does not flush periodically
            (void)timeout_ms;
            (void)flags;
            (void)flush_interval_ms;

            auto& t = as_table(h);
            auto lock = t.get_env()->lock();
            auto m = t.create_mutator();
            *out = new mut_handle(std::move(m));
        });
}

std::int32_t HYPERLITE_CALL fn_tbl_scn_create(void* h, void* ss, std::int32_t timeout_ms,
                                              std::int32_t flags, void** out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            *out = nullptr;
            (void)timeout_ms;

            auto& t = as_table(h);
            auto lock = t.get_env()->lock();
            (void)flags; // Hypertable's table cache flags, the sqlite provider has no table cache
            auto s = t.create_scanner(as_scan_spec(ss));
            *out = new scn_handle(std::make_unique<scanner_state>(std::move(s)));
        });
}

std::int32_t HYPERLITE_CALL fn_ss_create(void** out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            *out = new ss_handle(std::make_unique<scan_spec>());
        });
}

std::int32_t HYPERLITE_CALL fn_ss_release(void* h)
{
    return guard(
        [&]
        {
            auto& handle = checked<k_magic_ss, std::unique_ptr<scan_spec>>(h, "scan spec");
            handle.magic = 0;
            delete &handle;
        });
}

std::int32_t HYPERLITE_CALL fn_ss_clear(void* h)
{
    return guard([&] { as_scan_spec(h).clear(); });
}

std::int32_t HYPERLITE_CALL fn_ss_set_i(void* h, std::int32_t field, std::int64_t value)
{
    return guard(
        [&]
        {
            scan_spec& ss = as_scan_spec(h);
            const auto i32 = static_cast<std::int32_t>(value);
            switch (field)
            {
            case ssi_max_rows:
                ss.set_max_rows(i32);
                break;
            case ssi_max_versions:
                ss.set_max_versions(i32);
                break;
            case ssi_max_cells:
                ss.set_max_cells(i32);
                break;
            case ssi_max_cells_column_family:
                ss.set_max_cells_column_family(i32);
                break;
            case ssi_row_offset:
                ss.set_row_offset(i32);
                break;
            case ssi_cell_offset:
                ss.set_cell_offset(i32);
                break;
            case ssi_keys_only:
                ss.set_keys_only(value != 0);
                break;
            case ssi_not_use_query_cache:
                ss.set_not_use_query_cache(value != 0);
                break;
            case ssi_scan_and_filter:
                ss.set_scan_and_filter(value != 0);
                break;
            case ssi_column_predicate_and:
                ss.set_column_predicate_and(value != 0);
                break;
            case ssi_start_timestamp:
                ss.set_start_timestamp(static_cast<std::uint64_t>(value));
                break;
            case ssi_end_timestamp:
                ss.set_end_timestamp(static_cast<std::uint64_t>(value));
                break;
            case ssi_reserve_rows:
                ss.reserve_rows(i32);
                break;
            case ssi_reserve_columns:
                ss.reserve_columns(i32);
                break;
            case ssi_reserve_cells:
                ss.reserve_cells(i32);
                break;
            default:
                HYPERLITE_THROWF(error::BAD_SCAN_SPEC, "Unknown scan spec field %d", field);
            }
        });
}

void check_cell(const t_cell& c)
{
    refuse_nul(c.row, c.row_n, error::BAD_KEY, "Row key");
    refuse_nul(c.cf, c.cf_n, error::BAD_KEY, "Column family");
    refuse_nul(c.cq, c.cq_n, error::BAD_KEY, "Column qualifier");

    // -1 means nul terminated for text, a value has no such reading and would reach sqlite as
    // a length it leaves undefined
    if (c.value_n < 0)
    {
        HYPERLITE_THROWF(error::BAD_VALUE, "Invalid value length %d", c.value_n);
    }
}

std::int32_t HYPERLITE_CALL fn_ss_set_s(void* h, std::int32_t field, const char* value,
                                        std::int32_t value_n)
{
    return guard(
        [&]
        {
            scan_spec& ss = as_scan_spec(h);
            refuse_nul(value, value_n, error::BAD_SCAN_SPEC, "Regular expression");
            switch (field)
            {
            case sss_row_regex:
                ss.set_row_regex(value, value_n);
                break;
            case sss_value_regex:
                ss.set_value_regex(value, value_n);
                break;
            default:
                HYPERLITE_THROWF(error::BAD_SCAN_SPEC, "Unknown scan spec field %d", field);
            }
        });
}

std::int32_t HYPERLITE_CALL fn_ss_add_row(void* h, const char* row, std::int32_t row_n)
{
    return guard(
        [&]
        {
            refuse_nul(row, row_n, error::BAD_SCAN_SPEC, "Row key");
            as_scan_spec(h).add_row(row, row_n);
        });
}

std::int32_t HYPERLITE_CALL fn_ss_add_column(void* h, const char* column, std::int32_t column_n)
{
    return guard(
        [&]
        {
            refuse_nul(column, column_n, error::BAD_SCAN_SPEC, "Column");
            as_scan_spec(h).add_column(column, column_n);
        });
}

std::int32_t HYPERLITE_CALL fn_ss_add_cell(void* h, const char* row, std::int32_t row_n,
                                           const char* column, std::int32_t column_n)
{
    return guard(
        [&]
        {
            refuse_nul(row, row_n, error::BAD_SCAN_SPEC, "Row key");
            refuse_nul(column, column_n, error::BAD_SCAN_SPEC, "Column");
            as_scan_spec(h).add_cell(row, row_n, column, column_n);
        });
}

std::int32_t HYPERLITE_CALL fn_ss_add_row_interval(void* h, const char* from, std::int32_t from_n,
                                                   std::int32_t include_from, const char* to,
                                                   std::int32_t to_n, std::int32_t include_to)
{
    return guard(
        [&]
        {
            refuse_nul(from, from_n, error::BAD_SCAN_SPEC, "Row key");
            refuse_nul(to, to_n, error::BAD_SCAN_SPEC, "Row key");
            as_scan_spec(h).add_row_interval(from, from_n, include_from != 0, to, to_n,
                                             include_to != 0);
        });
}

std::int32_t HYPERLITE_CALL fn_ss_add_cell_interval(
    void* h, const char* from_row, std::int32_t from_row_n, const char* from_column,
    std::int32_t from_column_n, std::int32_t include_from, const char* to_row,
    std::int32_t to_row_n, const char* to_column, std::int32_t to_column_n, std::int32_t include_to)
{
    return guard(
        [&]
        {
            refuse_nul(from_row, from_row_n, error::BAD_SCAN_SPEC, "Row key");
            refuse_nul(from_column, from_column_n, error::BAD_SCAN_SPEC, "Column");
            refuse_nul(to_row, to_row_n, error::BAD_SCAN_SPEC, "Row key");
            refuse_nul(to_column, to_column_n, error::BAD_SCAN_SPEC, "Column");
            as_scan_spec(h).add_cell_interval(from_row, from_row_n, from_column, from_column_n,
                                              include_from != 0, to_row, to_row_n, to_column,
                                              to_column_n, include_to != 0);
        });
}

std::int32_t HYPERLITE_CALL fn_ss_add_column_predicate(void* h, const char* cf, std::int32_t cf_n,
                                                       const char* cq, std::int32_t cq_n,
                                                       std::int32_t match, const void* value,
                                                       std::int32_t value_n)
{
    return guard(
        [&]
        {
            refuse_nul(cf, cf_n, error::BAD_SCAN_SPEC, "Column family");
            refuse_nul(cq, cq_n, error::BAD_SCAN_SPEC, "Column qualifier");
            as_scan_spec(h).add_column_predicate(cf, cf_n, cq, cq_n,
                                                 static_cast<std::uint32_t>(match), value, value_n);
        });
}

std::int32_t HYPERLITE_CALL fn_mut_release(void* h)
{
    return guard(
        [&]
        {
            auto& handle = checked<k_magic_mut, std::shared_ptr<sqlite::db::mutator>>(h, "mutator");
            if (handle.payload)
            {
                // pinned, see fn_tbl_release: the reset below may drop the last reference to it
                sqlite::env_ptr pin = handle.payload->get_env()->shared_from_this();
                auto lock = pin->lock();
                // ht4n flushes on dispose, and so does ht4c's mutator teardown. A release
                // must succeed, so a failure here goes unreported: Hyperlite's Dispose
                // flushes first, through fn_mut_flush, and throws what that reports
                try
                {
                    handle.payload->flush();
                }
                catch (...)
                {
                }
                handle.payload.reset();
            }
            handle.magic = 0;
            delete &handle;
        });
}

/// Translates an ABI cell into the key spec the mutator consumes, applying the
/// same flag and timestamp narrowing ht4c did in its stdafx helpers.
key_spec to_key_spec(const t_cell& c)
{
    check_cell(c);
    key_spec ks;
    ks.row = c.row;
    ks.row_len = static_cast<std::size_t>(c.row_n);
    ks.column_family = CF(c.cf);
    ks.column_qualifier = c.cq;
    ks.column_qualifier_len = static_cast<std::size_t>(c.cq_n);
    ks.flag = FLAG(c.cf, c.cq, c.flag);
    ks.timestamp = static_cast<std::int64_t>(TIMESTAMP(c.ts));
    ks.revision = AUTO_ASSIGN;
    return ks;
}

std::int32_t HYPERLITE_CALL fn_mut_set(void* h, const t_cell* c)
{
    return guard(
        [&]
        {
            if (!c)
            {
                HYPERLITE_THROW(error::BAD_KEY, "Null cell");
            }
            auto& m = as_mutator(h);
            auto lock = m.get_env()->lock();
            key_spec ks = to_key_spec(*c);
            m.set(ks, c->value, static_cast<std::uint32_t>(c->value_n));
        });
}

std::int32_t HYPERLITE_CALL fn_mut_set_gen(void* h, const t_cell* c, t_buf* out)
{
    return guard(
        [&]
        {
            if (!c)
            {
                HYPERLITE_THROW(error::BAD_KEY, "Null cell");
            }
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }

            char generated[key_builder::size_key + 1];
            key_builder::generate(generated);

            auto& m = as_mutator(h);
            auto lock = m.get_env()->lock();

            t_cell copy = *c;
            copy.row = generated;
            copy.row_n = static_cast<std::int32_t>(key_builder::size_key);

            // A generated row key is always an insert: ht4c's generated-row overload took
            // no flag and hardcoded FLAG_INSERT, so a caller asking to delete a row it is
            // simultaneously inventing still writes one. Kept for the ABI; the managed side
            // asks for a generated row only for a cell written.
            copy.flag = cf_insert;

            key_spec ks = to_key_spec(copy);
            m.set(ks, c->value, static_cast<std::uint32_t>(c->value_n));

            publish(out, std::string(generated, key_builder::size_key));
        });
}

std::int32_t HYPERLITE_CALL fn_mut_set_many(void* h, const t_cell* items, std::int32_t count)
{
    return guard(
        [&]
        {
            if (count < 0)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Negative cell count");
            }
            if (count == 0)
            {
                return;
            }
            if (!items)
            {
                HYPERLITE_THROW(error::BAD_KEY, "Null cell array");
            }

            auto& m = as_mutator(h);
            auto lock = m.get_env()->lock();

            // one lock and one ambient transaction for the whole batch, which is the
            // point of the batched entry point - and all or nothing: a batch reported as
            // failed must not leave its first cells for the next flush to commit
            sqlite::env& e = *m.get_env();
            e.tx_batch_begin();
            try
            {
                for (std::int32_t i = 0; i < count; ++i)
                {
                    key_spec ks = to_key_spec(items[i]);
                    m.set(ks, items[i].value, static_cast<std::uint32_t>(items[i].value_n));
                }
                e.tx_batch_commit();
            }
            catch (...)
            {
                e.tx_batch_rollback();
                throw;
            }
        });
}

std::int32_t HYPERLITE_CALL fn_mut_del(void* h, const t_cell* c)
{
    return guard(
        [&]
        {
            if (!c)
            {
                HYPERLITE_THROW(error::BAD_KEY, "Null cell");
            }
            auto& m = as_mutator(h);
            auto lock = m.get_env()->lock();

            check_cell(*c);
            key_spec ks;
            ks.row = c->row;
            ks.row_len = static_cast<std::size_t>(c->row_n);
            ks.column_family = CF(c->cf);
            ks.column_qualifier = c->cq;
            ks.column_qualifier_len = static_cast<std::size_t>(c->cq_n);
            // a delete's flag follows from what was specified, unless the caller asked
            // for a specific version
            ks.flag = c->flag == FLAG_DELETE_CELL_VERSION ? FLAG_DELETE_CELL_VERSION
                                                          : FLAG_DELETE(c->cf, c->cq);
            ks.timestamp = static_cast<std::int64_t>(TIMESTAMP(c->ts));
            ks.revision = AUTO_ASSIGN;

            m.del(ks);
        });
}

std::int32_t HYPERLITE_CALL fn_mut_flush(void* h)
{
    return guard(
        [&]
        {
            auto& m = as_mutator(h);
            auto lock = m.get_env()->lock();
            m.flush();
        });
}

std::int32_t HYPERLITE_CALL fn_scn_release(void* h)
{
    return guard(
        [&]
        {
            auto& handle = checked<k_magic_scn, std::unique_ptr<scanner_state>>(h, "scanner");
            if (handle.payload && handle.payload->scanner)
            {
                // pinned, see fn_tbl_release: the reset below may drop the last reference to it
                sqlite::env_ptr pin = handle.payload->scanner->get_env()->shared_from_this();
                auto lock = pin->lock();
                handle.payload.reset();
            }
            handle.payload.reset();
            handle.magic = 0;
            delete &handle;
        });
}

/// Length of a cell string: what the producer recorded, or measured if it did not.
std::int32_t cell_len(const char* text, std::int32_t recorded) noexcept
{
    if (!text)
    {
        return 0;
    }
    return recorded >= 0 ? recorded : static_cast<std::int32_t>(std::strlen(text));
}

void fill(t_cell& out, const cell& c)
{
    out.row = c.row_key;
    out.row_n = cell_len(c.row_key, c.row_key_len);
    out.cf = c.column_family;
    out.cf_n = cell_len(c.column_family, c.column_family_len);
    out.cq = c.column_qualifier;
    out.cq_n = cell_len(c.column_qualifier, c.column_qualifier_len);
    out.value = c.value;
    out.value_n = static_cast<std::int32_t>(c.value_len);
    out.ts = static_cast<std::uint64_t>(c.timestamp);
    out.flag = c.flag;
}

std::int32_t HYPERLITE_CALL fn_scn_next(void* h, t_cell* out, std::int32_t* eos)
{
    return guard(
        [&]
        {
            if (!out || !eos)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            *eos = 0;

            auto& state = as_scanner(h);
            auto lock = state.scanner->get_env()->lock();

            cell c;
            if (!state.scanner->next_cell(c))
            {
                *eos = 1;
                return;
            }
            // borrowed, valid until the next call on this scanner - the contract abi.h
            // documents for scn_next
            fill(*out, c);
        });
}

std::int32_t HYPERLITE_CALL fn_scn_next_many(void* h, t_cell* out, std::int32_t count,
                                             std::int32_t* got)
{
    return guard(
        [&]
        {
            if (!out || !got)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            *got = 0;
            if (count <= 0)
            {
                return;
            }

            auto& state = as_scanner(h);
            auto lock = state.scanner->get_env()->lock();

            // Each cell points into sqlite's current row, which the next step
            // invalidates, so a batch has to copy. Everything is appended into the
            // scanner's staging buffer first and the pointers are patched afterwards:
            // the buffer may reallocate while filling, which would dangle a pointer
            // written earlier.
            struct span
            {
                std::size_t row, row_n, cf, cf_n, cq, cq_n, value, value_n;
                bool has_cq;
                bool has_value;
            };

            std::string& staging = state.staging;
            staging.clear();

            std::vector<span> spans;
            spans.reserve(static_cast<std::size_t>(count));

            std::int32_t n = 0;
            for (; n < count; ++n)
            {
                cell c;
                if (!state.scanner->next_cell(c))
                {
                    break;
                }

                span s{};
                const std::string_view row(c.row_key ? c.row_key : "");
                s.row = staging.size();
                s.row_n = row.size();
                staging.append(row);

                const std::string_view cf(c.column_family ? c.column_family : "");
                s.cf = staging.size();
                s.cf_n = cf.size();
                staging.append(cf);

                s.has_cq = c.column_qualifier != nullptr;
                const std::string_view cq(c.column_qualifier ? c.column_qualifier : "");
                s.cq = staging.size();
                s.cq_n = cq.size();
                staging.append(cq);

                s.has_value = c.value != nullptr;
                s.value = staging.size();
                s.value_n = c.value_len;
                if (c.value && c.value_len)
                {
                    staging.append(reinterpret_cast<const char*>(c.value), c.value_len);
                }

                spans.push_back(s);

                out[n].ts = static_cast<std::uint64_t>(c.timestamp);
                out[n].flag = c.flag;
                std::memset(out[n]._pad, 0, sizeof(out[n]._pad));
            }

            const char* base = staging.data();
            for (std::int32_t i = 0; i < n; ++i)
            {
                const span& s = spans[static_cast<std::size_t>(i)];
                out[i].row = base + s.row;
                out[i].row_n = static_cast<std::int32_t>(s.row_n);
                out[i].cf = base + s.cf;
                out[i].cf_n = static_cast<std::int32_t>(s.cf_n);
                out[i].cq = s.has_cq ? base + s.cq : nullptr;
                out[i].cq_n = static_cast<std::int32_t>(s.has_cq ? s.cq_n : 0);
                out[i].value = s.has_value ? base + s.value : nullptr;
                out[i].value_n = static_cast<std::int32_t>(s.has_value ? s.value_n : 0);
            }

            *got = n;
        });
}

std::int32_t HYPERLITE_CALL fn_key_generate(t_buf* out)
{
    return guard(
        [&]
        {
            if (!out)
            {
                HYPERLITE_THROW(error::BAD_VALUE, "Null out parameter");
            }
            char generated[key_builder::size_key + 1];
            key_builder::generate(generated);
            publish(out, std::string(generated, key_builder::size_key));
        });
}

// Deliberately not wrapped in guard(): these are plain allocator calls with no
// error slot to fill, and a failure is reported the way malloc reports it.

void* HYPERLITE_CALL fn_mem_alloc(std::int64_t size)
{
    if (size <= 0)
    {
        return nullptr;
    }
    return malloc(static_cast<std::size_t>(size));
}

void* HYPERLITE_CALL fn_mem_realloc(void* block, std::int64_t size)
{
    if (size <= 0)
    {
        free(block);
        return nullptr;
    }
    return realloc(block, static_cast<std::size_t>(size));
}

void HYPERLITE_CALL fn_mem_free(void* block)
{
    free(block);
}

const t_dispatch k_dispatch = {
    &fn_abi_revision,
    &fn_set_callbacks,
    &fn_cfg_defaults,
    &fn_sqlite_version,
    &fn_err_get,
    &fn_buf_release,

    &fn_env_get_or_create,
    &fn_env_close,
    &fn_env_optimize,

    &fn_cli_create,
    &fn_cli_release,
    &fn_cli_ns_create,
    &fn_cli_ns_open,
    &fn_cli_ns_drop,
    &fn_cli_ns_exists,

    &fn_ns_release,
    &fn_ns_name,
    &fn_ns_tbl_create,
    &fn_ns_tbl_create_like,
    &fn_ns_tbl_alter,
    &fn_ns_tbl_rename,
    &fn_ns_tbl_open,
    &fn_ns_tbl_drop,
    &fn_ns_tbl_exists,
    &fn_ns_tbl_schema,
    &fn_ns_listing,

    &fn_tbl_release,
    &fn_tbl_name,
    &fn_tbl_schema,
    &fn_tbl_mut_create,
    &fn_tbl_scn_create,

    &fn_ss_create,
    &fn_ss_release,
    &fn_ss_clear,
    &fn_ss_set_i,
    &fn_ss_set_s,
    &fn_ss_add_row,
    &fn_ss_add_column,
    &fn_ss_add_cell,
    &fn_ss_add_row_interval,
    &fn_ss_add_cell_interval,
    &fn_ss_add_column_predicate,

    &fn_mut_release,
    &fn_mut_set,
    &fn_mut_set_gen,
    &fn_mut_set_many,
    &fn_mut_del,
    &fn_mut_flush,

    &fn_scn_release,
    &fn_scn_next,
    &fn_scn_next_many,

    &fn_key_generate,

    &fn_mem_alloc,
    &fn_mem_realloc,
    &fn_mem_free,
};
} // namespace

HYPERLITE_EXPORT std::int32_t HYPERLITE_CALL q4kf9wz(std::int32_t abi_revision,
                                                     const hyperlite::abi::t_dispatch** out)
{
    if (!out)
    {
        return -1;
    }
    *out = nullptr;
    if (abi_revision != hyperlite::abi::k_abi_revision)
    {
        return hyperlite::abi::k_abi_revision;
    }
    *out = &k_dispatch;
    return 0;
}
