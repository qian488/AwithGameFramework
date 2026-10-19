using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using UnityEngine;

namespace AwithGameFrame.Unity.Hosting
{
    /// <summary>Drives one independent host. Legacy FrameworkEntry remains a separate compatibility entry.</summary>
    public class GameHostBehaviour : MonoBehaviour
    {
        public GameHost Host { get; private set; }
        public bool IsReady { get; private set; }
        public Exception Failure { get; private set; }
        private bool closing;
        private bool applicationPaused;
        private bool allowQuit;
        private bool quitPending;
        private readonly SemaphoreSlim pauseGate = new SemaphoreSlim(1, 1);

        protected virtual void ConfigureHost(GameHost host) { }
        protected virtual Task OnStartedAsync() => Task.CompletedTask;
        protected virtual Task OnFailureAsync(Exception exception) => Task.CompletedTask;

        protected virtual async void Awake()
        {
            Host = new GameHost();
            Application.wantsToQuit += WantsToQuit;
            try
            {
                ConfigureHost(Host);
                await Host.StartAsync();
                if (closing) return;
                IsReady = true;
                await ApplyPauseStateAsync();
                if (closing) return;
                await OnStartedAsync();
            }
            catch (OperationCanceledException) when (closing) { }
            catch (Exception exception)
            {
                Failure = exception;
                Debug.LogException(exception);
                try { await ShutdownAsync(); }
                catch (Exception cleanup) { Debug.LogException(cleanup); }
                await OnFailureAsync(exception);
            }
        }

        protected virtual void Update()
        {
            // Startup and shutdown may need the dispatcher; pump until the host itself closes it.
            if (Host == null) return;
            try { Host.Tick(Time.unscaledDeltaTime); }
            catch (Exception exception) { Failure = exception; Debug.LogException(exception); }
        }

        protected virtual async void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (!IsReady || closing) return;
            try { await ApplyPauseStateAsync(); }
            catch (OperationCanceledException) when (closing) { }
            catch (Exception exception) { Failure = exception; Debug.LogException(exception); }
        }

        private async Task ApplyPauseStateAsync()
        {
            await pauseGate.WaitAsync();
            try
            {
                while (!closing)
                {
                    if (applicationPaused && Host.State == GameHostState.Running) await Host.PauseAsync();
                    else if (!applicationPaused && Host.State == GameHostState.Paused) await Host.ResumeAsync();
                    else break;
                }
            }
            finally { pauseGate.Release(); }
        }

        /// <summary>Await before destroying the host object or changing its owning scene.</summary>
        public async Task ShutdownAsync()
        {
            closing = true;
            IsReady = false;
            if (Host != null) await Host.StopAsync();
            allowQuit = true;
        }

        private bool WantsToQuit()
        {
            if (allowQuit || Host == null || Host.State == GameHostState.Stopped || Host.State == GameHostState.Faulted) return true;
            if (!quitPending) { quitPending = true; _ = QuitAsync(); }
            return false;
        }

        private async Task QuitAsync()
        {
            try { await ShutdownAsync(); }
            catch (Exception exception) { Failure = exception; Debug.LogException(exception); }
            finally { allowQuit = true; Application.Quit(); }
        }

        protected virtual async void OnDestroy()
        {
            Application.wantsToQuit -= WantsToQuit;
            try { await ShutdownAsync(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
