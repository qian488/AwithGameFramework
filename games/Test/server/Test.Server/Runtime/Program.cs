using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using Test.Application;
using Test.Server.Pvp;

namespace Test.Server
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            ServerOptions options;
            double? runForSeconds;
            try { options = ServerOptions.Load(args, out runForSeconds); }
            catch (Exception error) { Log("error", "configuration_failed", error.ToString()); return 1; }

            var previousContext = SynchronizationContext.Current;
            var context = new HostSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            using var startupCancellation = new CancellationTokenSource();
            int shutdownRequested = 0;
            var elapsed = Stopwatch.StartNew();
            bool ShutdownRequested() => Volatile.Read(ref shutdownRequested) != 0
                || (runForSeconds.HasValue && elapsed.Elapsed.TotalSeconds >= runForSeconds.Value);
            ConsoleCancelEventHandler cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                Interlocked.Exchange(ref shutdownRequested, 1);
            };
            Console.CancelKeyPress += cancelHandler;
            using var termination = OperatingSystem.IsWindows() ? null :
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal =>
                {
                    signal.Cancel = true;
                    Interlocked.Exchange(ref shutdownRequested, 1);
                });

            var host = new GameHost(dispatchBudgetPerTick: options.DispatchBudgetPerTick);
            host.Lifecycle += entry => Log("info", "host_lifecycle", new
            {
                entry.HostId, Phase = entry.Phase.ToString(), Status = entry.Status.ToString(),
                entry.ModuleId, entry.ElapsedSeconds, State = entry.State.ToString(),
                entry.IsRollback, Error = entry.Error?.ToString()
            });
            int exitCode = 0;
            try
            {
                host.AddModule(new TestModule());
                if (options.PvpPort != 0) host.AddModule(new PvpRealtimeModule(options.PvpPort));
                if (options.OnlineProbePort != 0) host.AddModule(new OnlineProbeModule(options.OnlineProbePort));
                if (options.ScorePort != 0) host.AddModule(new ContractScoresModule(options));
                Task startup = host.StartAsync(startupCancellation.Token);
                PumpUntil(startup, host, context, () =>
                {
                    if (ShutdownRequested() && !startupCancellation.IsCancellationRequested)
                        startupCancellation.Cancel();
                });
                startup.GetAwaiter().GetResult();
                Log("info", "server_started", new { Game = "Test", options.TickRate, host.Id });
                RunTicks(host, context, options.TickRate, elapsed, ShutdownRequested);
            }
            catch (OperationCanceledException) when (ShutdownRequested())
            {
                Log("info", "startup_cancelled", null);
            }
            catch (Exception error)
            {
                Log("error", "server_failed", error.ToString());
                exitCode = 1;
            }
            finally
            {
                try
                {
                    Task shutdown = host.StopAsync();
                    PumpUntil(shutdown, host, context);
                    shutdown.GetAwaiter().GetResult();
                    Log("info", "server_stopped", new { host.Id, ExitCode = exitCode });
                }
                catch (Exception error)
                {
                    Log("error", "shutdown_failed", error.ToString());
                    exitCode = 1;
                }
                finally
                {
                    Console.CancelKeyPress -= cancelHandler;
                    SynchronizationContext.SetSynchronizationContext(previousContext);
                }
            }
            return exitCode;
        }

        private static void RunTicks(GameHost host, HostSynchronizationContext context, int tickRate,
            Stopwatch elapsed, Func<bool> shutdownRequested)
        {
            double step = 1d / tickRate;
            double next = elapsed.Elapsed.TotalSeconds + step;
            while (!shutdownRequested())
            {
                context.Drain();
                double now = elapsed.Elapsed.TotalSeconds;
                int ticks = 0;
                while (now >= next && ticks < 4 && !shutdownRequested())
                {
                    host.Tick(step);
                    next += step;
                    ticks++;
                }
                if (ticks == 4 && now >= next)
                {
                    Log("warning", "tick_backlog_dropped", new { Seconds = now - next });
                    next = now + step;
                }
                Thread.Sleep(1);
            }
        }

        private static void PumpUntil(Task task, GameHost host, HostSynchronizationContext context, Action poll = null)
        {
            while (!task.IsCompleted)
            {
                poll?.Invoke();
                context.Drain();
                if (!task.IsCompleted) host.Tick(0);
                if (!task.IsCompleted) Thread.Sleep(1);
            }
        }

        private static void Log(string level, string eventName, object data)
        {
            string line = JsonSerializer.Serialize(new { TimestampUtc = DateTimeOffset.UtcNow, Level = level, Event = eventName, Data = data });
            if (level == "error") Console.Error.WriteLine(line);
            else Console.WriteLine(line);
        }
    }
}
