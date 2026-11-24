using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AwithGameFrame.Data.Generation
{
    /// <summary>One generated output tracked by a deterministic asset generator.</summary>
    public sealed class GeneratedAssetOutput
    {
        public GeneratedAssetOutput(string path, long size, string sha256)
        {
            ValidatePath(path);
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (!IsSha256(sha256)) throw new ArgumentException("Expected a SHA-256 output hash.", nameof(sha256));
            Path = path;
            Size = size;
            Sha256 = sha256.ToLowerInvariant();
        }

        public string Path { get; }
        public long Size { get; }
        public string Sha256 { get; }

        internal static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(new[] { '\\', ':', '*', '?', '"', '<', '>', '|', '\0' }) >= 0 ||
                path.Split('/').Any(part => part.Length == 0 || part == "." || part == ".." || part.EndsWith(" ", StringComparison.Ordinal) || part.EndsWith(".", StringComparison.Ordinal)))
                throw new ArgumentException("Expected a portable relative output path.", nameof(path));
        }

        internal static bool IsSha256(string value) => value != null && value.Length == 64 && value.All(IsHex);

        private static bool IsHex(char value) => value >= '0' && value <= '9' || value >= 'a' && value <= 'f' || value >= 'A' && value <= 'F';
    }

    /// <summary>Provenance and output identity for runtime-generated or editor-baked assets.</summary>
    public sealed class GeneratedAssetManifest
    {
        public GeneratedAssetManifest(string generatorId, string generatorVersion, string inputHash, int? seed,
            IEnumerable<GeneratedAssetOutput> outputs)
        {
            ValidateText(generatorId, nameof(generatorId));
            ValidateText(generatorVersion, nameof(generatorVersion));
            if (!GeneratedAssetOutput.IsSha256(inputHash)) throw new ArgumentException("Expected a SHA-256 input hash.", nameof(inputHash));
            if (outputs == null) throw new ArgumentNullException(nameof(outputs));
            var ordered = outputs.OrderBy(output => output?.Path, StringComparer.Ordinal).ToArray();
            if (ordered.Length == 0 || ordered.Any(output => output == null)) throw new ArgumentException("At least one generated output is required.", nameof(outputs));
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var output in ordered)
                if (!paths.Add(output.Path)) throw new ArgumentException("Generated output paths must be unique.", nameof(outputs));

            GeneratorId = generatorId;
            GeneratorVersion = generatorVersion;
            InputHash = inputHash.ToLowerInvariant();
            Seed = seed;
            Outputs = new ReadOnlyCollection<GeneratedAssetOutput>(ordered);
            Fingerprint = ComputeFingerprint();
        }

        public string GeneratorId { get; }
        public string GeneratorVersion { get; }
        public string InputHash { get; }
        public int? Seed { get; }
        public IReadOnlyList<GeneratedAssetOutput> Outputs { get; }
        public string Fingerprint { get; }

        public bool Matches(string generatorId, string generatorVersion, string inputHash, int? seed,
            IEnumerable<GeneratedAssetOutput> outputs)
        {
            try
            {
                var candidate = new GeneratedAssetManifest(generatorId, generatorVersion, inputHash, seed, outputs);
                return string.Equals(Fingerprint, candidate.Fingerprint, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
        }

        private string ComputeFingerprint()
        {
            var canonical = new StringBuilder()
                .Append(GeneratorId).Append('\n')
                .Append(GeneratorVersion).Append('\n')
                .Append(InputHash).Append('\n')
                .Append(Seed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<none").Append('\n');
            foreach (var output in Outputs)
                canonical.Append(output.Path).Append('\0').Append(output.Size.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append('\0').Append(output.Sha256).Append('\n');
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void ValidateText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new ArgumentException("Non-empty text without control characters is required.", name);
        }
    }
}
