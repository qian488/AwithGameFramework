using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using AwithGameFrame.Tools.Configuration;
using NUnit.Framework;

namespace AwithGameFrame.Tests
{
    public class ConfigurationComplexTests
    {
        private const string Schema = @"{
          ""namespace"":""Example.Configuration"",
          ""enums"":[{""name"":""Rarity"",""values"":{""Common"":0,""Rare"":1}}],
          ""types"":[
            {""name"":""Reward"",""fields"":[{""name"":""ItemId"",""type"":""int"",""references"":""Item""},{""name"":""Count"",""type"":""int""}]},
            {""name"":""Details"",""fields"":[{""name"":""Bonus"",""type"":""Reward""},{""name"":""Enabled"",""type"":""bool""}]}
          ],
          ""tables"":[{""name"":""Item"",""fields"":[
            {""name"":""Id"",""type"":""int""},
            {""name"":""Title"",""type"":""string""},
            {""name"":""Rarity"",""type"":""Rarity""},
            {""name"":""Tags"",""type"":""string[]""},
            {""name"":""Modes"",""type"":""Rarity[]""},
            {""name"":""Rewards"",""type"":""Reward[]""},
            {""name"":""Details"",""type"":""Details""},
            {""name"":""Groups"",""type"":""int[][]"",""references"":""Item""}
          ]}]
        }";
        private const string Row = @"{""Id"":1,""Title"":""Sword"",""Rarity"":""Rare"",""Tags"":[""weapon"",""shop""],""Modes"":[""Common"",1],""Rewards"":[{""ItemId"":1,""Count"":2}],""Details"":{""Bonus"":{""ItemId"":1,""Count"":3},""Enabled"":true},""Groups"":[[1],[]]}";
        private const string ScalarSchema = @"{""namespace"":""Example.Configuration"",""tables"":[{""name"":""Item"",""fields"":[{""name"":""Id"",""type"":""int""},{""name"":""Title"",""type"":""string""}]}]}";
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "AwithComplexConfig", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, true);

        [Test]
        public void GeneratesStronglyTypedEnumsStructuresAndNestedArrays()
        {
            var output = Compile(Schema, Row);
            Assert.Multiple(() =>
            {
                Assert.That(output.Keys, Is.EquivalentTo(new[] { "Item.cs", "Item.json", "Rarity.cs", "Reward.cs", "Details.cs" }));
                Assert.That(output["Item.cs"], Does.Contain("global::Example.Configuration.Rarity Rarity"));
                Assert.That(output["Item.cs"], Does.Contain("global::Example.Configuration.Reward[] Rewards"));
                Assert.That(output["Item.cs"], Does.Contain("global::Example.Configuration.Rarity[] Modes"));
                Assert.That(output["Item.cs"], Does.Contain("int[][] Groups"));
                Assert.That(output["Details.cs"], Does.Contain("global::Example.Configuration.Reward Bonus"));
                Assert.That(output["Rarity.cs"], Does.Contain("Common = 0").And.Contain("Rare = 1"));
                Assert.That(output["Reward.cs"], Does.Not.Contain("SchemaHash"));
            });
        }

        [TestCase("\"Rarity\":\"Rare\"", "\"Rarity\":\"Unknown\"", "Item.Rarity")]
        [TestCase("\"Rarity\":\"Rare\"", "\"Rarity\":999", "Item.Rarity")]
        [TestCase("\"Modes\":[\"Common\",1]", "\"Modes\":[2]", "Item.Modes[0]")]
        [TestCase("\"Tags\":[\"weapon\",\"shop\"]", "\"Tags\":[7]", "Item.Tags[0]")]
        [TestCase("\"Tags\":[\"weapon\",\"shop\"]", "\"Tags\":[null]", "Item.Tags[0]")]
        [TestCase("\"Tags\":[\"weapon\",\"shop\"]", "\"Tags\":null", "Item.Tags")]
        [TestCase("\"Rewards\":[{\"ItemId\":1,\"Count\":2}]", "\"Rewards\":[null]", "Item.Rewards[0]")]
        [TestCase("\"Bonus\":{\"ItemId\":1,\"Count\":3}", "\"Bonus\":null", "Item.Details.Bonus")]
        [TestCase("\"Count\":3", "\"Count\":null", "Item.Details.Bonus.Count")]
        [TestCase("\"Count\":3", "\"Typo\":3", "Item.Details.Bonus")]
        [TestCase("\"Count\":3", "\"Count\":3,\"Count\":4", "Item.Details.Bonus")]
        [TestCase(",\"Count\":3", "", "Item.Details.Bonus.Count")]
        [TestCase("\"Groups\":[[1],[]]", "\"Groups\":[null]", "Item.Groups[0]")]
        public void RejectsInvalidValuesAtTheirNestedPath(string before, string after, string path)
        {
            var error = Assert.Throws<InvalidDataException>(() => Compile(Schema, Row.Replace(before, after)));
            Assert.That(error.Message, Does.Contain(path));
        }

        [TestCase("\"ItemId\":1,\"Count\":2", "\"ItemId\":9,\"Count\":2", "Item.Rewards[0].ItemId")]
        [TestCase("\"ItemId\":1,\"Count\":3", "\"ItemId\":9,\"Count\":3", "Item.Details.Bonus.ItemId")]
        [TestCase("\"Groups\":[[1],[]]", "\"Groups\":[[9],[]]", "Item.Groups[0][0]")]
        public void ChecksForeignKeysInsideStructuresAndArrays(string before, string after, string path)
        {
            var error = Assert.Throws<InvalidDataException>(() => Compile(Schema, Row.Replace(before, after)));
            Assert.That(error.Message, Does.Contain(path).And.Contain("missing Item Id 9"));
        }

        [Test]
        public void ZeroForeignKeyRequiresExplicitAllowanceForEveryReferencingField()
        {
            var zeroRow = Row.Replace("\"ItemId\":1", "\"ItemId\":0").Replace("[[1],[]]", "[[0],[]]");
            Assert.Throws<InvalidDataException>(() => Compile(Schema, zeroRow));
            Assert.DoesNotThrow(() => Compile(Schema.Replace("\"references\":\"Item\"", "\"references\":\"Item\",\"allowZero\":true"), zeroRow));
        }

        [TestCase("\"Rare\":1", "\"Rare\":0")]
        [TestCase("\"Rare\":1", "\"bad-name\":1")]
        [TestCase("\"Common\":0,\"Rare\":1", "")]
        [TestCase("\"Common\":0,\"Rare\":1", "\"Rare\":0,\"Rare\":1")]
        [TestCase("\"type\":\"Reward[]\"", "\"type\":\"Missing[]\"")]
        [TestCase("\"type\":\"int\",\"references\":\"Item\"", "\"type\":\"string\",\"references\":\"Item\"")]
        [TestCase("\"type\":\"Reward\"", "\"type\":\"Details\"")]
        [TestCase("\"name\":\"Details\",\"fields\"", "\"name\":\"Rarity\",\"fields\"")]
        [TestCase("\"type\":\"string[]\"", "\"type\":\"Item[]\"")]
        public void RejectsInvalidOrRecursiveSchema(string before, string after)
        {
            Assert.Throws<InvalidDataException>(() => Compile(Schema.Replace(before, after), Row));
        }

        [Test]
        public void EnumObjectOrderDoesNotChangeGeneratedArtifactsButDefinitionChangesHash()
        {
            var first = Compile(Schema, Row);
            var reordered = Compile(Schema.Replace("\"Common\":0,\"Rare\":1", "\"Rare\":1,\"Common\":0"), Row);
            foreach (var file in first) Assert.That(reordered[file.Key], Is.EqualTo(file.Value), file.Key);
            var changed = Compile(Schema.Replace("\"Rare\":1", "\"Rare\":2"), Row.Replace("\"Modes\":[\"Common\",1]", "\"Modes\":[\"Common\",2]"));
            Assert.That(changed["Item.json"], Is.Not.EqualTo(first["Item.json"]));
            var structureChanged = Compile(Schema.Replace("\"name\":\"Count\",\"type\":\"int\"", "\"name\":\"Count\",\"type\":\"long\""), Row);
            Assert.That(structureChanged["Item.json"], Is.Not.EqualTo(first["Item.json"]));
        }

        [Test]
        public void CsvAndJsonProduceIdenticalTypedArtifactsIncludingComplexJsonCells()
        {
            using var row = JsonDocument.Parse(Row);
            var fields = row.RootElement.EnumerateObject().Reverse().ToArray();
            var csv = string.Join(",", fields.Select(property => property.Name)) + "\r\n" +
                string.Join(",", fields.Select(property => Quote(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText()))) + "\r\n";
            Write("Item.csv", csv);
            var csvOutput = ConfigurationCompiler.CompileDirectory(Schema, directory);
            var jsonOutput = Compile(Schema, Row);
            foreach (var file in jsonOutput) Assert.That(csvOutput[file.Key], Is.EqualTo(file.Value), file.Key);
        }

        [Test]
        public void Utf8BomCsvPreservesUnicodeCommasQuotesAndEmbeddedNewlines()
        {
            const string title = "中文,\"引号\"\r\n第二行\nthird line";
            File.WriteAllText(Path.Combine(directory, "Item.csv"), "Id,Title\r\n1," + Quote(title) + "\r\n2,\"\"\r\n", new UTF8Encoding(true));
            var result = ConfigurationCompiler.CompileDirectory(ScalarSchema, directory);
            using var document = JsonDocument.Parse(result["Item.json"]);
            var rows = document.RootElement.GetProperty("rows");
            Assert.That(rows.GetArrayLength(), Is.EqualTo(2));
            Assert.That(rows[0].GetProperty("Title").GetString(), Is.EqualTo(title));
            Assert.That(rows[1].GetProperty("Title").GetString(), Is.Empty);
        }

        [TestCase("Id,Title\n1,last")]
        [TestCase("Id,Title\r\n1,\"last\"")]
        [TestCase("Id,Title\n1,\n")]
        [TestCase("Id,Title\n1,")]
        public void CsvAcceptsFinalRecordWithOrWithoutNewlineAndEmptyString(string csv)
        {
            Write("Item.csv", csv);
            Assert.DoesNotThrow(() => ConfigurationCompiler.CompileDirectory(ScalarSchema, directory));
        }

        [TestCase("")]
        [TestCase("Id,Id\n1,2")]
        [TestCase("Id,Unknown\n1,a")]
        [TestCase("Id\n1")]
        [TestCase("Id,Title\n1")]
        [TestCase("Id,Title\n1,a,extra")]
        [TestCase("Id,Title\n1,\"unterminated")]
        [TestCase("Id,Title\n1,a\"quote")]
        [TestCase("Id,Title\n1,\"quoted\"tail")]
        [TestCase("Id,Title\n1,\"quoted\" \n")]
        [TestCase("Id,Title\n,missing id")]
        [TestCase("Id,Title\n2147483648,overflow")]
        public void CsvRejectsMalformedHeadersRecordsAndRequiredTypedCells(string csv)
        {
            Write("Item.csv", csv);
            Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.CompileDirectory(ScalarSchema, directory));
        }

        [Test]
        public void CsvScalarNumbersAreInvariantAndBooleanCaseIsAccepted()
        {
            var schema = ScalarSchema.Replace("{\"name\":\"Title\",\"type\":\"string\"}", "{\"name\":\"Large\",\"type\":\"long\"},{\"name\":\"Ratio\",\"type\":\"double\"},{\"name\":\"Weight\",\"type\":\"float\"},{\"name\":\"Enabled\",\"type\":\"bool\"}");
            Write("Item.csv", "Id,Large,Ratio,Weight,Enabled\n1,9223372036854775807,1.25,2.5,TRUE");
            Assert.DoesNotThrow(() => ConfigurationCompiler.CompileDirectory(schema, directory));
            Write("Item.csv", "Id,Large,Ratio,Weight,Enabled\n1,9223372036854775807,1e999,2.5,TRUE");
            Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.CompileDirectory(schema, directory));
        }

        [Test]
        public void CsvRejectsInvalidComplexJsonWithTableRecordAndFieldDiagnostic()
        {
            var schema = ScalarSchema.Replace("\"type\":\"string\"", "\"type\":\"int[]\"");
            Write("Item.csv", "Id,Title\n1,\"[broken\"");
            var error = Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.CompileDirectory(schema, directory));
            Assert.That(error.Message, Does.Contain("Item CSV record 2 field Title"));
            Write("Item.csv", "Id,Title\n1,\"[null]\"");
            Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.CompileDirectory(schema, directory));
        }

        [Test]
        public void DirectoryRequiresOneKnownSourcePerTableAndUtf8Encoding()
        {
            Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.CompileDirectory(ScalarSchema, directory));
            Write("Item.csv", "Id,Title\n1,A");
            Write("Item.json", "[]");
            Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.CompileDirectory(ScalarSchema, directory));
            File.Delete(Path.Combine(directory, "Item.json"));
            Write("Unknown.csv", "Id\n1");
            Assert.Throws<InvalidDataException>(() => ConfigurationCompiler.CompileDirectory(ScalarSchema, directory));
            File.Delete(Path.Combine(directory, "Unknown.csv"));
            File.WriteAllBytes(Path.Combine(directory, "Item.csv"), new byte[] { 0xff, 0xfe, 0x49, 0 });
            Assert.Throws<DecoderFallbackException>(() => ConfigurationCompiler.CompileDirectory(ScalarSchema, directory));
        }

        [Test]
        public void EmptyExtensionArraysPreserveLegacySchemaHashAndSourceBytes()
        {
            var legacy = ConfigurationCompiler.Compile(ScalarSchema, new Dictionary<string, string> { ["Item"] = "[]" });
            var explicitEmpty = ConfigurationCompiler.Compile(ScalarSchema.Replace("\"tables\":", "\"enums\":[],\"types\":[],\"tables\":"), new Dictionary<string, string> { ["Item"] = "[]" });
            foreach (var file in legacy) Assert.That(explicitEmpty[file.Key], Is.EqualTo(file.Value));
        }

        [Test]
        public void LegacyScalarDtoKeepsPublishedHashAndExactGeneratedSource()
        {
            const string schema = "{\"namespace\":\"AwithGameFrame.Samples.Shared\",\"tables\":[{\"name\":\"LevelConfig\",\"fields\":[{\"name\":\"Id\",\"type\":\"int\"},{\"name\":\"Title\",\"type\":\"string\"},{\"name\":\"Reward\",\"type\":\"int\"}]}]}";
            var output = ConfigurationCompiler.Compile(schema, new Dictionary<string, string> { ["LevelConfig"] = "[]" });
            const string hash = "e4d1b5d81e6d4047ddb796c39516b7aa1a1f271b1d829665b49972caec13bdaf";
            Assert.That(output["LevelConfig.cs"], Is.EqualTo(
                "// <auto-generated />\nusing System;\n\nnamespace AwithGameFrame.Samples.Shared\n{\n    [global::System.SerializableAttribute]\n    public sealed class LevelConfig\n    {\n" +
                "        public const string SchemaHash = \"" + hash + "\";\n        public int Id { get; set; }\n        public string Title { get; set; }\n        public int Reward { get; set; }\n    }\n}\n"));
            Assert.That(output["LevelConfig.json"], Is.EqualTo("{\"schemaHash\":\"" + hash + "\",\"rows\":[]}\n"));
        }

        private static IReadOnlyDictionary<string, string> Compile(string schema, string row) => ConfigurationCompiler.Compile(schema, new Dictionary<string, string> { ["Item"] = "[" + row + "]" });
        private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        private void Write(string name, string contents) => File.WriteAllText(Path.Combine(directory, name), contents, new UTF8Encoding(false));
    }
}
