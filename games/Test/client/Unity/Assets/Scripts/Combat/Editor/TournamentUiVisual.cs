using System;
using System.Collections.Generic;
using System.IO;
using AwithGameFrame.Unity.Presentation;
using Test.Application.Protocol;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Test.Client.Combat.Editor
{
    public static class TournamentUiVisual
    {
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-tournament-ui-visual");
            if (index < 0 || index + 1 == args.Length) throw new ArgumentException("Missing -tournament-ui-visual.");
            string output = args[index + 1];
            Directory.CreateDirectory(output);
            var prefab = Resources.Load<GameObject>("Tournament/Lobby");
            var lobby = Object.Instantiate(prefab).GetComponent<TournamentLobbyView>();
            var camera = new GameObject("Tournament camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.09f, .13f, .16f);
            camera.transform.position = new Vector3(0, 0, -10);
            lobby.Initialize("http://127.0.0.1:17840", "evening-match", "Wayfarer", null, null, null, null);
            lobby.RenderForCamera(camera);
            var view = lobby.GetComponentInChildren<UnityPrototypeView>();
            var canvas = lobby.GetComponent<Canvas>();
            lobby.GetComponent<CanvasScaler>().enabled = false;
            var spec = new Spec();
            var state = new PvpTournamentState
            {
                FirstScore = 1, RoundNumber = 2, Mode = "bbads", ConnectedMask = 3, ReadyMask = 2,
                FirstPlayerId = "Wayfarer_With_A_Very_Long_Name_012345678901234567",
                SecondPlayerId = "Sentinel_With_A_Very_Long_Name_012345678901234567"
            };
            try
            {
                foreach (var size in new[] { (1280, 720), (1920, 1080), (3440, 1440) })
                {
                    foreach (string page in new[] { "lobby", "reconnect", "results" })
                    {
                        state.ConnectedMask = page == "reconnect" ? 1 : 3;
                        state.FirstScore = page == "results" ? 2 : 1;
                        state.SecondScore = page == "results" ? 1 : 0;
                        state.Rounds = page == "results" ? SampleRounds() : null;
                        string status = page == "lobby" ? "Round 1 confirmed" :
                            page == "reconnect" ? "Opponent reconnecting…" : "Victory";
                        lobby.Present(page, status, state, 0, false, page == "lobby", page == "reconnect");
                        if (page == "results") view.Get<Button>("details").onClick.Invoke();
                        var check = new Frame
                        {
                            id = $"{page}-{size.Item1}x{size.Item2}", width = size.Item1, height = size.Item2
                        };
                        check.image = check.id + ".png";
                        check.baseline = check.id + "-repeat.png";
                        Capture(camera, canvas, view, page, check, Path.Combine(output, check.image));
                        Capture(camera, canvas, view, page, check, Path.Combine(output, check.baseline));
                        spec.cases.Add(check);
                    }
                }

                lobby.Present("lobby", "Round 1 confirmed", state, 0, false, true, false);
                var ready = view.Get<Button>("ready");
                ((RectTransform)ready.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 30);
                try
                {
                    Capture(camera, canvas, view, "lobby", spec.cases[0], null);
                    throw new InvalidDataException("A compressed ready button passed the layout check.");
                }
                catch (InvalidDataException error) when (error.Message.StartsWith("ready:", StringComparison.Ordinal))
                {
                    File.WriteAllText(Path.Combine(output, "negative-layout.txt"), error.Message);
                }
                File.WriteAllText(Path.Combine(output, "visual-cases.json"), JsonUtility.ToJson(spec, true));
            }
            finally
            {
                Object.DestroyImmediate(lobby.gameObject);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        private static void Capture(Camera camera, Canvas canvas, UnityPrototypeView view, string page, Frame frame, string path)
        {
            var target = RenderTexture.GetTemporary(frame.width, frame.height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                canvas.scaleFactor = frame.height / 720f;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                var status = view.Get<Text>("status");
                var score = view.Get<Text>("score");
                var action = view.Get<Button>(page == "lobby" ? "ready" : page == "reconnect" ? "retry" : "details");
                var statusBox = Check(status.rectTransform, camera, frame, status);
                var scoreBox = Check(score.rectTransform, camera, frame, score);
                var actionBox = Check((RectTransform)action.transform, camera, frame, action.GetComponentInChildren<Text>());
                if (statusBox.Overlaps(scoreBox) || statusBox.Overlaps(actionBox) || scoreBox.Overlaps(actionBox))
                    throw new InvalidDataException("Status, score and primary button overlap.");
                if (page != "results")
                {
                    foreach (string key in new[] { "first", "second" })
                    {
                        var name = view.Get<Text>(key);
                        Check(name.rectTransform, camera, frame, name);
                    }
                }
                else
                {
                    var scroll = view.Get<ScrollRect>("report");
                    if (scroll.content.rect.height <= scroll.viewport.rect.height)
                        throw new InvalidDataException("Expanded three-round report did not create a scrollable area.");
                }
                if (path == null) return;
                RenderTexture.active = target;
                var image = new Texture2D(frame.width, frame.height, TextureFormat.RGBA32, false);
                try
                {
                    image.ReadPixels(new Rect(0, 0, frame.width, frame.height), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(path, image.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(image); }
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Rect Check(RectTransform widget, Camera camera, Frame frame, Text label)
        {
            var corners = new Vector3[4];
            widget.GetWorldCorners(corners);
            var start = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var end = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            if (!widget.gameObject.activeInHierarchy || start.x < -.5f || start.y < -.5f ||
                end.x > frame.width + .5f || end.y > frame.height + .5f ||
                label.preferredWidth > label.rectTransform.rect.width + 1 ||
                label.preferredHeight > label.rectTransform.rect.height + 1)
                throw new InvalidDataException(widget.name + ": invisible, clipped or undersized control.");
            return Rect.MinMaxRect(start.x, start.y, end.x, end.y);
        }

        private static PvpTournamentRoundSummary[] SampleRounds() => new[]
        {
            new PvpTournamentRoundSummary { Mode = "rain-pass", Winner = 0, Reason = "Combat" },
            new PvpTournamentRoundSummary { Mode = "bbads", Winner = 1, Reason = "Combat",
                FirstBackpack = new[] { "Fish:1:1", "Weapon:1:0" }, SecondBackpack = new[] { "Fish:0:1", "Weapon:0:0" } },
            new PvpTournamentRoundSummary { Mode = "rain-pass", Winner = 0, Reason = "Combat" }
        };

        [Serializable]
        private sealed class Spec
        {
            public int schemaVersion = 1;
            public List<Frame> cases = new List<Frame>();
        }

        [Serializable]
        private sealed class Frame
        {
            public string id, image, baseline;
            public int width, height;
            public bool requireNonUniform = true;
            public float maxMagentaRatio = .005f, maxDarkRatio = .97f, maxChangedRatio = .002f;
            public int pixelTolerance = 4;
        }
    }
}
