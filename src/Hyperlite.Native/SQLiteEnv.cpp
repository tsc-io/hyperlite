#include "pch.h"
#include "SQLiteEnv.h"
#include "SQLiteDb.h"

namespace hyperlite::sqlite
{
void verify(int st, ::sqlite3* db, char* errmsg, const char* func, const char* file, int line)
{
    if (st == SQLITE_OK || st == SQLITE_ROW || st == SQLITE_DONE)
    {
        if (errmsg)
        {
            ::sqlite3_free(errmsg);
        }
        return;
    }

    std::string what =
        (errmsg && *errmsg) ? errmsg : (db ? ::sqlite3_errmsg(db) : "sqlite failure");
    if (errmsg)
    {
        ::sqlite3_free(errmsg);
    }

    // ht4c wrapped every sqlite failure as Error::EXTERNAL and appended the
    // origin to the message. Kept, so the managed exception and message match.
    what += format("\n\tat %s (%s:%d)", func ? func : "", file ? file : "", line);
    throw exception(error::EXTERNAL, std::move(what), func, file, line);
}

namespace util
{
void stmt_finalize(::sqlite3* db, ::sqlite3_stmt** stmt)
{
    if (*stmt)
    {
        ::sqlite3_clear_bindings(*stmt);
        ::sqlite3_reset(*stmt);
        const int st = ::sqlite3_finalize(*stmt);
        *stmt = nullptr;
        HYPERLITE_SQLITE_VERIFY(st, db, nullptr);
    }
}
} // namespace util

env::env(const std::string& filename, const env_config& config)
    : auto_vacuum_(config.auto_vacuum), unique_rows_(config.unique_rows),
      no_cell_revisions_(config.no_cell_revisions), index_column_(config.index_column),
      index_column_family_(config.index_column_family),
      index_column_qualifier_(config.index_column_qualifier),
      index_timestamp_(config.index_timestamp)
{
    try
    {
        int st = ::sqlite3_open(filename == "memory" ? ":memory:" : filename.c_str(), &db_);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        // Without this any lock contention fails on the spot with SQLITE_BUSY. One process
        // normally holds one connection per database file, so this matters when the file is
        // shared with another process, or with another connection of this one, and a mutator
        // batch or a scan holds its lock for far longer than a single statement would.
        ::sqlite3_busy_timeout(db_, k_busy_timeout_ms);

        read_only_ = ::sqlite3_db_readonly(db_, "main") == 1;

        // the cache size is given in pages, so it has to follow the effective page size
        const int page_size_kb = std::max(1, std::min(config.page_size_kb, 64));

        if (!read_only_)
        {
            char* errmsg = nullptr;
            st = ::sqlite3_exec(
                db_,
                format("PRAGMA page_size=%d;"
                       "PRAGMA cache_size=%d;"
                       "PRAGMA journal_mode=%s;"
                       "PRAGMA synchronous=%s;"
                       "PRAGMA temp_store=MEMORY;"
                       "PRAGMA auto_vacuum=%d;"
                       "CREATE TABLE IF NOT EXISTS "
                       "sys_db (id INTEGER PRIMARY KEY AUTOINCREMENT, k TEXT NOT NULL, v BLOB, "
                       "UNIQUE(k));",
                       page_size_kb * 1024,
                       std::max(1, 1024 * std::max(1, config.cache_size_mb) / page_size_kb),
                       config.write_ahead_log ? "WAL" : "TRUNCATE",
                       config.synchronous ? "NORMAL" : "OFF", config.auto_vacuum)
                    .c_str(),
                nullptr, nullptr, &errmsg);

            HYPERLITE_SQLITE_VERIFY(st, db_, errmsg);
        }

        prepare_tx_statements();

        st = ::sqlite3_prepare_v2(db_, "INSERT INTO sys_db (k, v) VALUES(?, ?);", -1, &stmt_insert_,
                                  nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_prepare_v2(db_, "UPDATE sys_db SET k=? WHERE id=?;", -1, &stmt_update_key_,
                                  nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_prepare_v2(db_, "UPDATE sys_db SET v=? WHERE id=?;", -1, &stmt_update_value_,
                                  nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st =
            ::sqlite3_prepare_v2(db_, "SELECT id FROM sys_db WHERE k=?;", -1, &stmt_find_, nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_prepare_v2(db_, "SELECT id, v FROM sys_db WHERE k=?;", -1, &stmt_read_,
                                  nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

        st = ::sqlite3_prepare_v2(db_, "DELETE FROM sys_db WHERE k=?;", -1, &stmt_delete_, nullptr);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
    }
    catch (...)
    {
        if (db_)
        {
            // close_v2: the statements prepared above are not necessarily finalised yet
            ::sqlite3_close_v2(db_);
            db_ = nullptr;
        }
        throw;
    }
}

void env::prepare_tx_statements()
{
    int st = ::sqlite3_prepare_v2(db_, "BEGIN;", -1, &stmt_begin_, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_prepare_v2(db_, "COMMIT;", -1, &stmt_commit_, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_prepare_v2(db_, "ROLLBACK;", -1, &stmt_rollback_, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
}

env::~env()
{
    // a destructor must not let anything escape, every step below is best effort
    try
    {
        // dispose() calls back into sys_dispose_table, which erases from tables_, so walk
        // them out of it
        const auto tables = std::move(tables_);
        tables_.clear();
        for (const auto& entry : tables)
        {
            for (db::table* t : entry.second)
            {
                t->dispose();
            }
        }

        // A transaction still open at this point - a mutator which never got flushed, or
        // a scope abandoned by an exception - has to be ended here.
        //
        // Left open it would be rolled back by sqlite3_close below, silently discarding
        // what a flush would have committed, and it also makes the housekeeping batch
        // fail, since neither journal_mode nor BEGIN works inside a transaction. An
        // ambient transaction holds what a mutator flush would have committed, so it is
        // committed; whatever a scope left behind was abandoned and is rolled back.
        if (db_ && tx_active_)
        {
            ::sqlite3_exec(db_, tx_ambient_ ? "COMMIT;" : "ROLLBACK;", nullptr, nullptr, nullptr);
            tx_active_ = false;
            tx_ambient_ = false;
            tx_depth_ = 0;
        }

        // finalises the pooled statements; the ones still checked out belong to a
        // scanner or a mutator which outlives this environment, see the close below
        clear_stmt_cache();

        util::stmt_finalize(db_, &stmt_begin_);
        util::stmt_finalize(db_, &stmt_commit_);
        util::stmt_finalize(db_, &stmt_rollback_);
        util::stmt_finalize(db_, &stmt_insert_);
        util::stmt_finalize(db_, &stmt_update_key_);
        util::stmt_finalize(db_, &stmt_update_value_);
        util::stmt_finalize(db_, &stmt_find_);
        util::stmt_finalize(db_, &stmt_read_);
        util::stmt_finalize(db_, &stmt_delete_);

        if (db_)
        {
            if (!read_only_)
            {
                char* errmsg = nullptr;
                // the BEGIN;COMMIT; pair forces the journal to be deleted
                const char* sql = auto_vacuum_ == 2 ? "PRAGMA incremental_vacuum;"
                                                      "PRAGMA optimize;"
                                                      "PRAGMA journal_mode=DELETE;"
                                                      "BEGIN;COMMIT;"
                                                    : "PRAGMA optimize;"
                                                      "PRAGMA journal_mode=DELETE;"
                                                      "BEGIN;COMMIT;";
                ::sqlite3_exec(db_, sql, nullptr, nullptr, &errmsg);
                if (errmsg)
                {
                    ::sqlite3_free(errmsg);
                }
            }

            // close_v2, not close: a statement still checked out by a leaked scanner or
            // mutator would make close fail with SQLITE_BUSY and leave the connection - and
            // the lock on the database file - in place for the lifetime of the process.
            // close_v2 hands the connection to sqlite, which closes it once that last
            // statement is finalised.
            ::sqlite3_close_v2(db_);
            db_ = nullptr;
        }
    }
    catch (...)
    {
    }
}

void env::tx_begin()
{
    tx_recover();
    if (tx_active_ && tx_ambient_ && tx_depth_ == 0)
    {
        // The mutators own the transaction in progress. A scope cannot simply join it: it
        // would neither be able to commit its own work - tx_commit declines to commit an
        // ambient transaction, so the scope's effect would silently depend on the mutator
        // being flushed later - nor to roll back without discarding the batch with it. So
        // the batch is committed here, exactly as a flush would have, and the scope opens
        // a transaction it owns. A loss is left for the mutator's flush to report, not this.
        tx_commit_ambient();
    }

    if (!tx_active_)
    {
        util::stmt_reset reset(stmt_begin_);
        const int st = ::sqlite3_step(stmt_begin_);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
        tx_active_ = true;
        ++tx_serial_;
        tx_ambient_ = false;
        tx_depth_ = 1;
        return;
    }

    // a scope within a scope; a savepoint is what lets the inner one unwind on its own
    tx_savepoint("SAVEPOINT", tx_depth_);
    ++tx_depth_;
}

void env::tx_commit()
{
    if (tx_depth_ == 0)
    {
        return;
    }

    if (tx_depth_ > 1)
    {
        tx_savepoint("RELEASE", --tx_depth_);

        // a released scope's drops are its parent's now, to commit or to roll back
        for (auto& dropped : tx_dropped_)
        {
            dropped.second = std::min(dropped.second, tx_depth_);
        }
        return;
    }

    tx_depth_ = 0;

    // an ambient transaction stays open, the mutator owning it decides when to commit;
    // committing here would tear a mutator batch apart
    if (tx_active_ && !tx_ambient_)
    {
        tx_end(stmt_commit_);
        tx_active_ = false;
    }

    std::vector<std::int64_t> ids;
    for (const auto& dropped : tx_dropped_)
    {
        ids.push_back(dropped.first);
    }
    tx_dropped_.clear();
    dispose_tables(ids);
}

void env::tx_rollback()
{
    if (tx_depth_ == 0)
    {
        return;
    }

    if (tx_depth_ > 1)
    {
        --tx_depth_;
        tx_savepoint("ROLLBACK TO", tx_depth_);
        tx_savepoint("RELEASE", tx_depth_);

        // what the rolled back scope dropped is back
        tx_dropped_.erase(std::remove_if(tx_dropped_.begin(), tx_dropped_.end(),
                                         [this](const auto& dropped)
                                         { return dropped.second > tx_depth_; }),
                          tx_dropped_.end());
        return;
    }

    tx_depth_ = 0;
    tx_dropped_.clear();

    if (tx_active_)
    {
        tx_end(stmt_rollback_);
        tx_active_ = false;
        tx_ambient_ = false;
    }
}

// Savepoints are named after the nesting depth, not after the scope which created them,
// which holds up only as long as scopes unwind in reverse order of their creation. A scope
// living entirely within one lock() section does so by construction; a scanner's read
// transaction, which spans sections, is not a scope but an ambient transaction, see
// tx_read_begin. A scope which spans lock sections and can roll back would need to carry its
// own savepoint name instead; rolling back to the wrong one silently discards someone else's
// work.
void env::tx_savepoint(const char* verb, int depth)
{
    // not pooled: a savepoint name is part of the sql, so each depth would need its own
    // statement, and nesting is rare enough that preparing on demand is the cheaper trade
    char* errmsg = nullptr;
    const int st = ::sqlite3_exec(db_, format("%s Hyperlite_tx%d;", verb, depth).c_str(), nullptr,
                                  nullptr, &errmsg);
    HYPERLITE_SQLITE_VERIFY(st, db_, errmsg);
}

void env::tx_ensure()
{
    tx_recover();
    if (!tx_active_)
    {
        util::stmt_reset reset(stmt_begin_);
        const int st = ::sqlite3_step(stmt_begin_);
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
        tx_active_ = true;
        ++tx_serial_;
        tx_ambient_ = true;
    }
}

// Recorded only once the statement has succeeded, so tx_writes_ always says what the
// transaction holds - which is what a rollback by sqlite, whatever statement caused it, loses.
void env::tx_wrote() noexcept
{
    tx_writes_ = true;
}

void env::tx_commit_ambient()
{
    tx_sync();

    // mutations issued inside a scoped transaction get committed with that scope
    if (tx_active_ && tx_ambient_ && tx_depth_ == 0)
    {
        const bool writes = tx_writes_;
        try
        {
            tx_end(stmt_commit_);
        }
        catch (...)
        {
            // rolled back with the failed commit, and the caller may be a scope that only
            // committed the mutators' writes before its own work: the mutators are told too
            tx_lost_ = tx_lost_ || writes;
            throw;
        }
        tx_active_ = false;
        tx_ambient_ = false;
        tx_writes_ = false;
    }
}

void env::tx_flush()
{
    try
    {
        tx_commit_ambient();
    }
    catch (...)
    {
        // this exception is the report, not a second one at the next flush
        tx_lost_ = false;
        throw;
    }

    // what sqlite rolled back with a failed write is lost, and the mutator's flush that was to
    // commit it says so rather than report success - once
    if (tx_lost_)
    {
        tx_lost_ = false;
        HYPERLITE_THROW(error::EXTERNAL,
                        "Writes were lost: a failed write made sqlite roll back their transaction");
    }
}

void env::tx_batch_begin()
{
    tx_ensure();
    char* errmsg = nullptr;
    const int st = ::sqlite3_exec(db_, "SAVEPOINT Hyperlite_batch;", nullptr, nullptr, &errmsg);
    HYPERLITE_SQLITE_VERIFY(st, db_, errmsg);
    tx_batch_held_ = tx_writes_;
}

void env::tx_batch_commit()
{
    char* errmsg = nullptr;
    const int st = ::sqlite3_exec(db_, "RELEASE Hyperlite_batch;", nullptr, nullptr, &errmsg);
    HYPERLITE_SQLITE_VERIFY(st, db_, errmsg);
}

void env::tx_batch_rollback() noexcept
{
    // fails harmlessly when sqlite has already rolled back the whole transaction, savepoint
    // and all; tx_sync then sees that
    if (::sqlite3_exec(db_, "ROLLBACK TO Hyperlite_batch;", nullptr, nullptr, nullptr) == SQLITE_OK)
    {
        ::sqlite3_exec(db_, "RELEASE Hyperlite_batch;", nullptr, nullptr, nullptr);
        // undone, the batch wrote nothing: the transaction holds what it held before
        tx_writes_ = tx_batch_held_;
    }
    else if (!::sqlite3_get_autocommit(db_))
    {
        // the batch could not be undone and the transaction is still open: committed later, it
        // would carry half the batch, so the whole transaction goes, and with it what it held
        const bool held = tx_ambient_ && tx_batch_held_;
        tx_abandon();
        tx_lost_ = tx_lost_ || held;
        return;
    }
    else
    {
        // sqlite rolled it all back: what is lost beyond the batch is what came before it
        tx_writes_ = tx_batch_held_;
    }
    tx_sync();
}

std::uint64_t env::tx_read_begin()
{
    tx_recover();
    if (tx_active_)
    {
        if (tx_read_serial_ != tx_serial_)
        {
            return 0;
        }
        ++tx_readers_;
        return tx_serial_;
    }

    util::stmt_reset reset(stmt_begin_);
    const int st = ::sqlite3_step(stmt_begin_);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
    tx_active_ = true;
    tx_ambient_ = true;
    tx_writes_ = false;
    tx_read_serial_ = ++tx_serial_;
    tx_readers_ = 1;
    return tx_serial_;
}

void env::tx_read_end(std::uint64_t token)
{
    // A flush or a scope may have committed the transaction since, and another one begun: that
    // one is not the scanner's to end, which the serial tells.
    if (!token || token != tx_serial_ || token != tx_read_serial_ || !tx_active_)
    {
        return;
    }
    if (--tx_readers_ == 0 && tx_ambient_ && tx_depth_ == 0 && !tx_writes_)
    {
        tx_end(stmt_commit_);
        tx_active_ = false;
        tx_ambient_ = false;
    }
}

void env::tx_end(::sqlite3_stmt* stmt)
{
    util::stmt_reset reset(stmt);
    const int st = ::sqlite3_step(stmt);
    try
    {
        HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
    }
    catch (...)
    {
        // the exception carries sqlite's message already, the rollback below would replace it
        tx_abandon();
        throw;
    }
}

// A COMMIT that fails has either rolled the transaction back on its own - a full disk, an I/O
// error - or left it open - SQLITE_BUSY. Left open, the next flush or the destructor would
// commit what the caller has just been told failed, so it is rolled back: what was reported
// as failed stays failed. Either way the flags then say what sqlite says, rather than claim
// a transaction that is gone - after which BEGIN would be skipped and every flush would fail.
void env::tx_abandon() noexcept
{
    if (!::sqlite3_get_autocommit(db_))
    {
        ::sqlite3_exec(db_, "ROLLBACK;", nullptr, nullptr, nullptr);
    }
    tx_active_ = !::sqlite3_get_autocommit(db_);
    tx_depth_ = 0;
    tx_dropped_.clear();
    tx_writes_ = false;
    // a transaction the rollback could not end is owned by no one now: tx_recover rolls it
    // back before anything else runs, so nothing joins it and nothing commits it
    tx_ambient_ = false;
}

void env::tx_sync() noexcept
{
    if (tx_active_ && ::sqlite3_get_autocommit(db_))
    {
        // gone: left as it was, BEGIN would be skipped and every later write would commit on
        // its own, outside any batch
        tx_lost_ = tx_lost_ || (tx_ambient_ && tx_writes_);
        tx_active_ = false;
        tx_ambient_ = false;
        tx_writes_ = false;
        tx_depth_ = 0;
        tx_dropped_.clear();
    }
}

void env::tx_recover()
{
    tx_sync();
    if (tx_active_ && !tx_ambient_ && tx_depth_ == 0)
    {
        tx_abandon();
        if (tx_active_)
        {
            HYPERLITE_THROW(error::EXTERNAL, "A failed transaction could not be rolled back");
        }
    }
}

::sqlite3_stmt* env::acquire_stmt(const std::string& sql)
{
    const auto it = stmt_cache_.find(sql);
    if (it != stmt_cache_.end() && !it->second.empty())
    {
        ::sqlite3_stmt* stmt = it->second.back();
        it->second.pop_back();
        --stmt_cache_size_;
        return stmt;
    }

    ::sqlite3_stmt* stmt = nullptr;
    const int st =
        ::sqlite3_prepare_v3(db_, sql.c_str(), -1, SQLITE_PREPARE_PERSISTENT, &stmt, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
    return stmt;
}

void env::release_stmt(::sqlite3_stmt*& stmt)
{
    if (!stmt)
    {
        return;
    }

    ::sqlite3_reset(stmt);

    // unlike stmt_reset this has to drop the bindings: a pooled statement would
    // otherwise retain SQLITE_STATIC pointers into memory owned by the scanner or
    // mutator that just went away
    ::sqlite3_clear_bindings(stmt);

    if (stmt_cache_size_ < k_max_cached_stmt)
    {
        if (const char* sql = ::sqlite3_sql(stmt))
        {
            stmt_cache_[sql].push_back(stmt);
            ++stmt_cache_size_;
            stmt = nullptr;
            return;
        }
    }

    ::sqlite3_finalize(stmt);
    stmt = nullptr;
}

void env::clear_stmt_cache()
{
    for (auto& entry : stmt_cache_)
    {
        for (::sqlite3_stmt* stmt : entry.second)
        {
            ::sqlite3_finalize(stmt);
        }
        entry.second.clear();
    }
    stmt_cache_.clear();
    stmt_cache_size_ = 0;
}

void env::sys_insert(const char* name, int len, const void* value, int size, std::int64_t* rowid)
{
    util::stmt_reset reset(stmt_insert_);

    int st = ::sqlite3_bind_text(stmt_insert_, 1, name, len, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_bind_blob(stmt_insert_, 2, value, size, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_step(stmt_insert_);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    if (rowid)
    {
        *rowid = ::sqlite3_last_insert_rowid(db_);
    }
}

void env::sys_update_key(std::int64_t rowid, const char* key, int len)
{
    util::stmt_reset reset(stmt_update_key_);

    int st = ::sqlite3_bind_text(stmt_update_key_, 1, key, len, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_bind_int64(stmt_update_key_, 2, rowid);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_step(stmt_update_key_);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
}

void env::sys_update_value(std::int64_t rowid, const void* value, int size)
{
    util::stmt_reset reset(stmt_update_value_);

    int st = ::sqlite3_bind_blob(stmt_update_value_, 1, value, size, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_bind_int64(stmt_update_value_, 2, rowid);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_step(stmt_update_value_);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
}

bool env::sys_read(const char* name, int len, dynamic_buffer& buf, std::int64_t* rowid)
{
    buf.clear();

    util::stmt_reset reset(stmt_read_);

    int st = ::sqlite3_bind_text(stmt_read_, 1, name, len, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    if ((st = ::sqlite3_step(stmt_read_)) == SQLITE_ROW)
    {
        if (rowid)
        {
            *rowid = ::sqlite3_column_int64(stmt_read_, 0);
        }
        if (::sqlite3_column_type(stmt_read_, 1) != SQLITE_NULL)
        {
            if (const void* v = ::sqlite3_column_blob(stmt_read_, 1))
            {
                buf.add(v, static_cast<std::size_t>(::sqlite3_column_bytes(stmt_read_, 1)));
            }
        }
        return true;
    }
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
    return false;
}

bool env::sys_exists(const char* name, int len, std::int64_t* rowid)
{
    util::stmt_reset reset(stmt_find_);

    int st = ::sqlite3_bind_text(stmt_find_, 1, name, len, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_step(stmt_find_);
    const bool exists = st == SQLITE_ROW;
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    if (exists && rowid)
    {
        *rowid = ::sqlite3_column_int64(stmt_find_, 0);
    }
    return exists;
}

bool env::sys_delete(const char* name, int len, std::int64_t* rowid)
{
    if (!sys_exists(name, len, rowid))
    {
        return false;
    }

    util::stmt_reset reset(stmt_delete_);

    int st = ::sqlite3_bind_text(stmt_delete_, 1, name, len, nullptr);
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);

    st = ::sqlite3_step(stmt_delete_);
    const bool dropped = st == SQLITE_DONE;
    HYPERLITE_SQLITE_VERIFY(st, db_, nullptr);
    return dropped;
}

void env::sys_create_table(const char* name, int len, const void* value, int size, std::int64_t& id)
{
    sys_insert(name, len, value, size, &id);

    const char* row_column = unique_rows_ ? "r TEXT NOT NULL PRIMARY KEY" : "r TEXT NOT NULL";
    const char* unique_constraint =
        no_cell_revisions_ ? "UNIQUE(r, cf, cq)" : "UNIQUE(r, cf, cq, ts)";

    char* errmsg = nullptr;
    int st = ::sqlite3_exec(
        db_,
        format("CREATE TABLE IF NOT EXISTS "
               "t%lld (%s, cf INTEGER NOT NULL, cq TEXT NOT NULL, ts INTEGER NOT NULL, v BLOB,"
               "%s);",
               static_cast<long long>(id), row_column, unique_constraint)
            .c_str(),
        nullptr, nullptr, &errmsg);
    HYPERLITE_SQLITE_VERIFY(st, db_, errmsg);

    struct index_spec
    {
        bool enabled;
        const char* name;
        const char* columns;
    };
    const index_spec indices[] = {
        {index_column_, "i_cfcq", "(cf, cq)"},
        {index_column_family_, "i_cf", "(cf)"},
        {index_column_qualifier_, "i_cq", "(cq)"},
        {index_timestamp_, "i_ts", "(ts)"},
    };

    for (const auto& idx : indices)
    {
        if (!idx.enabled)
        {
            continue;
        }
        errmsg = nullptr;
        st = ::sqlite3_exec(db_,
                            format("CREATE INDEX IF NOT EXISTS %s ON t%lld %s;", idx.name,
                                   static_cast<long long>(id), idx.columns)
                                .c_str(),
                            nullptr, nullptr, &errmsg);
        HYPERLITE_SQLITE_VERIFY(st, db_, errmsg);
    }
}

bool env::sys_open_table(db::table* t, std::int64_t& id)
{
    const char* key = nullptr;
    const int len = t->to_key(key);
    dynamic_buffer buf;
    if (sys_read(key, len, buf, &id))
    {
        t->from_record(buf);
        tables_[id].insert(t);
        return true;
    }
    return false;
}

void env::sys_refresh_table(std::int64_t id, const std::string& schema_xml,
                            const std::shared_ptr<const schema>& sch) noexcept
{
    const auto it = tables_.find(id);
    if (it != tables_.end())
    {
        for (db::table* t : it->second)
        {
            t->refresh(schema_xml, sch);
        }
    }
}

void env::sys_rename_table(std::int64_t id, const std::string& name) noexcept
{
    const auto it = tables_.find(id);
    if (it != tables_.end())
    {
        for (db::table* t : it->second)
        {
            t->rename(name);
        }
    }
}

void env::sys_dispose_table(std::int64_t id, db::table* t)
{
    // only this handle: the table's other open handles still have to follow an alter
    const auto it = tables_.find(id);
    if (it != tables_.end())
    {
        it->second.erase(t);
        if (it->second.empty())
        {
            tables_.erase(it);
        }
    }
}

bool env::sys_delete_table(const char* name, int len)
{
    std::int64_t id = 0;
    if (!sys_delete(name, len, &id))
    {
        return false;
    }

    // pooled scanner statements reference t<id>, they have to go before the drop
    clear_stmt_cache();

    // finalise begin, commit and rollback, otherwise the table stays locked
    util::stmt_finalize(db_, &stmt_begin_);
    util::stmt_finalize(db_, &stmt_commit_);
    util::stmt_finalize(db_, &stmt_rollback_);

    char* errmsg = nullptr;
    const int dropped =
        ::sqlite3_exec(db_, format("DROP TABLE t%lld;", static_cast<long long>(id)).c_str(),
                       nullptr, nullptr, &errmsg);

    // .. and prepare them again, whether or not the drop succeeded
    prepare_tx_statements();

    HYPERLITE_SQLITE_VERIFY(dropped, db_, errmsg);

    // The open handles go once the drop is committed, not before: a drop can fail - sqlite
    // refuses it while another statement is reading - and so can a later step of the scope it
    // is part of, a deep namespace drop among them. Rolled back, the table is still there to be
    // used through them.
    if (tx_depth_ == 0)
    {
        dispose_tables({id});
    }
    else
    {
        tx_dropped_.emplace_back(id, tx_depth_);
    }
    return true;
}

void env::dispose_tables(const std::vector<std::int64_t>& ids)
{
    for (const std::int64_t id : ids)
    {
        const auto it = tables_.find(id);
        if (it != tables_.end())
        {
            // shallow copy, dispose() may call back into sys_dispose_table(id)
            std::set<db::table*> copy = it->second;
            for (db::table* t : copy)
            {
                t->dispose();
            }
            tables_.erase(id);
        }
    }
}
} // namespace hyperlite::sqlite
