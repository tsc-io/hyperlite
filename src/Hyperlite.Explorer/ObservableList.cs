namespace Hyperlite.Explorer
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Windows.Threading;

    /// <summary>
    /// A thread-safe observable list.
    /// </summary>
    internal sealed class ObservableList<T> : IList<T>, INotifyCollectionChanged
    {
        private readonly Dispatcher dispatcher;

        private readonly List<T> list = new List<T>();

        private readonly object syncRoot = new object();

        public ObservableList()
        {
            this.dispatcher = Dispatcher.CurrentDispatcher;
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public int Capacity
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.list.Capacity;
                }
            }

            set
            {
                lock (this.syncRoot)
                {
                    this.list.Capacity = value;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.list.Count;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether is read only.
        /// </summary>
        public bool IsReadOnly
        {
            get
            {
                return false;
            }
        }

        public T this[int index]
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.list[index];
                }
            }

            set
            {
                lock (this.syncRoot)
                {
                    this.list[index] = value;
                }
            }
        }

        public void Add(T item)
        {
            if (this.dispatcher.CheckAccess())
            {
                lock (this.syncRoot)
                {
                    this.list.Add(item);
                    this.FireCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
                }
            }
            else
            {
                this.dispatcher.Invoke(new Action<T>(this.Add), DispatcherPriority.Send, item);
            }
        }

        public void AddRange(IEnumerable<T> items)
        {
            if (this.dispatcher.CheckAccess())
            {
                lock (this.syncRoot)
                {
                    this.list.AddRange(items);
                    this.FireCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToList()));
                }
            }
            else
            {
                this.dispatcher.Invoke(new Action<IEnumerable<T>>(this.AddRange), DispatcherPriority.Send, items);
            }
        }

        public void Clear()
        {
            if (this.dispatcher.CheckAccess())
            {
                lock (this.syncRoot)
                {
                    this.list.Clear();
                    this.FireCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
            }
            else
            {
                this.dispatcher.Invoke(new Action(this.Clear), DispatcherPriority.Send);
            }
        }

        public bool Contains(T item)
        {
            lock (this.syncRoot)
            {
                return this.list.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.syncRoot)
            {
                this.list.CopyTo(array, arrayIndex);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.syncRoot)
            {
                return this.list.GetEnumerator();
            }
        }

        public int IndexOf(T item)
        {
            lock (this.syncRoot)
            {
                return this.list.IndexOf(item);
            }
        }

        public void Insert(int index, T item)
        {
            if (this.dispatcher.CheckAccess())
            {
                lock (this.syncRoot)
                {
                    this.list.Insert(index, item);
                    this.FireCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
                }
            }
            else
            {
                this.dispatcher.Invoke(new Action<int, T>(this.Insert), DispatcherPriority.Send, index, item);
            }
        }

        public bool Remove(T item)
        {
            if (this.dispatcher.CheckAccess())
            {
                lock (this.syncRoot)
                {
                    var index = this.list.IndexOf(item);
                    var result = this.list.Remove(item);
                    this.FireCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
                    return result;
                }
            }

            return (bool)this.dispatcher.Invoke(new Func<T, bool>(this.Remove), DispatcherPriority.Send, item);
        }

        public void RemoveAt(int index)
        {
            if (this.dispatcher.CheckAccess())
            {
                lock (this.syncRoot)
                {
                    var item = this.list[index];
                    this.list.RemoveAt(index);
                    this.FireCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
                }
            }
            else
            {
                this.dispatcher.Invoke(new Action<int>(this.RemoveAt), DispatcherPriority.Send, index);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        private void FireCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (this.CollectionChanged != null)
            {
                this.CollectionChanged(this, e);
            }
        }
    }
}
