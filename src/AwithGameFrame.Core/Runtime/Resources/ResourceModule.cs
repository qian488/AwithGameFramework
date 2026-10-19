using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;

namespace AwithGameFrame.Core.Resources
{
    /// <summary>Registers the store in this host and releases it when this module stops.</summary>
    public sealed class ResourceModule : GameModule
    {
        public ResourceModule(ResourceStore store, string id = "resources", IReadOnlyCollection<string> dependencies = null)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Module ID cannot be empty.", nameof(id));
            Id = id;
            Dependencies = dependencies == null ? Array.Empty<string>() : new List<string>(dependencies).AsReadOnly();
        }

        public ResourceStore Store { get; }
        public override string Id { get; }
        public override IReadOnlyCollection<string> Dependencies { get; }
        public override Task StartAsync(GameContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Services.Register(Store);
            return Task.CompletedTask;
        }
        public override Task StopAsync(GameContext context, CancellationToken cancellationToken) => Store.DisposeAsync().AsTask();
    }
}
