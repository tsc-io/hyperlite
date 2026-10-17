namespace Hyperlite.Persistence.Collections
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    /// <summary>
    ///     Represents a strongly typed collection of objects that can be accessed by index. Provides methods to manipulate
    ///     collections.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of elements in the collection.
    /// </typeparam>
    internal sealed class ChunkedCollection<T> : ICollection<T>, ICollection
    {
        private readonly List<T[]> chunks = new List<T[]>(4);

        private readonly int chunkSize = 4 * 1024;

        /// <summary>
        ///     A collection holding a handful of items should not allocate a chunk sized for
        ///     thousands of them - the deserializer builds three of these per entity and the
        ///     readers one per fetch. Only the first chunk is small, every later one has the full
        ///     chunk size, which keeps the index arithmetic at one comparison and one division.
        /// </summary>
        private const int FirstChunkSize = 32;

        /// <summary>
        ///     The effective size of the first chunk, never larger than the chunk size.
        /// </summary>
        private readonly int firstChunkSize = FirstChunkSize;

        private int count;

        /// <summary>
        ///     The current chunk.
        /// </summary>
        private T[] current;

        /// <summary>
        ///     The current chunk's element count.
        /// </summary>
        private int currentCount;

        private object syncRoot;

        public ChunkedCollection()
        {
        }

        /// <exception cref="ArgumentException">
        ///     If <paramref name="chunkSize" /> is less than two.
        /// </exception>
        public ChunkedCollection(int chunkSize)
        {
            if (chunkSize < 2)
            {
                throw new ArgumentException(@"Invalid chunk size", nameof(chunkSize));
            }

            this.chunkSize = chunkSize;
            this.firstChunkSize = Math.Min(FirstChunkSize, chunkSize);
        }

        /// <summary>
        ///     Gets the number of elements actually contained in the <see cref="ChunkedCollection{T}" />.
        /// </summary>
        public int Count => this.count;

        /// <summary>
        ///     Gets a value indicating whether the <see cref="ChunkedCollection{T}" /> is read-only.
        /// </summary>
        /// <returns>
        ///     True if the <see cref="ChunkedCollection{T}" /> is read-only; otherwise, false.
        /// </returns>
        public bool IsReadOnly => false;

        /// <summary>
        ///     Gets a value indicating whether access to the <see cref="ChunkedCollection{T}" /> is synchronized (thread safe).
        /// </summary>
        /// <returns>
        ///     True if access to the <see cref="ChunkedCollection{T}" /> is synchronized (thread safe); otherwise, false.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public bool IsSynchronized => false;

        /// <summary>
        ///     Gets an object that can be used to synchronize access to the <see cref="ChunkedCollection{T}" />.
        /// </summary>
        /// <returns>
        ///     An object that can be used to synchronize access to the <see cref="ChunkedCollection{T}" />.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public object SyncRoot
        {
            get
            {
                if (this.syncRoot == null)
                {
                    Interlocked.CompareExchange<object>(ref this.syncRoot, new object(), null);
                }

                return this.syncRoot;
            }
        }

        /// <summary>
        ///     Gets the number of elements contained in the <see cref="ChunkedCollection{T}" />.
        /// </summary>
        /// <returns>
        ///     The number of elements contained in the <see cref="ChunkedCollection{T}" />.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        int ICollection.Count => this.Count;

        /// <summary>
        ///     Gets the number of elements contained in the <see cref="ChunkedCollection{T}" />.
        /// </summary>
        /// <returns>
        ///     The number of elements contained in the <see cref="ChunkedCollection{T}" />.
        /// </returns>
        int ICollection<T>.Count => this.Count;

        /// <summary>
        ///     Gets the chunk size of the <see cref="ChunkedCollection{T}" />.
        /// </summary>
        internal int ChunkSize => this.chunkSize;

        /// <summary>
        ///     Gets or sets the element at the specified index.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Index is not a valid index in the collection.
        /// </exception>
        /// <returns>
        ///     The element at the specified index.
        /// </returns>
        internal T this[int index]
        {
            get
            {
                if (index < 0 || index >= this.count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                if (index < this.firstChunkSize)
                {
                    return this.chunks[0][index];
                }

                var offset = index - this.firstChunkSize;
                return this.chunks[1 + (offset / this.chunkSize)][offset % this.chunkSize];
            }

            set
            {
                if (index < 0 || index >= this.count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                if (index < this.firstChunkSize)
                {
                    this.chunks[0][index] = value;
                }
                else
                {
                    var offset = index - this.firstChunkSize;
                    this.chunks[1 + (offset / this.chunkSize)][offset % this.chunkSize] = value;
                }
            }
        }

        /// <summary>
        ///     Adds an item to the <see cref="ChunkedCollection{T}" />.
        /// </summary>
        /// <param name="item">
        ///     The object to add to the <see cref="ChunkedCollection{T}" />.
        /// </param>
        /// <exception cref="T:System.NotSupportedException">
        ///     The <see cref="ChunkedCollection{T}" /> is read-only.
        /// </exception>
        public void Add(T item)
        {
            if (this.current == null || this.currentCount == this.current.Length)
            {
                this.AddChunk();
            }

            this.current[this.currentCount++] = item;
            ++this.count;
        }

        /// <summary>
        ///     Removes all items from the <see cref="ChunkedCollection{T}" />.
        /// </summary>
        /// <exception cref="T:System.NotSupportedException">
        ///     The <see cref="ChunkedCollection{T}" /> is read-only.
        /// </exception>
        public void Clear()
        {
            this.chunks.Clear();
            this.current = null;
            this.currentCount = 0;
            this.count = 0;
        }

        /// <summary>
        ///     Determines whether the <see cref="ChunkedCollection{T}" /> contains a specific value.
        /// </summary>
        /// <returns>
        ///     True if <paramref name="item" /> is found in the <see cref="ChunkedCollection{T}" />; otherwise, false.
        /// </returns>
        /// <param name="item">
        ///     The object to locate in the <see cref="ChunkedCollection{T}" />.
        /// </param>
        public bool Contains(T item)
        {
            return this.chunks.Any(c => c.Contains(item));
        }

        /// <summary>
        ///     Copies the elements of the <see cref="ChunkedCollection{T}" /> to an <see cref="T:System.Array" />, starting at a
        ///     particular <see cref="T:System.Array" /> index.
        /// </summary>
        /// <param name="array">
        ///     The one-dimensional <see cref="T:System.Array" /> that is the destination of the elements copied from
        ///     <see cref="ChunkedCollection{T}" />. The <see cref="T:System.Array" /> must have zero-based indexing.
        /// </param>
        /// <param name="arrayIndex">
        ///     The zero-based index in <paramref name="array" /> at which copying begins.
        /// </param>
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            this.CopyTo((Array)array, arrayIndex);
        }

        /// <summary>
        ///     Copies the elements of the <see cref="ChunkedCollection{T}" /> to an <see cref="T:System.Array" />, starting at a
        ///     particular <see cref="T:System.Array" /> index.
        /// </summary>
        /// <param name="array">
        ///     The one-dimensional <see cref="T:System.Array" /> that is the destination of the elements copied from
        ///     <see cref="ChunkedCollection{T}" />. The <see cref="T:System.Array" /> must have zero-based indexing.
        /// </param>
        /// <param name="index">
        ///     The zero-based index in <paramref name="array" /> at which copying begins.
        /// </param>
        public void CopyTo(Array array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (array.Rank != 1)
            {
                throw new ArgumentException(@"Array is multidimensional", nameof(array));
            }

            try
            {
                this.CopyToUnchecked(array, index);
            }
            catch (ArrayTypeMismatchException)
            {
                throw new ArgumentException(@"Invalid array element type", nameof(array));
            }
        }

        /// <summary>
        ///     Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        ///     A <see cref="T:System.Collections.Generic.IEnumerator`1" /> that can be used to iterate through the collection.
        /// </returns>
        /// <filterpriority>1</filterpriority>
        public IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <summary>
        ///     Removes the first occurrence of a specific object from the <see cref="ChunkedCollection{T}" />.
        /// </summary>
        /// <returns>
        ///     True if <paramref name="item" /> was successfully removed from the <see cref="ChunkedCollection{T}" />; otherwise,
        ///     false. This method also returns false if <paramref name="item" /> is not found in the original
        ///     <see cref="ChunkedCollection{T}" />.
        /// </returns>
        /// <param name="item">
        ///     The object to remove from the <see cref="ChunkedCollection{T}" />.
        /// </param>
        /// <exception cref="T:System.NotSupportedException">
        ///     Remove is not implemented yet.
        /// </exception>
        public bool Remove(T item)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Copies the elements of the <see cref="ChunkedCollection{T}" /> to a new array.
        /// </summary>
        /// <returns>
        ///     An array containing copies of the elements of the <see cref="ChunkedCollection{T}" />.
        /// </returns>
        public T[] ToArray()
        {
            var array = new T[this.count];
            this.CopyToUnchecked(array, 0);
            return array;
        }

        /// <summary>
        ///     Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        ///     An <see cref="T:System.Collections.IEnumerator" /> object that can be used to iterate through the collection.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        private void AddChunk()
        {
            var chunk = new T[this.chunks.Count == 0 ? this.firstChunkSize : this.chunkSize];
            this.chunks.Add(chunk);
            this.current = chunk;
            this.currentCount = 0;
        }

        private void CopyToUnchecked(Array array, int index)
        {
            // the first chunk may be smaller than the rest, so go by what each chunk holds
            var remaining = this.count;
            var chunk = 0;
            while (remaining > 0)
            {
                var source = this.chunks[chunk++];
                var copyCount = Math.Min(remaining, source.Length);
                Array.Copy(source, 0, array, index, copyCount);
                index += copyCount;
                remaining -= copyCount;
            }
        }

        public struct Enumerator : IEnumerator<T>
        {
            private readonly ChunkedCollection<T> chunkedCollection;

            private readonly int count;

            private int chunkIndex;

            private T current;

            private T[] currentChunk;

            private int index;

            private int localChunkIndex;

            internal Enumerator(ChunkedCollection<T> chunkedCollection)
            {
                this.chunkedCollection = chunkedCollection;
                this.count = chunkedCollection.count;
                this.index = 0;
                this.localChunkIndex = 0;
                this.chunkIndex = 0;
                this.currentChunk = null;
                this.current = default(T);
            }

            public T Current => this.current;

            /// <value>
            ///     The current item.
            /// </value>
            object IEnumerator.Current => this.Current;

            /// <summary>
            ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            /// <filterpriority>2</filterpriority>
            public void Dispose()
            {
            }

            /// <summary>
            ///     Advances the enumerator to the next element of the collection.
            /// </summary>
            /// <returns>
            ///     True if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of
            ///     the collection.
            /// </returns>
            public bool MoveNext()
            {
                if (this.index < this.count)
                {
                    if (this.currentChunk == null || this.localChunkIndex == this.currentChunk.Length)
                    {
                        this.currentChunk = this.chunkedCollection.chunks[this.chunkIndex++];
                        this.localChunkIndex = 0;
                    }

                    this.current = this.currentChunk[this.localChunkIndex++];
                    ++this.index;
                    return true;
                }

                this.current = default(T);
                return false;
            }

            void IEnumerator.Reset()
            {
                this.index = 0;
                this.localChunkIndex = 0;
                this.chunkIndex = 0;
                this.currentChunk = null;
                this.current = default(T);
            }
        }
    }
}
