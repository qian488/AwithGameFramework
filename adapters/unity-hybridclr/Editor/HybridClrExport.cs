using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace AwithGameFrame.Unity.HybridUpdate.Editor
{
    public static class HybridClrExport
    {
        public static void Generate() => PrebuildCommand.GenerateAll();

        public static void RunBatch()
        {
            Export(EditorUserBuildSettings.activeBuildTarget, Argument("-awith-player-build"),
                Argument("-awith-hybrid-output"));
        }

        /// <summary>Exports the hot DLLs and stripped AOT metadata produced for the selected Player build.</summary>
        public static void Export(BuildTarget target, string playerBuild, string output)
        {
            if (string.IsNullOrWhiteSpace(playerBuild)) throw new ArgumentException("Supply a Player build identifier.", nameof(playerBuild));
            if (!SettingsUtil.HybridCLRSettings.enable) throw new BuildFailedException("Enable HybridCLR in this project before exporting.");
            var metadata = SettingsUtil.HybridCLRSettings.patchAOTAssemblies ?? Array.Empty<string>();
            var hot = SettingsUtil.HotUpdateAssemblyNamesExcludePreserved;
            var names = metadata.Concat(hot).ToArray();
            if (hot.Count == 0 || names.Any(name => string.IsNullOrEmpty(name) ||
                !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_][A-Za-z0-9_.-]*$")) ||
                names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
                throw new BuildFailedException("Declare unique names for AOT metadata and at least one hot-update assembly in HybridCLR settings.");

            var destination = Path.GetFullPath(output);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new export directory: " + destination);

            var stage = destination + ".stage-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stage);
            try
            {
                var manifest = new HotUpdateManifest
                {
                    playerBuild = playerBuild,
                    unityVersion = Application.unityVersion,
                    target = target.ToString(),
                    metadata = ExportFiles(metadata, SettingsUtil.GetAssembliesPostIl2CppStripDir(target), stage, "aot"),
                    assemblies = ExportFiles(hot.ToArray(), SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target), stage, "hot")
                };
                File.WriteAllText(Path.Combine(stage, "hot-update.json"), JsonUtility.ToJson(manifest, true));
                Directory.Move(stage, destination);
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }

        private static HotUpdateFile[] ExportFiles(string[] names, string source, string stage, string group)
        {
            if (names.Length == 0) return Array.Empty<HotUpdateFile>();
            Directory.CreateDirectory(Path.Combine(stage, group));
            var inventory = new HotUpdateFile[names.Length];
            using var sha = SHA256.Create();
            for (int index = 0; index < names.Length; index++)
            {
                var name = names[index];
                var relative = group + "/" + name + ".dll";
                var file = Path.Combine(source, name + ".dll");
                if (!File.Exists(file)) throw new FileNotFoundException("Missing HybridCLR build input.", file);
                var target = Path.Combine(stage, relative);
                File.Copy(file, target);
                if (AssemblyName.GetAssemblyName(target).Name != name)
                    throw new InvalidDataException("HybridCLR assembly name does not match its file: " + file);
                using var stream = File.OpenRead(target);
                inventory[index] = new HotUpdateFile { assembly = name, path = relative,
                    sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() };
            }
            return inventory;
        }

        private static string Argument(string option)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, option);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Supply " + option);
            return args[index + 1];
        }
    }
}
