using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;
using AwithGameFrame.Networking;
using AwithGameFrame.Networking.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Test.Application.Combat;
using Test.Application.Protocol;

namespace Test.Server
{
    internal sealed class ContractScoresModule : GameModule
    {
        private static readonly JsonSerializer Serializer = new JsonSerializer();
        private static readonly Regex Slug = new Regex(@"\A[a-z0-9][a-z0-9-]{0,47}\z", RegexOptions.CultureInvariant);
        private const int Slot = 0;
        private readonly SemaphoreSlim writeGate = new SemaphoreSlim(1, 1);
        private readonly ServerOptions options;
        private readonly VersionedSaveService<ScoreLedger> saves;
        private ScoreLedger ledger;
        private WebApplication web;
        private int confirmationDropped;

        public ContractScoresModule(ServerOptions options)
        {
            this.options = options;
            saves = new VersionedSaveService<ScoreLedger>(options.ScoreDirectory, Serializer, 1);
        }

        public override string Id => "Test.contract-scores";

        public override async Task StartAsync(GameContext context, CancellationToken token)
        {
            var saved = await saves.LoadAsync(Slot, token);
            ledger = saved.Status == SaveLoadStatus.NotFound ? new ScoreLedger() :
                saved.Data ?? throw new InvalidDataException("Missing score ledger.");
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{options.ScorePort}");
            web = builder.Build();
            web.UseWebSockets();
            web.MapGet("/contract-scores/recovery", RecoverAsync);
            web.MapGet("/contract-scores/session", RunSessionAsync);
            web.MapPost("/" + GameProtocol.SubmitContractScore.RelativePath, SubmitHttpAsync);
            web.MapPost("/" + GameProtocol.GetContractLeaderboard.RelativePath, LeaderboardAsync);
            try { await web.StartAsync(token); }
            catch { await web.DisposeAsync(); web = null; throw; }
        }

        public override async Task StopAsync(GameContext context, CancellationToken token)
        {
            if (web == null) return;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try { await web.StopAsync(deadline.Token); }
            finally { await web.DisposeAsync(); web = null; }
        }

        private async Task RecoverAsync(HttpContext context)
        {
            if (Volatile.Read(ref confirmationDropped) != 0)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            var current = Volatile.Read(ref ledger);
            if (!long.TryParse(context.Request.Query["after"], out long after) || after < 0 || after > current.Scores.Count)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                return;
            }
            await WriteAsync(context, new ContractScoreRecovery
            {
                ThroughSequence = current.Scores.Count,
                Receipts = current.Scores.Skip((int)after).ToArray()
            });
        }

