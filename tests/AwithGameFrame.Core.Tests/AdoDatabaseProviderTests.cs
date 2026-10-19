using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using AwithGameFrame.Foundation.DataPersistence;
using AwithGameFrame.Core.Logging;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class AdoDatabaseProviderTests
    {
        private AdoDatabaseProvider provider;
        private readonly List<SqliteConnection> connections = new List<SqliteConnection>();

        [SetUp]
        public async Task SetUp()
        {
            connections.Clear();
            provider = new AdoDatabaseProvider(DatabaseType.SQLite, connectionString =>
            {
                var connection = new SqliteConnection(connectionString);
                connections.Add(connection);
                return connection;
            });
            Assert.That(await provider.InitializeAsync("Data Source=:memory:"), Is.EqualTo(DataOperationResult.Success));
        }

        [TearDown]
        public async Task TearDown()
        {
            await provider.CloseAsync();
            foreach (var connection in connections) connection.Dispose();
        }

        [Test]
        public async Task Query_ShouldMapNamedColumnsToPropertiesAndFieldsWithSqliteConversions()
        {
            var result = await provider.QuerySingleAsync<PlayerRow>(
                "SELECT 12 AS level, 'Ada' AS Name, 2 AS Mode, NULL AS OptionalScore, 7 AS FieldValue, 99 AS Unmapped");

            Assert.That(result.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(result.data.Name, Is.EqualTo("Ada"));
            Assert.That(result.data.Level, Is.EqualTo(12));
            Assert.That(result.data.Mode, Is.EqualTo(PlayerMode.Active));
            Assert.That(result.data.OptionalScore, Is.Null);
            Assert.That(result.data.FieldValue, Is.EqualTo(7));
        }

        [Test]
        public async Task Query_ShouldConvertUnsignedDecimalEnumsAndPreserveNullRows()
        {
            var unsigned = await provider.QuerySingleAsync<ulong>("SELECT '18446744073709551615'");
            Assert.That(unsigned.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(unsigned.data, Is.EqualTo(ulong.MaxValue));
            var amount = await provider.QuerySingleAsync<decimal>("SELECT '123.45'");
            Assert.That(amount.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(amount.data, Is.EqualTo(123.45m));
            var mode = await provider.QuerySingleAsync<PlayerMode>("SELECT 2");
            Assert.That(mode.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(mode.data, Is.EqualTo(PlayerMode.Active));
            var rows = await provider.QueryAsync<int?>("SELECT 5 UNION ALL SELECT NULL");
            Assert.That(rows.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(rows.data, Is.EqualTo(new int?[] { 5, null }));
            var strings = await provider.QueryAsync<string>("SELECT 'value' UNION ALL SELECT NULL");
            Assert.That(strings.data, Is.EqualTo(new string[] { "value", null }));
        }

        [Test]
        public async Task Query_ShouldReturnFailureForIncompatibleMappedValue()
        {
            var result = await provider.QuerySingleAsync<PlayerRow>("SELECT 'not a number' AS Level");
            Assert.That(result.result, Is.EqualTo(DataOperationResult.Failed));
            Assert.That(result.data, Is.Null);
            var rows = await provider.QueryAsync<PlayerRow>("SELECT 5 AS Level UNION ALL SELECT 'not a number'");
            Assert.That(rows.result, Is.EqualTo(DataOperationResult.Failed));
            Assert.That(rows.data, Is.Empty);
        }

        [Test]
        public async Task Query_ShouldConvertDateAndGuidValuesFromSqliteStorage()
        {
            var timestamp = new DateTime(2026, 10, 19, 12, 34, 56, DateTimeKind.Utc);
            var date = await provider.QuerySingleAsync<DateTime>("SELECT @value",
                new Dictionary<string, object> { ["@value"] = timestamp.ToString("O") });
            Assert.That(date.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(date.data, Is.EqualTo(timestamp));
            Assert.That(date.data.Kind, Is.EqualTo(DateTimeKind.Utc));
            var id = Guid.NewGuid();
            var guid = await provider.QuerySingleAsync<Guid>("SELECT @value",
                new Dictionary<string, object> { ["@value"] = id });
            Assert.That(guid.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(guid.data, Is.EqualTo(id));
        }

        [Test]
        public async Task Query_ShouldPreserveBlobAndDistinguishMissingRowFromNull()
        {
            var blob = await provider.QuerySingleAsync<byte[]>("SELECT @payload",
                new Dictionary<string, object> { ["@payload"] = new byte[] { 0, 127, 255 } });
            Assert.That(blob.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(blob.data, Is.EqualTo(new byte[] { 0, 127, 255 }));
            var nullValue = await provider.QuerySingleAsync<int?>("SELECT NULL");
            Assert.That(nullValue.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(nullValue.data, Is.Null);
            var missing = await provider.QuerySingleAsync<int>("SELECT 1 WHERE 0");
            Assert.That(missing.result, Is.EqualTo(DataOperationResult.NotFound));
        }

        [Test]
        public async Task CommittedTransaction_ShouldReleaseStateSoCloseAndNextTransactionWork()
        {
            Assert.That(await provider.ExecuteAsync("CREATE TABLE entries (value INTEGER)"), Is.EqualTo(DataOperationResult.Success));
            using (var transaction = await provider.BeginTransactionAsync())
            {
                Assert.That(transaction, Is.Not.Null);
                Assert.That(await transaction.ExecuteAsync("INSERT INTO entries VALUES (1)"), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await transaction.CommitAsync(), Is.EqualTo(DataOperationResult.Success));
            }
            using (var next = await provider.BeginTransactionAsync())
            {
                Assert.That(next, Is.Not.Null);
                Assert.That(await next.RollbackAsync(), Is.EqualTo(DataOperationResult.Success));
            }
            Assert.That((await provider.QuerySingleAsync<int>("SELECT COUNT(*) FROM entries")).data, Is.EqualTo(1));
            Assert.That(await provider.CloseAsync(), Is.EqualTo(DataOperationResult.Success));
            Assert.That(provider.IsConnected, Is.False);
            Assert.That(await provider.CloseAsync(), Is.EqualTo(DataOperationResult.Success));
        }

        [Test]
        public async Task ProviderCommands_ShouldParticipateInActiveTransactionAndRollbackOnDispose()
        {
            Assert.That(await provider.ExecuteAsync("CREATE TABLE entries (value INTEGER)"), Is.EqualTo(DataOperationResult.Success));
            var transaction = await provider.BeginTransactionAsync();
            Assert.That(transaction, Is.Not.Null);
            Assert.That(await provider.BeginTransactionAsync(), Is.Null);
            Assert.That(await provider.ExecuteAsync("INSERT INTO entries VALUES (@value)",
                new Dictionary<string, object> { ["@value"] = 4 }), Is.EqualTo(DataOperationResult.Success));
            var inside = await provider.QuerySingleAsync<int>("SELECT COUNT(*) FROM entries");
            Assert.That(inside.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(inside.data, Is.EqualTo(1));
            transaction.Dispose();
            Assert.That((await provider.QuerySingleAsync<int>("SELECT COUNT(*) FROM entries")).data, Is.Zero);
            Assert.That(await transaction.ExecuteAsync("INSERT INTO entries VALUES (9)"), Is.EqualTo(DataOperationResult.NotInitialized));
            Assert.That(await transaction.CommitAsync(), Is.EqualTo(DataOperationResult.NotInitialized));
            Assert.That(await provider.CloseAsync(), Is.EqualTo(DataOperationResult.Success));
        }

        [Test]
        public async Task Close_ShouldRollbackAndInvalidateOutstandingTransaction()
        {
            var transaction = await provider.BeginTransactionAsync();
            Assert.That(transaction, Is.Not.Null);
            Assert.That(await provider.CloseAsync(), Is.EqualTo(DataOperationResult.Success));
            Assert.That(provider.IsConnected, Is.False);
            Assert.That(await transaction.RollbackAsync(), Is.EqualTo(DataOperationResult.NotInitialized));
            Assert.That(await transaction.ExecuteAsync("SELECT 1"), Is.EqualTo(DataOperationResult.NotInitialized));
        }

        [Test]
        public async Task FailedCommit_ShouldRollbackAndAllowNextTransaction()
        {
            Assert.That(await provider.ExecuteAsync("PRAGMA foreign_keys=ON; CREATE TABLE parent (id INTEGER PRIMARY KEY); " +
                "CREATE TABLE child (parent_id INTEGER REFERENCES parent(id) DEFERRABLE INITIALLY DEFERRED)"),
                Is.EqualTo(DataOperationResult.Success));
            using (var transaction = await provider.BeginTransactionAsync())
            {
                Assert.That(await transaction.ExecuteAsync("INSERT INTO child VALUES (99)"), Is.EqualTo(DataOperationResult.Success));
                Assert.That(await transaction.CommitAsync(), Is.EqualTo(DataOperationResult.Failed));
                Assert.That(await transaction.RollbackAsync(), Is.EqualTo(DataOperationResult.NotInitialized));
            }
            var count = await provider.QuerySingleAsync<int>("SELECT COUNT(*) FROM child");
            Assert.That(count.result, Is.EqualTo(DataOperationResult.Success));
            Assert.That(count.data, Is.Zero);
            using (var next = await provider.BeginTransactionAsync())
            {
                Assert.That(next, Is.Not.Null);
                Assert.That(await next.RollbackAsync(), Is.EqualTo(DataOperationResult.Success));
            }
            Assert.That(await provider.CloseAsync(), Is.EqualTo(DataOperationResult.Success));
        }

        [Test]
        [NonParallelizable]
        public async Task FailedCommit_ShouldRollbackAndCloseWhenHostLoggingThrows()
        {
            Assert.That(await provider.ExecuteAsync("PRAGMA foreign_keys=ON; CREATE TABLE parent (id INTEGER PRIMARY KEY); " +
                "CREATE TABLE child (parent_id INTEGER REFERENCES parent(id) DEFERRABLE INITIALLY DEFERRED)"),
                Is.EqualTo(DataOperationResult.Success));
            var logging = LoggingManager.GetInstance();
            var originalConfig = logging.Config;
            var originalOutput = LoggingHost.Output;
            var outputAttempts = 0;
            logging.Config = new LoggingConfig { Level = LogLevel.Error, Mode = LogMode.FrameworkLog };
            LoggingHost.Output = (_, _, _) =>
            {
                outputAttempts++;
                throw new InvalidOperationException("Host log output failed.");
            };
            try
            {
                using (var transaction = await provider.BeginTransactionAsync())
                {
                    Assert.That(await transaction.ExecuteAsync("INSERT INTO child VALUES (99)"), Is.EqualTo(DataOperationResult.Success));
                    Assert.That(await transaction.CommitAsync(), Is.EqualTo(DataOperationResult.Failed));
                }
                Assert.That(outputAttempts, Is.GreaterThan(0));
                var count = await provider.QuerySingleAsync<int>("SELECT COUNT(*) FROM child");
                Assert.That(count.result, Is.EqualTo(DataOperationResult.Success));
                Assert.That(count.data, Is.Zero);
                Assert.That(await provider.CloseAsync(), Is.EqualTo(DataOperationResult.Success));
                Assert.That(provider.IsConnected, Is.False);
                Assert.That(connections[0].State, Is.EqualTo(ConnectionState.Closed));
            }
            finally
            {
                LoggingHost.Output = originalOutput;
                logging.Config = originalConfig;
            }
        }

        [Test]
        public async Task SchemaHelpers_ShouldCreateFindAndDropSqliteTable()
        {
            Assert.That(await provider.TableExistsAsync("players"), Is.False);
            Assert.That(await provider.CreateTableAsync("players", new Dictionary<string, string>
            {
                ["id"] = "INTEGER PRIMARY KEY",
                ["name"] = "TEXT"
            }), Is.EqualTo(DataOperationResult.Success));
            Assert.That(await provider.TableExistsAsync("players"), Is.True);
            Assert.That(await provider.DropTableAsync("players"), Is.EqualTo(DataOperationResult.Success));
            Assert.That(await provider.TableExistsAsync("players"), Is.False);
        }

        [Test]
        public async Task Reinitialize_ShouldClosePreviouslyOwnedConnection()
        {
            var original = connections[0];
            Assert.That(await provider.InitializeAsync("Data Source=:memory:"), Is.EqualTo(DataOperationResult.Success));
            Assert.That(original.State, Is.EqualTo(ConnectionState.Closed));
            Assert.That(provider.IsConnected, Is.True);
        }

        public enum PlayerMode : short { None, Idle, Active }

        public class PlayerRow
        {
            public string Name { get; set; }
            public int Level { get; set; }
            public PlayerMode Mode { get; set; }
            public int? OptionalScore { get; set; } = 99;
            public int FieldValue;
        }
    }
}
