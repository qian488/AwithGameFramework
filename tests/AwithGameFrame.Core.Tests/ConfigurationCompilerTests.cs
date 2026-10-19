using System;
using System.Collections.Generic;
using System.IO;
using AwithGameFrame.Tools.Configuration;
using NUnit.Framework;

namespace AwithGameFrame.Tests
{
    public class ConfigurationCompilerTests
    {
        private const string Schema = "{\"namespace\":\"Example.Configuration\",\"tables\":[{\"name\":\"Item\",\"fields\":[{\"name\":\"Id\",\"type\":\"int\"},{\"name\":\"Title\",\"type\":\"string\"}]}]}";

        [Test]
        public void ReorderedRowsAndObjectFieldsProduceIdenticalData()
        {
            var first = ConfigurationCompiler.Compile(Schema, Data("[{\"Id\":2,\"Title\":\"B\"},{\"Id\":1,\"Title\":\"A\"}]"));
            var second = ConfigurationCompiler.Compile(Schema, Data("[{\"Title\":\"A\",\"Id\":1},{\"Title\":\"B\",\"Id\":2}]"));
            Assert.That(first["Item.json"], Is.EqualTo(second["Item.json"]));
            Assert.That(first["Item.cs"], Is.EqualTo(second["Item.cs"]));
        }

        [TestCase("[{\"Id\":1,\"Title\":\"A\"},{\"Id\":1,\"Title\":\"B\"}]")]
        [TestCase("[{\"Id\":1,\"Title\":12}]")]
        [TestCase("[{\"Id\":1,\"Title\":\"A\",\"Typo\":5}]")]
        [TestCase("[{\"Id\":1}]")]
        [TestCase("[{\"Id\":1,\"Id\":2,\"Title\":\"A\"}]")]
        public void RejectsAmbiguousOrInvalidRows(string json)
        {
            Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.Compile(Schema, Data(json)));
        }

        [Test]
        public void RejectsMissingReferencedIds()
        {
            var schema = Schema.Replace("\"Title\",\"type\":\"string\"", "\"ParentId\",\"type\":\"int\",\"references\":\"Item\"");
            Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.Compile(schema, Data("[{\"Id\":1,\"ParentId\":2}]")));
            Assert.DoesNotThrow(() => ConfigurationCompiler.Compile(schema, Data("[{\"Id\":1,\"ParentId\":1}]")));
        }

        [Test]
        public void SchemaChangesInvalidateGeneratedData()
        {
            var before = ConfigurationCompiler.Compile(Schema, Data("[]"));
            var after = ConfigurationCompiler.Compile(Schema.Replace("string", "bool"), Data("[]"));
            Assert.That(before["Item.json"], Is.Not.EqualTo(after["Item.json"]));
        }

        [Test]
        public void ObsoleteGeneratedTablesCannotPassVerificationOrBeRepublished()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AwithConfigOutput", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "RemovedTable.json"), "{}");
                Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.AssertOutputSet(directory, new[] { "Item.json", "Item.cs" }));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void GeneratedNamesCannotShadowRequiredMembersOrAttributes()
        {
            Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.Compile(Schema.Replace("\"Item\"", "\"SchemaHash\""), new Dictionary<string, string> { ["SchemaHash"] = "[]" }));
            var output = ConfigurationCompiler.Compile(Schema.Replace("\"Item\"", "\"SerializableAttribute\""), new Dictionary<string, string> { ["SerializableAttribute"] = "[]" });
            Assert.That(output["SerializableAttribute.cs"], Does.Contain("[global::System.SerializableAttribute]"));
        }

        private static Dictionary<string, string> Data(string json) => new Dictionary<string, string> { ["Item"] = json };
    }
}
