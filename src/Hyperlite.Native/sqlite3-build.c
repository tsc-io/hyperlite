// Single translation unit that pulls in the sqlite amalgamation. Its compile time
// configuration, SqliteDefines in Hyperlite.Native.vcxproj, is fixed, because it is part of
// the on-disk contract.
//
// ht4c also routed sqlite through mimalloc from here. Measured on a quiet machine,
// that bought nothing this workload could detect and cost 65 MB of commit charge,
// so sqlite now allocates through the crt.
//
// Nothing is included ahead of it: sqlite defines _GNU_SOURCE itself, and a system header
// read first fixes the feature macros without it - on Linux, mremap is then not declared.

#include "sqlite3.c"
