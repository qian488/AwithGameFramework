using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using NativeAddressables = UnityEngine.AddressableAssets.Addressables;

namespace AwithGameFrame.Unity.Addressables
{
    /// <summary>Creates asset and scene backends whose keys resolve against one external Addressables catalog.</summary>
    public sealed class AddressablesCatalog
    {
        private static readonly SemaphoreSlim CatalogLoads = new SemaphoreSlim(1, 1);
        private readonly IResourceLocator locator;
        private readonly int thread;

        private AddressablesCatalog(IResourceLocator locator)
        {
            this.locator = locator;
            thread = Environment.CurrentManagedThreadId;
        }

        public string Id => locator.LocatorId;

        public static async Task<AddressablesCatalog> LoadAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri == null || !uri.IsAbsoluteUri || (uri.Scheme != "https" && uri.Scheme != "http" && !uri.IsFile))
                throw new ArgumentException("Supply an absolute HTTP, HTTPS or file catalog URI.", nameof(uri));

            await CatalogLoads.WaitAsync(cancellationToken);
            try
            {
                var operation = NativeAddressables.LoadContentCatalogAsync(uri.AbsoluteUri, autoReleaseHandle: false);
                try
                {
                    await AddressablesOperation.CompleteAsync(operation);
                    if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null)
                        throw new InvalidOperationException("Addressables catalog load failed: " + uri.GetLeftPart(UriPartial.Path), operation.OperationException);

                    // This catalog resolves keys for its own backends. Restore global lookup after Unity registers it.
                    NativeAddressables.RemoveResourceLocator(operation.Result);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new AddressablesCatalog(operation.Result);
                }
                finally { NativeAddressables.Release(operation); }
            }
            finally { CatalogLoads.Release(); }
        }

        public AddressablesResourceLoader CreateResourceLoader()
        {
            VerifyThread();
            return new AddressablesResourceLoader(this);
        }

        public AddressablesSceneBackend CreateSceneBackend()
        {
            VerifyThread();
            return new AddressablesSceneBackend(this);
        }

        internal IResourceLocation Locate(string key, Type type)
        {
            VerifyThread();
            if (!locator.Locate(key, type, out var locations) || locations.Count == 0)
                throw new InvalidOperationException($"Addressables key '{key}' has no asset of type {type.FullName} in catalog '{Id}'.");
            if (locations.Count != 1)
                throw new InvalidOperationException($"Addressables key '{key}' resolves to multiple assets in catalog '{Id}'. Use a unique asset address.");
            return locations[0];
        }

        private void VerifyThread()
        {
            if (Environment.CurrentManagedThreadId != thread)
                throw new InvalidOperationException("Addressables catalogs require the creating Unity thread.");
        }
    }
}
