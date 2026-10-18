// What the native test needs from the operating system: loading the library, deleting a file
// and looking at sqlite's file locks from outside.

#pragma once

#include "abi.h"

/// Loads the library beside the test and returns its dispatch table for the revision this
/// test was compiled against, or null after printing why not.
const hyperlite::abi::t_dispatch* load_dispatch();

/// Deletes a file, if it is there.
void remove_file(const char* path);

/// Whether a connection holds a read lock on the database file: sqlite takes a shared lock
/// on 510 bytes at 0x40000002 for it, which an exclusive lock taken without waiting cannot
/// get past - an outside view of whether a read transaction is open. With the rollback
/// journal only, the default: in WAL mode sqlite keeps that lock between transactions.
bool read_locked(const char* path);
