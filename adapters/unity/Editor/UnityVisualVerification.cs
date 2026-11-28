using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AwithGameFrame.Unity.Editor
{
    public static class UnityVisualVerification
    {
        [Serializable]
        private sealed class Specification
        {
            public int schemaVersion;
            public VisualCase[] cases;
        }

        [Serializable]
        private sealed class VisualCase
        {
            public string id, scene, camera, image, baseline;
            public int width, height, pixelTolerance;
            public float maxChangedRatio, maxDarkRatio, maxMagentaRatio;
            public bool requireNonUniform;
            public string[] visible;
            public LayoutPair[] noOverlap;
        }

        [Serializable]
        private sealed class LayoutPair
        {
            public string first, second;
        }

        [Serializable]
        private sealed class CaseResult
        {
            public string id, source, actual, baseline, diff;
            public VisualFrameMetrics metrics;
            public List<string> issues = new List<string>();
        }

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public bool passed;
            public string unity = Application.unityVersion;
            public string platform = Application.platform.ToString();
            public string graphics = SystemInfo.graphicsDeviceType.ToString();
            public string device = SystemInfo.graphicsDeviceName;
            public string colorSpace = QualitySettings.activeColorSpace.ToString();
            public string renderPipeline = GraphicsSettings.currentRenderPipeline?.GetType().FullName ?? "Built-in";
            public List<CaseResult> cases = new List<CaseResult>();
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Visual verification requires batch mode.");
            if (!Run(Argument("-awith-visual-spec"), Argument("-awith-visual-output")))
                throw new InvalidOperationException("Visual verification failed. Inspect visual-report.json.");
        }

        public static bool Run(string specificationPath, string outputDirectory)
        {
            if (!Application.isBatchMode || EditorApplication.isPlaying)
                throw new InvalidOperationException("Visual verification requires a disposable batch editor project in Edit Mode.");
            specificationPath = Path.GetFullPath(specificationPath);
            var specification = JsonUtility.FromJson<Specification>(File.ReadAllText(specificationPath));
            if (specification == null || specification.schemaVersion != 1 || specification.cases == null || specification.cases.Length == 0)
                throw new InvalidDataException("Expected visual specification schemaVersion 1 and at least one case.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in specification.cases)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || !char.IsLetterOrDigit(item.id[0]) ||
                    !item.id.All(character => char.IsLetterOrDigit(character) || character == '-' || character == '_') || !ids.Add(item.id))
                    throw new InvalidDataException("Each visual case needs a unique alphanumeric id (hyphens and underscores allowed).");
            }
            Directory.CreateDirectory(outputDirectory);
            var report = new Report();
            foreach (var item in specification.cases)
            {
                var result = new CaseResult { id = item.id, source = string.IsNullOrWhiteSpace(item.scene) ? item.image : item.scene };
                report.cases.Add(result);
                void RecordError(string message, string stackTrace, LogType type)
                {
                    if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                    string issue = $"Unity {type} during capture: {message}";
                    if (!result.issues.Contains(issue)) result.issues.Add(issue);
                }
                bool capturesScene = !string.IsNullOrWhiteSpace(item.scene);
                if (capturesScene) Application.logMessageReceived += RecordError;
                try { Inspect(item, Path.GetDirectoryName(specificationPath), outputDirectory, result); }
                catch (Exception exception) { result.issues.Add(exception.Message); }
                finally
                {
                    if (capturesScene) Application.logMessageReceived -= RecordError;
                }
                foreach (var issue in result.issues) Debug.LogWarning($"Visual case {item.id}: {issue}");
            }
            report.passed = report.cases.All(result => result.issues.Count == 0);
            string reportPath = Path.Combine(outputDirectory, "visual-report.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log($"Unity visual report ({(report.passed ? "passed" : "failed")}): {reportPath}");
            return report.passed;
        }

        private static void Inspect(VisualCase item, string inputDirectory, string outputDirectory, CaseResult result)
        {
            bool sceneCapture = !string.IsNullOrWhiteSpace(item.scene);
            if (sceneCapture == !string.IsNullOrWhiteSpace(item.image))
                throw new InvalidDataException("Specify either a scene or a PNG image.");
            if (sceneCapture || item.width != 0 || item.height != 0)
                VisualFrameChecks.ValidateSize(item.width, item.height);
            if (item.pixelTolerance < 0 || item.pixelTolerance > 255 || !Ratio(item.maxChangedRatio) ||
                !Ratio(item.maxDarkRatio) || !Ratio(item.maxMagentaRatio))
                throw new InvalidDataException("Pixel tolerance must be 0–255 and ratios must be 0–1.");

            Texture2D frame = null;
            Texture2D baseline = null;
            try
            {
                if (sceneCapture) frame = Capture(item, result);
                else
                {
                    if ((item.visible?.Length ?? 0) != 0 || (item.noOverlap?.Length ?? 0) != 0 || !string.IsNullOrEmpty(item.camera))
                        throw new InvalidDataException("Layout assertions and camera paths require a scene.");
                    frame = VisualFrameChecks.LoadPng(Path.Combine(inputDirectory, item.image));
                }
                result.actual = item.id + "-actual.png";
                File.WriteAllBytes(Path.Combine(outputDirectory, result.actual), frame.EncodeToPNG());
                if (item.width != 0 && (frame.width != item.width || frame.height != item.height))
                    result.issues.Add($"Actual image size {frame.width}×{frame.height} differs from requested {item.width}×{item.height}.");
                result.metrics = VisualFrameChecks.Measure(frame);
                if (item.requireNonUniform && result.metrics.colorSpan <= item.pixelTolerance)
                    result.issues.Add("Image is solid within the configured pixel tolerance.");
                if (item.maxDarkRatio > 0 && result.metrics.darkRatio > item.maxDarkRatio)
                    result.issues.Add($"Near-black pixels exceed the limit ({result.metrics.darkRatio:P2} > {item.maxDarkRatio:P2}).");
                if (item.maxMagentaRatio > 0 && result.metrics.magentaRatio > item.maxMagentaRatio)
                    result.issues.Add($"Magenta pixels exceed the limit ({result.metrics.magentaRatio:P2} > {item.maxMagentaRatio:P2}).");
                if (string.IsNullOrWhiteSpace(item.baseline)) return;
                result.baseline = Path.GetFullPath(Path.Combine(inputDirectory, item.baseline));
                baseline = VisualFrameChecks.LoadPng(result.baseline);
                if (baseline.width != frame.width || baseline.height != frame.height)
                    throw new InvalidDataException($"Baseline size {baseline.width}×{baseline.height} differs from actual {frame.width}×{frame.height}.");
                result.diff = item.id + "-diff.png";
                result.metrics.changedRatio = VisualFrameChecks.Compare(frame, baseline, item.pixelTolerance, Path.Combine(outputDirectory, result.diff));
                result.metrics.compared = true;
                if (result.metrics.changedRatio > item.maxChangedRatio)
                    result.issues.Add($"Changed pixels exceed the limit ({result.metrics.changedRatio:P2} > {item.maxChangedRatio:P2}).");
            }
            finally
            {
                if (frame != null) UnityEngine.Object.DestroyImmediate(frame);
                if (baseline != null) UnityEngine.Object.DestroyImmediate(baseline);
            }
        }

        private static Texture2D Capture(VisualCase item, CaseResult result)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Scene capture needs a graphics device. Use batch mode with graphics enabled.");
            if (GraphicsSettings.currentRenderPipeline != null)
                throw new InvalidOperationException("Direct scene capture requires the Built-in render pipeline. Supply a captured PNG for this pipeline.");
            if (!item.scene.StartsWith("Assets/", StringComparison.Ordinal) || !item.scene.EndsWith(".unity", StringComparison.Ordinal) ||
                item.scene.Split('/').Any(segment => segment == ".." || segment == ".") || AssetDatabase.LoadAssetAtPath<SceneAsset>(item.scene) == null)
                throw new InvalidDataException($"Expected a Unity scene under Assets: {item.scene}");
            var scene = EditorSceneManager.OpenScene(item.scene, OpenSceneMode.Single);
            if (scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Canvas>())
                .Any(canvas => canvas.isActiveAndEnabled && canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay))
                throw new InvalidOperationException("Camera capture omits Screen Space Overlay. Supply a captured PNG for this scene.");
            var camera = Resolve(scene, item.camera).GetComponent<Camera>();
            if (camera == null || !camera.gameObject.activeInHierarchy)
                throw new InvalidDataException($"Expected an active Camera at {item.camera}.");

            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(item.width, item.height, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = target;
                RenderTexture.active = target;
                GL.Clear(true, true, Color.clear);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                foreach (string path in item.visible ?? Array.Empty<string>()) CheckVisible(scene, camera, path, item.width, item.height, result);
                foreach (var pair in item.noOverlap ?? Array.Empty<LayoutPair>())
                {
                    if (pair == null) throw new InvalidDataException("Expected a noOverlap pair.");
                    var first = Bounds(Resolve(scene, pair.first), camera, item.width, item.height);
                    var second = Bounds(Resolve(scene, pair.second), camera, item.width, item.height);
                    if (Mathf.Min(first.xMax, second.xMax) - Mathf.Max(first.xMin, second.xMin) > 1 &&
                        Mathf.Min(first.yMax, second.yMax) - Mathf.Max(first.yMin, second.yMin) > 1)
                        result.issues.Add($"UI overlap: {pair.first} {first} and {pair.second} {second}.");
                }
                return VisualFrameChecks.ReadTarget(target);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static void CheckVisible(Scene scene, Camera camera, string path, int width, int height, CaseResult result)
        {
            var target = Resolve(scene, path);
            var graphic = target.GetComponent<Graphic>();
            if (graphic == null || !graphic.isActiveAndEnabled || graphic.color.a <= .001f ||
                graphic.canvasRenderer.GetInheritedAlpha() <= .001f || graphic.canvasRenderer.cull ||
                (camera.cullingMask & (1 << target.layer)) == 0)
                result.issues.Add($"UI graphic is missing, inactive, transparent or culled: {path}.");
            var bounds = Bounds(target, camera, width, height);
            if (bounds.width < 1 || bounds.height < 1 || !Contains(new Rect(0, 0, width, height), bounds))
                result.issues.Add($"UI outside viewport or has zero area: {path} {bounds}.");
            foreach (var mask in target.GetComponentsInParent<RectMask2D>())
            {
                if (!mask.isActiveAndEnabled || mask.gameObject == target) continue;
                var clipping = Bounds(mask.gameObject, camera, width, height, mask.padding);
                if (!Contains(clipping, bounds)) result.issues.Add($"UI clipped by {mask.name}: {path} {bounds}.");
            }
            if (graphic is Text text && !text.resizeTextForBestFit && !string.IsNullOrEmpty(text.text))
            {
                var rect = text.rectTransform.rect;
                if (text.font == null || text.preferredHeight > rect.height + 1 ||
                    (text.horizontalOverflow == HorizontalWrapMode.Overflow && text.preferredWidth > rect.width + 1))
                    result.issues.Add($"UI text does not fit its rectangle (or has no font): {path}.");
            }
        }

        private static Rect Bounds(GameObject target, Camera camera, int width, int height, Vector4 padding = default)
        {
            var rect = target.transform as RectTransform;
            var canvas = target.GetComponentInParent<Canvas>(true)?.rootCanvas;
            if (rect == null || canvas == null || !canvas.isActiveAndEnabled || canvas.renderMode == RenderMode.ScreenSpaceOverlay ||
                (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera != camera))
                throw new InvalidDataException($"{target.name} needs a RectTransform on an active camera-space or world-space canvas for this capture.");
            var local = rect.rect;
            var first = new Vector2(local.xMin + padding.x, local.yMin + padding.y);
            var last = new Vector2(local.xMax - padding.z, local.yMax - padding.w);
            if (last.x <= first.x || last.y <= first.y) return new Rect();
            var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in new[] { first, new Vector2(first.x, last.y), last, new Vector2(last.x, first.y) })
            {
                var point = camera.WorldToViewportPoint(rect.TransformPoint(corner));
                if (point.z < camera.nearClipPlane || point.z > camera.farClipPlane)
                    throw new InvalidDataException($"{target.name} crosses or lies outside the camera depth range.");
                var pixel = new Vector2(point.x * width, point.y * height);
                minimum = Vector2.Min(minimum, pixel);
                maximum = Vector2.Max(maximum, pixel);
            }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1 && inner.yMin >= outer.yMin - 1 &&
            inner.xMax <= outer.xMax + 1 && inner.yMax <= outer.yMax + 1;

        private static GameObject Resolve(Scene scene, string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '/') throw new InvalidDataException($"Expected an absolute hierarchy path: {path}");
            var segments = path.Substring(1).Split('/');
            var node = Unique(scene.GetRootGameObjects(), segments[0], path);
            foreach (var segment in segments.Skip(1))
                node = Unique(node.transform.Cast<Transform>().Select(child => child.gameObject), segment, path);
            return node;
        }

        private static GameObject Unique(IEnumerable<GameObject> objects, string name, string path)
        {
            var matches = objects.Where(item => item.name == name).Take(2).ToArray();
            if (matches.Length != 1) throw new InvalidDataException($"Missing or ambiguous hierarchy path: {path}");
            return matches[0];
        }

        private static bool Ratio(float value) => value >= 0 && value <= 1;

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException($"Missing {name}.");
            return args[index + 1];
        }
    }
}
