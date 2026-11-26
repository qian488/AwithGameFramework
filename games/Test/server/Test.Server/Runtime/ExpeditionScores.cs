using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.GameSave;
using AwithGameFrame.Foundation.DataPersistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Test.Application.Protocol;

namespace Test.Server
{
    internal sealed class ExpeditionScores
    {
        private const int Slot = 0;
        private readonly SemaphoreSlim writeGate = new SemaphoreSlim(1, 1);
        private readonly VersionedSaveService<ScoreLedger> saves;
        private ScoreLedger ledger;

        public ExpeditionScores(string scoreDirectory)
        {
            saves = new VersionedSaveService<ScoreLedger>(Path.Combine(scoreDirectory, "expedition"), new JsonSerializer(), 1);
        }

        public async Task LoadAsync(CancellationToken token)
        {
            var saved = await saves.LoadAsync(Slot, token);
            ledger = saved.Status == SaveLoadStatus.NotFound ? new ScoreLedger() : saved.Data;
            if (ledger?.Scores == null) throw new InvalidDataException("Missing expedition score ledger.");
        }

        public void MapRoutes(WebApplication web)
        {
            web.MapPost("/" + GameProtocol.SubmitExpeditionScore.RelativePath, SubmitAsync);
            web.MapPost("/" + GameProtocol.GetExpeditionLeaderboard.RelativePath, LeaderboardAsync);
        }

        private async Task SubmitAsync(HttpContext context)
        {
            try
            {
                var submission = await ContractScoresModule.ReadRequestAsync<SubmitExpeditionScoreRequest>(context);
                if (submission == null || !Guid.TryParseExact(submission.SubmissionId, "N", out var id) ||
                    !ValidKey(submission.PlayerId) || !ValidKey(submission.ContentVersion) ||
                    !ValidKey(submission.RouteId) || !ValidKey(submission.GearId) ||
                    submission.FirstTicks <= 0 || submission.SecondTicks <= 0)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                submission.SubmissionId = id.ToString("N");
                if (!await RegisterAsync(submission, context.RequestAborted))
                {
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                    return;
                }

                await ContractScoresModule.WriteProtocolAsync(context, new SubmitExpeditionScoreResponse
                {
                    SubmissionId = submission.SubmissionId,
                    Accepted = true
                });
            }
            catch (Newtonsoft.Json.JsonException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
            catch (BadHttpRequestException error) { context.Response.StatusCode = error.StatusCode; }
        }

        private async Task<bool> RegisterAsync(SubmitExpeditionScoreRequest submission, CancellationToken token)
        {
            await writeGate.WaitAsync(token);
            try
            {
                var current = Volatile.Read(ref ledger);
                var previous = current.Scores.Find(score => score.SubmissionId == submission.SubmissionId);
                if (previous != null)
                {
                    return previous.PlayerId == submission.PlayerId && previous.ContentVersion == submission.ContentVersion &&
                        previous.RouteId == submission.RouteId && previous.GearId == submission.GearId &&
                        previous.FirstTicks == submission.FirstTicks && previous.SecondTicks == submission.SecondTicks;
                }

                var next = new ScoreLedger
                {
                    Scores = new List<SubmitExpeditionScoreRequest>(current.Scores) { submission }
                };
                await saves.SaveAsync(Slot, next, "Expedition score ledger", token);
                Volatile.Write(ref ledger, next);
                return true;
            }
            finally { writeGate.Release(); }
        }

        private async Task LeaderboardAsync(HttpContext context)
        {
            try
            {
                var request = await ContractScoresModule.ReadRequestAsync<GetExpeditionLeaderboardRequest>(context);
                if (request == null || !ValidKey(request.PlayerId) || !ValidKey(request.ContentVersion) ||
                    !ValidKey(request.RouteId) || !ValidKey(request.GearId) || request.Limit < 1 || request.Limit > 50)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var ranked = Volatile.Read(ref ledger).Scores
                    .Where(score => score.ContentVersion == request.ContentVersion &&
                        score.RouteId == request.RouteId && score.GearId == request.GearId)
                    .GroupBy(score => score.PlayerId, StringComparer.Ordinal)
                    .Select(player => player.OrderBy(TotalTicks).ThenBy(score => score.FirstTicks).First())
                    .OrderBy(TotalTicks)
                    .ThenBy(score => score.PlayerId, StringComparer.Ordinal)
                    .ToArray();
                int ownIndex = Array.FindIndex(ranked, score => score.PlayerId == request.PlayerId);
                await ContractScoresModule.WriteProtocolAsync(context, new GetExpeditionLeaderboardResponse
                {
                    Entries = ranked.Take(request.Limit).Select((score, index) => new ExpeditionLeaderboardEntry
                    {
                        PlayerId = score.PlayerId,
                        FirstTicks = score.FirstTicks,
                        SecondTicks = score.SecondTicks,
                        TotalTicks = TotalTicks(score),
                        Rank = index + 1,
                        IsMine = index == ownIndex
                    }).ToArray(),
                    OwnBestTicks = ownIndex < 0 ? 0 : TotalTicks(ranked[ownIndex]),
                    OwnRank = ownIndex + 1
                });
            }
            catch (Newtonsoft.Json.JsonException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
            catch (BadHttpRequestException error) { context.Response.StatusCode = error.StatusCode; }
        }

        private static long TotalTicks(SubmitExpeditionScoreRequest score) => (long)score.FirstTicks + score.SecondTicks;

        private static bool ValidKey(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;

        private sealed class ScoreLedger
        {
            public List<SubmitExpeditionScoreRequest> Scores { get; set; } = new List<SubmitExpeditionScoreRequest>();
        }
    }
}
