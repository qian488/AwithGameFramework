using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using NativeAddressables = UnityEngine.AddressableAssets.Addressables;

namespace AwithGameFrame.Unity.Addressables
{
    /// <summary>Loads Addressables assets through ResourceStore ownership and releases the native handle with the lease.</summary>
    public sealed class AddressablesResourceLoader : IResourceLoader
    {
        private readonly int thread = Environment.CurrentManagedThreadId;
        private readonly AddressablesCatalog catalog;

        public AddressablesResourceLoader() { }

        internal AddressablesResourceLoader(AddressablesCatalog catalog) => this.catalog = catalog;

        public async Task<LoadedResource> LoadAsync(string key, Type resourceType, CancellationToken cancellationToken)
        {
            VerifyThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Addressables key cannot be empty.", nameof(key));
            if (!typeof(UnityEngine.Object).IsAssignableFrom(resourceType))
                throw new ArgumentException("Expected a Unity asset type.", nameof(resourceType));

            var location = catalog == null ? await LocateAsync(key, resourceType) : catalog.Locate(key, resourceType);
            cancellationToken.ThrowIfCancellationRequested();
            var handle = NativeAddressables.LoadAssetAsync<UnityEngine.Object>(location);
            try
            {
                await AddressablesOperation.CompleteAsync(handle);
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw new InvalidOperationException("Addressables asset load failed: " + key, handle.OperationException);
                if (!resourceType.IsInstanceOfType(handle.Result))
                    throw new InvalidOperationException("Addressables asset type does not match request: " + key);

                var asset = handle.Result;
                return new LoadedResource(asset, () => ReleaseAsync(handle),
                    UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(asset));
            }
            catch
            {
                if (handle.IsValid()) NativeAddressables.Release(handle);
                throw;
            }
        }

        private static async Task<IResourceLocation> LocateAsync(string key, Type resourceType)
        {
            var locations = NativeAddressables.LoadResourceLocationsAsync(key, resourceType);
            try
            {
                await AddressablesOperation.CompleteAsync(locations);
                if (locations.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException("Addressables lookup failed: " + key, locations.OperationException);
                if (locations.Result.Count == 0)
                    throw new InvalidOperationException($"Addressables key '{key}' has no asset of type {resourceType.FullName}.");
                if (locations.Result.Count != 1)
                    throw new InvalidOperationException($"Addressables key '{key}' resolves to multiple assets. Use a unique asset address.");
                return locations.Result[0];
            }
            finally { NativeAddressables.Release(locations); }
        }

        private Task ReleaseAsync(AsyncOperationHandle<UnityEngine.Object> handle)
        {
            VerifyThread();
            if (handle.IsValid()) NativeAddressables.Release(handle);
            return Task.CompletedTask;
        }

        private void VerifyThread()
        {
            if (Environment.CurrentManagedThreadId != thread)
                throw new InvalidOperationException("Addressables resources require the creating Unity thread.");
        }
    }

    internal static class AddressablesOperation
    {
        internal static Task CompleteAsync<T>(AsyncOperationHandle<T> handle)
        {
            if (handle.IsDone) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            handle.Completed += _ => completion.TrySetResult(true);
            return completion.Task;
        }
    }
}
