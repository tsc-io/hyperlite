namespace Hyperlite.Test
{
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    using Hyperlite.Xml;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    ///     Parsing a table schema, and rendering one back.
    /// </summary>
    [TestClass]
    public sealed class TestTableSchema : TestBase
    {
        private readonly Regex regex = new Regex("test-", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Tests the xml table schema parse.
        /// </summary>
        [TestMethod]
        public void Parse()
        {
            var schema = TableSchema.Parse(
                "<Schema><AccessGroup name=\"default\"><ColumnFamily><Name>a</Name></ColumnFamily></AccessGroup></Schema>");
            Assert.IsNotNull(schema);

            Assert.IsNotNull(schema.AccessGroups);
            Assert.AreEqual(1, schema.AccessGroups.Count);

            var accessGroup = schema.AccessGroups[0];
            Assert.AreEqual("default", accessGroup.Name);
            Assert.IsNull(accessGroup.Options);
            Assert.IsNotNull(accessGroup.ColumnFamilies);
            Assert.AreEqual(1, accessGroup.ColumnFamilies.Count);

            var columnFamily = accessGroup.ColumnFamilies[0];
            Assert.AreEqual("a", columnFamily.Name);

            schema = TableSchema.Parse(
                "<Schema><AccessGroup name=\"default\"><ColumnFamily><Name>a</Name></ColumnFamily><ColumnFamily><Name>b</Name><Options><MaxVersions>3</MaxVersions></Options></ColumnFamily></AccessGroup></Schema>");
            Assert.IsNotNull(schema);

            Assert.IsNotNull(schema.AccessGroups);
            Assert.AreEqual(1, schema.AccessGroups.Count);

            accessGroup = schema.AccessGroups[0];
            Assert.AreEqual("default", accessGroup.Name);
            Assert.IsNull(accessGroup.Options);
            Assert.IsNotNull(accessGroup.ColumnFamilies);
            Assert.AreEqual(2, accessGroup.ColumnFamilies.Count);

            columnFamily = accessGroup.ColumnFamilies[0];
            Assert.AreEqual("a", columnFamily.Name);

            columnFamily = accessGroup.ColumnFamilies[1];
            Assert.AreEqual("b", columnFamily.Name);
            Assert.IsNotNull(columnFamily.Options);
            Assert.IsTrue(columnFamily.Options.MaxVersionsSpecified);
            Assert.AreEqual(3, columnFamily.Options.MaxVersions);
        }

        /// <summary>
        /// Tests the TableSchema rendering.
        /// </summary>
        [TestMethod]
        public void Render()
        {
            DropTables(this.regex);

            Assert.IsFalse(Ns.TableExists("test-1"));

            var schema = new TableSchema { AccessGroups = new List<AccessGroup>() };

            var accessGroup = new AccessGroup { Name = "default", ColumnFamilies = new List<ColumnFamily>() };
            accessGroup.ColumnFamilies.Add(new ColumnFamily { Name = "a" });
            accessGroup.ColumnFamilies.Add(new ColumnFamily { Name = "b", Options = new ColumnFamilyOptions { MaxVersions = 5, MaxVersionsSpecified = true } });
            schema.AccessGroups.Add(accessGroup);

            accessGroup = new AccessGroup { Name = "other", ColumnFamilies = new List<ColumnFamily>() };
            accessGroup.ColumnFamilies.Add(new ColumnFamily { Name = "c" });
            schema.AccessGroups.Add(accessGroup);

            var xml = schema.ToString();

            schema = TableSchema.Parse(xml);
            Assert.IsNotNull(schema);
            Assert.IsFalse(schema.GenerationSpecified);

            Assert.IsNotNull(schema.AccessGroups);
            Assert.AreEqual(2, schema.AccessGroups.Count);

            accessGroup = schema.AccessGroups[0];
            Assert.AreEqual("default", accessGroup.Name);
            Assert.IsNotNull(accessGroup.ColumnFamilies);
            Assert.AreEqual(2, accessGroup.ColumnFamilies.Count);

            var columnFamily = accessGroup.ColumnFamilies[0];
            Assert.AreEqual("a", columnFamily.Name);

            columnFamily = accessGroup.ColumnFamilies[1];
            Assert.AreEqual("b", columnFamily.Name);
            Assert.IsNotNull(columnFamily.Options);
            Assert.IsTrue(columnFamily.Options.MaxVersionsSpecified);
            Assert.AreEqual(5, columnFamily.Options.MaxVersions);

            accessGroup = schema.AccessGroups[1];
            Assert.AreEqual("other", accessGroup.Name);
            Assert.IsNotNull(accessGroup.ColumnFamilies);
            Assert.AreEqual(1, accessGroup.ColumnFamilies.Count);

            columnFamily = accessGroup.ColumnFamilies[0];
            Assert.AreEqual("c", columnFamily.Name);

            Ns.CreateTable("test-1", xml);
            Assert.IsTrue(Ns.TableExists("test-1"));
        }
    }
}
