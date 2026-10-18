// Native smoke test for Hyperlite.Native: drives the dispatch table directly so the
// storage engine can be validated before the managed layer exists.

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <regex>
#include <string>
#include <vector>

#include "abi.h"
#include "platform.h"

using namespace hyperlite::abi;

static const t_dispatch* d = nullptr;
static int g_failures = 0;

#define CHECK(cond, what)                                                                          \
    do                                                                                             \
    {                                                                                              \
        if (!(cond))                                                                               \
        {                                                                                          \
            std::printf("FAIL %s (line %d)\n", what, __LINE__);                                    \
            ++g_failures;                                                                          \
        }                                                                                          \
    } while (0)

static void report(std::int32_t rc, const char* what)
{
    if (rc == 0)
    {
        return;
    }
    t_error e{};
    std::string msg = "<no detail>";
    if (d->err_get(0, &e))
    {
        msg.assign(e.text, static_cast<std::size_t>(e.text_n));
    }
    std::printf("FAIL %s rc=%d (%08x): %s\n", what, rc, rc, msg.c_str());
    ++g_failures;
}

#define OK(expr, what)                                                                             \
    do                                                                                             \
    {                                                                                              \
        const std::int32_t rc__ = (expr);                                                          \
        report(rc__, what);                                                                        \
    } while (0)

//
// managed style regex callbacks, std::regex standing in for .NET Regex
//

static void* HYPERLITE_CALL re_compile(const char* pattern, std::int32_t n)
{
    try
    {
        return new std::regex(std::string(pattern, static_cast<std::size_t>(n)),
                              std::regex::ECMAScript);
    }
    catch (...)
    {
        return nullptr;
    }
}

static std::int32_t HYPERLITE_CALL re_match(void* re, const char* text, std::int32_t n)
{
    try
    {
        const std::string s(text, static_cast<std::size_t>(n));
        return std::regex_search(s, *static_cast<std::regex*>(re)) ? 1 : 0;
    }
    catch (...)
    {
        return 0;
    }
}

static void HYPERLITE_CALL re_release(void* re)
{
    delete static_cast<std::regex*>(re);
}

static std::string take(t_buf& b)
{
    std::string s(b.p, static_cast<std::size_t>(b.n));
    d->buf_release(&b);
    return s;
}

static t_cell make_cell(const char* row, const char* cf, const char* cq, const char* value,
                        std::uint64_t ts = 0)
{
    t_cell c{};
    c.row = row;
    c.row_n = row ? static_cast<std::int32_t>(std::strlen(row)) : 0;
    c.cf = cf;
    c.cf_n = cf ? static_cast<std::int32_t>(std::strlen(cf)) : 0;
    c.cq = cq;
    c.cq_n = cq ? static_cast<std::int32_t>(std::strlen(cq)) : 0;
    c.value = value;
    c.value_n = value ? static_cast<std::int32_t>(std::strlen(value)) : 0;
    c.ts = ts;
    c.flag = cf_insert;
    return c;
}

/// The native floor benchmark, in bench.cpp.
int run_bench(int cells, int repetitions);

