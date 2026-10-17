// The native floor for the cases Hyperlite.Bench measures.
//
// Hyperlite.Bench times the whole stack, so a case being slow says nothing about which half
// is responsible. This driver runs the same shapes straight through the dispatch table
// with no managed code above it, which puts a floor under each case: whatever it costs
// here is sqlite plus the native layer, and the rest of what the managed benchmark
// reports is the abi crossing and the marshalling. Only the second part is ours to
// improve without touching the on-disk format.
//
// Output is the same RESULT tsv the managed benchmark emits, so the same driver can
// join them.

#include "abi.h"

// windows.h defines min/max as macros, which breaks std::min below
#define NOMINMAX
#include <windows.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

using namespace hyperlite::abi;

namespace
{
const t_dispatch* dd = nullptr;

void die(const char* what, std::int32_t rc)
{
    std::printf("FAIL %s rc=%d\n", what, rc);
    std::exit(1);
}

#define MUST(expr, what)                                                                           \
    do                                                                                             \
    {                                                                                              \
        const std::int32_t rc_ = (expr);                                                           \
        if (rc_ != 0)                                                                              \
        {                                                                                          \
            die((what), rc_);                                                                      \
        }                                                                                          \
    } while (false)

/// Regex callbacks are never exercised here, but the abi wants them set.
void* __cdecl re_compile(const char*, std::int32_t)
{
    return nullptr;
}

std::int32_t __cdecl re_match(void*, const char*, std::int32_t)
{
    return 0;
}

void __cdecl re_release(void*) {}

std::string row_key(const char* prefix, int i)
{
    char buffer[32];
    std::snprintf(buffer, sizeof(buffer), "%s%07d", prefix, i);
    return buffer;
}

t_cell make(const char* row, std::int32_t row_n, const char* cf, const char* value,
            std::int32_t value_n)
{
    t_cell c{};
    c.row = row;
    c.row_n = row_n;
    c.cf = cf;
    c.cf_n = 1;
    c.cq = nullptr;
    c.cq_n = 0;
    c.value = reinterpret_cast<const std::uint8_t*>(value);
    c.value_n = value_n;
    c.ts = 0;
    c.flag = 255; // cf_insert, the abi's auto flag
    return c;
}

struct sample
{
    std::string name;
    double best = 0.0;
};

std::vector<sample> results;

template <typename F>
void measure(const char* name, F&& body)
{
    const auto start = std::chrono::steady_clock::now();
    body();
    const auto elapsed = std::chrono::steady_clock::now() - start;
    const double ms =
        std::chrono::duration_cast<std::chrono::duration<double, std::milli>>(elapsed).count();

    for (auto& s : results)
    {
        if (s.name == name)
        {
            // parenthesized, so a windows.h min macro cannot intercept it
            s.best = (std::min)(s.best, ms);
            return;
        }
    }
    results.push_back(sample{name, ms});
}

/// A cell value of the shape the managed benchmark writes.
const char* const k_value = "value-0000000000000000000000000000";

void write_single(void* tbl, int n, const char* prefix)
{
    void* mut = nullptr;
    MUST(dd->tbl_mut_create(tbl, 0, 0, 0, &mut), "mut_create");
    const auto value_n = static_cast<std::int32_t>(std::strlen(k_value));
    for (int i = 0; i < n; ++i)
    {
        const std::string row = row_key(prefix, i);
        t_cell c = make(row.c_str(), static_cast<std::int32_t>(row.size()), "a", k_value, value_n);
        MUST(dd->mut_set(mut, &c), "mut_set");
    }
    MUST(dd->mut_flush(mut), "mut_flush");
    MUST(dd->mut_release(mut), "mut_release");
}

// Creating and releasing a mutator, without writing anything through it. The mutator takes
// its statements from the environment pool on first use, so this measures the handle and the
// pool lookup, not five sqlite3_prepare_v2 calls and five finalises - which is what it used
// to measure, at roughly fifty times the cost per mutator.
void mutator_create(void* tbl, int n)
{
    for (int i = 0; i < n; ++i)
    {
        void* mut = nullptr;
        MUST(dd->tbl_mut_create(tbl, 0, 0, 0, &mut), "mut_create");
        MUST(dd->mut_release(mut), "mut_release");
    }
}

void write_batched(void* tbl, int n, const char* prefix)
{
    void* mut = nullptr;
    MUST(dd->tbl_mut_create(tbl, 0, 0, 0, &mut), "mut_create");
    const auto value_n = static_cast<std::int32_t>(std::strlen(k_value));

    const int chunk = 1000;
    std::vector<std::string> rows;
    std::vector<t_cell> cells;
    rows.reserve(chunk);
    cells.reserve(chunk);

    for (int i = 0; i < n; ++i)
    {
        rows.push_back(row_key(prefix, i));
        if (static_cast<int>(rows.size()) == chunk || i + 1 == n)
        {
            cells.clear();
            for (const auto& row : rows)
            {
                cells.push_back(make(row.c_str(), static_cast<std::int32_t>(row.size()), "a",
                                     k_value, value_n));
            }
            MUST(dd->mut_set_many(mut, cells.data(), static_cast<std::int32_t>(cells.size())),
                 "mut_set_many");
            rows.clear();
        }
    }
    MUST(dd->mut_flush(mut), "mut_flush");
    MUST(dd->mut_release(mut), "mut_release");
}

/// Full scan, one abi call per cell - the shape TableScanner.Step uses.
int scan_next(void* tbl, bool keys_only)
{
    void* ss = nullptr;
    MUST(dd->ss_create(&ss), "ss_create");
    if (keys_only)
    {
        MUST(dd->ss_set_i(ss, ssi_keys_only, 1), "ss keys_only");
    }
    void* scn = nullptr;
    MUST(dd->tbl_scn_create(tbl, ss, 0, 0, &scn), "scn_create");

    int n = 0;
    for (;;)
    {
        t_cell c{};
        std::int32_t eos = 0;
        MUST(dd->scn_next(scn, &c, &eos), "scn_next");
        if (eos)
        {
            break;
        }
        // touch the cell, so nothing is optimised away
        n += c.row_n > 0 && c.cf_n > 0 ? 1 : 0;
    }

    MUST(dd->scn_release(scn), "scn_release");
    MUST(dd->ss_release(ss), "ss_release");
    return n;
}

/// Full scan in batches, which is what the managed side could do instead.
int scan_many(void* tbl, std::int32_t batch)
{
    void* ss = nullptr;
    MUST(dd->ss_create(&ss), "ss_create");
    void* scn = nullptr;
    MUST(dd->tbl_scn_create(tbl, ss, 0, 0, &scn), "scn_create");

    std::vector<t_cell> many(static_cast<std::size_t>(batch));
    int n = 0;
    for (;;)
    {
        std::int32_t got = 0;
        MUST(dd->scn_next_many(scn, many.data(), batch, &got), "scn_next_many");
        if (got == 0)
        {
            break;
        }
        for (std::int32_t i = 0; i < got; ++i)
        {
            n += many[i].row_n > 0 && many[i].cf_n > 0 ? 1 : 0;
        }
        if (got < batch)
        {
            break;
        }
    }

    MUST(dd->scn_release(scn), "scn_release");
    MUST(dd->ss_release(ss), "ss_release");
    return n;
}

void delete_single(void* tbl, int n, const char* prefix)
{
    void* mut = nullptr;
    MUST(dd->tbl_mut_create(tbl, 0, 0, 0, &mut), "mut_create");
    for (int i = 0; i < n; ++i)
    {
        const std::string row = row_key(prefix, i);
        t_cell c{};
        c.row = row.c_str();
        c.row_n = static_cast<std::int32_t>(row.size());
        c.flag = 0; // cf_delete_row
        MUST(dd->mut_del(mut, &c), "mut_del");
    }
    MUST(dd->mut_flush(mut), "mut_flush");
    MUST(dd->mut_release(mut), "mut_release");
}

void run_once(int n, bool report)
{
    ::DeleteFileA("nbench.db");

    t_cfg cfg{};
    dd->cfg_defaults(&cfg);

    void* env = nullptr;
    MUST(dd->env_get_or_create("nbench.db", -1, &cfg, &env), "env_get_or_create");
    void* cli = nullptr;
    MUST(dd->cli_create(env, &cli), "cli_create");
    MUST(dd->cli_ns_create(cli, "bench", -1, nullptr, 0), "ns_create");
    void* ns = nullptr;
    MUST(dd->cli_ns_open(cli, "bench", -1, nullptr, &ns), "ns_open");

    const char* schema = "<Schema><AccessGroup name=\"default\">"
                         "<ColumnFamily><Name>a</Name></ColumnFamily>"
                         "<ColumnFamily><Name>b</Name></ColumnFamily>"
                         "</AccessGroup></Schema>";
    MUST(dd->ns_tbl_create(ns, "t", -1, schema, -1), "tbl_create");

    void* tbl = nullptr;
    MUST(dd->ns_tbl_open(ns, "t", -1, 0, &tbl), "tbl_open");

    measure("mutator.create", [&] { mutator_create(tbl, n); });
    measure("write.single", [&] { write_single(tbl, n, "s"); });
    measure("write.batched", [&] { write_batched(tbl, n, "t"); });

    int seen = 0;
    measure("scan.full.next", [&] { seen = scan_next(tbl, false); });
    if (report)
    {
        std::printf("# verify       : scan.full.next saw %d cells\n", seen);
    }
    measure("scan.full.many", [&] { seen = scan_many(tbl, 256); });
    if (report)
    {
        std::printf("# verify       : scan.full.many saw %d cells\n", seen);
    }
    measure("scan.keysonly", [&] { seen = scan_next(tbl, true); });
    measure("delete.single", [&] { delete_single(tbl, n, "t"); });

    MUST(dd->tbl_release(tbl), "tbl_release");
    MUST(dd->ns_release(ns), "ns_release");
    MUST(dd->cli_release(cli), "cli_release");
    MUST(dd->env_close(env), "env_close");
    ::DeleteFileA("nbench.db");
}
} // namespace

