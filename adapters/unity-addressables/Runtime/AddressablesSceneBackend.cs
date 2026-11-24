using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Scenes;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using NativeAddressables = UnityEngine.AddressableAssets.Addressables;

namespace AwithGameFrame.Unity.Addressables
{
    /// <summary>Loads scenes additively so SceneService can own their activation and unloading.</summary>
    public sealed class AddressablesSceneBackend : ISceneBackend
    {
        private readonly int thread = Environment.CurrentManagedThreadId;
        private readonly AddressablesCatalog catalog;

        public AddressablesSceneBackend() { }

        internal AddressablesSceneBackend(AddressablesCatalog catalog) => this.catalog = catalog;

        public async Task<ISceneInstance> LoadAsync(string key, ResourceScope resources, CancellationToken cancellationToken)
        {
            VerifyThread(thread);
            cancellationToken.ThrowIfCancellationRequested();
            var handle = catalog == null
                ? NativeAddressables.LoadSceneAsync(key, LoadSceneMode.Additive)
                : NativeAddressables.LoadSceneAsync(catalog.Locate(key, typeof(SceneInstance)), LoadSceneMode.Additive);
            try
            {
                await AddressablesOperation.CompleteAsync(handle);
                if (handle.Status != AsyncOperationStatus.Succeeded || !handle.Result.Scene.isLoaded)
                    throw new InvalidOperationException("Addressables scene load failed: " + key, handle.OperationException);

                // SceneService disposes a late native scene when the original caller cancels.
                return new Instance(handle, thread);
            }
            catch
            {
                if (handle.IsValid()) NativeAddressables.Release(handle);
                throw;
            }
        }

        private static void VerifyThread(int owner)
        {
            if (Environment.CurrentManagedThreadId != owner)
                throw new InvalidOperationException("Addressables scenes require the creating Unity thread.");
        }

        private sealed class Instance : ISceneInstance
        {
            private readonly AsyncOperationHandle<SceneInstance> load;
            private readonly Scene scene;
            private readonly int owner;
            private Task unloading;

            public Instance(AsyncOperationHandle<SceneInstance> load, int owner)
            {
                this.load = load;
                this.owner = owner;
                scene = load.Result.Scene;
            }

            public Task ActivateAsync(CancellationToken token)
            {
                VerifyThread(owner);
                token.ThrowIfCancellationRequested();
                if (!scene.IsValid() || !scene.isLoaded || !SceneManager.SetActiveScene(scene))
                    throw new InvalidOperationException("Cannot activate Addressables scene: " + scene.path);
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                VerifyThread(owner);
                if (unloading == null || unloading.IsFaulted || unloading.IsCanceled)
                    unloading = UnloadAsync();
                return new ValueTask(unloading);
            }

            private async Task UnloadAsync()
            {
                if (!load.IsValid()) return;
                var unload = NativeAddressables.UnloadSceneAsync(load, autoReleaseHandle: false);
                try
                {
                    await AddressablesOperation.CompleteAsync(unload);
                    if (unload.Status != AsyncOperationStatus.Succeeded || scene.isLoaded)
                        throw new InvalidOperationException("Addressables scene unload failed: " + scene.path, unload.OperationException);
                }
                finally { NativeAddressables.Release(unload); }
            }
        }
    }
}
