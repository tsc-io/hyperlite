namespace Hyperlite.Xml
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Xml;
    using System.Xml.Serialization;

    /// <summary>
    /// Time order of a column family's cell versions.
    /// </summary>
    [XmlType(TypeName = "timeOrderType", AnonymousType = true)]
    // lower case on purpose: the member names are the xml literals the schema uses
    public enum ColumnFamilyTimeOrder
    {
        /// <summary>
        /// Oldest version first.
        /// </summary>
        asc,

        /// <summary>
        /// Newest version first.
        /// </summary>
        desc
    }

    /// <summary>
    /// Access group options.
    /// </summary>
    /// <remarks>
    /// Every value pairs with a Specified flag. Only options whose flag is set are
    /// written, which is how the schema distinguishes "not stated" from "stated as the
    /// default" - the distinction the provider keys its own defaults off.
    /// </remarks>
    [XmlType(AnonymousType = true)]
    public sealed class AccessGroupOptions
    {
        /// <summary>
        /// Gets or sets the replication factor.
        /// </summary>
        [XmlElement("Replication")]
        public int Replication { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="Replication" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool ReplicationSpecified { get; set; }

        [XmlElement("BlockSize")]
        public int BlockSize { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="BlockSize" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool BlockSizeSpecified { get; set; }

        /// <summary>
        /// Gets or sets the compressor specification.
        /// </summary>
        [XmlElement("Compressor")]
        public string Compressor { get; set; }

        /// <summary>
        /// Gets or sets the bloom filter specification.
        /// </summary>
        [XmlElement("BloomFilter")]
        public string BloomFilter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the access group is held in memory.
        /// </summary>
        [XmlElement("InMemory")]
        public bool InMemory { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="InMemory" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool InMemorySpecified { get; set; }
    }

    /// <summary>
    /// Column family options.
    /// </summary>
    [XmlType(AnonymousType = true)]
    public sealed class ColumnFamilyOptions
    {
        /// <summary>
        /// Gets or sets the number of cell versions to keep, or 0 for all of them.
        /// </summary>
        [XmlElement("MaxVersions")]
        public int MaxVersions { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="MaxVersions" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool MaxVersionsSpecified { get; set; }

        /// <summary>
        /// Gets or sets the time order of the cell versions.
        /// </summary>
        [XmlElement("TimeOrder")]
        public ColumnFamilyTimeOrder TimeOrder { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="TimeOrder" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool TimeOrderSpecified { get; set; }

        /// <summary>
        /// Gets or sets the time to live, in seconds, or 0 for none.
        /// </summary>
        [XmlElement("TTL")]
        public int Ttl { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="Ttl" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool TtlSpecified { get; set; }
    }

    /// <summary>
    /// One column family of a table schema.
    /// </summary>
    [XmlType(AnonymousType = true)]
    public sealed class ColumnFamily
    {
        /// <summary>
        /// Gets or sets the column family id, assigned by the provider.
        /// </summary>
        [XmlAttribute("id")]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="Id" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool IdSpecified { get; set; }

        /// <summary>
        /// Gets or sets the generation this column family was last changed in.
        /// </summary>
        [XmlElement("Generation")]
        [CLSCompliant(false)]
        public ulong Generation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="Generation" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool GenerationSpecified { get; set; }

        /// <summary>
        /// Gets or sets the column family name.
        /// </summary>
        [XmlElement("Name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the column family has been deleted.
        /// </summary>
        [XmlElement("Deleted")]
        public bool Deleted { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="Deleted" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool DeletedSpecified { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the values are indexed.
        /// </summary>
        [XmlElement("Index")]
        public bool Index { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="Index" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool IndexSpecified { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the qualifiers are indexed.
        /// </summary>
        [XmlElement("QualifierIndex")]
        public bool QualifierIndex { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="QualifierIndex" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool QualifierIndexSpecified { get; set; }

        [XmlElement("Options")]
        public ColumnFamilyOptions Options { get; set; }
    }

    /// <summary>
    /// One access group of a table schema.
    /// </summary>
    [XmlType(AnonymousType = true)]
    public sealed class AccessGroup
    {
        public AccessGroup()
        {
            this.ColumnFamilies = new List<ColumnFamily>();
        }

        /// <summary>
        /// Gets or sets the access group name.
        /// </summary>
        [XmlAttribute("name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the generation this access group was last changed in.
        /// </summary>
        [XmlElement("Generation")]
        [CLSCompliant(false)]
        public ulong Generation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="Generation" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool GenerationSpecified { get; set; }

        [XmlElement("Options")]
        public AccessGroupOptions Options { get; set; }

        /// <summary>
        /// Gets or sets the column family options every family in this group inherits.
        /// </summary>
        [XmlElement("ColumnFamilyDefaults")]
        public ColumnFamilyOptions ColumnFamilyDefaults { get; set; }

        /// <summary>
        /// Gets or sets the column families of this access group.
        /// </summary>
        [XmlElement("ColumnFamily")]
        public List<ColumnFamily> ColumnFamilies { get; set; }
    }

    /// <summary>
    /// A table schema as an object model.
    /// </summary>
    /// <example>
    /// <code>
    /// var schema = new TableSchema { AccessGroups = new List&lt;AccessGroup&gt;() };
    /// var group = new AccessGroup { Name = "default" };
    /// group.ColumnFamilies.Add(new ColumnFamily { Name = "a" });
    /// schema.AccessGroups.Add(group);
    /// ns.CreateTable("t", schema.ToString());
    /// </code>
    /// </example>
    [XmlRoot(ElementName = "Schema", IsNullable = false)]
    [XmlType(AnonymousType = true)]
    public sealed class TableSchema
    {
        /// <summary>
        /// Built once: XmlSerializer generates and compiles a serialization assembly per
        /// instance, which is far too expensive to repeat per call.
        /// </summary>
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(TableSchema));

        public TableSchema()
        {
            this.AccessGroups = new List<AccessGroup>();
        }

        /// <summary>
        /// Gets or sets the schema generation.
        /// </summary>
        [XmlElement("Generation")]
        [CLSCompliant(false)]
        public ulong Generation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="Generation" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool GenerationSpecified { get; set; }

        /// <summary>
        /// Gets or sets the group commit interval, in milliseconds.
        /// </summary>
        [XmlElement("GroupCommitInterval")]
        public int GroupCommitInterval { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether <see cref="GroupCommitInterval" /> is stated.
        /// </summary>
        [XmlIgnore]
        public bool GroupCommitIntervalSpecified { get; set; }

        /// <summary>
        /// Gets or sets the access group options every group inherits.
        /// </summary>
        [XmlElement("AccessGroupDefaults")]
        public AccessGroupOptions AccessGroupDefaults { get; set; }

        /// <summary>
        /// Gets or sets the column family options every family inherits.
        /// </summary>
        [XmlElement("ColumnFamilyDefaults")]
        public ColumnFamilyOptions ColumnFamilyDefaults { get; set; }

        /// <summary>
        /// Gets or sets the access groups of this schema.
        /// </summary>
        [XmlElement("AccessGroup")]
        public List<AccessGroup> AccessGroups { get; set; }

        /// <summary>
        /// Parses an xml table schema.
        /// </summary>
        /// <param name="xml">Xml table schema, mandatory.</param>
        /// <returns>The parsed schema.</returns>
        public static TableSchema Parse(string xml)
        {
            if (string.IsNullOrEmpty(xml))
            {
                throw new ArgumentNullException(nameof(xml));
            }

            using (var reader = new StringReader(xml))
            {
                return (TableSchema)Serializer.Deserialize(reader);
            }
        }

        /// <summary>
        /// Renders this schema as xml, in the form the providers accept.
        /// </summary>
        /// <returns>The xml table schema.</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();

            var namespaces = new XmlSerializerNamespaces();

            // no xsi/xsd declarations: the provider's parser rejects unknown attributes
            namespaces.Add(string.Empty, string.Empty);

            using (var writer = new SchemaWriter(sb))
            {
                Serializer.Serialize(writer, this, namespaces);
                writer.Flush();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Writes with UTF-8 declared and no self-closing tags.
        /// </summary>
        /// <remarks>
        /// Both details matter to the provider's schema parser, which is why ht4n did the
        /// same: it reads element content, and an empty element written as &lt;Name /&gt;
        /// rather than &lt;Name&gt;&lt;/Name&gt; reads differently to it.
        /// </remarks>
        private sealed class SchemaWriter : XmlTextWriter
        {
            internal SchemaWriter(StringBuilder sb)
                : base(new Utf8StringWriter(sb))
            {
            }

            public override void WriteEndElement()
            {
                this.WriteFullEndElement();
            }
        }

        /// <summary>
        /// A StringWriter that claims UTF-8, so the xml declaration says so.
        /// </summary>
        private sealed class Utf8StringWriter : StringWriter
        {
            internal Utf8StringWriter(StringBuilder sb)
                : base(sb, CultureInfo.InvariantCulture)
            {
            }

            public override Encoding Encoding
            {
                get
                {
                    return Encoding.UTF8;
                }
            }
        }
    }
}
