using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AwithGameFrame.Networking.Realtime;
using NUnit.Framework;
using Test.Application.Combat.Pvp;
using Test.Application.Protocol;
using Test.Domain.Combat.Pvp;
using WirePlayer = Test.Application.Protocol.PvpPlayerSnapshot;

namespace Test.Server.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed partial class PvpServerIntegrationTests
    {
        [Test]
        public async Task RestartRejectsAnOldMatchAndAllowsTheSameRoomCodeToStartANewMatch()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var original = await ServerProcess.StartAsync(deadline.Token);
            await using var client = new PvpClientSession(original.Session);
            var recovery = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.RecoveryReceived += message => recovery.TrySetResult(message.Reason);
            var initial = await client.JoinAsync("duel", "first", cancellationToken: deadline.Token);
            Assert.That(initial.Accepted, Is.True, initial.Reason);
            await original.StopAsync();
            await using var restarted = await ServerProcess.StartAsync(deadline.Token, original.Session.Port);
            Assert.That(await recovery.Task.WaitAsync(deadline.Token), Is.EqualTo("MatchExpired"));
            await using var newClient = new PvpClientSession(restarted.Session);
            var next = await newClient.JoinAsync("duel", "first", cancellationToken: deadline.Token);
            Assert.That(next.Accepted, Is.True, next.Reason);
            Assert.That(next.MatchId, Is.Not.EqualTo(initial.MatchId));
        }

        [TestCase("rain-pass")]
        [TestCase("bbads")]
        public async Task TwoApplicationClientsPlayAndSeeTheSameAuthoritativeResult(string mode)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await using var server = await ServerProcess.StartAsync(deadline.Token);
            var connection = new InterruptibleTransportFactory();
            await using var first = new PvpClientSession(server.Session, connection);
            await using var second = new PvpClientSession(server.Session);
            var firstView = new View(first);
            var secondView = new View(second);
            var firstJoin = await first.JoinAsync("duel", "zeta", mode, deadline.Token);
            var secondJoin = await second.JoinAsync("duel", "alpha", mode, deadline.Token);
            Assert.That(firstJoin.Accepted, Is.True, firstJoin.Reason);
            Assert.That(secondJoin.Accepted, Is.True, secondJoin.Reason);
            Assert.That(firstJoin.PlayerSlot, Is.EqualTo(0));
            Assert.That(secondJoin.PlayerSlot, Is.EqualTo(1));
            Assert.That(firstJoin.MatchId, Is.EqualTo(secondJoin.MatchId));
            await Until(() => firstView.Current?.PeerConnected == true && secondView.Current?.PeerConnected == true, deadline.Token);

            if (mode == "rain-pass") connection.InterruptOnNextFrame();
            else connection.Interrupt();
            await Until(() => secondView.Snapshots.Values.Any(snapshot => !snapshot.PeerConnected && snapshot.RoomState == "Running") &&
                first.SessionEpoch > firstJoin.SessionEpoch && firstView.Current.SessionEpoch == first.SessionEpoch, deadline.Token);
            Assert.That(first.MatchId, Is.EqualTo(firstJoin.MatchId));
            Assert.That(first.PlayerSlot, Is.EqualTo(firstJoin.PlayerSlot));
            Assert.That(first.ReplayedFrames, mode == "rain-pass" ? Is.GreaterThan(0) : Is.Zero);

            long currentTick = firstView.Current.ServerTick;
            await first.SendInputAsync(currentTick, PvpButtons.None, 0, 0, 0, 0, deadline.Token);
            await first.SendInputAsync(currentTick + 60, PvpButtons.None, 0, 0, 0, 0, deadline.Token);
            long futureTick = firstView.Current.ServerTick + 11;
            await first.SendInputAsync(futureTick, PvpButtons.None, 0, 0, 0, 0, deadline.Token);
            await first.SendInputAsync(futureTick, PvpButtons.None, 0, 0, 0, 0, deadline.Token);
            await first.SendInputAsync(futureTick - 1, PvpButtons.None, 0, 0, 0, 0, deadline.Token);
            await Until(() => connection.Rejections.Contains("Late") && connection.Rejections.Contains("OutsideInputWindow") &&
                connection.Rejections.Contains("Duplicate") && connection.Rejections.Contains("OutOfOrder"), deadline.Token);
            await Until(() => firstView.Current.ServerTick >= futureTick, deadline.Token);

            if (mode == "bbads")
            {
                await SendAndConfirm(first, firstView, PvpButtons.EquipSword | PvpButtons.AttackPressed, 0, deadline.Token);
                await SendAndConfirm(first, firstView, PvpButtons.EquipBow | PvpButtons.AttackPressed, 1000, deadline.Token);
                await SendAndConfirm(second, secondView, PvpButtons.EquipSword | PvpButtons.AttackPressed, 0, deadline.Token);
                await Until(() => firstView.Players.TryGetValue(0, out var player) && player.Backpack?.Length == 2, deadline.Token);
                await SendAndConfirm(first, firstView, PvpButtons.Finish, 0, deadline.Token);
                await SendAndConfirm(second, secondView, PvpButtons.Finish, 0, deadline.Token);
                await Until(() => firstView.Snapshots.Values.Any(snapshot => snapshot.Phase == "Countdown" && snapshot.RemainingTicks > 0), deadline.Token);
            }
            else
            {
                for (int hit = 0; hit < 10; hit++)
                {
                    var buttons = hit == 0 ? PvpButtons.Attack | PvpButtons.Lock : PvpButtons.Attack;
                    long tick = await SendAndConfirm(first, firstView, buttons, 0, deadline.Token);
                    await Until(() => firstView.Current.ServerTick >= tick + 35 || firstView.Result != null, deadline.Token);
                    if (firstView.Result != null) break;
                }
            }

            await Until(() => firstView.Result != null && secondView.Result != null, deadline.Token);
            Assert.That(firstView.Result.Winner, Is.Zero);
            Assert.That(secondView.Result.Winner, Is.EqualTo(firstView.Result.Winner));
            await Until(() => firstView.Cues.Count != 0 && secondView.Cues.Count == firstView.Cues.Count, deadline.Token);
            Assert.That(firstView.Cues.ToArray(), Is.EqualTo(secondView.Cues.ToArray()));
            var common = firstView.Snapshots.Keys.Intersect(secondView.Snapshots.Keys).ToArray();
            Assert.That(common, Has.Length.GreaterThan(10));
            foreach (long sequence in common)
                Assert.That(firstView.Snapshots[sequence].StateHash, Is.EqualTo(secondView.Snapshots[sequence].StateHash));
            if (mode == "rain-pass")
            {
                Assert.That(connection.FramePushes, Is.GreaterThan(10));
                Assert.That(connection.StatePushes, Is.Zero);
                Assert.That(first.SimulationTick, Is.EqualTo(firstView.Result.Tick));
                Assert.That(second.SimulationTick, Is.EqualTo(secondView.Result.Tick));
            }
            else
            {
                Assert.That(connection.StatePushes, Is.GreaterThan(10));
                Assert.That(connection.FramePushes, Is.Zero);
                Assert.That(first.SimulationTick, Is.Zero);
                Assert.That(second.SimulationTick, Is.Zero);
            }

            await first.LeaveAsync();
            await second.LeaveAsync();
        }

        private static async Task<long> SendAndConfirm(PvpClientSession session, View view, PvpButtons buttons, int x, CancellationToken token)
        {
            long confirmed = view.Current.ConfirmedInputSequence;
            long tick = view.Current.ServerTick + 7;
            await session.SendInputAsync(tick, buttons, x, 0, 1000, 0, token);
            await Until(() => view.Current.ConfirmedInputSequence > confirmed || view.Current.RoomState == "Finished", token);
            return tick;
        }

        private static async Task Until(Func<bool> predicate, CancellationToken token)
        {
            while (!predicate()) await Task.Delay(10, token);
        }

        private sealed class View
        {
            private PvpSnapshotMessage current;
            private PvpMatchResultMessage result;
            internal readonly ConcurrentDictionary<long, PvpSnapshotMessage> Snapshots = new();
            internal readonly ConcurrentDictionary<int, WirePlayer> Players = new();
            internal readonly ConcurrentQueue<(long Sequence, string Type)> Cues = new();

            internal View(PvpClientSession session)
            {
                session.SnapshotReceived += snapshot =>
                {
                    Snapshots[snapshot.SnapshotSequence] = snapshot;
                    Volatile.Write(ref current, snapshot);
                };
                session.PlayerReceived += player => Players[player.PlayerSlot] = player;
                session.CueReceived += cue => Cues.Enqueue((cue.CueSequence, cue.CueType));
                session.ResultReceived += message => Volatile.Write(ref result, message);
            }

            internal PvpSnapshotMessage Current => Volatile.Read(ref current);
            internal PvpMatchResultMessage Result => Volatile.Read(ref result);
        }

        private sealed class InterruptibleTransportFactory : IWebSocketTransportFactory
        {
            private Transport current;
            private int interruptOnFrame, framePushes, statePushes;
            private int interruptOnTournamentResult;
            private int droppedTournamentResults;
            internal readonly ConcurrentQueue<string> Rejections = new();
            internal int FramePushes => Volatile.Read(ref framePushes);
            internal int StatePushes => Volatile.Read(ref statePushes);

            public void Interrupt() => Volatile.Read(ref current).Interrupted.TrySetResult(true);
            public void InterruptOnNextFrame() => Interlocked.Exchange(ref interruptOnFrame, 1);
            public void InterruptOnTournamentResult() => Interlocked.Exchange(ref interruptOnTournamentResult, 1);
            public int DroppedTournamentResults => Volatile.Read(ref droppedTournamentResults);
            public Task SendCommandAsync(byte[] command, CancellationToken token) =>
                Volatile.Read(ref current).SendAsync(new BinaryRealtimeCodec().Encode(
                    new RealtimeMessage(RealtimeMessageKind.Business, command)), token);

            public IWebSocketTransport Create(int maxMessageBytes)
            {
                var transport = new Transport(new ClientWebSocketTransport(maxMessageBytes), Record);
                Volatile.Write(ref current, transport);
                return transport;
            }

            private void Record(byte[] frame)
            {
                if (frame == null) return;
                var message = new BinaryRealtimeCodec().Decode(frame);
                if (message.Kind != RealtimeMessageKind.Push) return;
                using var document = JsonDocument.Parse(message.Payload);
                var type = document.RootElement.GetProperty("type").GetString();
                var payload = document.RootElement.GetProperty("message");
                if (type == "frames")
                {
                    Interlocked.Increment(ref framePushes);
                    if (payload.TryGetProperty("players", out _) || payload.TryGetProperty("cues", out _) ||
                        payload.TryGetProperty("arrows", out _))
                        throw new InvalidOperationException("Rain Pass sent authoritative player states in a frame batch.");
                    if (payload.GetProperty("frames").GetArrayLength() != 0 &&
                        Interlocked.Exchange(ref interruptOnFrame, 0) != 0)
                        throw new IOException("Injected disconnect before the client applied the frame batch.");
                }
                if (type == "snapshot") Interlocked.Increment(ref statePushes);
                if (type == "tournamentState" && payload.GetProperty("rounds").GetArrayLength() != 0 &&
                    Interlocked.Exchange(ref interruptOnTournamentResult, 0) != 0)
                {
                    Interlocked.Increment(ref droppedTournamentResults);
                    throw new IOException("Injected disconnect after the tournament commit and before its confirmation.");
                }
                if (type == "ack" && payload.TryGetProperty("reason", out var reason) &&
                    reason.ValueKind == JsonValueKind.String)
                    Rejections.Enqueue(reason.GetString());
            }

            private sealed class Transport : IWebSocketTransport
            {
                private readonly ClientWebSocketTransport inner;
                private readonly Action<byte[]> received;
                internal readonly TaskCompletionSource<bool> Interrupted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                internal Transport(ClientWebSocketTransport inner, Action<byte[]> received)
                {
                    this.inner = inner;
                    this.received = received;
                }
                public Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken token) =>
                    inner.ConnectAsync(uri, headers, token);
                public Task SendAsync(byte[] message, CancellationToken token) => inner.SendAsync(message, token);
                public Task CloseAsync(CancellationToken token) => inner.CloseAsync(token);
                public ValueTask DisposeAsync() => inner.DisposeAsync();

                public async Task<byte[]> ReceiveAsync(CancellationToken token)
                {
                    using var receive = CancellationTokenSource.CreateLinkedTokenSource(token);
                    var read = inner.ReceiveAsync(receive.Token);
                    if (await Task.WhenAny(read, Interrupted.Task) == read)
                    {
                        var frame = await read;
                        received(frame);
                        return frame;
                    }
                    receive.Cancel();
                    try { await read; }
                    catch (Exception failure) when (failure is OperationCanceledException || failure is WebSocketException) { }
                    throw new IOException("Injected transport interruption.");
                }
            }
        }

        private sealed class ServerProcess : IAsyncDisposable
        {
            private readonly string config;
            private readonly Process process;
            private readonly ConcurrentQueue<string> logs = new();
            internal Uri Session { get; }

            private ServerProcess(string config, Process process, int port)
            {
                this.config = config;
                this.process = process;
                Session = new Uri($"ws://127.0.0.1:{port}/pvp/session");
            }

            internal static async Task<ServerProcess> StartAsync(CancellationToken token, int port = 0, string tournamentDirectory = null)
            {
                if (port == 0)
                {
                    var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    listener.Stop();
                }
                string config = Path.GetTempFileName();
                var settings = new Dictionary<string, object>
                {
                    ["pvpPort"] = port,
                    ["pvpTournamentDirectory"] = tournamentDirectory ?? config + "-tournaments"
                };
                await File.WriteAllTextAsync(config, JsonSerializer.Serialize(settings), token);
                var start = new ProcessStartInfo("dotnet")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true, UseShellExecute = false
                };
                foreach (string argument in new[] { Path.Combine(AppContext.BaseDirectory, "Test.Server.dll"),
                    "--config", config, "--run-for-seconds", "30" })
                    start.ArgumentList.Add(argument);

                var process = new Process { StartInfo = start };
                var server = new ServerProcess(config, process, port);
                process.OutputDataReceived += (_, line) => { if (line.Data != null) server.logs.Enqueue(line.Data); };
                process.ErrorDataReceived += (_, line) => { if (line.Data != null) server.logs.Enqueue(line.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                using var http = new HttpClient();
                try
                {
                    while (!process.HasExited)
                    {
                        try
                        {
                            using var response = await http.GetAsync($"http://127.0.0.1:{port}/pvp/status", token);
                            if (response.IsSuccessStatusCode) return server;
                        }
                        catch (HttpRequestException) { }
                        await Task.Delay(40, token);
                    }
                    throw new InvalidOperationException("Test.Server exited before PvP was ready.");
                }
                catch
                {
                    await server.DisposeAsync();
                    throw;
                }
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await StopAsync();
                    TestContext.Progress.WriteLine(string.Join(Environment.NewLine, logs));
                }
                finally
                {
                    process.Dispose();
                    File.Delete(config);
                }
            }

            internal async Task StopAsync()
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
