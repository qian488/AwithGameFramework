using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AwithGameFrame.Core.Tests")]

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>File replacement and decoding shared by the file storage providers.</summary>
    internal static class FileStorageIO
    {
        // A bounded set of locks also coordinates separate providers using the same path.
        private static readonly object[] FileLocks = CreateFileLocks();
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static void Write(string path, Action<Stream> write)
        {
            var fullPath = Path.GetFullPath(path);
            lock (GetFileLock(fullPath))
            {
                var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath), ".awith-save-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        write(stream);
                        stream.Flush(flushToDisk: true);
                    }

                    // Keep the existing file intact when the filesystem cannot replace it.
                    // Deleting it before moving the new file would lose that guarantee.
                    if (File.Exists(fullPath))
                        File.Replace(temporaryPath, fullPath, null);
                    else
                        File.Move(temporaryPath, fullPath);
                }
                finally
                {
                    try { File.Delete(temporaryPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        internal static void WriteAllBytes(string path, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Write(path, stream => stream.Write(data, 0, data.Length));
        }

        internal static void WriteAllText(string path, string text)
        {
            WriteAllBytes(path, StrictUtf8.GetBytes(text));
        }

        internal static byte[] ReadAllBytes(string path)
        {
            var fullPath = Path.GetFullPath(path);
            // Windows replacement can briefly prevent new read handles from opening.
            // Coordinate snapshots with our writers as well as allowing file deletion.
            lock (GetFileLock(fullPath))
            {
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
        }

        internal static string ReadAllText(string path)
        {
            var bytes = ReadAllBytes(path);
            // Preserve File.ReadAllText's BOM support while keeping invalid text an error.
            // StreamReader's automatic detection would select encodings with replacement fallback.
            if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0)
                return new UTF32Encoding(false, true, true).GetString(bytes, 4, bytes.Length - 4);
            if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff)
                return new UTF32Encoding(true, true, true).GetString(bytes, 4, bytes.Length - 4);
            if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
                return StrictUtf8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
                return new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
                return new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
            return DecodeJson(bytes);
        }

        internal static string DecodeJson(byte[] bytes)
        {
            return StrictUtf8.GetString(bytes);
        }

        internal static T DeserializeJson<T>(string json, SerializationConfig config = null)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The stored JSON is empty.");
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json,
                config == null ? PortableJson.CreateSettings() : PortableJson.CreateSettings(config));
        }

        internal static byte[] Compress(byte[] bytes)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
                    gzip.Write(bytes, 0, bytes.Length);
                return output.ToArray();
            }
        }

        internal static byte[] Decompress(byte[] bytes)
        {
            // Older compression helpers could fall back to storing uncompressed data.
            // Preserve those files; the JSON decoder still validates their content.
            if (bytes.Length < 2 || bytes[0] != 0x1f || bytes[1] != 0x8b) return bytes;
            if (bytes.Length < 18) throw new InvalidDataException("The GZip file is truncated.");

            byte[] result;
            using (var input = new MemoryStream(bytes, writable: false))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                result = output.ToArray();
            }

            // The existing file format contains a single GZip member. Some runtimes
            // accept a missing footer, so verify its checksum and length explicitly.
            using (var footer = new BinaryReader(new MemoryStream(bytes, bytes.Length - 8, 8, writable: false)))
            {
                if (footer.ReadUInt32() != ComputeCrc32(result) || footer.ReadUInt32() != (uint)result.Length)
                    throw new InvalidDataException("The GZip checksum or content length is invalid.");
            }
            return result;
        }

        internal static bool IsCorruptData(Exception exception)
        {
            return exception is InvalidDataException || exception is EndOfStreamException ||
                exception is Newtonsoft.Json.JsonException || exception is DecoderFallbackException ||
                exception is FormatException;
        }

        private static uint ComputeCrc32(byte[] bytes)
        {
            uint crc = uint.MaxValue;
            foreach (var value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
            }
            return ~crc;
        }

        private static object GetFileLock(string fullPath)
        {
            var hash = (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(fullPath);
            return FileLocks[hash % FileLocks.Length];
        }

        private static object[] CreateFileLocks()
        {
            var locks = new object[64];
            for (int i = 0; i < locks.Length; i++) locks[i] = new object();
            return locks;
        }
    }
}
