// The sqlite connection, the pragma and DDL that shape the database, the sys_db
// accessors, the scoped/ambient transaction pair and the prepared statement pool.
//
// The pragma block, the sys_db schema, the t<id> DDL and the config defaults are
// part of the on-disk contract: a database written with one set of them has to be
// readable with the same set, so none of the five may drift without a migration.

#pragma once

#include "Support.h"
#include "sqlite3.h"

#include <memory>
#include <mutex>
#include <set>
#include <string>
#include <unordered_map>
#include <vector>

namespace hyperlite
{
class schema;
}

namespace hyperlite::sqlite
{
namespace db
{
class table;
}

/// The environment configuration. The defaults are part of the on-disk contract and
/// must stay as they are; Hyperlite reports them through Context.Properties.
struct env_config
{
    int cache_size_mb = 64;
    int page_size_kb = 4;
    bool write_ahead_log = false;
    bool synchronous = false;
    int auto_vacuum = 0; ///< 0 = NONE, 1 = FULL, 2 = INCREMENTAL
    bool unique_rows = false;
    bool no_cell_revisions = false;
    bool index_column = false;
    bool index_column_family = false;
    bool index_column_qualifier = false;
    bool index_timestamp = false;
};

class env;
using env_ptr = std::shared_ptr<env>;

/// Translates a sqlite status into an hyperlite::exception, matching what
/// HT4C_SQLITE_VERIFY plus HT4C_SQLITE_RETHROW produced: error::EXTERNAL with the
/// sqlite message.
///
/// errmsg, when given, is a sqlite allocated string and is freed here.
void verify(int st, ::sqlite3* db, char* errmsg, const char* func, const char* file, int line);

#define HYPERLITE_SQLITE_VERIFY(st, db, msg)                                                       \
    ::hyperlite::sqlite::verify((st), (db), (msg), HYPERLITE_FUNC, __FILE__, __LINE__)

// enable_shared_from_this so that a caller holding a raw env* can pin the environment for
// as long as it needs it - see the release paths in api.cpp, which lock the environment and
// then drop the very reference that may own it.
class env final : public std::enable_shared_from_this<env>
{
public:
    /// filename == "memory" opens an in memory database, as ht4c did.
    env(const std::string& filename, const env_config& config);
    ~env();

    env(const env&) = delete;
    env& operator=(const env&) = delete;

    ::sqlite3* handle() const noexcept { return db_; }
    bool unique_rows() const noexcept { return unique_rows_; }
    bool no_cell_revisions() const noexcept { return no_cell_revisions_; }
    bool read_only() const noexcept { return read_only_; }

    //
    // transactions
    //

    /// Scoped transaction, use tx.
    ///
    /// A scope always owns a transaction, never joins one. Beginning a scope while a
    /// mutator's ambient transaction is in progress commits that batch first, exactly as
    /// a flush would have: joining it instead would leave the scope unable to commit its
    /// own work, and unable to roll back without discarding the batch as well.
    ///
    /// Nested scopes are savepoints, so an inner scope unwinds on its own rather than
    /// leaving its half finished work for the outer one to commit.
    void tx_begin();
    void tx_commit();
    void tx_rollback();

    /// Ambient transaction, used by the mutators. Opens one unless a transaction
    /// is already in progress; tx_flush commits it. Mutations issued inside a
    /// scoped transaction become part of that scope instead.
    void tx_ensure();
    /// Records that a mutation in the ambient transaction succeeded.
    void tx_wrote() noexcept;
    /// Commits the ambient transaction, and reports writes sqlite rolled back since the last
    /// flush. The mutators' flush; everything else commits with tx_commit_ambient, which
    /// leaves that report for the mutators.
    void tx_flush();
    void tx_commit_ambient();

    /// A batch of mutations, all or nothing within the transaction it joins: a batch that
    /// fails part way leaves nothing behind for the next flush to commit.
    void tx_batch_begin();
    void tx_batch_commit();
    void tx_batch_rollback() noexcept;

