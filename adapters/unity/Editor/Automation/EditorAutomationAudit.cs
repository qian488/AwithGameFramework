using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor
{
    internal static class EditorAutomationAudit
    {
        private const int MaxEntries = 500;
        private static string AuditPath => Path.Combine(EditorAutomation.AutomationDirectory, "audit.jsonl");

        internal static void Record(AutomationRequest request, AutomationResponse response, string resultFile)
        {
            try
            {
                EditorAutomation.RequireUnlinked(AuditPath);
                var entry = new AutomationAuditEntry
                {
                    id = response.id,
                    action = response.action,
                    batch = response.batch,
                    preview = request != null && request.preview,
                    save = request != null && request.save,
                    ok = response.ok,
                    outcome = response.outcome,
                    error = response.error,
                    scene = response.scene,
                    asset = request?.asset,
                    target = request?.target,
                    stepCount = request?.steps?.Length ?? 0,
                    completedUtc = response.completedUtc,
                    resultFile = Path.GetFileName(resultFile)
                };
                File.AppendAllText(AuditPath, JsonUtility.ToJson(entry) + "\n", new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogError("AwithGameFrame editor audit could not be written: " + exception.Message);
            }
        }

        internal static AutomationAuditEntry[] Read(int limit)
        {
            EditorAutomation.RequireUnlinked(AuditPath);
            if (!File.Exists(AuditPath)) return Array.Empty<AutomationAuditEntry>();
            limit = Mathf.Clamp(limit <= 0 ? 100 : limit, 1, MaxEntries);
            var entries = new List<AutomationAuditEntry>(limit);
            foreach (string line in File.ReadLines(AuditPath).Reverse().Take(limit).Reverse())
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = JsonUtility.FromJson<AutomationAuditEntry>(line);
                if (entry == null || string.IsNullOrEmpty(entry.id)) throw new InvalidDataException("The editor audit log contains an invalid entry.");
                entries.Add(entry);
            }
            return entries.ToArray();
        }
    }
}
