using System;

namespace BBads
{
    internal static class BBadsResource
    {
        public const string UnitPrefab = "BBads/Unit";

        public static string Key(string path) => string.IsNullOrWhiteSpace(path) || path.StartsWith("BBads/", StringComparison.Ordinal) ? path ?? string.Empty : "BBads/" + path;
    }
}
