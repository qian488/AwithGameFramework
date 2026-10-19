using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AwithGameFrame.Core.Resources
{
    /// <summary>Reads raw files resolved by a pinned content snapshot; it does not interpret engine asset formats.</summary>
    public sealed class FileResourceLoader : IResourceLoader
    {
        private readonly Func<string, string> resolvePath;
        private readonly long maxFileBytes;
        public FileResourceLoader(Func<string, string> resolvePath, long maxFileBytes = 64 * 1024 * 1024)
        {
            this.resolvePath = resolvePath ?? throw new ArgumentNullException(nameof(resolvePath));
            if (maxFileBytes < 0 || maxFileBytes > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            this.maxFileBytes = maxFileBytes;
        }
        public async Task<LoadedResource> LoadAsync(string key, Type resourceType, CancellationToken cancellationToken)
        {
            if (resourceType != typeof(byte[])) throw new ArgumentException("Raw file resources use byte[].", nameof(resourceType));
            cancellationToken.ThrowIfCancellationRequested();
            using var input = new FileStream(resolvePath(key), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            if (input.Length > maxFileBytes) throw new InvalidDataException("File exceeds the resource loader limit.");
            var bytes = new byte[(int)input.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = await input.ReadAsync(bytes, offset, bytes.Length - offset, cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("File changed while loading.");
                offset += read;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new LoadedResource(bytes, () => Task.CompletedTask, bytes.LongLength);
        }
    }
}