    /// A read transaction, which a scanner takes so that its statements do not each run an
    /// implicit one. It is opened as an ambient transaction, so that a mutation made while it
    /// is open joins it and the mutator's flush commits it - the scan carries on outside a
    /// transaction then, as a single statement scan does. Another scanner's read transaction is
    /// shared; a mutator's or a scope's is joined, and the token returned for it is 0.
    std::uint64_t tx_read_begin();
    /// Gives up the share of the read transaction the token names, and ends that transaction
    /// once no scanner holds it any more - unless a mutation has joined it since: that one is
    /// the mutator's to commit. A token whose transaction has ended since is ignored.
    void tx_read_end(std::uint64_t token);

    //
    // prepared statement pool
    //

    /// Statements are handed out exclusively, release_stmt returns them. Bind
    /// parameters rather than formatting literals, otherwise nothing recycles.
    ::sqlite3_stmt* acquire_stmt(const std::string& sql);
    void release_stmt(::sqlite3_stmt*& stmt);
    void clear_stmt_cache();

    //
    // sys_db
    //

    void sys_insert(const char* name, int len, const void* value, int size,
                    std::int64_t* rowid = nullptr);
    void sys_update_key(std::int64_t rowid, const char* key, int len);
    void sys_update_value(std::int64_t rowid, const void* value, int size);
    bool sys_read(const char* name, int len, dynamic_buffer& buf, std::int64_t* rowid = nullptr);
    bool sys_exists(const char* name, int len, std::int64_t* rowid = nullptr);
    bool sys_delete(const char* name, int len, std::int64_t* rowid = nullptr);

    void sys_create_table(const char* name, int len, const void* value, int size, std::int64_t& id);
    bool sys_open_table(db::table* t, std::int64_t& id);
    /// Hands an altered table's schema to every open handle on it.
    void sys_refresh_table(std::int64_t id, const std::string& schema_xml,
                           const std::shared_ptr<const schema>& sch) noexcept;
    /// Hands a renamed table's new name to every open handle on it.
    void sys_rename_table(std::int64_t id, const std::string& name) noexcept;
    void sys_dispose_table(std::int64_t id, db::table* t);
    /// Disposes every open handle on the tables named.
    void dispose_tables(const std::vector<std::int64_t>& ids);
    bool sys_delete_table(const char* name, int len);

    //
    // locking
    //

    // sqlite is compiled with SQLITE_THREADSAFE=2 (multi-thread), which omits the
    // mutex on the connection object. Serialising is therefore this lock's job
    // alone: every path touching the connection, or a statement prepared from it,
    // has to hold it. Missing it is a data race inside sqlite, not merely on our
    // own state.
    std::unique_lock<std::recursive_mutex> lock()
    {
        return std::unique_lock<std::recursive_mutex>(mutex_);
    }

private:
    ::sqlite3* db_ = nullptr;
    bool read_only_ = false;

    /// How long sqlite waits for a lock held by another connection before it gives up
    /// with SQLITE_BUSY.
    static constexpr int k_busy_timeout_ms = 5000;

    bool tx_active_ = false;           ///< a BEGIN has been issued
    bool tx_ambient_ = false;          ///< the mutators' or the scanners' transaction
    bool tx_writes_ = false;           ///< the ambient transaction holds a mutation
    bool tx_batch_held_ = false;       ///< tx_writes_ when the batch in progress began
    bool tx_lost_ = false;             ///< sqlite rolled back an ambient transaction that held one
    int tx_depth_ = 0;                 ///< scoped transaction nesting level
    std::uint64_t tx_serial_ = 0;      ///< counts the BEGINs, so names the transaction in progress
    std::uint64_t tx_read_serial_ = 0; ///< the transaction the scanners share, if a read one
    int tx_readers_ = 0;               ///< the scanners sharing it
    /// Tables the scope in progress dropped, with the depth of the scope that owns the drop:
    /// their open handles go once the outermost scope commits.
    std::vector<std::pair<std::int64_t, int>> tx_dropped_;

