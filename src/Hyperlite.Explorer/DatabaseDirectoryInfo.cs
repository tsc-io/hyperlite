namespace Hyperlite.Explorer
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    internal sealed class DatabaseDirectoryInfo
    {
        private readonly NamespaceListing namespaceListing;

        private readonly Tuple<string, string> table;

        /// <param name="namespaceListing">
        /// The ns listing.
        /// </param>
        public DatabaseDirectoryInfo(NamespaceListing namespaceListing)
        {
            this.namespaceListing = namespaceListing;
        }

        private DatabaseDirectoryInfo(string namespaceName, string tableName)
        {
            this.table = new Tuple<string, string>(namespaceName, tableName);
        }

        public ObservableList<CellInfo> Cells
        {
            get
            {
                var observable = new ObservableList<CellInfo>();
                if (this.table != null)
                {
                    DatabaseSession.Instance.BeginScan(this.table.Item1, this.table.Item2, cells => observable.AddRange(cells.Select(c => new CellInfo(this, c)).ToList()));
                }

                return observable;
            }
        }

        /// <summary>
        /// Gets namespaces and tables.
        /// </summary>
        public ObservableList<DatabaseDirectoryInfo> Directories
        {
            get
            {
                var observable = new ObservableList<DatabaseDirectoryInfo>();
                if (this.namespaceListing != null && (this.namespaceListing.Namespaces.Count > 0 || this.namespaceListing.Tables.Count > 0))
                {
                    Task.Factory.StartNew(
                        () =>
                            {
                                foreach (var ns in this.namespaceListing.Namespaces)
                                {
                                    observable.Add(DatabaseSession.Instance.GetDirectoryInfo(ns.FullName));
                                }

                                foreach (var t in this.namespaceListing.Tables)
                                {
                                    observable.Add(new DatabaseDirectoryInfo(this.namespaceListing.FullName, t));
                                }
                            });
                }

                return observable;
            }
        }

        public string FullName
        {
            get
            {
                return "/" + (this.namespaceListing != null ? this.namespaceListing.FullName : this.table.Item1 + "/" + this.table.Item2);
            }
        }

        /// <summary>
        /// Gets the namespace name
        /// </summary>
        public string Name
        {
            get
            {
                return this.namespaceListing != null ? !string.IsNullOrEmpty(this.namespaceListing.Name) ? this.namespaceListing.Name : "/" : this.table.Item2;
            }
        }

        public Cell Find(Key key)
        {
            if (this.table != null)
            {
                return DatabaseSession.Instance.Find(this.table.Item1, this.table.Item2, key);
            }

            return null;
        }
    }
}
