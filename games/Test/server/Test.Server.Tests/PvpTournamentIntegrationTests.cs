using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using NUnit.Framework;
using Test.Application.Combat.Pvp;
using Test.Application.Protocol;
using Test.Domain.Combat.Pvp;
using SaveSerializer = AwithGameFrame.Foundation.DataPersistence.JsonSerializer;

namespace Test.Server.Tests
{
    public sealed partial class PvpServerIntegrationTests
    {
        private const string TournamentRoom = "tournament-check";

        [TestCase(new[] { 0, 0 }, true, 0)]
        [TestCase(new[] { -1, 1 }, true, 1)]
        [TestCase(new[] { 0, 1 }, false, -1)]
        [TestCase(new[] { -1, -1, -1 }, true, -1)]
        public void TournamentScoresCompletedRounds(int[] winners, bool finished, int winner)
        {
            var score = PvpTournamentRules.Score(winners);
            Assert.That(score.Finished, Is.EqualTo(finished));
            Assert.That(score.Winner, Is.EqualTo(winner));
            Assert.That(score.First, Is.EqualTo(winners.Count(slot => slot == 0)));
            Assert.That(score.Second, Is.EqualTo(winners.Count(slot => slot == 1)));
        }

        [Test]
        public async Task TournamentRecoversCommittedScoreAndRestartsTheInterruptedRound()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var token = deadline.Token;
            using var save = new TournamentTestDirectory();
            await using var server = await ServerProcess.StartAsync(token, tournamentDirectory: save.Path);
            var transport = new InterruptibleTransportFactory();
            var otherTransport = new InterruptibleTransportFactory();
            await using var first = new TournamentPlayer(server.Session, transport);
            await using var second = new TournamentPlayer(server.Session, otherTransport);
            var alpha = await first.JoinAsync("alpha", token);
            var beta = await second.JoinAsync("beta", token);
            await ReadyBothAsync(first, second, token);
            await Until(() => first.View.Current?.ServerTick >= 10, token);

            transport.InterruptOnNextFrame();
            await Until(() => first.Session.SessionEpoch > alpha.SessionEpoch && first.Session.ReplayedFrames > 0 &&
                first.View.Current.SessionEpoch == first.Session.SessionEpoch, token);
            string rainPassId = first.Session.MatchId;
            transport.InterruptOnTournamentResult();
            for (int hit = 0; hit < 12 && first.View.Current.RoomState != "Finished" && second.State.Rounds.Length == 0; hit++)
            {
                var buttons = hit == 0 ? PvpButtons.Attack | PvpButtons.Lock : PvpButtons.Attack;
                long tick = await SendAndConfirm(first.Session, first.View, buttons, 0, token);
                await Until(() => first.View.Current.ServerTick >= tick + 36 ||
                    first.View.Current.RoomState == "Finished" || second.State.Rounds.Length == 1, token);
            }
            Assert.That(first.View.Current.RoomState, Is.EqualTo("Finished"), "Scripted Rain Pass attacks did not finish.");
            await Until(() => first.State.Rounds.Length == 1 && second.State.Rounds.Length == 1 &&
                first.State.ConnectedMask == 3 && second.State.ConnectedMask == 3, token);
            Assert.That(transport.DroppedTournamentResults, Is.EqualTo(1));
            Assert.That(first.State.FirstScore, Is.EqualTo(1));
            Assert.That(first.State.RoundNumber, Is.EqualTo(2));

            long before = second.Session.SessionEpoch;
            otherTransport.Interrupt();
            await Until(() => first.State.ConnectedMask == 1, token);
            await Until(() => second.Session.SessionEpoch > before && second.State.ConnectedMask == 3, token);
            Assert.That(second.Session.PlayerSlot, Is.EqualTo(beta.PlayerSlot));
            Assert.That(second.State.Rounds.Single().MatchId, Is.EqualTo(rainPassId));

