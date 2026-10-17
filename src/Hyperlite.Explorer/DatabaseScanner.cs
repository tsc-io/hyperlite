namespace Hyperlite.Explorer
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows.Threading;

    internal delegate void NotifyScannerStateChangedEventHandler(object sender, NotifyScannerStateChangedEventArgs e);

    internal enum ScannerState
    {
        Begin,

        Executing,

        Completed
    }

    internal sealed class NotifyScannerStateChangedEventArgs : EventArgs
    {
        public NotifyScannerStateChangedEventArgs()
        {
            this.Id = Guid.NewGuid();
            this.ScannerState = ScannerState.Begin;
        }

        public long BytesScanned { get; private set; }

        public long CellsScanned { get; private set; }

        public Guid Id { get; private set; }

        public ScannerState ScannerState { get; private set; }

        internal NotifyScannerStateChangedEventArgs Completed()
        {
            this.ScannerState = ScannerState.Completed;
            return this;
        }

        internal NotifyScannerStateChangedEventArgs Executing(IList<Cell> cells)
        {
            this.CellsScanned += cells.Count;
            this.BytesScanned += cells.Sum(cell => cell.Value != null ? cell.Value.Length : 0);
            this.ScannerState = ScannerState.Executing;
            return this;
        }
    }

    internal static class DatabaseScanner
    {
        private static readonly object SyncRoot = new object();

        private static Guid currentScan = Guid.Empty;

        private static List<Task> tasks = new List<Task>();

        public static event NotifyScannerStateChangedEventHandler ScannerStateChanged;

        public static void BeginScan(IClient client, string namespaceName, string tableName, Action<IList<Cell>> action)
        {
            lock (SyncRoot)
            {
                tasks = tasks.Where(t => !t.IsCompleted).ToList();
            }

            var dispatcher = Dispatcher.CurrentDispatcher;
            var beginScan = Task.Factory.StartNew(
                () =>
                    {
                        using (var ns = client.OpenNamespace(namespaceName))
                        using (var table = ns.OpenTable(tableName))
                        {
                            var notify = new NotifyScannerStateChangedEventArgs();
                            lock (SyncRoot)
                            {
                                currentScan = notify.Id;
                            }

                            FireScannerStateChanged(dispatcher, notify);

                            using (var scanner = table.CreateScanner())
                            using (var bc = new BlockingCollection<List<Cell>>())
                            {
                                var task = Task.Factory.StartNew(
                                    () =>
                                        {
                                            try
                                            {
                                                while (!IsCancelled(notify))
                                                {
                                                    var cells = bc.Take();
                                                    FireScannerStateChanged(dispatcher, notify.Executing(cells));
                                                    action(cells);
                                                }
                                            }
                                            catch (InvalidOperationException)
                                            {
                                            }
                                        });

                                const int ChunkSize = 2500;
                                var chunk = new List<Cell>(ChunkSize);
                                Cell cell;
                                for (var c = 1; !IsCancelled(notify) && scanner.Next(out cell); ++c)
                                {
                                    chunk.Add(cell);
                                    if ((c % chunk.Capacity) == 0)
                                    {
                                        bc.Add(chunk);
                                        chunk = new List<Cell>(ChunkSize);
                                    }
                                }

                                if (!IsCancelled(notify) && chunk.Count > 0)
                                {
                                    bc.Add(chunk);
                                }

                                bc.CompleteAdding();
                                task.Wait();
                            }

                            FireScannerStateChanged(dispatcher, notify.Completed());
                        }
                    });

            lock (SyncRoot)
            {
                tasks.Add(beginScan);
            }
        }

        public static void Cancel()
        {
            int countAlive;
            lock (SyncRoot)
            {
                currentScan = Guid.Empty;
                countAlive = tasks.Count(t => !t.IsCompleted);
            }

            var dispatcher = Dispatcher.CurrentDispatcher;
            var expire = DateTime.Now.AddSeconds(20).Ticks;
            while (countAlive > 0 && DateTime.Now.Ticks < expire)
            {
                dispatcher.Invoke(DispatcherPriority.Background, (DispatcherOperationCallback)(unused => null), null);
                lock (SyncRoot)
                {
                    countAlive = tasks.Count(t => !t.IsCompleted);
                }
            }
        }

        public static Cell Find(IClient client, string namespaceName, string tableName, Key key)
        {
            using (var ns = client.OpenNamespace(namespaceName))
            using (var table = ns.OpenTable(tableName))
            {
                using (var scanner = table.CreateScanner(new ScanSpec(key)))
                {
                    Cell cell;
                    return scanner.Next(out cell) ? cell : null;
                }
            }
        }

        private static void FireScannerStateChanged(Dispatcher dispatcher, NotifyScannerStateChangedEventArgs e)
        {
            NotifyScannerStateChangedEventHandler scannerStateChanged;
            lock (SyncRoot)
            {
                if (currentScan != e.Id)
                {
                    return;
                }

                scannerStateChanged = ScannerStateChanged;
            }

            if (scannerStateChanged != null)
            {
                if (dispatcher.CheckAccess())
                {
                    scannerStateChanged(null, e);
                }
                else
                {
                    dispatcher.Invoke(new Action<Dispatcher, NotifyScannerStateChangedEventArgs>(FireScannerStateChanged), DispatcherPriority.Send, dispatcher, e);
                }
            }
        }

        private static bool IsCancelled(NotifyScannerStateChangedEventArgs e)
        {
            lock (SyncRoot)
            {
                return currentScan != e.Id;
            }
        }
    }
}
