using System;
using System.IO;
using System.Threading.Tasks;
using AwithGameFrame.Networking;
using Test.Application.Protocol;

namespace Test.Application.Expedition
{
    public sealed class ExpeditionOnline : IDisposable
    {
        private readonly HttpClientTransport transport = new HttpClientTransport(64 * 1024);
        private readonly ProtocolClient client;

        public ExpeditionOnline(Uri address)
        {
            client = new ProtocolClient(address, transport, new DataContractJsonProtocolCodec(), TimeSpan.FromSeconds(4));
        }

        public async Task SubmitAsync(string player, ExpeditionResult result)
        {
            var run = result.Checkpoint;
            var response = await client.SendAsync(GameProtocol.SubmitExpeditionScore, new SubmitExpeditionScoreRequest
            {
                SubmissionId = run.Id, PlayerId = player, ContentVersion = run.Version, RouteId = run.Route,
                GearId = run.Gear, FirstTicks = run.FirstTicks, SecondTicks = result.SecondTicks
            });
            if (!response.IsSuccess || response.Data?.Accepted != true || response.Data.SubmissionId != run.Id)
                throw new IOException(response.Message ?? "The expedition score was not acknowledged.");
        }

        public async Task<GetExpeditionLeaderboardResponse> ReadAsync(string player, ExpeditionCheckpoint run)
        {
            var response = await client.SendAsync(GameProtocol.GetExpeditionLeaderboard, new GetExpeditionLeaderboardRequest
            {
                PlayerId = player, ContentVersion = run.Version, RouteId = run.Route, GearId = run.Gear, Limit = 10
            });
            if (!response.IsSuccess || response.Data == null) throw new IOException(response.Message ?? "Leaderboard unavailable.");
            return response.Data;
        }

        public void Dispose()
        {
            client.Dispose();
            transport.Dispose();
        }
    }
}
