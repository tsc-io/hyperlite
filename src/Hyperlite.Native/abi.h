// Hyperlite.Native public ABI.
//
// The library exports exactly one entry point, by ordinal 1 only on windows
// (NONAME, see Hyperlite.Native.def) and under an opaque name elsewhere. Everything
// else is reachable through the dispatch table below, whose members are
// identified by position rather than by name. The managed side binds the table
// once and calls through function pointers, so there is no per-call marshalling
// stub and no readable symbol to introspect.
//
// Conventions
//  - All strings are UTF-8. A pointer/length pair is always (const char*, int32),
//    a null pointer means "not specified", which is distinct from a zero length.
//  - No C++ exception ever crosses the boundary. Every fallible call returns
//    int32: 0 on success, otherwise a hypertable error code. Details for the
//    failing call are retrieved through the thread-local err_get slot.
//  - Handles are opaque void*. Ownership is documented per member; a handle is
//    released exactly once with its matching release member.
//  - Struct layout is fixed: 8 byte packing, no bit fields, no bool.

#pragma once

#include <cstdint>

#if defined(_WIN32)
#define HYPERLITE_CALL __cdecl
#define HYPERLITE_EXPORT extern "C" __declspec(dllexport)
#else
#define HYPERLITE_CALL
#define HYPERLITE_EXPORT extern "C" __attribute__((visibility("default")))
#endif

#pragma pack(push, 8)

