using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AwithGameFrame.Unity.Editor
{
    internal static class EditorAutomationCheckpoints
    {
        private static string ProjectPath => Path.GetDirectoryName(Application.dataPath);
        private static string Store => Path.Combine(ProjectPath, "Library", "AwithGameFrameAutomation", "Checkpoints");

        internal static AutomationResponse Execute(AutomationRequest request, Scene scene)
        {
            var response = EditorAutomationActions.Result(scene);
            EditorAutomation.RequireUnlinked(Store);
            switch (request.action)
            {
                case "checkpoint.capture": response.checkpoint = Capture(request.asset); break;
                case "checkpoint.inspect": response.checkpoint = Read(request.snapshot, true); break;
                case "checkpoint.list":
                    var limit = Mathf.Clamp(request.limit == 0 ? 100 : request.limit, 1, 500);
                    var directory = new DirectoryInfo(Store);
                    var entries = directory.Exists ? directory.EnumerateDirectories()
                        .Where(entry => Guid.TryParseExact(entry.Name, "N", out _) && File.Exists(Path.Combine(entry.FullName, "checkpoint.json")))
                        .OrderByDescending(entry => entry.CreationTimeUtc).ThenBy(entry => entry.Name, StringComparer.Ordinal)
                        .Take(limit + 1).Select(entry => Read(entry.Name, false)).ToArray() : Array.Empty<AutomationCheckpoint>();
                    response.truncated = entries.Length > limit;
                    response.checkpoints = entries.Take(limit).ToArray();
                    break;
                case "checkpoint.restore":
                    if (!request.batch) throw new InvalidOperationException("Close the project and use -EditorPath to restore a checkpoint.");
                    response.checkpoint = Restore(request.snapshot, request.expectedHash);
                    break;
                default: throw new ArgumentException("Unknown checkpoint action: " + request.action);
            }
            return response;
        }

        private static AutomationCheckpoint Capture(string asset)
        {
            var path = AssetFile(asset);
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.path == asset && scene.isDirty) throw new InvalidOperationException("Save the scene before capturing a checkpoint.");
            }
            var guid = AssetDatabase.AssetPathToGUID(asset);
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Unity has not imported the asset.");
            var id = Guid.NewGuid().ToString("N");
            var directory = CheckpointDirectory(id);
            Directory.CreateDirectory(directory);
            var data = Path.Combine(directory, "asset.data");
            File.Copy(path, data);
            var hash = Hash(data);
            if (Hash(path) != hash) throw new IOException("The source changed while capturing the checkpoint.");
            var checkpoint = new AutomationCheckpoint
            {
                id = id, asset = asset, guid = guid, savedHash = hash, currentHash = hash, createdUtc = DateTimeOffset.UtcNow.ToString("O")
            };
            var temporary = Path.Combine(directory, "checkpoint.tmp");
            File.WriteAllText(temporary, JsonUtility.ToJson(checkpoint, true));
            File.Move(temporary, Path.Combine(directory, "checkpoint.json"));
            return checkpoint;
        }

        private static AutomationCheckpoint Read(string id, bool includeCurrent)
        {
            var directory = CheckpointDirectory(id);
            var manifest = Path.Combine(directory, "checkpoint.json");
            EditorAutomation.RequireUnlinked(manifest);
            var checkpoint = JsonUtility.FromJson<AutomationCheckpoint>(File.ReadAllText(manifest));
            if (checkpoint == null || checkpoint.id != id) throw new InvalidDataException("The checkpoint manifest does not match its ID.");
            checkpoint.currentHash = null;
            if (includeCurrent)
            {
                var current = AssetFile(checkpoint.asset, false);
                if (File.Exists(current) && AssetDatabase.AssetPathToGUID(checkpoint.asset) == checkpoint.guid)
                    checkpoint.currentHash = Hash(current);
            }
            return checkpoint;
        }

        private static AutomationCheckpoint Restore(string id, string expectedHash)
        {
            var checkpoint = Read(id, true);
            if (string.IsNullOrEmpty(checkpoint.currentHash) || !string.Equals(checkpoint.currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Current asset identity or hash differs. Inspect the checkpoint and review the newer edit before restoring.");
            var path = AssetFile(checkpoint.asset);
            var data = Path.Combine(CheckpointDirectory(id), "asset.data");
            EditorAutomation.RequireUnlinked(data);
            var temporary = Path.Combine(Path.GetDirectoryName(path), ".awith-restore-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(data, temporary);
                if (Hash(temporary) != checkpoint.savedHash) throw new IOException("The checkpoint data failed its hash check.");
                if (Hash(path) != checkpoint.currentHash) throw new IOException("The asset changed during restore.");
                File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            checkpoint.currentHash = checkpoint.savedHash;
            return checkpoint;
        }

        private static string AssetFile(string asset, bool requireExists = true)
        {
            var extension = Path.GetExtension(asset);
            if (extension != ".unity" && extension != ".prefab") throw new ArgumentException("Checkpoints support saved scenes and Prefabs.");
            var path = Path.Combine(ProjectPath, EditorAutomationActions.ValidateAssetPath(asset, extension));
            EditorAutomation.RequireUnlinked(path + ".meta");
            if (requireExists && (!File.Exists(path) || !File.Exists(path + ".meta"))) throw new FileNotFoundException("The asset and its .meta must exist.", path);
            return path;
        }

        private static string CheckpointDirectory(string id)
        {
            if (!Guid.TryParseExact(id, "N", out var guid) || guid.ToString("N") != id)
                throw new ArgumentException("Supply a checkpoint ID from checkpoint.capture or checkpoint.list.");
            var path = Path.Combine(Store, id);
            EditorAutomation.RequireUnlinked(path);
            return path;
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
