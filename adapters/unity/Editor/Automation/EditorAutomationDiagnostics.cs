using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor
{
    internal static class EditorAutomationDiagnostics
    {
        private const int Capacity = 100;
        private const int MaxMessageLength = 2048;
        private static string destination;
        private static DiagnosticSnapshot snapshot;
        private static bool pending;
        private static double nextPublish;

        internal static void Start(string directory, bool resume)
        {
            destination = Path.Combine(directory, "diagnostics.json");
            using var process = Process.GetCurrentProcess();
            var processStart = process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
            snapshot = resume ? ReadPrevious(process.Id, processStart) : null;
            snapshot ??= new DiagnosticSnapshot { processId = process.Id, processStart = processStart };
            if (EditorApplication.isCompiling && snapshot.compilationState != "compiling") BeginCompilation(null);
            Application.logMessageReceived += Log;
            CompilationPipeline.compilationStarted += BeginCompilation;
            CompilationPipeline.assemblyCompilationFinished += AssemblyFinished;
            CompilationPipeline.compilationFinished += FinishCompilation;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Publish;
            pending = true;
            Publish();
        }

        internal static void Stop()
        {
            Application.logMessageReceived -= Log;
            CompilationPipeline.compilationStarted -= BeginCompilation;
            CompilationPipeline.assemblyCompilationFinished -= AssemblyFinished;
            CompilationPipeline.compilationFinished -= FinishCompilation;
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= Publish;
            if (destination != null) File.Delete(destination);
            destination = null;
            snapshot = null;
            pending = false;
        }

        private static DiagnosticSnapshot ReadPrevious(int processId, string processStart)
        {
            if (!File.Exists(destination)) return null;
            try
            {
                var previous = JsonUtility.FromJson<DiagnosticSnapshot>(File.ReadAllText(destination));
                return previous?.version == 1 && previous.processId == processId && previous.processStart == processStart
                    ? previous : null;
            }
            catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
        }

        private static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            Append(snapshot.logs, new DiagnosticEntry
            {
                timestampUtc = UtcNow(), severity = type == LogType.Warning ? "warning" : "error", message = Clip(message)
            }, ref snapshot.logsTruncated);
            pending = true;
        }

        private static void BeginCompilation(object context)
        {
            snapshot.compilationState = "compiling";
            snapshot.compilerMessages.Clear();
            snapshot.compilerMessagesTruncated = false;
            snapshot.compilationErrors = 0;
            pending = true;
            Publish();
        }

        private static void AssemblyFinished(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var message in messages)
            {
                var isError = message.type == CompilerMessageType.Error;
                if (isError) snapshot.compilationErrors++;
                Append(snapshot.compilerMessages, new DiagnosticEntry
                {
                    timestampUtc = UtcNow(), severity = isError ? "error" : "warning", message = Clip(message.message),
                    file = Clip(message.file), line = message.line, column = message.column
                }, ref snapshot.compilerMessagesTruncated);
            }
            pending = true;
            Publish();
        }

        private static void FinishCompilation(object context)
        {
            snapshot.compilationState = snapshot.compilationErrors == 0 ? "succeeded" : "failed";
            pending = true;
            Publish();
        }

        private static void Append(List<DiagnosticEntry> entries, DiagnosticEntry entry, ref bool truncated)
        {
            if (entries.Count == Capacity)
            {
                entries.RemoveAt(0);
                truncated = true;
            }
            entries.Add(entry);
        }

        private static void Update()
        {
            if (!pending || EditorApplication.timeSinceStartup < nextPublish) return;
            nextPublish = EditorApplication.timeSinceStartup + 0.25;
            Publish();
        }

        private static void Publish()
        {
            if (!pending) return;
            snapshot.updatedUtc = UtcNow();
            var temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(snapshot));
                if (File.Exists(destination)) File.Replace(temporaryPath, destination, null);
                else File.Move(temporaryPath, destination);
                pending = false;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string UtcNow() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        private static string Clip(string value) => value?.Length > MaxMessageLength ? value.Substring(0, MaxMessageLength) : value;

        [Serializable]
        private sealed class DiagnosticSnapshot
        {
            public int version = 1;
            public int processId;
            public string processStart;
            public string updatedUtc;
            public string compilationState = "not-observed";
            public int compilationErrors;
            public bool logsTruncated;
            public bool compilerMessagesTruncated;
            public List<DiagnosticEntry> logs = new List<DiagnosticEntry>();
            public List<DiagnosticEntry> compilerMessages = new List<DiagnosticEntry>();
        }

        [Serializable]
        private sealed class DiagnosticEntry
        {
            public string timestampUtc;
            public string severity;
            public string message;
            public string file;
            public int line;
            public int column;
        }
    }
}