namespace hyperlite::abi
{
// The layout below is the ABI, laid out as a table to be read against the managed
// side; the formatter would only flatten it.
// clang-format off

/// Bumped whenever the layout of t_dispatch or of any struct below changes, or the
/// contract of a call or callback does. The managed loader refuses a table it was not
/// compiled against.
constexpr int32_t k_abi_revision = 1;

/// Borrowed UTF-8 slice. p == nullptr means "not specified".
struct t_slice
{
    const char* p;
    int32_t     n;
    int32_t     _pad;
};

/// Native owned growable buffer handed back for out-of-band results (schemas,
/// listings, generated row keys). Released with t_dispatch::buf_release.
struct t_buf
{
    void*    h;   ///< opaque owner, pass to buf_release
    char*    p;   ///< payload, UTF-8, not nul terminated
    int32_t  n;   ///< payload length
    int32_t _pad;
};

/// One cell, as produced by a scanner or consumed by a mutator.
/// Scanner output points into scanner owned storage and stays valid until the
/// next scn_next / scn_release on the same handle.
struct t_cell
{
    const char* row;
    const char* cf;
    const char* cq;      ///< nullptr when the cell has no qualifier
    const void* value;   ///< nullptr when the cell has no value
    uint64_t    ts;      ///< nanoseconds since 1970-01-01 00:00:00 UTC, 0 = auto
    int32_t     row_n;
    int32_t     cf_n;
    int32_t     cq_n;
    int32_t     value_n;
    uint8_t     flag;    ///< see e_cell_flag
    uint8_t     _pad[7];
};

/// One frame of the failure detail for the calling thread, valid until the next
/// fallible call on that thread. Frame 0 is the outermost failure, frame n + 1
/// its inner cause, mirroring ht4c::Common::HypertableException::inner().
///
/// code is the raw hypertable error code (Common/Error.h in ht4w). The managed
/// side maps it to the exception type exactly as ht4n's HypertableException::Create
/// does, so no classification happens down here.
struct t_error
{
    int32_t     code;
    int32_t     line;
    const char* text;     ///< UTF-8, never null
    int32_t     text_n;
    int32_t     _pad;
    const char* func;     ///< UTF-8, may be null
    int32_t     func_n;
    int32_t     _pad2;
    const char* file;     ///< UTF-8, may be null
    int32_t     file_n;
    int32_t     _pad3;
};

/// Environment configuration. Mirrors env_config in SQLiteEnv.h one to one,
/// including the defaults, which cfg_defaults fills in.
struct t_cfg
{
    int32_t cache_size_mb;
    int32_t page_size_kb;
    int32_t write_ahead_log;        ///< 0/1
    int32_t synchronous;            ///< 0/1
    int32_t auto_vacuum;            ///< 0 = NONE, 1 = FULL, 2 = INCREMENTAL
    int32_t unique_rows;            ///< 0/1
    int32_t no_cell_revisions;      ///< 0/1
    int32_t index_column;           ///< 0/1
    int32_t index_column_family;    ///< 0/1
    int32_t index_column_qualifier; ///< 0/1
    int32_t index_timestamp;        ///< 0/1
    int32_t _pad;
};

/// Mirrors ht4c::Common::CellFlag.
enum e_cell_flag : uint8_t
{
    cf_delete_row            = 0,
    cf_delete_column_family  = 1,
    cf_delete_cell           = 2,
    cf_delete_cell_version   = 3,
    cf_insert                = 255
};

/// ns_create / ns_drop / tbl_* dispositions, combinable.
enum e_flags : int32_t
{
    fl_if_exists          = 0x0001,
    fl_if_not_exists      = 0x0002,
    fl_create_intermediate = 0x0004,
    fl_drop_tables        = 0x0008,
    fl_deep               = 0x0010,
    fl_with_ids           = 0x0020,
    fl_force              = 0x0040
};

/// Scalar scan spec fields, used with ss_set_i.
enum e_ss_i : int32_t
{
    ssi_max_rows              = 0,
    ssi_max_versions          = 1,
    ssi_max_cells             = 2,
    ssi_max_cells_column_family = 3,
    ssi_row_offset            = 4,
    ssi_cell_offset           = 5,
    ssi_keys_only             = 6,
    ssi_not_use_query_cache   = 7,
    ssi_scan_and_filter       = 8,
    ssi_column_predicate_and  = 9,
    ssi_start_timestamp       = 10,
    ssi_end_timestamp         = 11,
    ssi_reserve_rows          = 12,
    ssi_reserve_columns       = 13,
    ssi_reserve_cells         = 14
};

/// String scan spec fields, used with ss_set_s.
enum e_ss_s : int32_t
{
    sss_row_regex   = 0,
    sss_value_regex = 1
};

/// Column predicate match kinds. Values are those of Hypertable::ColumnPredicate
/// and of ht4n's MatchKind, which already agree, so the managed enum passes
/// through unchanged.
enum e_match : int32_t
{
    mk_no_operation = 0x0000,
    mk_exact_match  = 0x0001,
    mk_prefix_match = 0x0002,
    mk_regex_match  = 0x0004,
    mk_value_match  = 0x0007,
    mk_qualifier_exact_match  = 0x0100,
    mk_qualifier_prefix_match = 0x0200,
    mk_qualifier_regex_match  = 0x0400,
    mk_qualifier_match        = 0x0700
};

/// Compiles a pattern, returns an opaque handle or nullptr on failure.
/// Hyperlite.Native owns no regex engine, pattern matching is delegated to the
/// managed side so that .NET Regex semantics apply everywhere.
using fn_re_compile = void* (HYPERLITE_CALL*)(const char* pattern, int32_t pattern_n);
/// Unanchored match, equivalent to RE2::PartialMatch. Returns 0 or 1, -1 when the match
/// ran out of time and -2 when it failed otherwise - the scan then fails rather than report
/// a wrong result.
using fn_re_match   = int32_t (HYPERLITE_CALL*)(void* re, const char* text, int32_t text_n);
using fn_re_release = void (HYPERLITE_CALL*)(void* re);

struct t_callbacks
{
    fn_re_compile re_compile;
    fn_re_match   re_match;
    fn_re_release re_release;
};

// Members are bound by position. Never reorder or remove, append only, and bump
// k_abi_revision when doing so.
struct t_dispatch
{
    //
    // library
    //

    int32_t (HYPERLITE_CALL* abi_revision)();
    /// Installs the managed callbacks. Must be called once before env_get_or_create.
    int32_t (HYPERLITE_CALL* set_callbacks)(const t_callbacks* cb);
    /// Fills cfg with the ht4c compatible defaults.
    void    (HYPERLITE_CALL* cfg_defaults)(t_cfg* cfg);
    /// sqlite3_libversion, borrowed, nul terminated.
    const char* (HYPERLITE_CALL* sqlite_version)();
    /// Retrieves frame `depth` of the failure detail of the last fallible call on
    /// this thread. Returns 0 when there is no frame at that depth.
    int32_t (HYPERLITE_CALL* err_get)(int32_t depth, t_error* out);
    void    (HYPERLITE_CALL* buf_release)(t_buf* buf);

