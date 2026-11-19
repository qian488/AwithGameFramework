using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor
{
    [InitializeOnLoad]
    public static class EditorAutomation
    {
        private const string MenuPath = "Tools/AwithGameFrame/Enable Editor Automation";
        private const string SessionKey = "AwithGameFrame.EditorAutomation.Enabled";
        private const string BatchArgument = "-awithEditorRequest";
        private static string DirectoryPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "AwithGameFrameAutomation");
        private static string MarkerPath => Path.Combine(DirectoryPath, "live");
        private static double nextPoll;

        static EditorAutomation()
        {
            if (Application.isBatchMode) return;
            EditorApplication.quitting += Disable;
            if (SessionState.GetBool(SessionKey, false)) SetLive(true, true);
        }

        [MenuItem(MenuPath)]
        private static void Toggle() => SetLive(!SessionState.GetBool(SessionKey, false));

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(MenuPath, SessionState.GetBool(SessionKey, false));
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static void Disable() => SetLive(false);

        private static void SetLive(bool enabled, bool resume = false)
        {
            EditorApplication.update -= Poll;
            SessionState.SetBool(SessionKey, false);
            if (enabled)
            {
                EnsureDirectory();
                EditorAutomationDiagnostics.Start(DirectoryPath, resume);
                using (var process = Process.GetCurrentProcess()) File.WriteAllText(MarkerPath, process.Id.ToString());
                SessionState.SetBool(SessionKey, true);
                EditorApplication.update += Poll;
            }
            else
            {
                if (File.Exists(MarkerPath))
                {
                    EnsureDirectory();
                    File.Delete(MarkerPath);
                }
                EditorAutomationDiagnostics.Stop();
            }
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("RunBatch requires a batch editor.");
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, BatchArgument);
            if (index < 0 || index + 1 >= arguments.Length || !Guid.TryParseExact(arguments[index + 1], "N", out var requestId))
                throw new ArgumentException("A valid automation request ID is required.");

            EnsureDirectory();
            if (!ProcessRequest(requestId.ToString("N"), true)) EditorApplication.Exit(1);
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;

            nextPoll = EditorApplication.timeSinceStartup + 0.2;
            try
            {
                EnsureDirectory();
                foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.request"))
                {
                    if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var requestId)) continue;
                    ProcessRequest(requestId.ToString("N"), false);
                    break;
                }
            }
            catch (Exception exception)
            {
                Disable();
                UnityEngine.Debug.LogException(exception);
            }
        }

        private static bool ProcessRequest(string requestId, bool batch)
        {
            var requestPath = Path.Combine(DirectoryPath, requestId + ".request");
            var workingPath = Path.Combine(DirectoryPath, requestId + ".working");
            if (!File.Exists(requestPath)) return false;
            RequireUnlinked(requestPath);
            try { File.Move(requestPath, workingPath); }
            catch (FileNotFoundException) { return false; }

            AutomationResponse response;
            AutomationRequest request = null;
            try
            {
                if (new FileInfo(workingPath).Length > 65536) throw new ArgumentException("The request exceeds 64 KB.");
                request = JsonUtility.FromJson<AutomationRequest>(File.ReadAllText(workingPath));
                if (request == null || request.version != 1 || request.id != requestId || request.batch != batch)
                    throw new ArgumentException("The request ID, protocol version or editor mode does not match.");
                if (request.expiresUtc < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    throw new InvalidOperationException("The automation request expired before execution.");
                EditorAutomationPolicy.Require(request);
                response = EditorAutomationActions.Execute(request);
                response.ok = response.error == null;
            }
            catch (Exception exception)
            {
                response = new AutomationResponse { error = exception.Message };
            }

            response.id = requestId;
            response.action = request?.action;
            response.completedUtc = DateTimeOffset.UtcNow.ToString("O");
            response.batch = batch;
            if (request != null && string.IsNullOrEmpty(response.scene)) response.scene = request.scene;
            var resultPath = Path.Combine(DirectoryPath, requestId + ".result");
            using (var stream = new FileStream(resultPath + ".tmp", FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(response, true));
            File.Move(resultPath + ".tmp", resultPath);
            File.Delete(workingPath);
            return response.ok;
        }

        private static void EnsureDirectory()
        {
            RequireUnlinked(Path.GetDirectoryName(DirectoryPath));
            RequireUnlinked(DirectoryPath);
            Directory.CreateDirectory(DirectoryPath);
            RequireUnlinked(MarkerPath);
        }

        internal static void RequireUnlinked(string path)
        {
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked automation state is not supported: " + path);
        }
    }
}
