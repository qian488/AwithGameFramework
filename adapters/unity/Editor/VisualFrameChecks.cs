using System;
using System.IO;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor
{
    [Serializable]
    internal sealed class VisualFrameMetrics
    {
        public int width, height, colorSpan;
        public float darkRatio, magentaRatio, changedRatio;
        public bool compared;
    }

    internal static class VisualFrameChecks
    {
        internal static void ValidateSize(int width, int height)
        {
            if (width < 16 || height < 16 || width > 4096 || height > 4096)
                throw new InvalidDataException($"Visual dimensions must each be 16–4096 pixels: {width}×{height}.");
        }

        internal static Texture2D LoadPng(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException($"Visual PNG not found: {path}");
            if (info.Length < 33 || info.Length > 80 * 1024 * 1024) throw new InvalidDataException($"Invalid PNG size: {path}");
            var bytes = File.ReadAllBytes(path);
            if (bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 ||
                bytes[12] != 73 || bytes[13] != 72 || bytes[14] != 68 || bytes[15] != 82)
                throw new InvalidDataException($"Expected PNG data: {path}");
            ValidateSize(ReadInt(bytes, 16), ReadInt(bytes, 20));
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(texture, bytes)) return texture;
            UnityEngine.Object.DestroyImmediate(texture);
            throw new InvalidDataException($"Unity could not decode PNG: {path}");
        }

        internal static Texture2D ReadTarget(RenderTexture target)
        {
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                return image;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(image);
                throw;
            }
        }

        internal static VisualFrameMetrics Measure(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            int dark = 0, magenta = 0;
            int minRed = 255, minGreen = 255, minBlue = 255, maxRed = 0, maxGreen = 0, maxBlue = 0;
            foreach (var pixel in pixels)
            {
                if (pixel.r <= 12 && pixel.g <= 12 && pixel.b <= 12) dark++;
                if (pixel.r >= 245 && pixel.g <= 16 && pixel.b >= 245) magenta++;
                minRed = Math.Min(minRed, pixel.r);
                minGreen = Math.Min(minGreen, pixel.g);
                minBlue = Math.Min(minBlue, pixel.b);
                maxRed = Math.Max(maxRed, pixel.r);
                maxGreen = Math.Max(maxGreen, pixel.g);
                maxBlue = Math.Max(maxBlue, pixel.b);
            }
            return new VisualFrameMetrics
            {
                width = texture.width, height = texture.height,
                darkRatio = dark / (float)pixels.Length, magentaRatio = magenta / (float)pixels.Length,
                colorSpan = Math.Max(maxRed - minRed, Math.Max(maxGreen - minGreen, maxBlue - minBlue))
            };
        }

        internal static float Compare(Texture2D actual, Texture2D baseline, int tolerance, string diffPath)
        {
            var pixels = actual.GetPixels32();
            var expected = baseline.GetPixels32();
            int changed = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                var pixel = pixels[index];
                var reference = expected[index];
                if (Math.Abs(pixel.r - reference.r) > tolerance || Math.Abs(pixel.g - reference.g) > tolerance ||
                    Math.Abs(pixel.b - reference.b) > tolerance || Math.Abs(pixel.a - reference.a) > tolerance)
                {
                    changed++;
                    pixels[index] = new Color32(255, 40, 40, 255);
                }
                else
                {
                    byte shade = (byte)((pixel.r + pixel.g + pixel.b) / 9);
                    pixels[index] = new Color32(shade, shade, shade, 255);
                }
            }
            var image = new Texture2D(actual.width, actual.height, TextureFormat.RGBA32, false);
            try
            {
                image.SetPixels32(pixels);
                image.Apply();
                File.WriteAllBytes(diffPath, image.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            return changed / (float)pixels.Length;
        }

        private static int ReadInt(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }
}
