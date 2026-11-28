using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor.Content
{
    public static class UnityArtImportProfileProcessor
    {
        public static UnityArtImportReport Preview(string profilePath, string reportPath) => Run(profilePath, reportPath, false);

        public static UnityArtImportReport Apply(string profilePath, string reportPath) => Run(profilePath, reportPath, true);

        private static UnityArtImportReport Run(string profilePath, string reportPath, bool apply)
        {
            var project = Directory.GetParent(Application.dataPath).FullName;
            profilePath = Path.GetFullPath(Path.Combine(project, profilePath));
            reportPath = Path.GetFullPath(Path.Combine(project, reportPath));
            if (!string.Equals(Path.GetExtension(reportPath), ".json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profilePath, reportPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a .json report path distinct from the profile.", nameof(reportPath));

            var profile = UnityArtImportRules.Read(profilePath);
            var comparer = StringComparer.OrdinalIgnoreCase;
            var exceptions = profile.exceptions.ToDictionary(item => item.assetPath, comparer);
            var paths = AssetDatabase.FindAssets("t:Texture", profile.assetRoots)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => AssetImporter.GetAtPath(path) is TextureImporter)
                .Concat(exceptions.Keys)
                .Distinct(comparer)
                .OrderBy(path => path, StringComparer.Ordinal);
            var report = new UnityArtImportReport { mode = apply ? "apply" : "preview" };
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            var temporaryReport = reportPath + "." + Guid.NewGuid().ToString("N") + "~";
            try
            {
                using (var writer = new StreamWriter(new FileStream(temporaryReport, FileMode.CreateNew)))
                {
                    foreach (var path in paths)
                    {
                        exceptions.TryGetValue(path, out var exception);
                        var entry = Process(project, path, UnityArtImportRules.Match(profile.groups, path, exception?.groupId), exception, apply);
                        report.assets.Add(entry);
                        switch (entry.status)
                        {
                            case "pending": report.pending++; break;
                            case "changed": report.changed++; break;
                            case "unchanged": report.unchanged++; break;
                            case "excluded": report.excluded++; break;
                            case "unmatched": report.unmatched++; break;
                            case "failed": report.failed++; break;
                        }
                    }
                    report.scanned = report.assets.Count;
                    UnityArtImportProfile.CreateSerializer().Serialize(new JsonTextWriter(writer) { Formatting = Formatting.Indented }, report);
                }
                if (File.Exists(reportPath)) File.Replace(temporaryReport, reportPath, null);
                else File.Move(temporaryReport, reportPath);
            }
            finally
            {
                if (File.Exists(temporaryReport)) File.Delete(temporaryReport);
            }
            if (report.failed != 0)
                throw new InvalidOperationException($"Art import {report.mode} failed for {report.failed} asset(s). Report: {reportPath}");
            return report;
        }

        private static UnityArtImportEntry Process(string project, string path, UnityArtImportGroup group, UnityArtImportException exception, bool apply)
        {
            var entry = new UnityArtImportEntry { assetPath = path, groupId = group?.id };
            try
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                    throw new InvalidDataException("Asset exception does not resolve to a TextureImporter.");
                if (exception?.exclude == true)
                {
                    entry.status = "excluded";
                    return entry;
                }
                if (group == null && exception?.settings == null)
                {
                    entry.status = "unmatched";
                    return entry;
                }

                var plan = new UnityTextureImportPlan(importer, group?.settings, exception?.settings);
                entry.before = plan.Before;
                entry.expected = plan.Expected;
                if (!apply || !plan.HasChanges)
                {
                    entry.status = plan.HasChanges ? "pending" : "unchanged";
                    if (apply) entry.applied = plan.Before;
                    return entry;
                }

                ApplyAsset(project, path, importer, group?.settings, exception?.settings, plan, entry);
                entry.status = "changed";
            }
            catch (Exception error)
            {
                entry.status = "failed";
                entry.message = error.Message;
                Debug.LogError($"Art import profile failed for {path}: {error.Message}");
            }
            return entry;
        }

        private static void ApplyAsset(string project, string path, TextureImporter importer, UnityTextureImportSettings group,
            UnityTextureImportSettings exception, UnityTextureImportPlan plan, UnityArtImportEntry entry)
        {
            var geometry = SpriteImportGeometry.Capture(importer);
            var metadataPath = Path.Combine(project, path + ".meta");
            var originalMetadata = File.ReadAllBytes(metadataPath);
            try
            {
                plan.Apply();
                importer.SaveAndReimport();
                var reimported = AssetImporter.GetAtPath(path) as TextureImporter;
                if (reimported == null) throw new InvalidOperationException("TextureImporter disappeared during reimport.");
                entry.applied = new UnityTextureImportPlan(reimported, group, exception).Before;
                entry.spriteGeometryPreserved = JToken.DeepEquals(geometry, SpriteImportGeometry.Capture(reimported));
                if (entry.spriteGeometryPreserved == false)
                    throw new InvalidOperationException("Sprite geometry or GUID/local file ID changed on reimport.");
                if (!JToken.DeepEquals(plan.Expected, entry.applied))
                    throw new InvalidOperationException("Unity reimported the asset with settings different from the preview.");
            }
            catch (Exception importError)
            {
                try
                {
                    File.WriteAllBytes(metadataPath, originalMetadata);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    if (!File.ReadAllBytes(metadataPath).SequenceEqual(originalMetadata) ||
                        !(AssetImporter.GetAtPath(path) is TextureImporter restored) ||
                        !JToken.DeepEquals(geometry, SpriteImportGeometry.Capture(restored)) ||
                        !JToken.DeepEquals(plan.Before, new UnityTextureImportPlan(restored, group, exception).Before))
                        throw new InvalidOperationException("Reimport did not restore the original metadata.");
                }
                catch (Exception restoreError)
                {
                    throw new InvalidOperationException($"{importError.Message} Restoring the original .meta also failed: {restoreError.Message}", importError);
                }
                throw new InvalidOperationException($"{importError.Message} Restored the original .meta.", importError);
            }
        }
    }
}
