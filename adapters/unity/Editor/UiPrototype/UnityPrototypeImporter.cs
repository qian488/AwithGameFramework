using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwithGameFrame.Unity.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AwithGameFrame.Tools.UiPrototype
{
    public static class UnityPrototypeImporter
    {
        [Serializable]
        public sealed class Report
        {
            public string output;
            public string guid;
            public string[] added;
            public string[] changed;
            public string[] removed;
            public string[] assets;
            public string[] problems;
            public bool written;
        }

        public static void RunBatch()
        {
            string reportPath = Argument("-awithPrototypeReport");
            Report report;
            try
            {
                var profile = JsonUtility.FromJson<PrototypeProfile>(File.ReadAllText(Argument("-awithPrototypeProfile")));
                report = Import(Argument("-awithPrototypeDesign"), profile, apply: true);
                if (report.problems.Length == 0)
                {
                    var repeated = Import(Argument("-awithPrototypeDesign"), profile, apply: true);
                    if (repeated.written || repeated.guid != report.guid)
                        report.problems = new[] { "output: repeated generation was not stable." };
                }
            }
            catch (Exception exception) { report = new Report { problems = new[] { exception.ToString() } }; }
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            if (report.problems.Length != 0) EditorApplication.Exit(1);
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 == args.Length) throw new ArgumentException("Missing " + name);
            return args[index + 1];
        }

        public static Report Import(string designPath, PrototypeProfile profile, bool apply)
        {
            var document = JsonUtility.FromJson<PrototypeDocument>(File.ReadAllText(designPath));
            var report = new Report { output = profile.output };
            var errors = PrototypeValidation.Check(document, profile, path => AssetDatabase.LoadMainAssetAtPath(path) != null).ToList();
            if (errors.Count != 0)
            {
                report.problems = errors.ToArray();
                return report;
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>(profile.font);
            if (font == null) errors.Add("font: expected an imported Unity Font at " + profile.font);
            var sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (var asset in profile.assets)
            {
                if (!document.nodes.Any(node => node.asset == asset.key)) continue;
                var sprite = AssetDatabase.LoadAllAssetsAtPath(asset.path).OfType<Sprite>().FirstOrDefault();
                if (sprite == null) errors.Add(asset.key + ": expected a Sprite at " + asset.path);
                else sprites.Add(asset.key, sprite);
            }
            report.assets = sprites.Keys.Select(key => key + " = " + profile.assets.First(asset => asset.key == key).path)
                .Prepend("font = " + profile.font).ToArray();
            report.problems = errors.ToArray();
            if (errors.Count != 0) return report;

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(profile.output);
            var existingView = existing != null ? existing.GetComponent<UnityPrototypeView>() : null;
            if (existing != null && existingView == null)
            {
                report.problems = new[] { "output: the prefab does not belong to the prototype importer." };
                return report;
            }
            string normalized = JsonUtility.ToJson(document);
            var previous = existingView == null ? null : JsonUtility.FromJson<PrototypeDocument>(existingView.Source);
            var oldNodes = (previous?.nodes ?? Array.Empty<PrototypeNode>()).ToDictionary(node => node.id, node => JsonUtility.ToJson(node));
            report.added = document.nodes.Where(node => !oldNodes.ContainsKey(node.id)).Select(node => node.id).ToArray();
            var changed = document.nodes.Where(node => oldNodes.TryGetValue(node.id, out string json) &&
                json != JsonUtility.ToJson(node)).Select(node => node.id);
            report.changed = (previous != null && (previous.width != document.width || previous.height != document.height ||
                previous.background != document.background) ? changed.Prepend("(panel)") : changed).ToArray();
            report.removed = oldNodes.Keys.Except(document.nodes.Select(node => node.id)).ToArray();
            report.guid = AssetDatabase.AssetPathToGUID(profile.output);
            if (!apply) return report;

            if (existingView != null && existingView.Source == normalized && MatchesResources(existingView, font, sprites, document))
                return report;
            var root = new GameObject("Prototype", typeof(RectTransform), typeof(Image), typeof(UnityPrototypeView));
            try
            {
                ConfigurePanel((RectTransform)root.transform, document.width, document.height);
                root.GetComponent<Image>().color = ColorOf(document.background, Color.black);
                var parents = new Dictionary<string, RectTransform>(StringComparer.Ordinal);
                var nodes = new UnityPrototypeView.Node[document.nodes.Length];
                for (int index = 0; index < nodes.Length; index++)
                {
                    var node = document.nodes[index];
                    RectTransform parent = string.IsNullOrEmpty(node.parent) ? (RectTransform)root.transform : parents[node.parent];
                    if (parent.TryGetComponent<ScrollRect>(out var scroll)) parent = scroll.content;
                    var rect = Box(node.id, parent, node.x, node.y, node.width, node.height);
                    Build(rect, node, font, sprites);
                    parents.Add(node.id, rect);
                    nodes[index] = new UnityPrototypeView.Node
                    {
                        id = node.id, binding = node.binding, pages = node.pages, target = rect.gameObject
                    };
                }
                root.GetComponent<UnityPrototypeView>().SetGeneratedNodes(nodes, normalized);
                Directory.CreateDirectory(Path.GetDirectoryName(profile.output));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                // PrefabUtility matches existing objects by name. Node IDs and generated child names are unique.
                PrefabUtility.SaveAsPrefabAsset(root, profile.output, out bool saved);
                if (!saved) throw new IOException("Could not save prefab " + profile.output);
                report.written = true;
                report.guid = AssetDatabase.AssetPathToGUID(profile.output);
                return report;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static bool MatchesResources(UnityPrototypeView view, Font font, Dictionary<string, Sprite> sprites,
            PrototypeDocument document) =>
            view.GetComponentsInChildren<Text>(true).All(text => text.font == font) &&
            view.Nodes.Length == document.nodes.Length &&
            document.nodes.Select((node, index) => view.Nodes[index].id == node.id &&
                (view.Nodes[index].pages ?? Array.Empty<string>()).SequenceEqual(node.pages ?? Array.Empty<string>())).All(matches => matches) &&
            document.nodes.Where(node => !string.IsNullOrEmpty(node.asset)).All(node =>
                view.Nodes.Any(binding => binding.id == node.id && binding.target != null &&
                    binding.target.GetComponent<Image>()?.sprite == sprites[node.asset]));

        private static void ConfigurePanel(RectTransform rect, int width, int height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        private static RectTransform Box(string name, Transform parent, float x, float y, float width, float height)
        {
            var item = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)item.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void Build(RectTransform rect, PrototypeNode node, Font font, Dictionary<string, Sprite> sprites)
        {
            Color color = ColorOf(node.color, Color.white);
            int fontSize = node.fontSize == 0 ? 22 : node.fontSize;
            switch (node.kind)
            {
                case "panel":
                    if (!string.IsNullOrEmpty(node.color)) rect.gameObject.AddComponent<Image>().color = color;
                    break;
                case "image":
                    var image = rect.gameObject.AddComponent<Image>();
                    image.color = color;
                    if (!string.IsNullOrEmpty(node.asset)) image.sprite = sprites[node.asset];
                    image.preserveAspect = image.sprite != null;
                    image.raycastTarget = false;
                    break;
                case "text":
                case "list":
                    var label = Label(rect, font, fontSize, node.text, color);
                    if (node.kind == "list")
                    {
                        label.alignment = TextAnchor.UpperLeft;
                        label.verticalOverflow = VerticalWrapMode.Overflow;
                        label.lineSpacing = 1f + node.spacing / (float)fontSize;
                        rect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                        ((RectTransform)rect.parent).gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                        var layout = rect.parent.gameObject.AddComponent<VerticalLayoutGroup>();
                        layout.padding = new RectOffset(node.x, node.x, node.y, node.padding);
                        layout.childControlHeight = layout.childControlWidth = true;
                        layout.childForceExpandHeight = false;
                    }
                    break;
                case "button":
                    var background = rect.gameObject.AddComponent<Image>();
                    background.color = color;
                    var button = rect.gameObject.AddComponent<Button>();
                    button.targetGraphic = background;
                    var colors = button.colors;
                    colors.selectedColor = new Color(.76f, .91f, 1);
                    colors.highlightedColor = new Color(.83f, .94f, 1);
                    button.colors = colors;
                    Label(Fill(rect, node.id + "-caption", node.padding), font, fontSize, node.text, Color.white)
                        .alignment = TextAnchor.MiddleCenter;
                    break;
                case "input":
                    var field = rect.gameObject.AddComponent<Image>();
                    field.color = color;
                    var input = rect.gameObject.AddComponent<InputField>();
                    input.targetGraphic = field;
                    input.characterLimit = node.binding == "server" ? 128 : 48;
                    input.textComponent = Label(Fill(rect, node.id + "-value", node.padding), font, fontSize, "", Color.white);
                    input.placeholder = Label(Fill(rect, node.id + "-placeholder", node.padding), font, fontSize, node.text,
                        new Color(.65f, .69f, .73f));
                    rect.gameObject.AddComponent<RectMask2D>();
                    break;
                case "scroll":
                    rect.gameObject.AddComponent<Image>().color = color;
                    var viewport = Fill(rect, node.id + "-viewport", node.padding);
                    viewport.gameObject.AddComponent<RectMask2D>();
                    var content = Box(node.id + "-content", viewport, 0, 0, 0, 0);
                    content.anchorMax = new Vector2(1, 1);
                    var scroll = rect.gameObject.AddComponent<ScrollRect>();
                    scroll.horizontal = false;
                    scroll.viewport = viewport;
                    scroll.content = content;
                    scroll.movementType = ScrollRect.MovementType.Clamped;
                    scroll.scrollSensitivity = 34;
                    break;
            }
        }

        private static RectTransform Fill(RectTransform parent, string name, int padding)
        {
            var rect = Box(name, parent, 0, 0, 0, 0);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * padding;
            rect.offsetMax = -Vector2.one * padding;
            return rect;
        }

        private static Text Label(RectTransform rect, Font font, int size, string value, Color color)
        {
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.text = value ?? "";
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return label;
        }

        private static Color ColorOf(string value, Color fallback) =>
            ColorUtility.TryParseHtmlString(value, out var color) ? color : fallback;
    }
}
