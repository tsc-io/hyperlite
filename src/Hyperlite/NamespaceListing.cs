namespace Hyperlite
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    using Hyperlite.Interop;

    /// <summary>
    /// Represents the contents of a namespace: its tables and its sub-namespaces.
    /// </summary>
    /// <seealso cref="INamespace.GetListing()" />
    [Serializable]
    public sealed class NamespaceListing
    {
        private readonly string name;

        private readonly string fullName;

        private readonly NamespaceListing parent;

        private readonly List<NamespaceListing> namespaces;

        private readonly List<string> tables;

        /// <summary>
        /// Initializes the root of a listing, whose full name is the namespace's own.
        /// </summary>
        internal NamespaceListing(INamespace ns, List<ListingEntry> entries)
        {
            if (ns == null)
            {
                throw new ArgumentNullException(nameof(ns));
            }

            this.name = LeafName(ns.Name);
            this.fullName = ns.Name;
            this.namespaces = new List<NamespaceListing>();
            this.tables = new List<string>();

            this.Fill(entries);
        }

        private NamespaceListing(NamespaceListing parent, string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            this.name = name;
            this.parent = parent;
            this.namespaces = new List<NamespaceListing>();
            this.tables = new List<string>();
            this.fullName = parent.fullName + "/" + name;
        }

        /// <summary>
        /// Gets the leaf name of this namespace.
        /// </summary>
        public string Name
        {
            get
            {
                return this.name;
            }
        }

        /// <summary>
        /// Gets the full path of this namespace.
        /// </summary>
        public string FullName
        {
            get
            {
                return this.fullName;
            }
        }

        /// <summary>
        /// Gets the enclosing listing, or null for the root of the listing.
        /// </summary>
        public NamespaceListing Parent
        {
            get
            {
                return this.parent;
            }
        }

        /// <summary>
        /// Gets the sub-namespaces.
        /// </summary>
        public IList<NamespaceListing> Namespaces
        {
            get
            {
                return this.namespaces;
            }
        }

        /// <summary>
        /// Gets the tables of this namespace.
        /// </summary>
        public IList<string> Tables
        {
            get
            {
                return this.tables;
            }
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}(Name={1}, FullName={2}, Namespaces.Count={3}, Tables.Count={4})",
                this.GetType(),
                this.Name ?? "null",
                this.FullName ?? "null",
                this.Namespaces != null ? this.Namespaces.Count : 0,
                this.Tables != null ? this.Tables.Count : 0);
        }

        /// <summary>
        /// The last path segment, or the whole name where there is no separator.
        /// </summary>
        private static string LeafName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            var at = name.LastIndexOf('/');
            return at >= 0 ? name.Substring(at + 1) : name;
        }

        private void Fill(List<ListingEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                if (entry.IsNamespace)
                {
                    var child = new NamespaceListing(this, LeafName(entry.Name));
                    child.Fill(entry.SubEntries);
                    this.namespaces.Add(child);
                }
                else
                {
                    this.tables.Add(LeafName(entry.Name));
                }
            }
        }
    }
}
