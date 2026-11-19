using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor
{
    internal static class EditorAutomationPolicy
    {
        [Serializable]
        private sealed class Policy
        {
            public int version;
            public string[] live;
            public string[] batch;
        }

        internal static void Require(AutomationRequest request)
        {
            if (string.IsNullOrEmpty(request.action)) throw new ArgumentException("An editor action is required.");
            var settings = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ProjectSettings");
            var path = Path.Combine(settings, "AwithEditorAutomation.json");
            EditorAutomation.RequireUnlinked(settings);
            EditorAutomation.RequireUnlinked(path);
            if (!File.Exists(path))
            {
                if (Directory.Exists(path)) throw new InvalidDataException("The automation policy must be a file.");
                return;
            }

            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("The project automation policy exceeds 16 KB.");
            var policy = JsonUtility.FromJson<Policy>(File.ReadAllText(path));
            if (policy == null || policy.version != 1 || policy.live == null || policy.batch == null)
                throw new InvalidDataException("The project automation policy requires version 1 and live and batch allowlists.");

            var allowed = request.batch ? policy.batch : policy.live;
            RequireAction(allowed, request.action);
            if (request.action == "scene.transaction" && request.steps != null)
                foreach (var step in request.steps) RequireAction(allowed, step?.action);
        }

        private static void RequireAction(string[] allowed, string action)
        {
            if (string.IsNullOrEmpty(action) || !allowed.Contains(action, StringComparer.Ordinal))
                throw new UnauthorizedAccessException("Project automation policy denies action: " + action);
        }
    }
}
