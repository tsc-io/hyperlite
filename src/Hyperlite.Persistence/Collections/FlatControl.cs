namespace Hyperlite.Persistence.Collections
{
    using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
    using System.Numerics;
    using System.Runtime.InteropServices;
    using System.Runtime.Intrinsics;
#endif

    /// <summary>
    ///     The control bytes of <see cref="FlatDictionary{TKey, TValue, TComparer}" /> and
    ///     <see cref="FlatHashSet{T, TComparer}" />, and the group operations over them.
    /// </summary>
    /// <remarks>
    ///     A Swiss table keeps one control byte per slot beside the slots themselves, and
    ///     probes a whole group of them at once: one compare finds every slot in the group
    ///     whose byte carries the key's seven bit tag, and only those slots have their key
    ///     compared. A miss usually costs one group compare and no key compare at all.
    ///     <para>
    ///     The encoding departs from Abseil's in one respect, on purpose. Abseil marks an
    ///     empty slot 0x80, so a new table has to be filled before use. Here an empty slot
    ///     is 0x00, which is what the runtime hands out for a new array anyway, so a table
    ///     costs its allocation and nothing more. That was the measured reason the
    ///     hand-written dictionary this library used to carry lost to the framework's
    ///     end to end.
    ///     </para>
    ///     <code>
    ///         0x00         empty     never used since the table was built
    ///         0x01         deleted   a tombstone: a probe has to walk past it
    ///         0x80 | tag   full      the low seven bits are seven bits of the hash
    ///     </code>
    ///     <para>
    ///     Groups are aligned, not sliding windows: group g is slots [g * width, g * width +
    ///     width), and a probe visits groups in triangular order, which reaches every group
    ///     of a power-of-two table. Aligned groups need no mirrored bytes at the end of the
    ///     array, and they make erasing simple: once a group has had no empty slot it never
    ///     gets one back, so a group that still has one was never walked past, and an
    ///     erased slot in it can go straight back to empty.
    ///     </para>
    ///     <para>
    ///     On .NET 8 and later a group is 16 slots compared with one SSE2 or AdvSimd
    ///     instruction. net48 and netstandard2.0 have no intrinsics, so there a group is 8
    ///     slots in one <see cref="ulong" /> compared with scalar SWAR arithmetic - exact,
    ///     with no false positives, in five integer operations.
    ///     </para>
    ///     <para>
    ///     The control words are <see cref="ulong" /> in both builds, so no build has to
    ///     reinterpret a byte array without the Unsafe package. A match is a mask with one
    ///     bit per matching slot; which bit stands for which slot differs between the two
    ///     builds, and only <see cref="Lowest" /> needs to know.
    ///     </para>
    /// </remarks>
    internal static class FlatControl
    {
        /// <summary>
        ///     The control byte of a slot that has not been used since the table was built.
        /// </summary>
        internal const byte Empty = 0x00;

        /// <summary>
        ///     The control byte of a slot whose entry was removed.
        /// </summary>
        internal const byte Deleted = 0x01;

#if NET8_0_OR_GREATER
        /// <summary>
        ///     The base two logarithm of the slots per group.
        /// </summary>
        internal const int GroupShift = 4;
#else
        /// <summary>
        ///     The base two logarithm of the slots per group.
        /// </summary>
        internal const int GroupShift = 3;
#endif

        /// <summary>
        ///     The slots per group.
        /// </summary>
        internal const int GroupWidth = 1 << GroupShift;

        /// <summary>
        ///     The largest number of slots a table may have.
        /// </summary>
        internal const int MaxSlots = 1 << 30;

#if !NET8_0_OR_GREATER
        private const ulong LowBits = 0x0101010101010101UL;

        private const ulong HighBits = 0x8080808080808080UL;

        private const ulong SevenBits = 0x7F7F7F7F7F7F7F7FUL;
#endif

        /// <summary>
        ///     One group of empty slots, shared by every table that has not allocated yet.
        /// </summary>
        /// <remarks>
        ///     A lookup in it finds nothing and an insert into it finds no growth left, so it
        ///     is never written and a table in this state needs no null checks.
        /// </remarks>
        internal static readonly ulong[] EmptyGroup = new ulong[GroupWidth / 8];

        /// <summary>
        ///     The number of entries a table of the slots given may hold before it grows.
        /// </summary>
        /// <remarks>
        ///     Seven eighths, as in Abseil. At least one slot per table therefore stays
        ///     empty, which is what ends every probe.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GrowthLimit(int slots)
        {
            return slots - (slots >> 3);
        }

        /// <summary>
        ///     Below this many slots a full table grows four times, above it twice.
        /// </summary>
        /// <remarks>
        ///     Growing is where a flat table pays most against the framework's: every entry is
        ///     hashed again and stored one at a time, where the framework copies its entry array
        ///     in one move and keeps the hash codes. A table filled from empty therefore does
        ///     better to grow in fewer, larger steps while it is small - it rehashes about a third
        ///     as many entries - at the cost of being up to four times larger than it needs for a
        ///     while. Above the threshold the doubling keeps a large table from overshooting by
        ///     megabytes.
        /// </remarks>
        internal const int QuadrupleBelowSlots = 1024;

        /// <summary>
        ///     The slots a table full of entries grows to.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GrownSlots(int slots)
        {
            return slots < QuadrupleBelowSlots ? slots << 2 : slots << 1;
        }

        /// <summary>
        ///     The slots a table needs to hold the entries given without growing.
        /// </summary>
        internal static int SlotsFor(int capacity)
        {
            var needed = ((long)capacity * 8 + 6) / 7;
            if (needed > MaxSlots)
            {
                throw new System.OutOfMemoryException();
            }

            var slots = GroupWidth;
            while (slots < needed)
            {
                slots <<= 1;
            }

            return slots;
        }

        /// <summary>
        ///     Splits a hash code into the group a probe starts at and the tag it looks for.
        /// </summary>
        /// <remarks>
        ///     The hash is mixed first, by a 64 bit Fibonacci multiply, because neither half
        ///     can use a weak hash as it comes: the group is taken from bits 32 upwards and
        ///     the tag from the top seven, both of which the multiply fills from all 32 bits
        ///     of the input. <c>RuntimeHelpers.GetHashCode</c> and small integers are the
        ///     hashes this is for.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Hash(int hashCode, int groupMask, out byte tag)
        {
            var mixed = (uint)hashCode * 0x9E3779B97F4A7C15UL;
            tag = (byte)(0x80 | (int)(mixed >> 57));
            return (int)(mixed >> 32) & groupMask;
        }

        /// <summary>
        ///     Whether a control byte marks a slot in use.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsFull(byte control)
        {
            return control >= 0x80;
        }

        /// <summary>
        ///     Clears the lowest bit of a match, moving to the next matching slot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong Next(ulong mask)
        {
            return mask & (mask - 1);
        }

#if NET8_0_OR_GREATER
        /// <summary>
        ///     The slots in the group whose control byte is the tag given.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong Match(ulong[] control, int group, byte tag)
        {
            return Vector128.Equals(Load(control, group), Vector128.Create(tag)).ExtractMostSignificantBits();
        }

        /// <summary>
        ///     The empty slots in the group.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong MatchEmpty(ulong[] control, int group)
        {
            return Vector128.Equals(Load(control, group), Vector128<byte>.Zero).ExtractMostSignificantBits();
        }

        /// <summary>
        ///     The slots in the group that are empty or deleted.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong MatchFree(ulong[] control, int group)
        {
            return ~Load(control, group).ExtractMostSignificantBits() & 0xFFFFu;
        }

        /// <summary>
        ///     The slots in the group that are in use.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong MatchFull(ulong[] control, int group)
        {
            return Load(control, group).ExtractMostSignificantBits();
        }

        /// <summary>
        ///     The slot within its group of the lowest slot in a match.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Lowest(ulong mask)
        {
            return BitOperations.TrailingZeroCount(mask);
        }

        /// <summary>
        ///     The control byte of a slot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte Get(ulong[] control, int slot)
        {
            return Unsafe.Add(ref Bytes(control), slot);
        }

        /// <summary>
        ///     Sets the control byte of a slot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Set(ulong[] control, int slot, byte value)
        {
            Unsafe.Add(ref Bytes(control), slot) = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte Bytes(ulong[] control)
        {
            return ref Unsafe.As<ulong, byte>(ref MemoryMarshal.GetArrayDataReference(control));
        }

        /// <remarks>
        ///     Unchecked: every caller passes a group below the table's group count, and the
        ///     array holds exactly that many groups.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> Load(ulong[] control, int group)
        {
            return Vector128.LoadUnsafe(ref Bytes(control), (nuint)((uint)group << GroupShift));
        }
#else
        /// <summary>
        ///     The slots in the group whose control byte is the tag given.
        /// </summary>
        /// <remarks>
        ///     A matching byte is zero after the xor. The usual zero byte test,
        ///     <c>(x - 0x01..) &amp; ~x &amp; 0x80..</c>, lets a borrow out of a zero byte
        ///     report the byte above it as well; this form adds within seven bits instead,
        ///     so nothing carries between bytes and the result is exact.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong Match(ulong[] control, int group, byte tag)
        {
            var x = control[group] ^ (LowBits * tag);
            return ~(((x & SevenBits) + SevenBits) | x) & HighBits;
        }

        /// <summary>
        ///     The empty slots in the group.
        /// </summary>
        /// <remarks>
        ///     Empty and deleted differ only in bit 0, which the shift moves up to bit 7 of
        ///     the same byte.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong MatchEmpty(ulong[] control, int group)
        {
            var word = control[group];
            return ~word & ~(word << 7) & HighBits;
        }

        /// <summary>
        ///     The slots in the group that are empty or deleted.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong MatchFree(ulong[] control, int group)
        {
            return ~control[group] & HighBits;
        }

        /// <summary>
        ///     The slots in the group that are in use.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong MatchFull(ulong[] control, int group)
        {
            return control[group] & HighBits;
        }

        /// <summary>
        ///     The slot within its group of the lowest slot in a match.
        /// </summary>
        /// <remarks>
        ///     A match has its bits at 8i + 7. The lowest one, shifted down to 8i, is a power
        ///     of 256, and multiplying by 0x0001020304050607 brings byte 7 - i of the constant,
        ///     which is i, into the top byte. No trailing zero count is needed, and neither
        ///     net48 nor netstandard2.0 has one.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Lowest(ulong mask)
        {
            return (int)((((mask & (0 - mask)) >> 7) * 0x0001020304050607UL) >> 56);
        }

        /// <summary>
        ///     The control byte of a slot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte Get(ulong[] control, int slot)
        {
            return (byte)(control[slot >> 3] >> ((slot & 7) << 3));
        }

        /// <summary>
        ///     Sets the control byte of a slot.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Set(ulong[] control, int slot, byte value)
        {
            var shift = (slot & 7) << 3;
            ref var word = ref control[slot >> 3];
            word = (word & ~(0xFFUL << shift)) | ((ulong)value << shift);
        }
#endif
    }
}
