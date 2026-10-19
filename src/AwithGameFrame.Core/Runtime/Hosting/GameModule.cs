using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Events;
using AwithGameFrame.Core.Timing;

namespace AwithGameFrame.Core.Hosting
{
    public enum GameHostState { Created, Starting, Running, Pausing, Paused, Resuming, Stopping, Stopped, Faulted }

    public sealed class GameContext
    {
        internal GameContext(GameServices services, MainThreadDispatcher dispatcher, CancellationToken lifetimeToken,
            GameClock clock, TimerScheduler timers, EventBus events)
        {
            Services = services;
            Dispatcher = dispatcher;
            LifetimeToken = lifetimeToken;
            Clock = clock;
            Timers = timers;
            Events = events;
        }

        public GameServices Services { get; }
        public MainThreadDispatcher Dispatcher { get; }
        public CancellationToken LifetimeToken { get; }
        public GameClock Clock { get; }
        public TimerScheduler Timers { get; }
        public EventBus Events { get; }
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
        /// <summary>Receives scaled game seconds; use context.Clock.UnscaledDeltaTime for unscaled work.</summary>
        void Tick(GameContext context, double deltaSeconds);
    }

    public interface IGamePauseModule
    {
        Task PauseAsync(GameContext context, CancellationToken cancellationToken);
        Task ResumeAsync(GameContext context, CancellationToken cancellationToken);
    }

    /// <summary>Optional cleanup polling while Paused. This must not advance game simulation or time.</summary>
    public interface IGameMaintenanceModule
    {
        void Maintain(GameContext context);
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
