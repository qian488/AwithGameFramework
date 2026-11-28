#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AwithGameFrame.Tools.UiPrototype
{
    [Serializable]
    public sealed class PrototypeDocument
    {
        public int schemaVersion;
        public string title;
        public int width;
        public int height;
        public string background;
        public PrototypeNode[] nodes;
    }

    [Serializable]
    public sealed class PrototypeNode
    {
        public string id;
        public string kind;
        public string parent;
        public string[] pages;
        public string binding;
        public string text;
        public string color;
        public string asset;
        public int x;
        public int y;
        public int width;
        public int height;
        public int fontSize;
        public int padding;
        public int spacing;
    }

    [Serializable]
    public sealed class PrototypeProfile
    {
        public string title;
        public string output;
        public string font;
        public PrototypeAsset[] assets;
        public PrototypeBinding[] bindings;
        public PrototypeFixture[] fixtures;
    }

    [Serializable]
    public sealed class PrototypeAsset
    {
        public string key;
        public string path;
    }

    [Serializable]
    public sealed class PrototypeBinding
    {
        public string key;
        public string kind;
    }

    [Serializable]
    public sealed class PrototypeFixture
    {
        public string page;
        public string title;
        public PrototypeValue[] values;
    }

    [Serializable]
    public sealed class PrototypeValue
    {
        public string key;
        public string text;
    }

    public static class PrototypeValidation
    {
        private static readonly HashSet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "panel", "text", "image", "input", "button", "scroll", "list"
        };

        public static bool IsId(string value) => value != null && Regex.IsMatch(value, @"\A[a-z][a-z0-9-]{0,47}\z");
        public static bool IsAssetPath(string value) => value != null && value.StartsWith("Assets/", StringComparison.Ordinal) &&
            value.Split('/').All(part => part.Length != 0 && part != "." && part != ".." && !part.Contains('\\') && !part.Contains(':'));

        public static string[] Check(PrototypeDocument document, PrototypeProfile profile, Func<string, bool> assetExists)
        {
            var problems = new List<string>();
            if (document?.schemaVersion != 1 || document.nodes == null || document.nodes.Length is < 1 or > 120 ||
                document.width is < 240 or > 1920 || document.height is < 160 or > 1080 || !IsColor(document.background))
                return new[] { "document: expected schema 1, a panel size, background color and 1–120 nodes." };
            if (profile == null || !IsAssetPath(profile.output) || !profile.output.EndsWith(".prefab", StringComparison.Ordinal) ||
                !IsAssetPath(profile.font) || profile.bindings == null || profile.fixtures == null || profile.assets == null)
                return new[] { "profile: expected output, font, assets, bindings and preview fixtures." };
            if (!assetExists(profile.font)) problems.Add("font: missing asset " + profile.font);

            var nodes = new Dictionary<string, PrototypeNode>(StringComparer.Ordinal);
            var scrollWithList = new HashSet<string>(StringComparer.Ordinal);
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in document.nodes)
            {
                if (!IsId(node?.id) || !nodes.TryAdd(node.id, node))
                {
                    problems.Add((node?.id ?? "(node)") + ": missing or duplicate node ID.");
                    continue;
                }
                var boxWidth = document.width;
                var boxHeight = document.height;
                if (!string.IsNullOrEmpty(node.parent))
                {
                    if (!nodes.TryGetValue(node.parent, out var parent) || parent == node ||
                        parent.kind != "panel" && parent.kind != "scroll")
                        problems.Add(node.id + ": parent must be an earlier panel or scroll node.");
                    else
                    {
                        boxWidth = parent.width;
                        boxHeight = parent.height;
                        if (parent.kind == "scroll" && (node.kind != "list" || !scrollWithList.Add(parent.id)))
                            problems.Add(node.id + ": scroll containers accept one list template.");
                        if (parent.kind != "scroll" && node.kind == "list")
                            problems.Add(node.id + ": list templates require a scroll parent.");
                    }
                }
                else if (node.kind == "list") problems.Add(node.id + ": list templates require a scroll parent.");
                if (!Kinds.Contains(node.kind) || node.width < 1 || node.height < 1 || node.x < 0 || node.y < 0 ||
                    (long)node.x + node.width > boxWidth || (long)node.y + node.height > boxHeight)
                    problems.Add(node.id + ": unknown widget or layout outside its parent.");
                if ((!string.IsNullOrEmpty(node.color) && !IsColor(node.color)) || node.fontSize is < 0 or > 96 ||
                    node.padding is < 0 or > 40 || node.spacing is < 0 or > 40 || node.text?.Length > 320)
                    problems.Add(node.id + ": invalid style or text length.");
                foreach (string page in node.pages ?? Array.Empty<string>())
                    if (!profile.fixtures.Any(fixture => fixture.page == page)) problems.Add(node.id + ": unknown page " + page);
                if (!string.IsNullOrEmpty(node.binding))
                {
                    if (!bindings.Add(node.binding) || !profile.bindings.Any(binding => binding.key == node.binding && binding.kind == node.kind))
                        problems.Add(node.id + ": unexpected, duplicate or incompatible binding " + node.binding);
                }
                if (!string.IsNullOrEmpty(node.asset))
                {
                    var asset = profile.assets.FirstOrDefault(item => item.key == node.asset);
                    if (asset == null || !IsAssetPath(asset.path) || !assetExists(asset.path))
                        problems.Add(node.id + ": missing asset " + node.asset);
                    if (node.kind != "image") problems.Add(node.id + ": only image widgets accept an asset key.");
                }
            }
            foreach (var binding in profile.bindings)
                if (!bindings.Contains(binding.key)) problems.Add(binding.key + ": required " + binding.kind + " binding is missing.");
            return problems.ToArray();
        }

        private static bool IsColor(string value) => value != null && Regex.IsMatch(value, @"\A#[A-Fa-f0-9]{6}([A-Fa-f0-9]{2})?\z");
    }
}
