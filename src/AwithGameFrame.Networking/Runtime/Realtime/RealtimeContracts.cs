using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Networking.Realtime
{
    public interface IWebSocketTransport : IAsyncDisposable
    {
        Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);
        Task SendAsync(byte[] message, CancellationToken cancellationToken);
        /// <summary>Returns one complete binary message, or null after the peer closes.</summary>
        Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);
        Task CloseAsync(CancellationToken cancellationToken);
    }

    public interface IWebSocketTransportFactory
    {
        /// <summary>Each call must return a new, independently owned transport.</summary>
        IWebSocketTransport Create(int maxMessageBytes);
    }

    public enum RealtimeMessageKind : byte { Business = 1, Push = 2, Ping = 3, Pong = 4 }

    public sealed class RealtimeMessage
    {
        public RealtimeMessageKind Kind { get; }
        public long Sequence { get; }
        public long HeartbeatId { get; }
        public byte[] Payload { get; }

        public RealtimeMessage(RealtimeMessageKind kind, byte[] payload = null, long sequence = 0, long heartbeatId = 0)
        {
            if (kind < RealtimeMessageKind.Business || kind > RealtimeMessageKind.Pong)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind == RealtimeMessageKind.Push ? sequence <= 0 : sequence != 0)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            bool heartbeat = kind == RealtimeMessageKind.Ping || kind == RealtimeMessageKind.Pong;
            if (heartbeat ? heartbeatId <= 0 : heartbeatId != 0)
                throw new ArgumentOutOfRangeException(nameof(heartbeatId));
            if (heartbeat && payload != null && payload.Length != 0)
                throw new ArgumentException("Heartbeat messages cannot contain a payload.", nameof(payload));
            Kind = kind;
            Sequence = sequence;
            HeartbeatId = heartbeatId;
            Payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone();
        }
    }

    public interface IRealtimeCodec
    {
        byte[] Encode(RealtimeMessage message);
        RealtimeMessage Decode(byte[] message);
    }

    public sealed class RealtimePush
    {
        public long Sequence { get; }
        public byte[] Payload { get; }
        public RealtimePush(long sequence, byte[] payload)
        {
            if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            Payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone();
        }
    }

    /// <summary>An authoritative snapshot of every push in (afterSequence, ThroughSequence].</summary>
    public sealed class PushRecoveryBatch
    {
        public long ThroughSequence { get; }
        public IReadOnlyList<RealtimePush> Pushes { get; }
        public PushRecoveryBatch(long throughSequence, IReadOnlyList<RealtimePush> pushes)
        {
            if (throughSequence < 0) throw new ArgumentOutOfRangeException(nameof(throughSequence));
            ThroughSequence = throughSequence;
            if (pushes == null) throw new ArgumentNullException(nameof(pushes));
            var copy = new RealtimePush[pushes.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = pushes[i] ?? throw new ArgumentException("A recovery push cannot be null.", nameof(pushes));
            Pushes = Array.AsReadOnly(copy);
        }
    }

    public interface IPushRecovery
    {
        /// <summary>Fetch a complete, ordered snapshot after the last successfully applied sequence.</summary>
        Task<PushRecoveryBatch> RecoverAsync(long afterSequence, CancellationToken cancellationToken);
    }

    public sealed class RealtimeProtocolException : Exception
    {
        public RealtimeProtocolException(string message, Exception innerException = null) : base(message, innerException) { }
    }

    public sealed class PushRecoveryException : Exception
    {
        public PushRecoveryException(string message, Exception innerException = null) : base(message, innerException) { }
    }

    public sealed class PushApplicationException : Exception
    {
        public long Sequence { get; }
        public PushApplicationException(long sequence, Exception innerException)
            : base("The push handler failed at sequence " + sequence + ". The cursor was not committed.", innerException)
        { Sequence = sequence; }
    }

    public sealed class RealtimeDisconnectedException : IOException
    {
        public RealtimeDisconnectedException(string message, Exception innerException = null) : base(message, innerException) { }
    }

    public sealed class RealtimeOptions
    {
        public int MaxMessageBytes { get; }
        public int MaxReconnectAttempts { get; }
        public TimeSpan InitialReconnectDelay { get; }
        public TimeSpan MaxReconnectDelay { get; }
        public TimeSpan ConnectTimeout { get; }
        public TimeSpan HeartbeatInterval { get; }
        public TimeSpan HeartbeatTimeout { get; }
        public TimeSpan CloseTimeout { get; }
        public int MaxPendingPushes { get; }

        public RealtimeOptions(int maxMessageBytes = 1024 * 1024, int maxReconnectAttempts = 3,
            TimeSpan? initialReconnectDelay = null, TimeSpan? maxReconnectDelay = null,
            TimeSpan? connectTimeout = null, TimeSpan? heartbeatInterval = null,
            TimeSpan? heartbeatTimeout = null, TimeSpan? closeTimeout = null, int maxPendingPushes = 256)
        {
            if (maxMessageBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
            if (maxReconnectAttempts < 0) throw new ArgumentOutOfRangeException(nameof(maxReconnectAttempts));
            if (maxPendingPushes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPendingPushes));
            MaxMessageBytes = maxMessageBytes;
            MaxReconnectAttempts = maxReconnectAttempts;
            MaxPendingPushes = maxPendingPushes;
            InitialReconnectDelay = Validate(initialReconnectDelay ?? TimeSpan.FromMilliseconds(250), nameof(initialReconnectDelay), true);
            MaxReconnectDelay = Validate(maxReconnectDelay ?? TimeSpan.FromSeconds(10), nameof(maxReconnectDelay), true);
            if (MaxReconnectDelay < InitialReconnectDelay) throw new ArgumentOutOfRangeException(nameof(maxReconnectDelay));
            ConnectTimeout = Validate(connectTimeout ?? TimeSpan.FromSeconds(15), nameof(connectTimeout));
            HeartbeatInterval = Validate(heartbeatInterval ?? TimeSpan.FromSeconds(15), nameof(heartbeatInterval));
            HeartbeatTimeout = Validate(heartbeatTimeout ?? TimeSpan.FromSeconds(10), nameof(heartbeatTimeout));
            CloseTimeout = Validate(closeTimeout ?? TimeSpan.FromSeconds(1), nameof(closeTimeout));
        }

        public TimeSpan GetReconnectDelay(int retryNumber)
        {
            if (retryNumber <= 0) throw new ArgumentOutOfRangeException(nameof(retryNumber));
            double multiplier = Math.Pow(2, Math.Min(retryNumber - 1, 31));
            return TimeSpan.FromTicks((long)Math.Min(MaxReconnectDelay.Ticks, InitialReconnectDelay.Ticks * multiplier));
        }

        private static TimeSpan Validate(TimeSpan value, string name, bool allowZero = false)
        {
            if (value < TimeSpan.Zero || (!allowZero && value == TimeSpan.Zero) || value.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }
    }
}
