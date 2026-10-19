using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Networking
{
    public enum NetworkFailure
    {
        None,
        Timeout,
        CallerCancelled,
        SessionChanged,
        ClientDisposed,
        NetworkError,
        HttpError,
        EncodingError,
        InvalidResponse
    }

    /// <summary>Transport and business outcomes are independent. Data is delivered only for business status zero.</summary>
    public sealed class NetworkResult<T>
    {
        public NetworkFailure Failure { get; }
        public int? HttpStatusCode { get; }
        public int? BusinessStatus { get; }
        public string Message { get; }
        public T Data { get; }
        public long SessionEpoch { get; }
        public bool IsSuccess => Failure == NetworkFailure.None && BusinessStatus == 0;

        internal NetworkResult(NetworkFailure failure, long sessionEpoch, int? httpStatusCode = null,
            int? businessStatus = null, string message = null, T data = default)
        {
            Failure = failure;
            SessionEpoch = sessionEpoch;
            HttpStatusCode = httpStatusCode;
            BusinessStatus = businessStatus;
            Message = message;
            Data = data;
        }
    }

    public sealed class ProtocolEndpoint<TRequest, TResponse>
    {
        public HttpMethod Method { get; }
        public string RelativePath { get; }
        public bool HasRequestBody { get; }

        public ProtocolEndpoint(HttpMethod method, string relativePath, bool hasRequestBody = true)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            if (string.IsNullOrWhiteSpace(relativePath) || !Uri.TryCreate(relativePath, UriKind.Relative, out _) ||
                relativePath != relativePath.Trim() ||
                relativePath.StartsWith("//", StringComparison.Ordinal) || relativePath.IndexOf('\\') >= 0 || relativePath.IndexOf('#') >= 0)
                throw new ArgumentException("A relative HTTP route without a fragment is required.", nameof(relativePath));
            RelativePath = relativePath;
            HasRequestBody = hasRequestBody;
        }
    }

    public readonly struct EmptyRequest { }

    [DataContract]
    public sealed class ProtocolEnvelope<T>
    {
        [DataMember(Name = "status", IsRequired = true)] public int Status { get; set; }
        [DataMember(Name = "message", EmitDefaultValue = false)] public string Message { get; set; }
        [DataMember(Name = "data", EmitDefaultValue = false)] public T Data { get; set; }
    }

    public interface IProtocolCodec
    {
        string ContentType { get; }
        byte[] Encode<T>(T request);
        ProtocolEnvelope<T> Decode<T>(byte[] response);
    }

    /// <summary>Optional BCL JSON codec. DTOs should explicitly declare DataContract/DataMember wire names.</summary>
    public sealed class DataContractJsonProtocolCodec : IProtocolCodec
    {
        public string ContentType => "application/json; charset=utf-8";

        public byte[] Encode<T>(T request)
        {
            using (var output = new MemoryStream())
            {
                CreateSerializer(typeof(T)).WriteObject(output, request);
                return output.ToArray();
            }
        }

        public ProtocolEnvelope<T> Decode<T>(byte[] response)
        {
            if (response == null || response.Length == 0) throw new SerializationException("The response body is empty.");
            using (var input = new MemoryStream(response, writable: false))
                return (ProtocolEnvelope<T>)CreateSerializer(typeof(ProtocolEnvelope<T>)).ReadObject(input);
        }

        private static DataContractJsonSerializer CreateSerializer(Type type) =>
            new DataContractJsonSerializer(type, new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
    }

    public interface IProtocolClient
    {
        long SessionEpoch { get; }
        long BeginSession(IReadOnlyDictionary<string, string> headers = null);
        bool IsCurrentEpoch(long sessionEpoch);
        Task<NetworkResult<TResponse>> SendAsync<TRequest, TResponse>(ProtocolEndpoint<TRequest, TResponse> endpoint,
            TRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    }
}
