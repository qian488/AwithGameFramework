using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Data.Configuration;
using AwithGameFrame.Foundation.DataPersistence;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class ConfigTableTests
    {
        private const string Schema = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private readonly ISerializer serializer = new JsonSerializer();

        [Test]
        public async Task GeneratedDocument_ShouldLoadFieldsAndLookupTypedRows()
        {
            var json = "{\"schemaHash\":\"" + Schema + "\",\"rows\":[{\"id\":7,\"name\":\"sword\",\"categoryId\":2}]}";
            var table = await ConfigTable<Item>.LoadAsync(Encoding.UTF8.GetBytes(json), serializer, Schema, row => row.Id);
            Assert.Multiple(() =>
            {
                Assert.That(table.Count, Is.EqualTo(1));
                Assert.That(table[7].Name, Is.EqualTo("sword"));
                Assert.That(table.ContainsId(7), Is.True);
                Assert.That(table.TryGet(99, out _), Is.False);
            });
        }

        [Test]
        public void WrongSchemaOrMalformedDocument_ShouldRejectBeforePublishingTable()
        {
            Assert.ThrowsAsync<ConfigurationValidationException>(async () =>
                await ConfigTable<Item>.LoadAsync(Encoding.UTF8.GetBytes("{\"schemaHash\":\"old\",\"rows\":[]}"), serializer, Schema, row => row.Id));
            Assert.ThrowsAsync<ConfigurationValidationException>(async () =>
                await ConfigTable<Item>.LoadAsync(Encoding.UTF8.GetBytes("{\"schemaHash\":\"" + Schema + "\"}"), serializer, Schema, row => row.Id));
            Assert.ThrowsAsync<ConfigurationValidationException>(async () =>
                await ConfigTable<Item>.LoadAsync(Encoding.UTF8.GetBytes("truncated {"), serializer, Schema, row => row.Id));
        }

        [Test]
        public async Task DuplicateIdsAndNullRows_ShouldFailWithUsefulDiagnostics()
        {
            var duplicate = await Encode(new Item { Id = 12 }, new Item { Id = 12 });
            var error = Assert.ThrowsAsync<ConfigurationValidationException>(async () =>
                await ConfigTable<Item>.LoadAsync(duplicate, serializer, Schema, row => row.Id));
            Assert.That(error.Message, Does.Contain("12"));
            var nullRow = await Encode((Item)null);
            Assert.ThrowsAsync<ConfigurationValidationException>(async () =>
                await ConfigTable<Item>.LoadAsync(nullRow, serializer, Schema, row => row.Id));
        }

        [Test]
        public async Task CrossTableReferences_ShouldRejectMissingTargetAndAllowExplicitOptionalZero()
        {
            var categories = await ConfigTable<Item>.LoadAsync(await Encode(new Item { Id = 2 }), serializer, Schema, row => row.Id);
            var valid = await ConfigTable<Item>.LoadAsync(await Encode(new Item { Id = 7, CategoryId = 2 }), serializer, Schema, row => row.Id);
            Assert.DoesNotThrow(() => valid.ValidateReferences(categories, row => row.CategoryId, "CategoryId"));
            var invalid = await ConfigTable<Item>.LoadAsync(await Encode(new Item { Id = 8, CategoryId = 99 }), serializer, Schema, row => row.Id);
            var error = Assert.Throws<ConfigurationValidationException>(() => invalid.ValidateReferences(categories, row => row.CategoryId, "CategoryId"));
            Assert.That(error.Message, Does.Contain("8").And.Contain("99").And.Contain("CategoryId"));
            var optional = await ConfigTable<Item>.LoadAsync(await Encode(new Item { Id = 9 }), serializer, Schema, row => row.Id);
            Assert.DoesNotThrow(() => optional.ValidateReferences(categories, row => row.CategoryId, "CategoryId", allowZero: true));
            Assert.Throws<ConfigurationValidationException>(() => optional.ValidateReferences(categories, row => row.CategoryId, "CategoryId"));
        }

        [Test]
        public void CancelledLoad_ShouldNotPublishTable()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(async () => await ConfigTable<Item>.LoadAsync(Encoding.UTF8.GetBytes("{}"), serializer, Schema, row => row.Id, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }

        private Task<byte[]> Encode(params Item[] rows) => serializer.SerializeAsync(new ConfigTableDocument<Item> { SchemaHash = Schema, Rows = new List<Item>(rows) });
        public sealed class Item { public int Id; public string Name; public int CategoryId; }
    }
}
