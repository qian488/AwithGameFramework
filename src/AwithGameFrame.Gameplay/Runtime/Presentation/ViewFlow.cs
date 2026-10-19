using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Flow;
using AwithGameFrame.Core.Hosting;

namespace AwithGameFrame.Extensions.Presentation
{
    /// <summary>A re-enterable navigation destination. Every entry owns exactly the view it opened.</summary>
    public sealed class ViewFlow : GameFlow
    {
        private readonly ViewService service;
        private readonly ViewRequest request;
        private ViewHandle handle;
        public ViewFlow(ViewService service, ViewRequest request)
        { this.service = service ?? throw new ArgumentNullException(nameof(service)); this.request = request ?? throw new ArgumentNullException(nameof(request)); }
        public override string Id => request.Key;
        public ViewHandle Handle => handle;
        public override async Task EnterAsync(GameContext context, CancellationToken cancellationToken) => handle = await service.OpenAsync(request, cancellationToken);
        public override async Task ExitAsync(GameContext context, CancellationToken cancellationToken)
        {
            var current = handle; handle = null;
            if (current != null) await current.CloseAsync();
        }
    }

    public sealed class PresentationModule : GameModule
    {
        private readonly ViewService service;
        public PresentationModule(ViewService service, string id = "presentation", IReadOnlyCollection<string> dependencies = null)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Module ID cannot be empty.", nameof(id));
            Id = id;
            Dependencies = new List<string>(dependencies ?? new[] { "resources" }).AsReadOnly();
        }
        public override string Id { get; }
        public override IReadOnlyCollection<string> Dependencies { get; }
        public override Task StartAsync(GameContext context, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); context.Services.Register(service); return Task.CompletedTask; }
        public override Task StopAsync(GameContext context, CancellationToken cancellationToken) => service.StopAsync();
    }
}
