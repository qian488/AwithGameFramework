using UnityEngine;
using UnityEngine.Scripting;

[assembly: AlwaysLinkAssembly]

namespace AwithGameFrame.Foundation.DataPersistence
{
    internal static class SQLiteIntegration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            DatabaseFactory.RegisterProvider(DatabaseType.SQLite, () => new SQLiteDatabaseProvider());
        }
    }
}
