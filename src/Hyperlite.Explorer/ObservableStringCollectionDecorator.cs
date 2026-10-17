namespace Hyperlite.Explorer
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Windows.Threading;

    /// <summary>
    /// A thread-safe observable string collection decorator.
    /// </summary>
    internal sealed class ObservableStringCollectionDecorator : IList<string>, INotifyCollectionChanged
    {
        private readonly Dispatcher dispatcher;

        private readonly StringCollection list;

        private readonly object syncRoot = new object();

        public ObservableStringCollectionDecorator(StringCollection list)
        {
            this.dispatcher = Dispatcher.CurrentDispatcher;
            this.list = list;
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;

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

        public string this[int index]
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

        public void Add(string item)
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
                this.dispatcher.Invoke(new Action<string>(this.Add), DispatcherPriority.Send, item);
            }
        }

        public void AddRange(IEnumerable<string> items)
        {
            if (this.dispatcher.CheckAccess())
            {
                lock (this.syncRoot)
                {
                    this.list.AddRange(items.ToArray());
                    this.FireCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToList()));
                }
            }
            else
            {
                this.dispatcher.Invoke(new Action<IEnumerable<string>>(this.AddRange), DispatcherPriority.Send, items);
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

        public bool Contains(string item)
        {
            lock (this.syncRoot)
            {
                return this.list.Contains(item);
            }
        }

        public void CopyTo(string[] array, int arrayIndex)
        {
            lock (this.syncRoot)
            {
                this.list.CopyTo(array, arrayIndex);
            }
        }

        public IEnumerator<string> GetEnumerator()
        {
            return this.list.Cast<string>().GetEnumerator();
        }

        public int IndexOf(string item)
        {
            lock (this.syncRoot)
            {
                return this.list.IndexOf(item);
            }
        }

        public void Insert(int index, string item)
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
                this.dispatcher.Invoke(new Action<int, string>(this.Insert), DispatcherPriority.Send, index, item);
            }
        }

        public bool Remove(string item)
        {
            if (this.dispatcher.CheckAccess())
            {
                lock (this.syncRoot)
                {
                    var index = this.list.IndexOf(item);
                    if (index < 0)
                    {
                        return false;
                    }

                    this.list.Remove(item);
                    this.FireCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
                    return true;
                }
            }

            return (bool)this.dispatcher.Invoke(new Func<string, bool>(this.Remove), DispatcherPriority.Send, item);
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
