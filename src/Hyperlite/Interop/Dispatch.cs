namespace Hyperlite.Interop
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// The Hyperlite.Native dispatch table. Mirrors Hyperlite::abi::t_dispatch, member for
    /// member and in the same order.
    /// </summary>
    /// <remarks>
    /// The fields are native function pointers, so a call compiles to calli with no
    /// marshalling stub in between, on every target net48 included: cdecl is one of the
    /// conventions a function pointer signature encodes by itself, without a CallConv type.
    /// On x64 there is only one native calling convention anyway.
    ///
    /// Never reorder or remove a member. Append only, and bump
    /// <see cref="Abi.AbiRevision" /> on both sides when doing so.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal unsafe struct Dispatch
    {
        //
        // library
        //

        internal delegate* unmanaged[Cdecl]<int> AbiRevision;

        internal delegate* unmanaged[Cdecl]<NativeCallbacks*, int> SetCallbacks;

        internal delegate* unmanaged[Cdecl]<NativeConfig*, void> CfgDefaults;

        internal delegate* unmanaged[Cdecl]<byte*> SqliteVersion;

        internal delegate* unmanaged[Cdecl]<int, NativeError*, int> ErrGet;

        internal delegate* unmanaged[Cdecl]<NativeBuffer*, void> BufRelease;

        //
        // environment
        //

        internal delegate* unmanaged[Cdecl]<byte*, int, NativeConfig*, IntPtr*, int> EnvGetOrCreate;

        internal delegate* unmanaged[Cdecl]<IntPtr, int> EnvClose;

        internal delegate* unmanaged[Cdecl]<IntPtr, int> EnvOptimize;

        //
        // client
        //

        internal delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, int> CliCreate;

        internal delegate* unmanaged[Cdecl]<IntPtr, int> CliRelease;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, IntPtr, int, int> CliNsCreate;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, IntPtr, IntPtr*, int> CliNsOpen;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, IntPtr, int, int> CliNsDrop;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, IntPtr, int*, int> CliNsExists;

        //
        // namespace
        //

        internal delegate* unmanaged[Cdecl]<IntPtr, int> NsRelease;

        internal delegate* unmanaged[Cdecl]<IntPtr, NativeBuffer*, int> NsName;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, byte*, int, int> NsTblCreate;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, byte*, int, int> NsTblCreateLike;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, byte*, int, int> NsTblAlter;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, byte*, int, int> NsTblRename;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int, IntPtr*, int> NsTblOpen;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int, int> NsTblDrop;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int*, int> NsTblExists;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int, NativeBuffer*, int> NsTblSchema;

        internal delegate* unmanaged[Cdecl]<IntPtr, int, NativeBuffer*, int> NsListing;

        //
        // table
        //

        internal delegate* unmanaged[Cdecl]<IntPtr, int> TblRelease;

        internal delegate* unmanaged[Cdecl]<IntPtr, NativeBuffer*, int> TblName;

        internal delegate* unmanaged[Cdecl]<IntPtr, int, NativeBuffer*, int> TblSchema;

        internal delegate* unmanaged[Cdecl]<IntPtr, int, int, int, IntPtr*, int> TblMutCreate;

        internal delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, int, IntPtr*, int> TblScnCreate;

        //
        // scan spec
        //

        internal delegate* unmanaged[Cdecl]<IntPtr*, int> SsCreate;

        internal delegate* unmanaged[Cdecl]<IntPtr, int> SsRelease;

        internal delegate* unmanaged[Cdecl]<IntPtr, int> SsClear;

        internal delegate* unmanaged[Cdecl]<IntPtr, int, long, int> SsSetI;

        internal delegate* unmanaged[Cdecl]<IntPtr, int, byte*, int, int> SsSetS;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int> SsAddRow;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int> SsAddColumn;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, byte*, int, int> SsAddCell;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int, byte*, int, int, int> SsAddRowInterval;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, byte*, int, int, byte*, int, byte*, int, int, int> SsAddCellInterval;

        internal delegate* unmanaged[Cdecl]<IntPtr, byte*, int, byte*, int, int, byte*, int, int> SsAddColumnPredicate;

        //
        // mutator
        //

        internal delegate* unmanaged[Cdecl]<IntPtr, int> MutRelease;

        internal delegate* unmanaged[Cdecl]<IntPtr, NativeCell*, int> MutSet;

        internal delegate* unmanaged[Cdecl]<IntPtr, NativeCell*, NativeBuffer*, int> MutSetGen;

        internal delegate* unmanaged[Cdecl]<IntPtr, NativeCell*, int, int> MutSetMany;

        internal delegate* unmanaged[Cdecl]<IntPtr, NativeCell*, int> MutDel;

        internal delegate* unmanaged[Cdecl]<IntPtr, int> MutFlush;

        //
        // scanner
        //

        internal delegate* unmanaged[Cdecl]<IntPtr, int> ScnRelease;

        internal delegate* unmanaged[Cdecl]<IntPtr, NativeCell*, int*, int> ScnNext;

        internal delegate* unmanaged[Cdecl]<IntPtr, NativeCell*, int, int*, int> ScnNextMany;

        //
        // key utilities
        //

        internal delegate* unmanaged[Cdecl]<NativeBuffer*, int> KeyGenerate;

        //
        // raw heap
        //

        internal delegate* unmanaged[Cdecl]<long, IntPtr> MemAlloc;

        internal delegate* unmanaged[Cdecl]<IntPtr, long, IntPtr> MemRealloc;

        internal delegate* unmanaged[Cdecl]<IntPtr, void> MemFree;
    }
}
