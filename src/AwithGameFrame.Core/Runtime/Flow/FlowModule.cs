using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;

namespace AwithGameFrame.Core.Flow
{
    /// <summary>Attaches flow entry, frame ticks and final exit to a GameHost session.</summary>
    public sealed class FlowModule : GameModule
    {
        private readonly FlowMachine _machine;
        private readonly IGameFlow _initialFlow;

        public FlowModule(FlowMachine machine, IGameFlow initialFlow = null, string id = "flow", IReadOnlyCollection<string> dependencies = null)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Module ID cannot be empty.", nameof(id));
            _initialFlow = initialFlow;
            Id = id;
            Dependencies = dependencies == null ? Array.Empty<string>() : new List<string>(dependencies).AsReadOnly();
        }

        public override string Id { get; }
        public override IReadOnlyCollection<string> Dependencies { get; }
        public override Task StartAsync(GameContext context, CancellationToken cancellationToken) =>
            _initialFlow == null ? Task.CompletedTask : _machine.SwitchAsync(_initialFlow, cancellationToken);
        public override Task StopAsync(GameContext context, CancellationToken cancellationToken) => _machine.StopAsync();
        public override void Tick(GameContext context, double deltaSeconds) => _machine.Tick(deltaSeconds);
    }
}
