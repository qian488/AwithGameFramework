using System;
using System.IO;
using AwithGameFrame.Unity.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AwithGameFrame.Samples.Editor
{
    public static class UnityVisualSmoke
    {
        private const string ScenePath = "Assets/VisualSmoke.unity";

        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.15f, .25f, .35f);
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)canvas.transform;
            root.sizeDelta = new Vector2(320, 180);
            root.localScale = Vector3.one / 100;
            var panel = CreateImage(root, "Panel", -80, 0, 90, 70, Color.cyan);
            var badge = CreateImage(root, "Badge", 55, 0, 80, 30, Color.white);
            var mask = CreateImage(root, "Mask", 0, -60, 60, 28, Color.gray);
            mask.gameObject.AddComponent<RectMask2D>();
            var label = new GameObject("Label", typeof(RectTransform)).AddComponent<Text>();
            var labelRect = label.rectTransform;
            labelRect.SetParent(mask, false);
            labelRect.sizeDelta = new Vector2(50, 24);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = "OK";
            label.fontSize = 14;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            EditorSceneManager.SaveScene(scene, ScenePath);

            const string rootOutput = "VisualSmoke";
            Directory.CreateDirectory(rootOutput);
            var spec = Path.Combine(rootOutput, "spec.json");
            const string geometry = @", ""visible"": [""/Canvas/Panel"", ""/Canvas/Badge"", ""/Canvas/Mask/Label""],
                ""noOverlap"": [{""first"": ""/Canvas/Panel"", ""second"": ""/Canvas/Badge""}]";
            WriteSpec(spec, CaptureCase("reference", geometry));
            Require(UnityVisualVerification.Run(spec, Path.Combine(rootOutput, "reference")), "The clean fixture failed.");
            WriteSpec(spec, CaptureCase("same", geometry + @", ""baseline"": ""reference/reference-actual.png"""));
            Require(UnityVisualVerification.Run(spec, Path.Combine(rootOutput, "same")), "A repeat capture differed from its baseline.");

            scene = EditorSceneManager.OpenScene(ScenePath);
            panel = (RectTransform)GameObject.Find("Panel").transform;
            badge = (RectTransform)GameObject.Find("Badge").transform;
            panel.anchoredPosition = new Vector2(-164, 0);
            badge.anchoredPosition = new Vector2(-130, 0);
            badge.GetComponent<Image>().color = Color.magenta;
            label = GameObject.Find("Label").GetComponent<Text>();
            label.rectTransform.anchoredPosition = new Vector2(29, 0);
            label.text = "Long enough to exceed the available rectangle";
            EditorSceneManager.SaveScene(scene, ScenePath);
            WriteSpec(spec, CaptureCase("regression", geometry + @", ""baseline"": ""reference/reference-actual.png"""));
            string regressionOutput = Path.Combine(rootOutput, "regression");
            Require(!UnityVisualVerification.Run(spec, regressionOutput), "The layout and shader-color regressions were accepted.");
            string report = File.ReadAllText(Path.Combine(regressionOutput, "visual-report.json"));
            foreach (var issue in new[] { "UI overlap", "UI outside viewport", "UI clipped", "UI text does not fit", "Magenta pixels", "Changed pixels" })
                Require(report.Contains(issue), $"Missing detection: {issue}");
            Require(File.Exists(Path.Combine(regressionOutput, "regression-diff.png")), "Missing difference image.");

            var png = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            try
            {
                png.SetPixels(new Color[256]);
                png.Apply();
                File.WriteAllBytes(Path.Combine(rootOutput, "blank.png"), png.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(png); }
            WriteSpec(spec, @"{""id"":""blank"",""image"":""blank.png"",""requireNonUniform"":true,""maxDarkRatio"":0.9}");
            string blankOutput = Path.Combine(rootOutput, "blank");
            Require(!UnityVisualVerification.Run(spec, blankOutput), "A blank image passed.");
            Require(File.ReadAllText(Path.Combine(blankOutput, "visual-report.json")).Contains("Near-black pixels"), "Missing dark frame detection.");
            Debug.Log("UNITY_VISUAL_SMOKE_PASSED");
        }

        private static RectTransform CreateImage(RectTransform root, string name, float x, float y, float width, float height, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            var rect = image.rectTransform;
            rect.SetParent(root, false);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
            image.color = color;
            return rect;
        }

        private static string CaptureCase(string id, string options) => $@"{{""id"":""{id}"",""scene"":""{ScenePath}"",""camera"":""/Camera"",
            ""width"":320,""height"":180,""pixelTolerance"":2,""maxChangedRatio"":0,
            ""maxMagentaRatio"":0.005,""requireNonUniform"":true{options}}}";

        private static void WriteSpec(string path, string item) => File.WriteAllText(path, @"{""schemaVersion"":1,""cases"":[" + item + "]}");

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