int main(int argc, char** argv)
{
    // "--bench [cells] [repetitions]" measures instead of asserting, see bench.cpp
    if (argc > 1 && std::strcmp(argv[1], "--bench") == 0)
    {
        const int cells = argc > 2 ? std::atoi(argv[2]) : 150000;
        const int reps = argc > 3 ? std::atoi(argv[3]) : 5;
        return run_bench(cells, reps);
    }

    d = load_dispatch();
    if (!d)
    {
        return 1;
    }
    std::printf("abi revision %d, sqlite %s\n", d->abi_revision(), d->sqlite_version());

    t_callbacks cb{&re_compile, &re_match, &re_release};
    OK(d->set_callbacks(&cb), "set_callbacks");

    //
    // defaults must match ht4c
    //
    t_cfg cfg{};
    d->cfg_defaults(&cfg);
    CHECK(cfg.cache_size_mb == 64, "default cache_size_mb == 64");
    CHECK(cfg.page_size_kb == 4, "default page_size_kb == 4");
    CHECK(cfg.write_ahead_log == 0, "default wal off");
    CHECK(cfg.synchronous == 0, "default synchronous off");
    CHECK(cfg.auto_vacuum == 0, "default auto_vacuum 0");
    CHECK(cfg.unique_rows == 0 && cfg.no_cell_revisions == 0, "default row options off");
    CHECK(cfg.index_column == 0 && cfg.index_column_family == 0 &&
              cfg.index_column_qualifier == 0 && cfg.index_timestamp == 0,
          "default indices off");

    //
    // environment
    //
    remove_file("smoke.db");
    void* env = nullptr;
    OK(d->env_get_or_create("smoke.db", -1, &cfg, &env), "env_get_or_create");
    if (!env)
    {
        return 1;
    }

    void* cli = nullptr;
    OK(d->cli_create(env, &cli), "cli_create");

    //
    // namespaces
    //
    std::int32_t exists = -1;

    // the root always exists, whichever way it is spelled
    OK(d->cli_ns_exists(cli, "/", -1, nullptr, &exists), "ns_exists root");
    CHECK(exists == 1, "root namespace always exists");
    OK(d->cli_ns_exists(cli, "", -1, nullptr, &exists), "ns_exists empty");
    CHECK(exists == 1, "empty namespace name is the root");

    OK(d->cli_ns_exists(cli, "test", -1, nullptr, &exists), "ns_exists before");
    CHECK(exists == 0, "namespace absent before create");

    // a leading or trailing separator is canonicalized away
    OK(d->cli_ns_exists(cli, "/test/", -1, nullptr, &exists), "ns_exists canonicalized");
    CHECK(exists == 0, "canonicalized name resolves to the same namespace");

    OK(d->cli_ns_create(cli, "test", -1, nullptr, 0), "ns_create");
    OK(d->cli_ns_exists(cli, "test", -1, nullptr, &exists), "ns_exists after");
    CHECK(exists == 1, "namespace present after create");

    CHECK(d->cli_ns_create(cli, "/", -1, nullptr, 0) == 42,
          "creating the root reports NAMESPACE_EXISTS");

    // creating again must fail with NAMESPACE_EXISTS (42)
    const std::int32_t dup = d->cli_ns_create(cli, "test", -1, nullptr, 0);
    CHECK(dup == 42, "duplicate namespace reports NAMESPACE_EXISTS");

    // .. unless if-not-exists is asked for
    OK(d->cli_ns_create(cli, "test", -1, nullptr, fl_if_not_exists), "ns_create if_not_exists");

    OK(d->cli_ns_create(cli, "test/sub/deep", -1, nullptr, fl_create_intermediate),
       "ns_create intermediate");
    OK(d->cli_ns_exists(cli, "test/sub", -1, nullptr, &exists), "ns_exists intermediate");
    CHECK(exists == 1, "intermediate namespace created");

    void* ns = nullptr;
    OK(d->cli_ns_open(cli, "test", -1, nullptr, &ns), "ns_open");

    t_buf name{};
    OK(d->ns_name(ns, &name), "ns_name");
    CHECK(take(name) == "test", "ns_name == test");

    //
    // tables
    //
    const char* schema =
        "<Schema><AccessGroup name=\"default\">"
        "<ColumnFamily><Name>a</Name></ColumnFamily>"
        "<ColumnFamily><Name>b</Name><Options><MaxVersions>2</MaxVersions></Options></ColumnFamily>"
        "</AccessGroup></Schema>";

    OK(d->ns_tbl_exists(ns, "t1", -1, &exists), "tbl_exists before");
    CHECK(exists == 0, "table absent before create");

    OK(d->ns_tbl_create(ns, "t1", -1, schema, -1), "tbl_create");
    OK(d->ns_tbl_exists(ns, "t1", -1, &exists), "tbl_exists after");
    CHECK(exists == 1, "table present after create");

    // opening a missing table reports TABLE_NOT_FOUND (22), not the storage layer's
    // HYPERSPACE_FILE_NOT_FOUND
    {
        void* missing_tbl = nullptr;
        CHECK(d->ns_tbl_open(ns, "does-not-exist", -1, 0, &missing_tbl) == 22,
              "opening a missing table reports TABLE_NOT_FOUND");
    }

    // an index table name is reserved and rejected with SYNTAX_ERROR (35)
    CHECK(d->ns_tbl_exists(ns, "^t1", -1, &exists) == 0, "tbl_exists tolerates a caret name");
    {
        void* bad_tbl = nullptr;
        CHECK(d->ns_tbl_open(ns, "^t1", -1, 0, &bad_tbl) == 35,
              "a caret table name reports SYNTAX_ERROR");
    }

    // duplicate table reports MASTER_TABLE_EXISTS (0x00040001)
    CHECK(d->ns_tbl_create(ns, "t1", -1, schema, -1) == 0x00040001,
          "duplicate table reports MASTER_TABLE_EXISTS");

    t_buf sbuf{};
    OK(d->ns_tbl_schema(ns, "t1", -1, fl_with_ids, &sbuf), "tbl_schema with ids");
    const std::string with_ids = take(sbuf);
    CHECK(with_ids.find("<Generation>") != std::string::npos, "schema with ids has Generation");
    CHECK(with_ids.find("id=\"1\"") != std::string::npos, "schema with ids assigns id 1");
    CHECK(with_ids.find("id=\"2\"") != std::string::npos, "schema with ids assigns id 2");

    OK(d->ns_tbl_schema(ns, "t1", -1, 0, &sbuf), "tbl_schema without ids");
    const std::string without_ids = take(sbuf);
    CHECK(without_ids.find("id=") == std::string::npos, "schema without ids has no id");
    CHECK(without_ids.find("<Name>a</Name>") != std::string::npos, "schema keeps family a");
    CHECK(without_ids.find("<MaxVersions>2</MaxVersions>") != std::string::npos,
          "schema keeps MaxVersions");

    OK(d->ns_tbl_create_like(ns, "t2", -1, "t1", -1), "tbl_create_like");
    OK(d->ns_tbl_exists(ns, "t2", -1, &exists), "tbl_exists t2");
    CHECK(exists == 1, "create_like produced t2");

    void* tbl = nullptr;
    OK(d->ns_tbl_open(ns, "t1", -1, 0, &tbl), "tbl_open");

    t_buf tname{};
    OK(d->tbl_name(tbl, &tname), "tbl_name");
    // the full path, as ht4c::SQLite::SQLiteTable::getName reported it
    CHECK(take(tname) == "test/t1", "tbl_name == test/t1");

    //
    // mutate
    //
    void* mut = nullptr;
    OK(d->tbl_mut_create(tbl, 0, 0, 0, &mut), "mut_create");

    {
        t_cell c = make_cell("r1", "a", nullptr, "v-r1-a");
        OK(d->mut_set(mut, &c), "mut_set r1/a");
    }
    {
        t_cell c = make_cell("r1", "b", "q1", "v-r1-b-q1");
        OK(d->mut_set(mut, &c), "mut_set r1/b:q1");
    }
    {
        t_cell c = make_cell("r2", "a", nullptr, "v-r2-a");
        OK(d->mut_set(mut, &c), "mut_set r2/a");
    }

    // batched
    std::vector<t_cell> batch{
        make_cell("r3", "a", nullptr, "v-r3-a"),
        make_cell("r4", "a", nullptr, "v-r4-a"),
        make_cell("r5", "b", "q2", "v-r5-b-q2"),
    };
    OK(d->mut_set_many(mut, batch.data(), static_cast<std::int32_t>(batch.size())), "mut_set_many");

    // auto generated row key
    t_buf gen{};
    {
        t_cell c = make_cell(nullptr, "a", nullptr, "v-generated");
        OK(d->mut_set_gen(mut, &c, &gen), "mut_set_gen");
    }
    const std::string generated = take(gen);
    CHECK(generated.size() == 20, "generated row key is 20 chars");

    OK(d->mut_flush(mut), "mut_flush");

    //
    // scan
    //
    auto count_scan = [&](void* spec)
    {
        void* scn = nullptr;
        OK(d->tbl_scn_create(tbl, spec, 0, 0, &scn), "scn_create");
        int n = 0;
        for (;;)
        {
            t_cell c{};
            std::int32_t eos = 0;
            OK(d->scn_next(scn, &c, &eos), "scn_next");
            if (eos)
            {
                break;
            }
            ++n;
        }
        OK(d->scn_release(scn), "scn_release");
        return n;
    };

    // Cells handed out after the scan already reported end of stream. A reader whose statement
    // has returned SQLITE_DONE runs its query again when stepped once more, so an exhausted
    // scanner used to hand out the same cells a second time and the interval readers used to
    // read through an iterator sitting at end() - reader::next_cell latches eos_ to stop that,
    // and every reader flavour has to hold to it, not just the one that was tested.
    auto cells_after_the_end = [&](void* spec, const char* what)
    {
        void* scn = nullptr;
        OK(d->tbl_scn_create(tbl, spec, 0, 0, &scn), what);
        for (;;)
        {
            t_cell c{};
            std::int32_t eos = 0;
            OK(d->scn_next(scn, &c, &eos), what);
            if (eos)
            {
                break;
            }
        }

        int extra = 0;
        for (int i = 0; i < 3; ++i)
        {
            t_cell c{};
            std::int32_t eos = 0;
            OK(d->scn_next(scn, &c, &eos), what);
            if (!eos)
            {
                ++extra;
            }
        }
        OK(d->scn_release(scn), what);
        return extra;
    };

    void* ss = nullptr;
    OK(d->ss_create(&ss), "ss_create");
    CHECK(count_scan(ss) == 7, "full scan returns 7 cells");
    OK(d->ss_release(ss), "ss_release");

    // single column family
    OK(d->ss_create(&ss), "ss_create cf");
    OK(d->ss_add_column(ss, "a", -1), "ss_add_column a");
    CHECK(count_scan(ss) == 5, "column a returns 5 cells");
    OK(d->ss_release(ss), "ss_release cf");

    // qualified column
    OK(d->ss_create(&ss), "ss_create cq");
    OK(d->ss_add_column(ss, "b:q1", -1), "ss_add_column b:q1");
    CHECK(count_scan(ss) == 1, "column b:q1 returns 1 cell");
    OK(d->ss_release(ss), "ss_release cq");

    // qualifier prefix, the path whose sql bound was fixed
    OK(d->ss_create(&ss), "ss_create prefix");
    OK(d->ss_add_column(ss, "b:^q", -1), "ss_add_column b:^q");
    CHECK(count_scan(ss) == 2, "column b:^q returns 2 cells");
    OK(d->ss_release(ss), "ss_release prefix");

    // exact row
    OK(d->ss_create(&ss), "ss_create row");
    OK(d->ss_add_row(ss, "r1", -1), "ss_add_row r1");
    CHECK(count_scan(ss) == 2, "row r1 returns 2 cells");
    OK(d->ss_release(ss), "ss_release row");

    // row interval r2..r4 inclusive
    OK(d->ss_create(&ss), "ss_create interval");
    OK(d->ss_add_row_interval(ss, "r2", -1, 1, "r4", -1, 1), "ss_add_row_interval");
    CHECK(count_scan(ss) == 3, "row interval r2..r4 returns 3 cells");
    OK(d->ss_release(ss), "ss_release interval");

    // row interval, exclusive end
    OK(d->ss_create(&ss), "ss_create interval excl");
    OK(d->ss_add_row_interval(ss, "r2", -1, 1, "r4", -1, 0), "ss_add_row_interval excl");
    CHECK(count_scan(ss) == 2, "row interval r2..<r4 returns 2 cells");
    OK(d->ss_release(ss), "ss_release interval excl");

    // scan and filter over a row set
    OK(d->ss_create(&ss), "ss_create saf");
    OK(d->ss_set_i(ss, ssi_scan_and_filter, 1), "ss scan_and_filter");
    OK(d->ss_add_row(ss, "r1", -1), "saf add r1");
    OK(d->ss_add_row(ss, "r4", -1), "saf add r4");
    CHECK(count_scan(ss) == 3, "scan_and_filter r1,r4 returns 3 cells");
    OK(d->ss_release(ss), "ss_release saf");

    // row regexp, exercising the managed callback
    OK(d->ss_create(&ss), "ss_create regex");
    OK(d->ss_set_s(ss, sss_row_regex, "^r[12]$", -1), "ss row_regex");
    CHECK(count_scan(ss) == 3, "row regex ^r[12]$ returns 3 cells");
    OK(d->ss_release(ss), "ss_release regex");

    // value regexp
    OK(d->ss_create(&ss), "ss_create vregex");
    OK(d->ss_set_s(ss, sss_value_regex, "q1$", -1), "ss value_regex");
    CHECK(count_scan(ss) == 1, "value regex q1$ returns 1 cell");
    OK(d->ss_release(ss), "ss_release vregex");

    // max rows
    OK(d->ss_create(&ss), "ss_create maxrows");
    OK(d->ss_set_i(ss, ssi_max_rows, 2), "ss max_rows 2");
    {
        void* scn = nullptr;
        OK(d->tbl_scn_create(tbl, ss, 0, 0, &scn), "scn_create maxrows");
        int rows = 0;
        std::string last;
        for (;;)
        {
            t_cell c{};
            std::int32_t eos = 0;
            OK(d->scn_next(scn, &c, &eos), "scn_next maxrows");
            if (eos)
            {
                break;
            }
            const std::string row(c.row, static_cast<std::size_t>(c.row_n));
            if (row != last)
            {
                last = row;
                ++rows;
            }
        }
        OK(d->scn_release(scn), "scn_release maxrows");
        CHECK(rows == 2, "max_rows 2 yields 2 distinct rows");
    }
    OK(d->ss_release(ss), "ss_release maxrows");

    // An exhausted scanner stays exhausted, one reader flavour at a time: the plain reader,
    // the row set behind scan_and_filter, the row interval reader and the cell interval one.
    OK(d->ss_create(&ss), "ss_create end plain");
    CHECK(cells_after_the_end(ss, "end plain") == 0, "plain scan hands out nothing after the end");
    OK(d->ss_release(ss), "ss_release end plain");

    OK(d->ss_create(&ss), "ss_create end saf");
    OK(d->ss_set_i(ss, ssi_scan_and_filter, 1), "ss scan_and_filter end");
    OK(d->ss_add_row(ss, "r1", -1), "end saf add r1");
    OK(d->ss_add_row(ss, "r4", -1), "end saf add r4");
    CHECK(cells_after_the_end(ss, "end saf") == 0, "row set hands out nothing after the end");
    OK(d->ss_release(ss), "ss_release end saf");

    OK(d->ss_create(&ss), "ss_create end interval");
    OK(d->ss_add_row_interval(ss, "r1", -1, 1, "r2", -1, 1), "end row_interval 1");
    OK(d->ss_add_row_interval(ss, "r4", -1, 1, "r5", -1, 1), "end row_interval 2");
    CHECK(cells_after_the_end(ss, "end interval") == 0,
          "row intervals hand out nothing after the end");
    OK(d->ss_release(ss), "ss_release end interval");

    OK(d->ss_create(&ss), "ss_create end cellinterval");
    OK(d->ss_add_cell_interval(ss, "r1", -1, "a", -1, 1, "r2", -1, "a", -1, 1),
       "end cell_interval");
    CHECK(cells_after_the_end(ss, "end cellinterval") == 0,
          "cell intervals hand out nothing after the end");
    OK(d->ss_release(ss), "ss_release end cellinterval");

    // The mutator statements come from the environment pool now, acquired on first use and
    // handed back on release rather than prepared and finalised per mutator. What has to hold
    // is that a statement comes out of the pool clean: a stale binding, or one statement
    // handed to two mutators at once, would show up as a missing, a duplicated or a wrong
    // cell. So run many mutators in sequence through the same table, insert and delete, and
    // count what is left. Its own table, so the counts the cases above rely on stay put.
    {
        OK(d->ns_tbl_create(ns, "pool", -1, schema, -1), "tbl_create pool");
        void* pool_tbl = nullptr;
        OK(d->ns_tbl_open(ns, "pool", -1, 0, &pool_tbl), "tbl_open pool");

        const auto count_pool = [&]()
        {
            void* pool_ss = nullptr;
            OK(d->ss_create(&pool_ss), "ss_create pool");
            void* scn = nullptr;
            OK(d->tbl_scn_create(pool_tbl, pool_ss, 0, 0, &scn), "scn_create pool");
            int n = 0;
            for (;;)
            {
                t_cell c{};
                std::int32_t eos = 0;
                OK(d->scn_next(scn, &c, &eos), "scn_next pool");
                if (eos)
                {
                    break;
                }
                const std::string row(c.row, static_cast<std::size_t>(c.row_n));
                const std::string value(static_cast<const char*>(c.value),
                                        static_cast<std::size_t>(c.value_n));
                CHECK(value == "v-" + row, "a pooled statement wrote the value of its own row");
                ++n;
            }
            OK(d->scn_release(scn), "scn_release pool");
            OK(d->ss_release(pool_ss), "ss_release pool");
            return n;
        };

        for (int i = 0; i < 32; ++i)
        {
            const std::string row = "pool" + std::to_string(i);
            const std::string value = "v-" + row;
            void* pool_mut = nullptr;
            OK(d->tbl_mut_create(pool_tbl, 0, 0, 0, &pool_mut), "pool mut_create");
            t_cell c = make_cell(row.c_str(), "a", nullptr, value.c_str());
            OK(d->mut_set(pool_mut, &c), "pool mut_set");
            OK(d->mut_flush(pool_mut), "pool mut_flush");
            OK(d->mut_release(pool_mut), "pool mut_release");
        }
        CHECK(count_pool() == 32, "32 mutators in sequence wrote 32 cells");

        // the delete statements come out of the pool as well
        {
            void* pool_mut = nullptr;
            OK(d->tbl_mut_create(pool_tbl, 0, 0, 0, &pool_mut), "pool mut_create delete");
            t_cell c = make_cell("pool7", nullptr, nullptr, nullptr);
            c.flag = cf_delete_row;
            OK(d->mut_del(pool_mut, &c), "pool mut_del");
            OK(d->mut_flush(pool_mut), "pool mut_flush delete");
            OK(d->mut_release(pool_mut), "pool mut_release delete");
        }
        CHECK(count_pool() == 31, "a pooled delete statement removed exactly one row");

        OK(d->tbl_release(pool_tbl), "tbl_release pool");
        OK(d->ns_tbl_drop(ns, "pool", -1, 0), "tbl_drop pool");
    }

    // keys only
    OK(d->ss_create(&ss), "ss_create keysonly");
    OK(d->ss_set_i(ss, ssi_keys_only, 1), "ss keys_only");
    {
        void* scn = nullptr;
        OK(d->tbl_scn_create(tbl, ss, 0, 0, &scn), "scn_create keysonly");
        int n = 0;
        bool any_value = false;
        for (;;)
        {
            t_cell c{};
            std::int32_t eos = 0;
            OK(d->scn_next(scn, &c, &eos), "scn_next keysonly");
            if (eos)
            {
                break;
            }
            if (c.value_n != 0)
            {
                any_value = true;
            }
            ++n;
        }
        OK(d->scn_release(scn), "scn_release keysonly");
        CHECK(n == 7, "keys_only still returns 7 cells");
        CHECK(!any_value, "keys_only returns no values");
    }
    OK(d->ss_release(ss), "ss_release keysonly");

    // batched read
    OK(d->ss_create(&ss), "ss_create batch");
    {
        void* scn = nullptr;
        OK(d->tbl_scn_create(tbl, ss, 0, 0, &scn), "scn_create batch");
        t_cell many[4]{};
        int total = 0;
        for (;;)
        {
            std::int32_t got = 0;
            OK(d->scn_next_many(scn, many, 4, &got), "scn_next_many");
            if (got == 0)
            {
                break;
            }
            for (std::int32_t i = 0; i < got; ++i)
            {
                CHECK(many[i].row != nullptr && many[i].row_n > 0, "batched cell has a row");
                CHECK(many[i].cf != nullptr && many[i].cf_n > 0, "batched cell has a family");
            }
            total += got;
            if (got < 4)
            {
                break;
            }
        }
        OK(d->scn_release(scn), "scn_release batch");
        CHECK(total == 7, "batched scan returns 7 cells");
    }
    OK(d->ss_release(ss), "ss_release batch");

    //
    // values
    //
    OK(d->ss_create(&ss), "ss_create value");
    OK(d->ss_add_row(ss, "r1", -1), "value add row");
    OK(d->ss_add_column(ss, "b:q1", -1), "value add column");
    {
        void* scn = nullptr;
        OK(d->tbl_scn_create(tbl, ss, 0, 0, &scn), "scn_create value");
        t_cell c{};
        std::int32_t eos = 0;
        OK(d->scn_next(scn, &c, &eos), "scn_next value");
        CHECK(eos == 0, "value scan produced a cell");
        if (!eos)
        {
            CHECK(std::string(c.row, static_cast<std::size_t>(c.row_n)) == "r1", "row is r1");
            CHECK(std::string(c.cf, static_cast<std::size_t>(c.cf_n)) == "b", "family is b");
            CHECK(std::string(c.cq, static_cast<std::size_t>(c.cq_n)) == "q1", "qualifier is q1");
            CHECK(std::string(static_cast<const char*>(c.value),
                              static_cast<std::size_t>(c.value_n)) == "v-r1-b-q1",
                  "value round trips");
            CHECK(c.ts != 0, "timestamp was auto assigned");
        }
        OK(d->scn_release(scn), "scn_release value");
    }
    OK(d->ss_release(ss), "ss_release value");

    //
    // delete
    //
    {
        t_cell c = make_cell("r1", nullptr, nullptr, nullptr);
        c.flag = cf_delete_row;
        OK(d->mut_del(mut, &c), "mut_del row r1");
        OK(d->mut_flush(mut), "mut_flush after delete");
    }
    OK(d->ss_create(&ss), "ss_create after delete");
    CHECK(count_scan(ss) == 5, "full scan returns 5 cells after deleting r1");
    OK(d->ss_release(ss), "ss_release after delete");

    OK(d->mut_release(mut), "mut_release");

    //
    // listing
    //
    t_buf listing{};
    OK(d->ns_listing(ns, 0, &listing), "ns_listing");
    {
        const std::string raw = take(listing);
        CHECK(raw.size() >= 4, "listing has a count");
        const auto read_i32 = [&raw](std::size_t at)
        {
            return static_cast<std::int32_t>((static_cast<unsigned char>(raw[at])) |
                                             (static_cast<unsigned char>(raw[at + 1]) << 8) |
                                             (static_cast<unsigned char>(raw[at + 2]) << 16) |
                                             (static_cast<unsigned char>(raw[at + 3]) << 24));
        };
        // t1, t2 and the sub namespace
        CHECK(read_i32(0) == 3, "listing has 3 direct entries");
    }

    //
    // rename and drop
    //
    OK(d->tbl_release(tbl), "tbl_release");
    OK(d->ns_tbl_rename(ns, "t2", -1, "t3", -1), "tbl_rename");
    OK(d->ns_tbl_exists(ns, "t2", -1, &exists), "tbl_exists t2 after rename");
    CHECK(exists == 0, "t2 gone after rename");
    OK(d->ns_tbl_exists(ns, "t3", -1, &exists), "tbl_exists t3 after rename");
    CHECK(exists == 1, "t3 present after rename");

    OK(d->ns_tbl_drop(ns, "t3", -1, 0), "tbl_drop t3");
    OK(d->ns_tbl_exists(ns, "t3", -1, &exists), "tbl_exists t3 after drop");
    CHECK(exists == 0, "t3 gone after drop");

    // dropping again without if-exists must fail
    CHECK(d->ns_tbl_drop(ns, "t3", -1, 0) != 0, "dropping a missing table fails");
    OK(d->ns_tbl_drop(ns, "t3", -1, fl_if_exists), "tbl_drop if_exists on missing table");

    // a non empty namespace cannot be dropped
    CHECK(d->cli_ns_drop(cli, "test", -1, nullptr, 0) == 0x00030013,
          "non empty namespace reports HYPERSPACE_DIR_NOT_EMPTY");

    OK(d->ns_release(ns), "ns_release");
    OK(d->cli_release(cli), "cli_release");
    OK(d->env_close(env), "env_close");

    //
    // reopen, proving what was written is readable
    //
    OK(d->env_get_or_create("smoke.db", -1, &cfg, &env), "env_get_or_create again");
    OK(d->cli_create(env, &cli), "cli_create again");
    OK(d->cli_ns_open(cli, "test", -1, nullptr, &ns), "ns_open again");
    OK(d->ns_tbl_open(ns, "t1", -1, 0, &tbl), "tbl_open again");
    OK(d->ss_create(&ss), "ss_create again");
    CHECK(count_scan(ss) == 5, "reopened database still holds 5 cells");
    OK(d->ss_release(ss), "ss_release again");
    OK(d->tbl_release(tbl), "tbl_release again");
    OK(d->ns_release(ns), "ns_release again");
    OK(d->cli_release(cli), "cli_release again");
    OK(d->env_close(env), "env_close again");

    //
    // error reporting
    //
    {
        void* missing = nullptr;
        OK(d->env_get_or_create("smoke.db", -1, &cfg, &missing),
           "env_get_or_create for error test");
        void* c2 = nullptr;
        OK(d->cli_create(missing, &c2), "cli_create for error test");
        void* n2 = nullptr;
        const std::int32_t rc = d->cli_ns_open(c2, "does-not-exist", -1, nullptr, &n2);
        CHECK(rc == 40, "opening a missing namespace reports NAMESPACE_DOES_NOT_EXIST");
        t_error e{};
        CHECK(d->err_get(0, &e) == 1, "err_get returns a frame");
        CHECK(e.code == 40, "error frame carries the code");
        CHECK(e.text_n > 0, "error frame carries a message");
        CHECK(e.func != nullptr && e.func_n > 0, "error frame carries the function");
        OK(d->cli_release(c2), "cli_release error test");
        OK(d->env_close(missing), "env_close error test");
    }

    //
    // transactions
    //

    // A scope that begins while a mutator's ambient transaction is in progress has to
    // commit that batch and own a transaction of its own. Joining it instead - which is
    // what the depth counting used to do - left the scope unable to commit: its work
    // became the mutator's to commit or lose, so a create_table could report success and
    // then vanish.
    //
    // Its own database, because it deliberately abandons a mutator.
    {
        remove_file("smoke-tx.db");

        void* e2 = nullptr;
        OK(d->env_get_or_create("smoke-tx.db", -1, &cfg, &e2), "tx env_get_or_create");
        void* c3 = nullptr;
        OK(d->cli_create(e2, &c3), "tx cli_create");
        OK(d->cli_ns_create(c3, "tx", -1, nullptr, 0), "tx ns_create");
        void* n3 = nullptr;
        OK(d->cli_ns_open(c3, "tx", -1, nullptr, &n3), "tx ns_open");
        OK(d->ns_tbl_create(n3, "t", -1, schema, -1), "tx tbl_create");

        void* t3 = nullptr;
        OK(d->ns_tbl_open(n3, "t", -1, 0, &t3), "tx tbl_open");

        // an ambient transaction, left open on purpose - no flush, no release
        void* m3 = nullptr;
        OK(d->tbl_mut_create(t3, 0, 0, 0, &m3), "tx mut_create");
        {
            t_cell c = make_cell("r1", "a", nullptr, "v");
            OK(d->mut_set(m3, &c), "tx mut_set");
        }

        // a scope, while that ambient transaction is in progress
        OK(d->ns_tbl_create(n3, "created-inside", -1, schema, -1), "tx tbl_create inside");

        // tear down without flushing the mutator
        OK(d->tbl_release(t3), "tx tbl_release");
        OK(d->ns_release(n3), "tx ns_release");
        OK(d->cli_release(c3), "tx cli_release");
        OK(d->env_close(e2), "tx env_close");

        // the scoped create has to have survived, whatever became of the mutator
        OK(d->env_get_or_create("smoke-tx.db", -1, &cfg, &e2), "tx env_get_or_create again");
        OK(d->cli_create(e2, &c3), "tx cli_create again");
        OK(d->cli_ns_open(c3, "tx", -1, nullptr, &n3), "tx ns_open again");
        OK(d->ns_tbl_exists(n3, "created-inside", -1, &exists), "tx tbl_exists inside");
        CHECK(exists == 1, "a scoped create during an ambient transaction is durable");

        OK(d->ns_release(n3), "tx ns_release again");
        OK(d->cli_release(c3), "tx cli_release again");
        OK(d->env_close(e2), "tx env_close again");
        remove_file("smoke-tx.db");

        // note the mutator handle is deliberately never released, so it leaks here; that
        // is the point of the case and the process is about to end anyway
        (void)m3;
    }

    // Two scanners which run more than one statement share one read transaction, and it lasts
    // until the last of them has ended: the first one to end used to commit it under the other,
    // which went on reading without a consistent snapshot.
    //
    // Its own database, so that nothing else holds a lock on it.
    {
        remove_file("smoke-read.db");

        void* e4 = nullptr;
        OK(d->env_get_or_create("smoke-read.db", -1, &cfg, &e4), "read env_get_or_create");
        void* c4 = nullptr;
        OK(d->cli_create(e4, &c4), "read cli_create");
        OK(d->cli_ns_create(c4, "read", -1, nullptr, 0), "read ns_create");
        void* n4 = nullptr;
        OK(d->cli_ns_open(c4, "read", -1, nullptr, &n4), "read ns_open");
        OK(d->ns_tbl_create(n4, "t", -1, schema, -1), "read tbl_create");
        void* t4 = nullptr;
        OK(d->ns_tbl_open(n4, "t", -1, 0, &t4), "read tbl_open");

        void* m4 = nullptr;
        OK(d->tbl_mut_create(t4, 0, 0, 0, &m4), "read mut_create");
        for (const char* row : {"r1", "r2", "r3", "r4"})
        {
            t_cell c = make_cell(row, "a", nullptr, "v");
            OK(d->mut_set(m4, &c), "read mut_set");
        }
        OK(d->mut_release(m4), "read mut_release");

        void* intervals = nullptr;
        OK(d->ss_create(&intervals), "read ss_create");
        OK(d->ss_add_row_interval(intervals, "r1", -1, 1, "r2", -1, 1), "read row_interval 1");
        OK(d->ss_add_row_interval(intervals, "r3", -1, 1, "r4", -1, 1), "read row_interval 2");

        const auto drain = [&](void* scn)
        {
            for (;;)
            {
                t_cell c{};
                std::int32_t eos = 0;
                OK(d->scn_next(scn, &c, &eos), "read scn_next");
                if (eos)
                {
                    break;
                }
            }
        };

        void* first = nullptr;
        void* second = nullptr;
        OK(d->tbl_scn_create(t4, intervals, 0, 0, &first), "read scn_create first");
        OK(d->tbl_scn_create(t4, intervals, 0, 0, &second), "read scn_create second");
        CHECK(!read_locked("smoke-read.db"), "a read transaction locks nothing before a read");

        drain(first);
        CHECK(read_locked("smoke-read.db"),
              "the read transaction outlasts the first of the scanners sharing it");

        drain(second);
        CHECK(!read_locked("smoke-read.db"), "the last scanner to end ends the read transaction");

        OK(d->scn_release(first), "read scn_release first");
        OK(d->scn_release(second), "read scn_release second");
        OK(d->ss_release(intervals), "read ss_release");
        OK(d->tbl_release(t4), "read tbl_release");
        OK(d->ns_release(n4), "read ns_release");
        OK(d->cli_release(c4), "read cli_release");
        OK(d->env_close(e4), "read env_close");
        remove_file("smoke-read.db");
    }

    std::printf("\n%s (%d failure%s)\n", g_failures == 0 ? "PASS" : "FAILED", g_failures,
                g_failures == 1 ? "" : "s");
    return g_failures == 0 ? 0 : 1;
}
