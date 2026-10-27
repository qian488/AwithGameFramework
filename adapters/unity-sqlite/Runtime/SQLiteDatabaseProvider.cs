namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>Unity project binding for the shipped Mono.Data.Sqlite plugin.</summary>
    public class SQLiteDatabaseProvider : AdoDatabaseProvider
    {
        public SQLiteDatabaseProvider()
            : base(DatabaseType.SQLite, connection => new Mono.Data.Sqlite.SqliteConnection(connection))
        {
        }
    }
}
