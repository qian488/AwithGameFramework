using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using UnityEngine;

namespace AwithGameFrame.Unity.Hosting
{
    /// <summary>Use with ResourceStore(host.Dispatcher). Resources has no native cancellation or per-prefab unload.</summary>
    public sealed class UnityResourceLoader : IResourceLoader
    {
        private static readonly Dictionary<int, int> References = new Dictionary<int, int>();

        public Task<LoadedResource> LoadAsync(string key, Type resourceType, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!typeof(UnityEngine.Object).IsAssignableFrom(resourceType)) throw new ArgumentException("Expected a Unity asset type.", nameof(resourceType));
#if UNITY_WEBGL && !UNITY_EDITOR
            // Unity 2022 WebGL can stall the resource-store await chain after ResourceRequest completes.
            return Task.FromResult(Acquire(key, Resources.Load(key, resourceType)));
#else
            var completion = new TaskCompletionSource<LoadedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = Resources.LoadAsync(key, resourceType);
            request.completed += _ =>
            {
                try { completion.TrySetResult(Acquire(key, request.asset)); }
                catch (Exception exception) { completion.TrySetException(exception); }
            };
            // ResourceRequest cannot be aborted. The store releases a late success after all callers cancel.
            return completion.Task;
#endif
        }

        private static LoadedResource Acquire(string key, UnityEngine.Object asset)
        {
            if (asset == null) throw new InvalidOperationException("Unity resource not found: " + key);
            var size = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(asset);
            var instanceId = asset.GetInstanceID();
            References.TryGetValue(instanceId, out var count);
            References[instanceId] = count + 1;
            return new LoadedResource(asset, () =>
            {
                var remaining = References[instanceId] - 1;
                if (remaining > 0) References[instanceId] = remaining;
                else
                {
                    References.Remove(instanceId);
                    // Unity only permits UnloadAsset on non-prefab assets. Prefabs become eligible for UnloadUnusedAssets.
                    if (asset != null && !(asset is GameObject) && !(asset is Component)) Resources.UnloadAsset(asset);
                }
                return Task.CompletedTask;
            }, size);
        }
    }
}
