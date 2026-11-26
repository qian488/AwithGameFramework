using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Test.Client.Tests
{
    internal static class RainPassContractFrames
    {
        internal static IEnumerator Check(Canvas canvas, string state, params string[] expectedButtons) =>
            CheckWithDisabledButton(canvas, state, null, expectedButtons);

        internal static IEnumerator CheckWithDisabledButton(Canvas canvas, string state, string disabledButton, params string[] expectedButtons)
            => CheckFrame(canvas, state, disabledButton, expectedButtons, null);

        internal static IEnumerator CheckLeaderboardRows(Canvas canvas, string state, Text[] rows, int visibleCount,
            params string[] expectedButtons)
        {
            Assert.That(rows.Count(row => !string.IsNullOrEmpty(row.text)), Is.EqualTo(visibleCount));
            return CheckFrame(canvas, state, null, expectedButtons, rows);
        }

        private static IEnumerator CheckFrame(Canvas canvas, string state, string disabledButton, string[] expectedButtons, Text[] rows)
        {
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(Camera.allCameras.Any(camera => camera.targetDisplay == 0 && camera.targetTexture == null), Is.True);

            Canvas.ForceUpdateCanvases();
            var buttons = canvas.GetComponentsInChildren<Button>();
            Assert.That(buttons.Select(button => button.GetComponentInChildren<Text>().text), Is.EquivalentTo(expectedButtons));
            var areas = new List<Rect>(buttons.Length);
            foreach (var button in buttons)
            {
                Assert.That(button.IsInteractable(), Is.EqualTo(button.GetComponentInChildren<Text>().text != disabledButton));
                Rect area = ScreenBounds((RectTransform)button.transform);
                Assert.That(areas.Any(previous => previous.Overlaps(area)), Is.False, "Buttons overlap.");
                areas.Add(area);
            }
            foreach (var text in canvas.GetComponentsInChildren<Text>().Where(text => !string.IsNullOrEmpty(text.text)))
            {
                ScreenBounds(text.rectTransform);
                if (text.horizontalOverflow == HorizontalWrapMode.Overflow)
                    Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(text.rectTransform.rect.width + 1), text.name);
                Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1), text.name);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            yield return new WaitForEndOfFrame();
            var frame = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Assert.That(frame, Is.Not.Null, state);
                Assert.That(frame.GetPixels32().Count(pixel => pixel.r > 145 && pixel.g > 155 && pixel.b > 145),
                    Is.GreaterThan(100), "The frame should contain rendered text or highlights.");
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        var bounds = ScreenBounds(row.rectTransform);
                        Assert.That(areas.Any(area => area.Overlaps(bounds)), Is.False, row.name + " overlaps a button.");
                        var pixels = frame.GetPixels(Mathf.CeilToInt(bounds.xMin), Mathf.CeilToInt(bounds.yMin) + 2,
                            Mathf.FloorToInt(bounds.width) - 1, Mathf.FloorToInt(bounds.height) - 4);
                        bool rendered = pixels.Count(pixel => pixel.r > 0.57f && pixel.g > 0.61f && pixel.b > 0.57f) > 12;
                        Assert.That(rendered, Is.EqualTo(!string.IsNullOrEmpty(row.text)), state + " · " + row.name + " · " + row.text);
                    }
                }
                var png = frame.EncodeToPNG();
                string encoded = Convert.ToBase64String(png);
                for (int offset = 0; offset < encoded.Length; offset += 4096)
                    Debug.Log("RAIN_PASS_FRAME " + state + " " + encoded.Substring(offset, Math.Min(4096, encoded.Length - offset)));
                Debug.Log("RAIN_PASS_FRAME_END " + state);
            }
            finally { if (frame != null) Object.Destroy(frame); }
#else
            yield break;
#endif
        }

        private static Rect ScreenBounds(RectTransform item)
        {
            var corners = new Vector3[4];
            item.GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            Assert.That(min.x >= -1 && min.y >= -1 && max.x <= Screen.width + 1 && max.y <= Screen.height + 1 &&
                max.x > min.x && max.y > min.y, Is.True, item.name + " needs a visible rectangle inside the viewport.");
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
