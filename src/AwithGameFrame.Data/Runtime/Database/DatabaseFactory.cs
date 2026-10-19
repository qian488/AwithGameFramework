using System;
using System.Collections.Generic;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>由宿主显式注册数据库驱动，便携库不依赖驱动程序。</summary>
    public static class DatabaseFactory
    {
        private static readonly Dictionary<DatabaseType, Func<IDatabaseProvider>> Providers = new Dictionary<DatabaseType, Func<IDatabaseProvider>>();
        private static readonly object Sync = new object();

        public static void Initialize() { }

        public static void RegisterProvider(DatabaseType databaseType, Type providerType)
        {
            if (providerType == null) throw new ArgumentNullException(nameof(providerType));
            if (!typeof(IDatabaseProvider).IsAssignableFrom(providerType) || providerType.IsAbstract || providerType.GetConstructor(Type.EmptyTypes) == null)
                throw new ArgumentException("Provider must implement IDatabaseProvider and have a public parameterless constructor.", nameof(providerType));
            RegisterProvider(databaseType, () => (IDatabaseProvider)Activator.CreateInstance(providerType));
        }

        public static void RegisterProvider(DatabaseType databaseType, Func<IDatabaseProvider> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (Sync) Providers[databaseType] = factory;
        }

        public static IDatabaseProvider CreateProvider(DatabaseType databaseType)
        {
            Func<IDatabaseProvider> factory;
            lock (Sync) Providers.TryGetValue(databaseType, out factory);
            return factory?.Invoke();
        }

        public static DatabaseType[] GetSupportedDatabaseTypes()
        {
            lock (Sync) return new List<DatabaseType>(Providers.Keys).ToArray();
        }

        public static bool IsDatabaseTypeSupported(DatabaseType databaseType)
        {
            lock (Sync) return Providers.ContainsKey(databaseType);
        }
    }
}
