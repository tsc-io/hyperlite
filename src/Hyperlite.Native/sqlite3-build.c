// Single translation unit that pulls in the sqlite amalgamation. Its compile time
// configuration, SqliteDefines in Hyperlite.Native.vcxproj, is fixed, because it is part of
// the on-disk contract.
//
// ht4c also routed sqlite through mimalloc from here. Measured on a quiet machine,
// that bought nothing this workload could detect and cost 65 MB of commit charge,
// so sqlite now allocates through the crt.

#include <string.h>
#include <stdlib.h>
#include "sqlite3.c"
