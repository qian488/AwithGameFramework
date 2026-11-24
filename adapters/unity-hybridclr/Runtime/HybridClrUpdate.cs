using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HybridCLR;
using UnityEngine;

namespace AwithGameFrame.Unity.HybridUpdate
{
    [Serializable]
    public sealed class HotUpdateFile
    {
        public string assembly;
        public string path;
        public string sha256;
    }

    [Serializable]
    public sealed class HotUpdateManifest
    {
        public int version = 1;
        public string playerBuild;
        public string unityVersion;
        public string target;
        public HotUpdateFile[] metadata;
        public HotUpdateFile[] assemblies;
    }

    /// <summary>Loads one generation at startup, after checking its complete payload against the current Player.</summary>
    public static class HybridClrUpdate
    {
        public static IReadOnlyList<Assembly> Load(HotUpdateManifest manifest, string playerBuild, string target, Func<string, byte[]> readFile)
        {
            if (manifest == null || manifest.version != 1 || string.IsNullOrWhiteSpace(playerBuild) ||
                string.IsNullOrWhiteSpace(target) || manifest.playerBuild != playerBuild ||
                manifest.target != target || manifest.unityVersion != Application.unityVersion)
                throw new InvalidDataException("The HybridCLR update manifest does not match this Player's build, target or Unity version.");
            if (readFile == null) throw new ArgumentNullException(nameof(readFile));
            if (manifest.metadata == null || manifest.assemblies == null || manifest.assemblies.Length == 0)
                throw new InvalidDataException("The HybridCLR update needs metadata and assembly inventories.");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) loaded.Add(assembly.GetName().Name);
            var metadata = ReadVerified(manifest.metadata, "aot/", readFile, names);
            var updates = ReadVerified(manifest.assemblies, "hot/", readFile, names);
            foreach (var file in manifest.assemblies)
                if (loaded.Contains(file.assembly))
                    throw new InvalidOperationException("A hot-update assembly is already loaded: " + file.assembly);

            for (int index = 0; index < metadata.Length; index++)
            {
                var status = RuntimeApi.LoadMetadataForAOTAssembly(metadata[index], HomologousImageMode.Consistent);
                if (status != LoadImageErrorCode.OK)
                    throw new InvalidOperationException($"HybridCLR metadata '{manifest.metadata[index].assembly}' failed with {status}.");
            }
            var result = new Assembly[updates.Length];
            for (int index = 0; index < updates.Length; index++)
            {
                try { result[index] = Assembly.Load(updates[index]); }
                catch (Exception error)
                {
                    throw new InvalidOperationException($"HybridCLR could not load '{manifest.assemblies[index].assembly}'.", error);
                }
                if (result[index].GetName().Name != manifest.assemblies[index].assembly)
                    throw new InvalidDataException("HybridCLR loaded an unexpected assembly: " + result[index].FullName);
            }
            return Array.AsReadOnly(result);
        }

        private static byte[][] ReadVerified(HotUpdateFile[] files, string prefix, Func<string, byte[]> readFile, HashSet<string> names)
        {
            var result = new byte[files.Length][];
            using var sha = SHA256.Create();
            for (int index = 0; index < files.Length; index++)
            {
                var file = files[index];
                if (file == null || string.IsNullOrWhiteSpace(file.assembly) ||
                    !System.Text.RegularExpressions.Regex.IsMatch(file.assembly, "^[A-Za-z0-9_][A-Za-z0-9_.-]*$") ||
                    !names.Add(file.assembly) || file.path != prefix + file.assembly + ".dll")
                    throw new InvalidDataException("Invalid or duplicate HybridCLR assembly entry.");
                var data = readFile(file.path);
                if (data == null || data.Length == 0 ||
                    !string.Equals(BitConverter.ToString(sha.ComputeHash(data)).Replace("-", ""), file.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("HybridCLR assembly hash mismatch: " + file.path);
                result[index] = (byte[])data.Clone();
            }
            return result;
        }
    }
}