        private async Task RunSessionAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            try
            {
                await RealtimeServerSession.RunAsync(socket, new BinaryRealtimeCodec(), async (payload, token) =>
                {
                    var result = await SubmitAsync(await Serializer.DeserializeAsync<SubmitContractScoreRequest>(payload), token);
                    if (options.DropFirstScoreConfirmation && result.Created && Interlocked.Exchange(ref confirmationDropped, 1) == 0)
                    {
                        // Exercise the gap between the committed transaction and the outgoing confirmation.
                        socket.Abort();
                        return null;
                    }
                    return new RealtimePush(result.Receipt.Sequence, await Serializer.SerializeAsync(result.Receipt));
                }, context.RequestAborted, 4096);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (WebSocketException) when (socket.State == WebSocketState.Aborted) { }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException || exception is Newtonsoft.Json.JsonException)
            {
                Console.Error.WriteLine("Contract score command rejected: " + exception.Message);
                if (socket.State == WebSocketState.Open)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Invalid score", context.RequestAborted);
            }
        }

        private async Task SubmitHttpAsync(HttpContext context)
        {
            try
            {
                var result = await SubmitAsync(await ReadRequestAsync<SubmitContractScoreRequest>(context), context.RequestAborted);
                await WriteAsync(context, result.Receipt);
            }
            catch (InvalidDataException) { context.Response.StatusCode = StatusCodes.Status409Conflict; }
            catch (Exception exception) when (exception is ArgumentException || exception is Newtonsoft.Json.JsonException)
            { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
            catch (BadHttpRequestException error) { context.Response.StatusCode = error.StatusCode; }
        }

        private async Task LeaderboardAsync(HttpContext context)
        {
            try
            {
                var request = await ReadRequestAsync<GetContractLeaderboardRequest>(context);
                if (request == null || !ValidSlug(request.PlayerId) || !ValidSlug(request.ContractId) ||
                    !ValidSlug(request.ContentVersion) || request.Limit < 1 || request.Limit > 50)
                    throw new ArgumentException("Invalid leaderboard request.");
                var matching = Volatile.Read(ref ledger).Scores.Where(score => score.ContractId == request.ContractId &&
                    score.ContentVersion == request.ContentVersion)
                    .GroupBy(score => score.PlayerId ?? "local-player", StringComparer.Ordinal)
                    .Select(group => new { PlayerId = group.Key, ElapsedTicks = group.Min(score => score.ElapsedTicks) })
                    .OrderBy(score => score.ElapsedTicks).ThenBy(score => score.PlayerId, StringComparer.Ordinal)
                    .ToArray();
                var entries = matching.Take(request.Limit).Select((score, index) => new LeaderboardEntry
                {
                    PlayerId = score.PlayerId, ElapsedTicks = score.ElapsedTicks, Rank = index + 1,
                    IsMine = score.PlayerId == request.PlayerId
                }).ToArray();
                int ownIndex = Array.FindIndex(matching, score => score.PlayerId == request.PlayerId);
                await WriteProtocolAsync(context, new GetContractLeaderboardResponse
                {
                    Entries = entries,
                    OwnBestTicks = ownIndex < 0 ? 0 : matching[ownIndex].ElapsedTicks,
                    OwnRank = ownIndex < 0 ? 0 : ownIndex + 1
                });
            }
            catch (ArgumentException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
            catch (Newtonsoft.Json.JsonException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
            catch (BadHttpRequestException error) { context.Response.StatusCode = error.StatusCode; }
        }

        private async Task<(SubmitContractScoreResponse Receipt, bool Created)> SubmitAsync(SubmitContractScoreRequest submission, CancellationToken token)
        {
            if (submission == null || !Guid.TryParseExact(submission.SubmissionId, "N", out _) ||
                (submission.PlayerId != null && !ValidSlug(submission.PlayerId)) || !ValidSlug(submission.ContractId) ||
                !ValidSlug(submission.ContentVersion) || submission.ElapsedTicks <= 0)
                throw new ArgumentException("Invalid contract score submission.");
            await writeGate.WaitAsync(token);
            try
            {
                var current = Volatile.Read(ref ledger);
                var previous = current.Scores.Find(score => score.SubmissionId == submission.SubmissionId);
                if (previous != null)
                {
                    if ((previous.PlayerId ?? "local-player") != (submission.PlayerId ?? "local-player") ||
                        previous.ContractId != submission.ContractId || previous.ContentVersion != submission.ContentVersion ||
                        previous.ElapsedTicks != submission.ElapsedTicks)
                        throw new InvalidDataException("The submission ID already identifies a different score.");
                    return (previous, false);
                }
                var receipt = new SubmitContractScoreResponse
                {
                    SubmissionId = submission.SubmissionId, Sequence = current.Scores.Count + 1L,
                    PlayerId = submission.PlayerId ?? "local-player", ContractId = submission.ContractId,
                    ContentVersion = submission.ContentVersion, ElapsedTicks = submission.ElapsedTicks
                };
                var next = new ScoreLedger { Scores = new List<SubmitContractScoreResponse>(current.Scores) { receipt } };
                await saves.SaveAsync(Slot, next, "Contract score ledger", token);
                Volatile.Write(ref ledger, next);
                return (receipt, true);
            }
            finally { writeGate.Release(); }
        }

        private static bool ValidSlug(string value) => value != null && Slug.IsMatch(value);

        private static async Task<T> ReadRequestAsync<T>(HttpContext context)
        {
            using var body = new MemoryStream();
            var buffer = new byte[1024];
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer.AsMemory(), context.RequestAborted)) != 0)
            {
                if (body.Length + read > 4096) throw new BadHttpRequestException("Request too large.", StatusCodes.Status413PayloadTooLarge);
                body.Write(buffer, 0, read);
            }
            return await Serializer.DeserializeAsync<T>(body.ToArray());
        }

        private static async Task WriteAsync<T>(HttpContext context, T value)
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(await Serializer.SerializeAsync(value), context.RequestAborted);
        }

        private static async Task WriteProtocolAsync<T>(HttpContext context, T value)
        {
            await WriteAsync(context, new ProtocolEnvelope<T> { Status = 0, Data = value });
        }

        private sealed class ScoreLedger
        {
            public List<SubmitContractScoreResponse> Scores { get; set; } = new List<SubmitContractScoreResponse>();
        }
    }
}
