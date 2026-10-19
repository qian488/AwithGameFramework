using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace AwithGameFrame.Networking.Content
{
    [DataContract]
    public sealed class ContentFile
    {
        public ContentFile(string key, string path, long size, string sha256)
        {
            Key = key; Path = path; Size = size; Sha256 = sha256;
        }
        [DataMember(Name = "key", IsRequired = true, Order = 0)] public string Key { get; private set; }
        [DataMember(Name = "path", IsRequired = true, Order = 1)] public string Path { get; private set; }
        [DataMember(Name = "size", IsRequired = true, Order = 2)] public long Size { get; private set; }
        [DataMember(Name = "sha256", IsRequired = true, Order = 3)] public string Sha256 { get; private set; }
    }

    /// <summary>Immutable, validated content contract. Hashes detect corruption; authenticity belongs to the publishing channel.</summary>
    public sealed class ContentManifest
    {
        private readonly ContentFile[] files;
        public ContentManifest(string version, IEnumerable<ContentFile> files)
        {
            ValidateText(version, nameof(version));
            if (files == null) throw new ArgumentNullException(nameof(files));
            Version = version;
            this.files = files.OrderBy(x => x?.Path, StringComparer.Ordinal).ToArray();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var file in this.files)
            {
                if (file == null) throw new ArgumentException("Null manifest file.", nameof(files));
                ValidateText(file.Key, "key");
                ValidatePath(file.Path);
                if (!keys.Add(file.Key) || !paths.Add(file.Path)) throw new ArgumentException("Duplicate content key or path.");
                if (file.Size < 0) throw new ArgumentOutOfRangeException(nameof(files), "Negative file size.");
                if (!IsHash(file.Sha256)) throw new ArgumentException("Expected SHA-256 in hexadecimal.");
                total = checked(total + file.Size);
            }
            // Reject file/directory collisions on every target filesystem, before downloading anything.
            foreach (var file in this.files)
            {
                var components = file.Path.Split('/');
                for (int i = 1; i < components.Length; i++)
                    if (paths.Contains(string.Join("/", components, 0, i))) throw new ArgumentException("Content file/directory collision.");
            }
            Files = Array.AsReadOnly(this.files);
            TotalBytes = total;
            Fingerprint = ComputeHash(Encoding.UTF8.GetBytes(version + "\n" + string.Join("\n",
                this.files.Select(x => x.Key + "\0" + x.Path + "\0" + x.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0" + x.Sha256.ToLowerInvariant()))));
        }

        public string Version { get; }
        public IReadOnlyList<ContentFile> Files { get; }
        public long TotalBytes { get; }
        public string Fingerprint { get; }

        public byte[] ToJson()
        {
            using var stream = new MemoryStream();
            new DataContractJsonSerializer(typeof(Document)).WriteObject(stream, new Document { Version = Version, Files = files });
            return stream.ToArray();
        }

        public static ContentManifest FromJson(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, writable: false);
            var document = (Document)new DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
            if (document == null) throw new SerializationException("Empty content manifest.");
            return new ContentManifest(document.Version, document.Files);
        }

        [DataContract]
        private sealed class Document
        {
            [DataMember(Name = "version", IsRequired = true, Order = 0)] public string Version;
            [DataMember(Name = "files", IsRequired = true, Order = 1)] public ContentFile[] Files;
        }

        internal static bool IsHash(string hash) => hash != null && hash.Length == 64 && hash.All(x =>
            x >= '0' && x <= '9' || x >= 'a' && x <= 'f' || x >= 'A' && x <= 'F');
        internal static string ComputeHash(byte[] bytes) { using var sha = SHA256.Create(); return Hex(sha.ComputeHash(bytes)); }
        internal static string HashFile(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return Hex(sha.ComputeHash(stream)); }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static void ValidateText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new ArgumentException("Non-empty text without control characters is required.", name);
        }
        internal static void ValidatePath(string path)
        {
            ValidateText(path, nameof(path));
            if (path.IndexOfAny(new[] { '\\', ':', '*', '?', '"', '<', '>', '|', '\0' }) >= 0 ||
                path.Split('/').Any(x => x.Length == 0 || x == "." || x == ".." || x.EndsWith(" ", StringComparison.Ordinal) || x.EndsWith(".", StringComparison.Ordinal)) ||
                path.Split('/')[0].Equals("content-manifest.json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Expected a portable relative file path.", nameof(path));
            foreach (var part in path.Split('/'))
            {
                var name = part.Split('.')[0].ToUpperInvariant();
                if (name == "CON" || name == "PRN" || name == "AUX" || name == "NUL" ||
                    name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] >= '0' && name[3] <= '9')
                    throw new ArgumentException("Reserved filesystem path.", nameof(path));
            }
        }
    }

    /// <summary>A pinned installation. Updating the current pointer never changes the files used by this snapshot.</summary>
    public sealed class ContentSnapshot
    {
        private readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);
        internal ContentSnapshot(string directory, ContentManifest manifest)
        {
            Directory = directory;
            Manifest = manifest;
            foreach (var file in manifest.Files) paths.Add(file.Key, System.IO.Path.Combine(directory, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        }
        public string Directory { get; }
        public ContentManifest Manifest { get; }
        public string GetPath(string key) => paths.TryGetValue(key, out var path) ? path : throw new KeyNotFoundException("Unknown content key: " + key);
        public Stream OpenRead(string key) => File.OpenRead(GetPath(key));
    }
}
