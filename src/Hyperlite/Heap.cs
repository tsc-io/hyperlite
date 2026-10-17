namespace Hyperlite
{
    using System;

    using Hyperlite.Interop;

    /// <summary>
    /// Represents a native heap.
    /// </summary>
    /// <remarks>
    /// Allocations go to the native companion's heap, which Hyperlite.Persistence's
    /// serializer uses for its scratch buffers. Allocating and freeing through the same
    /// module is the point: a block taken here must be released here.
    /// </remarks>
    public static unsafe class Heap
    {
        /// <summary>
        /// Allocates the given number of bytes on the native heap.
        /// </summary>
        /// <param name="size">The number of bytes to allocate.</param>
        /// <returns>Pointer to the block of memory, or <see cref="IntPtr.Zero" /> on failure.</returns>
        public static IntPtr Alloc(long size)
        {
            return Native.Table.MemAlloc(size);
        }

        /// <summary>
        /// Reallocates the specified memory block.
        /// </summary>
        /// <param name="p">Pointer to the block of memory.</param>
        /// <param name="size">The new number of bytes.</param>
        /// <returns>Pointer to the block of memory, or <see cref="IntPtr.Zero" /> on failure.</returns>
        public static IntPtr ReAlloc(IntPtr p, long size)
        {
            return Native.Table.MemRealloc(p, size);
        }

        /// <summary>
        /// Frees the specified memory block.
        /// </summary>
        /// <param name="p">Pointer to the block of memory, or <see cref="IntPtr.Zero" />.</param>
        public static void Free(IntPtr p)
        {
            Native.Table.MemFree(p);
        }
    }
}
