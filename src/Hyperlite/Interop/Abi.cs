namespace Hyperlite.Interop
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// The managed side of the Hyperlite.Native ABI. Mirrors abi.h field for field and
    /// member for member; the two must be changed together and AbiRevision bumped.
    /// </summary>
    /// <remarks>
    /// Members of <see cref="Dispatch" /> are bound by position, not by name, and are
    /// invoked through function pointers, so a call carries no marshalling stub. Never
    /// reorder or remove a member, append only.
    /// </remarks>
    internal static class Abi
    {
        /// <summary>
        /// Must equal Hyperlite::abi::k_abi_revision.
        /// </summary>
        internal const int AbiRevision = 1;

        //
        // cell flags, Hyperlite::abi::e_cell_flag
        //

        internal const byte CellFlagDeleteRow = 0;

        internal const byte CellFlagDeleteColumnFamily = 1;

        internal const byte CellFlagDeleteCell = 2;

        internal const byte CellFlagDeleteCellVersion = 3;

        internal const byte CellFlagInsert = 255;

        //
        // dispositions, Hyperlite::abi::e_flags
        //

        internal const int FlagIfExists = 0x0001;

        internal const int FlagIfNotExists = 0x0002;

        internal const int FlagCreateIntermediate = 0x0004;

        internal const int FlagDropTables = 0x0008;

        internal const int FlagDeep = 0x0010;

        internal const int FlagWithIds = 0x0020;

        internal const int FlagForce = 0x0040;

        //
        // scalar scan spec fields, Hyperlite::abi::e_ss_i
        //

        internal const int SsMaxRows = 0;

        internal const int SsMaxVersions = 1;

        internal const int SsMaxCells = 2;

        internal const int SsMaxCellsColumnFamily = 3;

        internal const int SsRowOffset = 4;

        internal const int SsCellOffset = 5;

        internal const int SsKeysOnly = 6;

        internal const int SsNotUseQueryCache = 7;

        internal const int SsScanAndFilter = 8;

        internal const int SsColumnPredicateAnd = 9;

        internal const int SsStartTimestamp = 10;

        internal const int SsEndTimestamp = 11;

        internal const int SsReserveRows = 12;

        internal const int SsReserveColumns = 13;

        internal const int SsReserveCells = 14;

        //
        // string scan spec fields, Hyperlite::abi::e_ss_s
        //

        internal const int SsRowRegex = 0;

        internal const int SsValueRegex = 1;
    }

    /// <summary>
    /// Native owned buffer handed back for out of band results. Release with
    /// <see cref="Dispatch.BufRelease" />. Mirrors Hyperlite::abi::t_buf.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct NativeBuffer
    {
        internal IntPtr Owner;

        internal IntPtr Payload;

        internal int Length;

        internal int Padding;
    }

    /// <summary>
    /// One cell, as produced by a scanner or consumed by a mutator. Mirrors
    /// Hyperlite::abi::t_cell.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal unsafe struct NativeCell
    {
        internal byte* Row;

        internal byte* ColumnFamily;

        /// <summary>Null when the cell has no qualifier, which is distinct from empty.</summary>
        internal byte* ColumnQualifier;

        /// <summary>Null when the cell has no value.</summary>
        internal byte* Value;

        /// <summary>Nanoseconds since 1970-01-01 00:00:00 UTC, 0 means auto assign.</summary>
        internal ulong Timestamp;

        internal int RowLength;

        internal int ColumnFamilyLength;

        internal int ColumnQualifierLength;

        internal int ValueLength;

        internal byte Flag;

        private byte padding0;

        private byte padding1;

        private byte padding2;

        private byte padding3;

        private byte padding4;

        private byte padding5;

        private byte padding6;
    }

    /// <summary>
    /// One frame of the calling thread's failure detail. Mirrors Hyperlite::abi::t_error.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal unsafe struct NativeError
    {
        internal int Code;

        internal int Line;

        internal byte* Text;

        internal int TextLength;

        private int padding0;

        internal byte* Func;

        internal int FuncLength;

        private int padding1;

        internal byte* File;

        internal int FileLength;

        private int padding2;
    }

    /// <summary>
    /// Environment configuration. Mirrors Hyperlite::abi::t_cfg, whose defaults are the
    /// ht4c defaults and are obtained through <see cref="Dispatch.CfgDefaults" />
    /// rather than duplicated here.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct NativeConfig
    {
        internal int CacheSizeMB;

        internal int PageSizeKB;

        internal int WriteAheadLog;

        internal int Synchronous;

        internal int AutoVacuum;

        internal int UniqueRows;

        internal int NoCellRevisions;

        internal int IndexColumn;

        internal int IndexColumnFamily;

        internal int IndexColumnQualifier;

        internal int IndexTimestamp;

        private int padding;
    }

    /// <summary>
    /// The regex callbacks Hyperlite installs. Mirrors Hyperlite::abi::t_callbacks.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct NativeCallbacks
    {
        internal IntPtr Compile;

        internal IntPtr Match;

        internal IntPtr Release;
    }
}