    /// Savepoint of a nested scope, verb is SAVEPOINT, RELEASE or ROLLBACK TO.
    void tx_savepoint(const char* verb, int depth);

    /// Brings the flags back in line with sqlite after a COMMIT or ROLLBACK failed.
    void tx_abandon() noexcept;

    /// Rolls back a transaction a failed ROLLBACK left behind, before anything joins it.
    void tx_recover();

    /// Brings the flags back in line with sqlite after it rolled a transaction back on its
    /// own, as it does when a statement fails with a full disk or an I/O error.
    void tx_sync() noexcept;

    /// Steps COMMIT or ROLLBACK, and abandons the transaction if that fails.
    void tx_end(::sqlite3_stmt* stmt);

    int auto_vacuum_;
    bool unique_rows_;
    bool no_cell_revisions_;
    bool index_column_;
    bool index_column_family_;
    bool index_column_qualifier_;
    bool index_timestamp_;

    ::sqlite3_stmt* stmt_begin_ = nullptr;
    ::sqlite3_stmt* stmt_commit_ = nullptr;
    ::sqlite3_stmt* stmt_rollback_ = nullptr;
    ::sqlite3_stmt* stmt_insert_ = nullptr;
    ::sqlite3_stmt* stmt_update_key_ = nullptr;
    ::sqlite3_stmt* stmt_update_value_ = nullptr;
    ::sqlite3_stmt* stmt_find_ = nullptr;
    ::sqlite3_stmt* stmt_read_ = nullptr;
    ::sqlite3_stmt* stmt_delete_ = nullptr;

    std::unordered_map<std::int64_t, std::set<db::table*>> tables_;

    static constexpr int k_max_cached_stmt = 128;
    std::unordered_map<std::string, std::vector<::sqlite3_stmt*>> stmt_cache_;
    int stmt_cache_size_ = 0;

    std::recursive_mutex mutex_;

    void prepare_tx_statements();
};

namespace util
{
/// Finalises a statement, clearing its bindings first so it releases any
/// SQLITE_STATIC pointer it still holds.
void stmt_finalize(::sqlite3* db, ::sqlite3_stmt** stmt);

/// Resets a statement on scope exit. Deliberately does not clear the bindings:
/// every statement guarded this way re-binds all of its parameters before the
/// next step. Bindings are SQLITE_STATIC, so a statement keeps pointers into
/// caller memory until re-bound; never step one without binding first.
class stmt_reset final
{
public:
    explicit stmt_reset(::sqlite3_stmt* stmt) : stmt_(stmt) {}
    ~stmt_reset() { ::sqlite3_reset(stmt_); }

    stmt_reset(const stmt_reset&) = delete;
    stmt_reset& operator=(const stmt_reset&) = delete;

private:
    ::sqlite3_stmt* stmt_;
};

/// Returns a pooled statement back to the environment on scope exit.
class stmt_release final
{
public:
    stmt_release(env* e, ::sqlite3_stmt*& stmt) : env_(e), stmt_(stmt) {}
    ~stmt_release() { env_->release_stmt(stmt_); }

    stmt_release(const stmt_release&) = delete;
    stmt_release& operator=(const stmt_release&) = delete;

private:
    env* env_;
    ::sqlite3_stmt*& stmt_;
};

/// Scoped transaction. Rolls back unless committed.
class tx final
{
public:
    explicit tx(env* e) : env_(e)
    {
        env_->tx_begin();
        active_ = true;
    }

    ~tx()
    {
        if (active_)
        {
            // a rollback while unwinding must not throw
            try
            {
                env_->tx_rollback();
            }
            catch (...)
            {
            }
        }
    }

    tx(const tx&) = delete;
    tx& operator=(const tx&) = delete;

    void begin()
    {
        if (!active_)
        {
            env_->tx_begin();
            active_ = true;
        }
    }

    void commit()
    {
        if (active_)
        {
            env_->tx_commit();
            active_ = false;
        }
    }

private:
    env* env_;
    bool active_ = false;
};
} // namespace util
} // namespace hyperlite::sqlite
