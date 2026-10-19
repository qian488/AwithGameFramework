using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using Godot;

namespace AwithGameFrame.Godot
{
    /// <summary>A synchronous Godot backend. ResourceStore owns sharing/cancellation; loading runs on its dispatcher.</summary>
    public sealed class GodotResourceLoader : IResourceLoader
    {
        public Task<LoadedResource> LoadAsync(string key, Type resourceType, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!typeof(Resource).IsAssignableFrom(resourceType)) throw new ArgumentException("Expected a Godot Resource type.", nameof(resourceType));
            // Ignore the global cache so disposing this owned wrapper cannot invalidate an unrelated direct loader's wrapper.
            var resource = ResourceLoader.Load(key, "", ResourceLoader.CacheMode.Ignore);
            if (resource == null) throw new InvalidOperationException("Godot resource not found: " + key);
            return Task.FromResult(new LoadedResource(resource, () => { resource.Dispose(); return Task.CompletedTask; }));
        }
    }
}
