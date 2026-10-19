using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using Godot;

namespace AwithGameFrame.Godot
{
    /// <summary>Loads known raw textures/audio in the background; scripts, containers and unknown formats stay on the engine thread.</summary>
    public sealed class GodotThreadedResourceLoader : IResourceLoader
    {
        private sealed class SharedLoad
        {
            public Task<Resource> Pending;
            public int References;
        }
        private static readonly Dictionary<string, SharedLoad> Loads = new Dictionary<string, SharedLoad>(StringComparer.Ordinal);
        private readonly Func<Resource, long> estimateBytes;
        public GodotThreadedResourceLoader(Func<Resource, long> estimateBytes = null) => this.estimateBytes = estimateBytes;

        public async Task<LoadedResource> LoadAsync(string key, Type resourceType, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!typeof(Resource).IsAssignableFrom(resourceType)) throw new ArgumentException("Expected a Godot Resource type.", nameof(resourceType));
            if (!Loads.TryGetValue(key, out var shared))
            {
                shared = new SharedLoad();
                Loads.Add(key, shared);
                shared.Pending = LoadNativeAsync(key, resourceType);
            }
            shared.References++;
            try
            {
                // A running native load cannot be aborted. ResourceStore owns cancellation and releases late success.
                var resource = await shared.Pending;
                return new LoadedResource(resource, () =>
                {
                    Release(key, shared);
                    return Task.CompletedTask;
                }, estimateBytes?.Invoke(resource) ?? 0);
            }
            catch
            {
                Release(key, shared);
                throw;
            }
        }

        private static Task<Resource> LoadNativeAsync(string key, Type resourceType)
        {
            // C# script bridges contain a weak native pointer for each managed type.
            // Recreating one on a worker can race its last reference being destroyed on
            // the engine thread, especially when exported scenes are reopened. Scene and
            // resource containers can embed scripts, so serialize them with node cleanup.
            bool raw = IsRawAsset(key) && !typeof(PackedScene).IsAssignableFrom(resourceType) && !typeof(Script).IsAssignableFrom(resourceType);
            if (!raw)
            {
                try { return Task.FromResult(LoadNative(key)); }
                catch (Exception error) { return Task.FromException<Resource>(error); }
            }
            // Godot 4.5.2 can publish THREAD_LOAD_LOADED before its worker releases the
            // LoadToken. A concurrent LoadThreadedGet then leaves that RefCounted at zero
            // without deleting it. Run the synchronous, thread-safe resource load on a
            // managed worker: its local token outlives the complete native load call.
            // The await in LoadAsync returns the resource to the engine context.
            return Task.Run(() => LoadNative(key));
        }

        private static Resource LoadNative(string key) => ResourceLoader.Load(key, "", ResourceLoader.CacheMode.Ignore)
            ?? throw new InvalidOperationException("Godot resource not found: " + key);

        private static bool IsRawAsset(string key)
        {
            switch (System.IO.Path.GetExtension(key).ToLowerInvariant())
            {
                case ".png": case ".jpg": case ".jpeg": case ".webp": case ".bmp":
                case ".tga": case ".svg": case ".hdr": case ".exr": case ".ktx": case ".dds":
                case ".wav": case ".ogg": case ".mp3": return true;
                default: return false;
            }
        }

        private static void Release(string key, SharedLoad shared)
        {
            if (--shared.References != 0) return;
            if (Loads.TryGetValue(key, out var current) && ReferenceEquals(current, shared)) Loads.Remove(key);
            if (shared.Pending.Status == TaskStatus.RanToCompletion) shared.Pending.Result.Dispose();
        }
    }
}
