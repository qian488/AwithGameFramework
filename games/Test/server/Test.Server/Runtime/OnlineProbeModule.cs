using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Networking.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Test.Server
{
    internal sealed class OnlineProbeModule : GameModule
    {
        private const int MaxCommandBytes = 1024;
        private const int MaxHistory = 128;
        private readonly int port;
        private readonly List<RealtimePush> history = new List<RealtimePush>(MaxHistory);
        private readonly object sync = new object();
        private WebApplication web;
        private long sequence;
        private int connected;

        public OnlineProbeModule(int port) => this.port = port;

        public override string Id => "Test.online-probe";

        public override async Task StartAsync(GameContext context, CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            web = builder.Build();
            web.UseWebSockets();
            web.MapGet("/status", (Func<IResult>)(() => Results.Ok(new { ready = true })));
            web.MapGet("/recovery", (Func<long, IResult>)Recover);
            web.MapGet("/session", RunSessionAsync);
            try { await web.StartAsync(cancellationToken); }
            catch
            {
                await web.DisposeAsync();
                web = null;
                throw;
            }
        }

        public override async Task StopAsync(GameContext context, CancellationToken cancellationToken)
        {
            if (web == null) return;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try { await web.StopAsync(deadline.Token); }
            finally { await web.DisposeAsync(); web = null; }
        }

        private IResult Recover(long after)
        {
            lock (sync)
            {
                if (after < 0 || after > sequence || (history.Count != 0 && after < history[0].Sequence - 1))
                    return Results.Conflict(new { throughSequence = sequence });
                return Results.Ok(new { throughSequence = sequence, pushes = history.Where(push => push.Sequence > after).ToArray() });
            }
        }

        private async Task RunSessionAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (Interlocked.Exchange(ref connected, 1) != 0)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                return;
            }
            try
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await RealtimeServerSession.RunAsync(socket, new BinaryRealtimeCodec(), HandleCommandAsync, context.RequestAborted,
                    MaxCommandBytes + BinaryRealtimeCodec.HeaderSize);
            }
            finally { Volatile.Write(ref connected, 0); }
        }

        private Task<RealtimePush> HandleCommandAsync(byte[] payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string command = JsonSerializer.Deserialize<string>(payload);
            if (!string.Equals(command, "probe", StringComparison.Ordinal))
                throw new RealtimeProtocolException("The Test endpoint expects the probe command.");
            lock (sync)
            {
                var push = new RealtimePush(++sequence, JsonSerializer.SerializeToUtf8Bytes("probe-ack"));
                if (history.Count == MaxHistory) history.RemoveAt(0);
                history.Add(push);
                return Task.FromResult(push);
            }
        }
    }
}
