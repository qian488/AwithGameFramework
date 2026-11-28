using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor.Content
{
    public static class UnityArtImportProfileMenu
    {
        private const string ProfileMenu = "Tools/AwithGameFrame/Content/Art Import Profile/";

        [MenuItem(ProfileMenu + "Preview...")]
        public static void PreviewMenu()
        {
            RunMenu(false);
        }

        [MenuItem(ProfileMenu + "Apply...")]
        public static void ApplyMenu()
        {
            RunMenu(true);
        }

        public static void RunBatch()
        {
            try
            {
                var arguments = Environment.GetCommandLineArgs();
                var profilePath = Argument(arguments, "-awith-art-profile");
                var reportPath = Argument(arguments, "-awith-art-profile-output");
                var mode = Argument(arguments, "-awith-art-profile-mode");
                var report = mode switch
                {
                    "apply" => UnityArtImportProfileProcessor.Apply(profilePath, reportPath),
                    "preview" => UnityArtImportProfileProcessor.Preview(profilePath, reportPath),
                    _ => throw new ArgumentException("-awith-art-profile-mode must be preview or apply.")
                };
                Debug.Log($"UNITY_ART_IMPORT_PROFILE_PASSED mode={report.mode} scanned={report.scanned} pending={report.pending} changed={report.changed}");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void RunMenu(bool apply)
        {
            var profile = EditorUtility.OpenFilePanel("Select Unity art import profile", Directory.GetParent(Application.dataPath).FullName, "json");
            if (string.IsNullOrEmpty(profile)) return;
            if (apply && !EditorUtility.DisplayDialog("Apply art import profile", "Apply importer settings from this profile? A report will be written after reimport.", "Apply", "Cancel")) return;
            var report = EditorUtility.SaveFilePanel("Save art import report", Directory.GetParent(Application.dataPath).FullName, apply ? "art-import-report.json" : "art-import-preview.json", "json");
            if (string.IsNullOrEmpty(report)) return;
            try
            {
                var result = apply ? UnityArtImportProfileProcessor.Apply(profile, report) : UnityArtImportProfileProcessor.Preview(profile, report);
                EditorUtility.DisplayDialog("Art import profile", $"Pending {result.pending}, changed {result.changed}, scanned {result.scanned}.\n{report}", "OK");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("Art import profile failed", error.Message, "OK");
            }
        }

        private static string Argument(string[] arguments, string name)
        {
            var index = Array.IndexOf(arguments, name);
            if (index < 0 || index + 1 >= arguments.Length || arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Missing value for " + name);
            return arguments[index + 1];
        }
    }
}