    //
    // environment
    //

    /// filename == "memory" opens an in memory database, as ht4c does.
    /// Returns the environment for the database file, opening it only if no handle on that
    /// file is open yet - one connection per file per process, see the registry in api.cpp.
    int32_t (HYPERLITE_CALL* env_get_or_create)(const char* filename, int32_t filename_n, const t_cfg* cfg, void** env);
    int32_t (HYPERLITE_CALL* env_close)(void* env);
    /// Client::optimize, VACUUM.
    int32_t (HYPERLITE_CALL* env_optimize)(void* env);

    //
    // client
    //

    int32_t (HYPERLITE_CALL* cli_create)(void* env, void** cli);
    int32_t (HYPERLITE_CALL* cli_release)(void* cli);
    int32_t (HYPERLITE_CALL* cli_ns_create)(void* cli, const char* name, int32_t name_n, void* ns_base, int32_t flags);
    int32_t (HYPERLITE_CALL* cli_ns_open)(void* cli, const char* name, int32_t name_n, void* ns_base, void** ns);
    int32_t (HYPERLITE_CALL* cli_ns_drop)(void* cli, const char* name, int32_t name_n, void* ns_base, int32_t flags);
    int32_t (HYPERLITE_CALL* cli_ns_exists)(void* cli, const char* name, int32_t name_n, void* ns_base, int32_t* exists);

    //
    // namespace
    //

    int32_t (HYPERLITE_CALL* ns_release)(void* ns);
    int32_t (HYPERLITE_CALL* ns_name)(void* ns, t_buf* out);
    int32_t (HYPERLITE_CALL* ns_tbl_create)(void* ns, const char* name, int32_t name_n, const char* schema, int32_t schema_n);
    int32_t (HYPERLITE_CALL* ns_tbl_create_like)(void* ns, const char* name, int32_t name_n, const char* like, int32_t like_n);
    int32_t (HYPERLITE_CALL* ns_tbl_alter)(void* ns, const char* name, int32_t name_n, const char* schema, int32_t schema_n);
    int32_t (HYPERLITE_CALL* ns_tbl_rename)(void* ns, const char* from, int32_t from_n, const char* to, int32_t to_n);
    int32_t (HYPERLITE_CALL* ns_tbl_open)(void* ns, const char* name, int32_t name_n, int32_t flags, void** tbl);
    int32_t (HYPERLITE_CALL* ns_tbl_drop)(void* ns, const char* name, int32_t name_n, int32_t flags);
    int32_t (HYPERLITE_CALL* ns_tbl_exists)(void* ns, const char* name, int32_t name_n, int32_t* exists);
    int32_t (HYPERLITE_CALL* ns_tbl_schema)(void* ns, const char* name, int32_t name_n, int32_t flags, t_buf* out);
    /// Listing is returned as a flat record stream; ListingDecoder.cs reads it back.
    int32_t (HYPERLITE_CALL* ns_listing)(void* ns, int32_t flags, t_buf* out);

    //
    // table
    //

    int32_t (HYPERLITE_CALL* tbl_release)(void* tbl);
    int32_t (HYPERLITE_CALL* tbl_name)(void* tbl, t_buf* out);
    int32_t (HYPERLITE_CALL* tbl_schema)(void* tbl, int32_t flags, t_buf* out);
    int32_t (HYPERLITE_CALL* tbl_mut_create)(void* tbl, int32_t timeout_ms, int32_t flags, int32_t flush_interval_ms, void** mut);
    int32_t (HYPERLITE_CALL* tbl_scn_create)(void* tbl, void* ss, int32_t timeout_ms, int32_t flags, void** scn);

    //
    // scan spec
    //