            await ReadyBothAsync(first, second, token);
            string lostMatch = first.Session.MatchId;
            await Until(() => first.View.Current?.MatchId == lostMatch && first.View.Current.ServerTick > 5, token);
            await transport.SendCommandAsync(JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "input", protocolVersion = GameProtocol.Version, roomId = TournamentRoom, matchId = rainPassId,
                playerId = alpha.PlayerId, sessionEpoch = first.Session.SessionEpoch,
                inputSequence = 9999, simulationTick = first.View.Current.ServerTick + 7
            }), token);
            await Until(() => transport.Rejections.Contains("SessionMismatch"), token);
            long secondEpoch = second.Session.SessionEpoch;
            otherTransport.Interrupt();
            await Until(() => second.Session.SessionEpoch > secondEpoch && second.View.Current?.MatchId == lostMatch &&
                second.View.Current.SessionEpoch == second.Session.SessionEpoch, token);
            Assert.That(second.Session.SimulationTick, Is.Zero);

            await server.StopAsync();
            await using var restarted = await ServerProcess.StartAsync(token, server.Session.Port, save.Path);
            await Until(() => first.State.Phase == PvpTournament.AwaitingReady && second.State.Phase == PvpTournament.AwaitingReady &&
                first.State.ConnectedMask == 3 && second.State.ConnectedMask == 3, token);
            Assert.That(first.State.TournamentId, Is.EqualTo(second.State.TournamentId));
            Assert.That(first.State.Rounds.Single().MatchId, Is.EqualTo(rainPassId));
            Assert.That(first.State.RoundNumber, Is.EqualTo(2));
            await ReadyBothAsync(first, second, token);
            string newMatch = first.Session.MatchId;
            Assert.That(newMatch, Is.Not.EqualTo(lostMatch));
            await Until(() => first.View.Current?.MatchId == newMatch && second.View.Current?.MatchId == newMatch &&
                first.View.Current.PeerConnected && second.View.Current.PeerConnected, token);
            await SendAndConfirm(first.Session, first.View, PvpButtons.EquipSword | PvpButtons.AttackPressed, 0, token);
            await SendAndConfirm(first.Session, first.View, PvpButtons.EquipBow | PvpButtons.AttackPressed, 1000, token);
            await SendAndConfirm(first.Session, first.View, PvpButtons.Finish, 0, token);
            await SendAndConfirm(second.Session, second.View, PvpButtons.Finish, 0, token);
            await Until(() => first.State.Phase == PvpTournament.Finished && second.State.Phase == PvpTournament.Finished, token);
            Assert.That(first.State.FirstScore, Is.EqualTo(2));
            Assert.That(second.State.FirstScore, Is.EqualTo(2));
            Assert.That(first.State.Rounds[1].FirstBackpack, Does.Contain("Fish:1:0"));
            Assert.That(second.State.Rounds[1].StateHash, Is.EqualTo(first.State.Rounds[1].StateHash));

            var store = new VersionedSaveService<SavedTournament>(System.IO.Path.Combine(save.Path, TournamentRoom),
                new SaveSerializer(), 1);
            var saved = await store.LoadAsync(0, token);
            saved.Data.Tournament.Rounds[0].VerifyRainPass();

            await restarted.StopAsync();
            await using var archive = await ServerProcess.StartAsync(token, server.Session.Port, save.Path);
            await using var reader = new TournamentPlayer(archive.Session);
            var rejoined = await reader.JoinAsync("alpha", token, alpha.ResumeToken);
            await Until(() => reader.State?.Phase == PvpTournament.Finished, token);
            Assert.That(rejoined.PlayerSlot, Is.EqualTo(alpha.PlayerSlot));
            Assert.That(reader.State.Rounds.Select(result => result.MatchId), Is.EqualTo(new[] { rainPassId, newMatch }));
            Assert.That(reader.State.Winner, Is.Zero);
        }

        [TestCase("PlayerLeft")]
        [TestCase("PlayerDisconnected")]
        [TestCase("BothPlayersDisconnected")]
        public async Task TournamentEndsWhenSeatsLeaveOrTimeOut(string reason)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(22));
            var token = deadline.Token;
            using var save = new TournamentTestDirectory();
            await using var server = await ServerProcess.StartAsync(token, tournamentDirectory: save.Path);
            await using var first = new TournamentPlayer(server.Session);
            await using var second = new TournamentPlayer(server.Session);
            var alpha = await first.JoinAsync("alpha", token);
            await second.JoinAsync("beta", token);
            await Until(() => first.State?.ConnectedMask == 3 && second.State?.ConnectedMask == 3, token);
            if (reason == "PlayerLeft") await second.Session.LeaveAsync();
            else await second.DisposeAsync();
            if (reason == "BothPlayersDisconnected")
            {
                await first.DisposeAsync();
                var store = new VersionedSaveService<SavedTournament>(System.IO.Path.Combine(save.Path, TournamentRoom),
                    new SaveSerializer(), 1);
                while ((await store.LoadAsync(0, token)).Data.Tournament.EndReason == null)
                    await Task.Delay(100, token);
                await using var reader = new TournamentPlayer(server.Session);
                await reader.JoinAsync("alpha", token, alpha.ResumeToken);
                await Until(() => reader.State?.Phase == PvpTournament.Finished, token);
                Assert.That(reader.State.EndReason, Is.EqualTo(reason));
                Assert.That(reader.State.Winner, Is.EqualTo(-1));
                Assert.That(reader.State.Rounds, Is.Empty);
            }
            else
            {
                await Until(() => first.State.Phase == PvpTournament.Finished, token);
                Assert.That(first.State.EndReason, Is.EqualTo(reason));
                Assert.That(first.State.Winner, Is.Zero);
                Assert.That(first.State.Rounds, Is.Empty);
            }
        }

        private static async Task ReadyBothAsync(TournamentPlayer first, TournamentPlayer second, CancellationToken token)
        {
            await Until(() => first.State?.Phase == PvpTournament.AwaitingReady && second.State?.Phase == PvpTournament.AwaitingReady &&
                first.State.ConnectedMask == 3 && second.State.ConnectedMask == 3, token);
            await first.Session.ReadyTournamentAsync(token);
            await second.Session.ReadyTournamentAsync(token);
            await Until(() => first.State.Phase == PvpTournament.Running && second.State.Phase == PvpTournament.Running, token);
        }

        private sealed class TournamentPlayer : IAsyncDisposable
        {
            private PvpTournamentState state;
            public PvpClientSession Session { get; }
            public View View { get; }
            public PvpTournamentState State => Volatile.Read(ref state);

            public TournamentPlayer(Uri uri, InterruptibleTransportFactory transport = null)
            {
                Session = new PvpClientSession(uri, transport);
                View = new View(Session);
                Session.TournamentChanged += update => Volatile.Write(ref state, update);
            }

            public async Task<PvpJoinResponse> JoinAsync(string id, CancellationToken token, string resumeToken = null)
            {
                var joined = await Session.JoinAsync(TournamentRoom, id, PvpTournamentRules.Tournament, token, resumeToken);
                Assert.That(joined.Accepted, Is.True, joined.Reason);
                return joined;
            }

            public ValueTask DisposeAsync() => Session.DisposeAsync();
        }

        private sealed class SavedTournament
        {
            public PvpTournament Tournament { get; set; }
        }

        private sealed class TournamentTestDirectory : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TestTournament-" + Guid.NewGuid().ToString("N"));
            public void Dispose()
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
        }
    }
}
