using System;
using System.IO;
using System.Threading.Tasks;
using AwithGameFrame.Core.Logging;
using AwithGameFrame.Foundation.DataPersistence;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture, NonParallelizable]
    public class PersistenceLifecycleTests
    {
        private string directory;

        [SetUp]
        public async Task SetUp()
        {
            await DataPersistenceAPI.ShutdownAsync();
            directory = Path.Combine(Path.GetTempPath(), "AwithGameFrame.Tests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public async Task TearDown()
        {
            await DataPersistenceAPI.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        [Test]
        public async Task Shutdown_ShouldDrainSaveAndRejectNewOperationsBeforeDisposing()
        {
            var completion = new TaskCompletionSource<DataOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new ControlledStorage { SaveHandler = () => completion.Task };
            await Initialize(provider);
            var save = DataPersistenceAPI.SaveAsync("profile", 42);
            var shutdown = DataPersistenceAPI.ShutdownAsync();
            try
            {
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(DataPersistenceAPI.IsInitialized, Is.False);
                Assert.That(provider.DisposeCount, Is.Zero);
                Assert.ThrowsAsync<InvalidOperationException>(async () => await DataPersistenceAPI.SaveAsync("late", 43));
                Assert.Throws<InvalidOperationException>(() => DataPersistenceAPI.RegisterStorageProvider(StorageType.PlayerPrefs, provider));
            }
            finally
            {
                completion.TrySetResult(DataOperationResult.Success);
                await Task.WhenAll(save, shutdown).WaitAsync(TimeSpan.FromSeconds(5));
            }
            Assert.That(await save, Is.EqualTo(DataOperationResult.Success));
            Assert.That(await shutdown, Is.EqualTo(DataOperationResult.Success));
            Assert.That(provider.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Shutdown_ShouldWaitForEveryAdmittedOperation()
        {
            var first = new TaskCompletionSource<DataOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<DataOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var provider = new ControlledStorage { SaveHandler = () => ++calls == 1 ? first.Task : second.Task };
            await Initialize(provider);
            var saveOne = DataPersistenceAPI.SaveAsync("one", 1);
            var saveTwo = DataPersistenceAPI.SaveAsync("two", 2);
            var shutdown = DataPersistenceAPI.ShutdownAsync();
            try
            {
                first.SetResult(DataOperationResult.Success);
                await saveOne.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(provider.DisposeCount, Is.Zero);
            }
            finally
            {
                first.TrySetResult(DataOperationResult.Success);
                second.TrySetResult(DataOperationResult.Success);
                await Task.WhenAll(saveOne, saveTwo, shutdown).WaitAsync(TimeSpan.FromSeconds(5));
            }
            Assert.That(provider.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FailedOperation_ShouldStillAllowShutdownToComplete()
        {
            var completion = new TaskCompletionSource<DataOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new ControlledStorage { SaveHandler = () => completion.Task };
            await Initialize(provider);
            var save = DataPersistenceAPI.SaveAsync("profile", 42);
            var shutdown = DataPersistenceAPI.ShutdownAsync();
            completion.SetException(new IOException("Injected write failure"));
            Assert.That(await save.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(DataOperationResult.Failed));
            Assert.That(await shutdown.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(DataOperationResult.Success));
            Assert.That(provider.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Shutdown_ShouldDrainAggregateEnumerationWithoutClearingProvidersMidway()
        {
            var completion = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new ControlledStorage { KeysHandler = () => completion.Task };
            await Initialize(provider);
            var keys = DataPersistenceAPI.GetAllKeysAsync();
            var shutdown = DataPersistenceAPI.ShutdownAsync();
            try
            {
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(provider.DisposeCount, Is.Zero);
            }
            finally
            {
                completion.TrySetResult(new[] { "profile" });
                await Task.WhenAll(keys, shutdown).WaitAsync(TimeSpan.FromSeconds(5));
            }
            Assert.That(await keys, Does.Contain("profile"));
        }

        [Test]
        public async Task FailedInitialization_ShouldDisposePartialSessionAndPermitCleanRetry()
        {
            var successful = new ControlledStorage();
            var failing = new ControlledStorage { InitializationResult = DataOperationResult.Failed };
            DataPersistenceAPI.RegisterStorageProvider(StorageType.JsonFile, successful);
            DataPersistenceAPI.RegisterStorageProvider(StorageType.BinaryFile, failing);

            Assert.That(await DataPersistenceAPI.InitializeAsync(new DataPersistenceConfig { DataRootPath = directory }),
                Is.EqualTo(DataOperationResult.Failed));
            Assert.That(DataPersistenceAPI.IsInitialized, Is.False);
            Assert.That(successful.DisposeCount, Is.EqualTo(1));
            Assert.That(failing.DisposeCount, Is.EqualTo(1));

            Assert.That(await DataPersistenceAPI.InitializeAsync(new DataPersistenceConfig { DataRootPath = directory }),
                Is.EqualTo(DataOperationResult.Success));
            Assert.That(await DataPersistenceAPI.SaveAsync("retry", 19), Is.EqualTo(DataOperationResult.Success));
            Assert.That((await DataPersistenceAPI.LoadAsync<int>("retry")).data, Is.EqualTo(19));
        }

        [Test]
        public async Task AliasedProvider_ShouldInitializeAndDisposeOnlyOnce()
        {
            var provider = new ControlledStorage();
            DataPersistenceAPI.RegisterStorageProvider(StorageType.JsonFile, provider);
            await Initialize(provider);
            Assert.That(provider.InitializeCount, Is.EqualTo(1));
            await DataPersistenceAPI.ShutdownAsync();
            Assert.That(provider.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DisposeFailure_ShouldNotPreventOtherProvidersOrNextSession()
        {
            var failing = new ControlledStorage { ThrowOnDispose = true };
            var healthy = new ControlledStorage();
            DataPersistenceAPI.RegisterStorageProvider(StorageType.JsonFile, healthy);
            await Initialize(failing);
            Assert.That(await DataPersistenceAPI.ShutdownAsync(), Is.EqualTo(DataOperationResult.Failed));
            Assert.That(healthy.DisposeCount, Is.EqualTo(1));
            Assert.That(failing.DisposeCount, Is.EqualTo(1));
            Assert.That(await DataPersistenceAPI.InitializeAsync(new DataPersistenceConfig { DataRootPath = directory }),
                Is.EqualTo(DataOperationResult.Success));
        }

        [Test]
        public async Task ThrowingLogCallback_ShouldNotInterruptProviderCleanup()
        {
            var providers = new[]
            {
                new ControlledStorage { ThrowOnDispose = true },
                new ControlledStorage { ThrowOnDispose = true },
                new ControlledStorage { ThrowOnDispose = true }
            };
            DataPersistenceAPI.RegisterStorageProvider(StorageType.JsonFile, providers[0]);
            DataPersistenceAPI.RegisterStorageProvider(StorageType.BinaryFile, providers[1]);
            await Initialize(providers[2]);
            int loggingFailures = 0;
            using (new HostLogScope((level, message, context) =>
            {
                loggingFailures++;
                throw new IOException("Injected host logging failure");
            }))
            {
                Assert.That(await DataPersistenceAPI.ShutdownAsync(), Is.EqualTo(DataOperationResult.Failed));
            }
            Assert.That(loggingFailures, Is.EqualTo(3));
            foreach (var provider in providers) Assert.That(provider.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ThrowingInitializationLog_ShouldRollbackBeforePublishingSession()
        {
            var provider = new ControlledStorage();
            DataPersistenceAPI.RegisterStorageProvider(StorageType.PlayerPrefs, provider);
            bool? initializedDuringCallback = null;
            using (new HostLogScope((level, message, context) =>
            {
                if (!message.Contains("Data persistence initialized.")) return;
                initializedDuringCallback = DataPersistenceAPI.IsInitialized;
                throw new IOException("Injected initialization log failure");
            }))
            {
                Assert.That(await DataPersistenceAPI.InitializeAsync(new DataPersistenceConfig { DataRootPath = directory }),
                    Is.EqualTo(DataOperationResult.Failed));
            }
            Assert.That(initializedDuringCallback, Is.False);
            Assert.That(DataPersistenceAPI.IsInitialized, Is.False);
            Assert.That(provider.DisposeCount, Is.EqualTo(1));
        }

        private async Task Initialize(ControlledStorage provider)
        {
            DataPersistenceAPI.RegisterStorageProvider(StorageType.PlayerPrefs, provider);
            Assert.That(await DataPersistenceAPI.InitializeAsync(new DataPersistenceConfig
            {
                DataRootPath = directory,
                DefaultStorageType = StorageType.PlayerPrefs
            }), Is.EqualTo(DataOperationResult.Success));
        }

        private sealed class HostLogScope : IDisposable
        {
            private readonly Action<LogLevel, string, object> previousOutput = LoggingHost.Output;
            private readonly LoggingConfig config = LoggingManager.GetInstance().Config;
            private readonly LogMode previousMode;
            private readonly LogLevel previousLevel;
            private readonly bool hadCategory;
            private readonly bool previousCategory;

            public HostLogScope(Action<LogLevel, string, object> output)
            {
                previousMode = config.Mode;
                previousLevel = config.Level;
                hadCategory = config.CategoryEnabled.TryGetValue(LogCategory.Core, out previousCategory);
                config.Mode = LogMode.Console;
                config.Level = LogLevel.Trace;
                config.CategoryEnabled[LogCategory.Core] = true;
                LoggingHost.Output = output;
            }

            public void Dispose()
            {
                LoggingHost.Output = previousOutput;
                config.Mode = previousMode;
                config.Level = previousLevel;
                if (hadCategory) config.CategoryEnabled[LogCategory.Core] = previousCategory;
                else config.CategoryEnabled.Remove(LogCategory.Core);
            }
        }

        private sealed class ControlledStorage : IStorageProvider
        {
            public StorageType StorageType => StorageType.PlayerPrefs;
            public bool IsInitialized { get; private set; }
            public int InitializeCount { get; private set; }
            public int DisposeCount { get; private set; }
            public bool ThrowOnDispose { get; set; }
            public DataOperationResult InitializationResult { get; set; } = DataOperationResult.Success;
            public Func<Task<DataOperationResult>> SaveHandler { get; set; } = () => Task.FromResult(DataOperationResult.Success);
            public Func<Task<string[]>> KeysHandler { get; set; } = () => Task.FromResult(Array.Empty<string>());

            public Task<DataOperationResult> InitializeAsync() => InitializeAsync(null);
            public Task<DataOperationResult> InitializeAsync(object config)
            {
                InitializeCount++;
                IsInitialized = true;
                return Task.FromResult(InitializationResult);
            }

            public Task<DataOperationResult> SaveAsync(string key, byte[] data) => SaveAsync<byte[]>(key, data);
            public async Task<DataOperationResult> SaveAsync<T>(string key, T data)
            {
                var result = await SaveHandler();
                if (!IsInitialized) throw new ObjectDisposedException(nameof(ControlledStorage));
                return result;
            }

            public Task<(byte[] data, DataOperationResult result)> LoadAsync(string key) => LoadAsync<byte[]>(key);
            public Task<(T data, DataOperationResult result)> LoadAsync<T>(string key) => Task.FromResult((default(T), DataOperationResult.NotFound));
            public Task<DataOperationResult> DeleteAsync(string key) => Task.FromResult(DataOperationResult.Success);
            public Task<bool> ExistsAsync(string key) => Task.FromResult(false);
            public Task<string[]> GetAllKeysAsync() => KeysHandler();
            public Task<DataOperationResult> ClearAsync() => Task.FromResult(DataOperationResult.Success);
            public Task<StorageStatistics> GetStatisticsAsync() => Task.FromResult(new StorageStatistics());

            public void Dispose()
            {
                DisposeCount++;
                IsInitialized = false;
                if (ThrowOnDispose) throw new IOException("Injected dispose failure");
            }
        }
    }
}