    int32_t (HYPERLITE_CALL* ss_create)(void** ss);
    int32_t (HYPERLITE_CALL* ss_release)(void* ss);
    int32_t (HYPERLITE_CALL* ss_clear)(void* ss);
    int32_t (HYPERLITE_CALL* ss_set_i)(void* ss, int32_t field, int64_t value);
    int32_t (HYPERLITE_CALL* ss_set_s)(void* ss, int32_t field, const char* value, int32_t value_n);
    int32_t (HYPERLITE_CALL* ss_add_row)(void* ss, const char* row, int32_t row_n);
    int32_t (HYPERLITE_CALL* ss_add_column)(void* ss, const char* column, int32_t column_n);
    int32_t (HYPERLITE_CALL* ss_add_cell)(void* ss, const char* row, int32_t row_n, const char* column, int32_t column_n);
    int32_t (HYPERLITE_CALL* ss_add_row_interval)(void* ss, const char* from, int32_t from_n, int32_t include_from,
                                              const char* to, int32_t to_n, int32_t include_to);
    int32_t (HYPERLITE_CALL* ss_add_cell_interval)(void* ss, const char* from_row, int32_t from_row_n,
                                               const char* from_column, int32_t from_column_n, int32_t include_from,
                                               const char* to_row, int32_t to_row_n,
                                               const char* to_column, int32_t to_column_n, int32_t include_to);
    int32_t (HYPERLITE_CALL* ss_add_column_predicate)(void* ss, const char* cf, int32_t cf_n, const char* cq, int32_t cq_n,
                                                  int32_t match, const void* value, int32_t value_n);

    //
    // mutator
    //

    int32_t (HYPERLITE_CALL* mut_release)(void* mut);
    int32_t (HYPERLITE_CALL* mut_set)(void* mut, const t_cell* cell);
    /// Auto generates the row key, base85 encoded guid, into out.
    int32_t (HYPERLITE_CALL* mut_set_gen)(void* mut, const t_cell* cell, t_buf* out);
    int32_t (HYPERLITE_CALL* mut_set_many)(void* mut, const t_cell* cells, int32_t count);
    int32_t (HYPERLITE_CALL* mut_del)(void* mut, const t_cell* cell);
    int32_t (HYPERLITE_CALL* mut_flush)(void* mut);

    //
    // scanner
    //

    int32_t (HYPERLITE_CALL* scn_release)(void* scn);
    /// Returns 0 and sets *eos to 1 once the scan is exhausted.
    int32_t (HYPERLITE_CALL* scn_next)(void* scn, t_cell* out, int32_t* eos);
    /// Fills up to count cells, sets *got to the number produced; *got < count
    /// means the scan is exhausted. Cuts the per-cell boundary crossing on large
    /// scans. The strings and values every returned cell points at live in a
    /// staging buffer owned by the scanner and stay valid until the next call on
    /// it, so the whole batch has to be consumed before stepping again.
    int32_t (HYPERLITE_CALL* scn_next_many)(void* scn, t_cell* out, int32_t count, int32_t* got);

    //
    // key utilities
    //

    /// base85 encoded guid, the row key generator ht4c uses.
    int32_t (HYPERLITE_CALL* key_generate)(t_buf* out);

    //
    // raw heap
    //

    // Backing for Hyperlite.Heap, which Hyperlite.Persistence's serializer uses for its
    // scratch buffers. Plain malloc / realloc / free semantics: a null return means
    // the allocation failed, and free(nullptr) is a no-op. Allocation and release
    // both happen inside this module, so the two stay symmetric.
    void* (HYPERLITE_CALL* mem_alloc)(int64_t size);
    void* (HYPERLITE_CALL* mem_realloc)(void* block, int64_t size);
    void  (HYPERLITE_CALL* mem_free)(void* block);
};

// clang-format on
} // namespace hyperlite::abi

#pragma pack(pop)

// The one and only export. On windows it is exported by ordinal 1 with NONAME,
// so the export table carries no readable identifier at all.
HYPERLITE_EXPORT int32_t HYPERLITE_CALL q4kf9wz(int32_t abi_revision,
                                                const hyperlite::abi::t_dispatch** out);
