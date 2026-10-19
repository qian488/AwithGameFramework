using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Core.Hosting
{
    public enum GameHostState { Created, Starting, Running, Pausing, Paused, Resuming, Stopping, Stopped, Faulted }

    public sealed class GameContext
    {
        internal GameContext(GameServices services, MainThreadDispatcher dispatcher, CancellationToken lifetimeToken)
        {
            Services = services;
            Dispatcher = dispatcher;
            LifetimeToken = lifetimeToken;
        }

        public GameServices Services { get; }
        public MainThreadDispatcher Dispatcher { get; }
        public CancellationToken LifetimeToken { get; }
    }

    public interface IGameModule
    {
        string Id { get; }
        IReadOnlyCollection<string> Dependencies { get; }
        Task StartAsync(GameContext context, CancellationToken cancellationToken);
        Task StopAsync(GameContext context, CancellationToken cancellationToken);
    }

    public interface IGameTickModule
    {
        void Tick(GameContext context, double deltaSeconds);
    }

    public interface IGamePauseModule
    {
        Task PauseAsync(GameContext context, CancellationToken cancellationToken);
        Task ResumeAsync(GameContext context, CancellationToken cancellationToken);
    }

    /// <summary>Convenience base; use instance fields, not the legacy static managers.</summary>
    public abstract class GameModule : IGameModule, IGameTickModule, IGamePauseModule
    {
        public virtual string Id => GetType().FullName;
        public virtual IReadOnlyCollection<string> Dependencies => Array.Empty<string>();
        public virtual Task StartAsync(GameContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task StopAsync(GameContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task PauseAsync(GameContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task ResumeAsync(GameContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual void Tick(GameContext context, double deltaSeconds) { }
    }
}