int run_bench(int cells, int repetitions)
{
    HMODULE lib = ::LoadLibraryW(L"Hyperlite.Native.win-x64.dll");
    if (!lib)
    {
        std::printf("FAIL LoadLibrary %lu\n", ::GetLastError());
        return 1;
    }

    using entry_t = std::int32_t(__cdecl*)(std::int32_t, const t_dispatch**);
    auto entry = reinterpret_cast<entry_t>(::GetProcAddress(lib, reinterpret_cast<LPCSTR>(1)));
    if (!entry || entry(k_abi_revision, &dd) != 0 || !dd)
    {
        std::printf("FAIL bootstrap\n");
        return 1;
    }

    t_callbacks cb{&re_compile, &re_match, &re_release};
    MUST(dd->set_callbacks(&cb), "set_callbacks");

    std::printf("# library      : Hyperlite.Native\n");
    std::printf("# cells / rows : %d\n", cells);
    std::printf("# repetitions  : %d\n", repetitions);

    for (int rep = 0; rep < repetitions; ++rep)
    {
        run_once(cells, rep == 0);
    }

    std::printf("\n");
    for (const auto& s : results)
    {
        std::printf("RESULT\t%s\t%.2f\t%.2f\n", s.name.c_str(), s.best, s.best);
    }
    return 0;
}
