using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text.RegularExpressions;

namespace AwithGameFrame.AgentKit;

public static class TaskFiles
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true
    };

    public static T Load<T>(string path)
    {
        Workspace.RejectLinks(path);
        var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path, new UTF8Encoding(false, true)), JsonOptions);
        if (value is null) throw new InvalidDataException("JSON document cannot be null: " + path);
        return value;
    }

    public static void Save<T>(string path, T data)
    {
        string absolute = Path.GetFullPath(path);
        Workspace.RejectLinks(absolute);
        string directory = Path.GetDirectoryName(absolute)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".agentkit-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(data, JsonOptions) + "\n");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            Workspace.RejectLinks(absolute);
            if (File.Exists(absolute)) File.Replace(temporary, absolute, null);
            else File.Move(temporary, absolute);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static TaskSpec ReadTask(Workspace ws, string taskPath)
    {
        ArgumentNullException.ThrowIfNull(ws);
        string relative = Path.IsPathRooted(taskPath) ? Path.GetRelativePath(ws.Root, taskPath) : taskPath;
        var task = Load<TaskSpec>(ws.Resolve(relative, mustExist: true));
        Validate(ws, task);
        return task;
    }

    public static string Create(Workspace ws, string templatePath, string id, string? objective)
    {
        ArgumentNullException.ThrowIfNull(ws);
        ValidateId(id);
        string template = Path.IsPathRooted(templatePath) ? Path.GetRelativePath(ws.Root, templatePath) : templatePath;
        var task = Load<TaskSpec>(ws.Resolve(template, mustExist: true));
        task.Id = id;
        if (objective != null) task.Objective = objective;
        Validate(ws, task);
        string createLock = ws.Resolve("artifacts/agent/.create.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(createLock)!);
        using var exclusiveCreate = new FileStream(createLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string relativeDirectory = "artifacts/agent/tasks/" + id;
        string directory = ws.Resolve(relativeDirectory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Task already exists: " + id);
        var baseline = ws.CaptureBaseline();
        string temporaryRelative = "artifacts/agent/tasks/.create-" + id + "-" + Guid.NewGuid().ToString("N");
        string temporary = ws.Resolve(temporaryRelative);
        Directory.CreateDirectory(temporary);
        try
        {
            Save(Path.Combine(temporary, "task.json"), task);
            Save(Path.Combine(temporary, "baseline.json"), baseline);
            ws.Resolve(relativeDirectory);
            ws.Resolve(temporaryRelative);
            // Directory.Move rejects an existing destination, including a task created concurrently.
            Directory.Move(temporary, directory);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                ws.Resolve(temporaryRelative);
                Directory.Delete(temporary, recursive: true);
            }
        }
        return relativeDirectory + "/task.json";
    }

    private static void Validate(Workspace ws, TaskSpec task)
    {
        if (task.SchemaVersion != 1) throw new InvalidDataException("Unsupported task schemaVersion: " + task.SchemaVersion);
        ValidateId(task.Id);
        if (string.IsNullOrWhiteSpace(task.Objective)) throw new InvalidDataException("Task objective is required.");
        if (task.Acceptance == null || task.Acceptance.Length == 0 || task.Acceptance.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("At least one nonempty acceptance criterion is required.");
        ValidatePaths(ws, task.ReadPaths, nameof(task.ReadPaths));
        ValidatePaths(ws, task.WritePaths, nameof(task.WritePaths));
        ValidatePaths(ws, task.ProtectedPaths, nameof(task.ProtectedPaths));
        if (task.ReadModules is { Length: > 0 })
        {
            if (task.ReadModules.Any(string.IsNullOrWhiteSpace) || task.ReadModules.Distinct(StringComparer.Ordinal).Count() != task.ReadModules.Length)
                throw new InvalidDataException("readModules must contain unique nonempty module IDs.");
            var modules = ModuleCatalog.Read(ws);
            foreach (string id in task.ReadModules) modules.Get(id);
        }
        if (task.AllowedTools == null || task.RequiredChecks == null || task.Parameters == null)
            throw new InvalidDataException("Task tool, check and parameter collections cannot be null.");
        if (task.RequiredChecks.Length == 0 || task.AllowedTools.Any(string.IsNullOrWhiteSpace) ||
            task.RequiredChecks.Any(string.IsNullOrWhiteSpace) || task.RequiredChecks.Except(task.AllowedTools, StringComparer.Ordinal).Any())
            throw new InvalidDataException("A task needs at least one required check, and every required check must be an allowed tool.");
        // Protected paths may overlap the write scope to preserve existing edits inside a task area.
    }

    private static void ValidatePaths(Workspace ws, IEnumerable<string>? paths, string name)
    {
        if (paths == null) throw new InvalidDataException("Task path collection cannot be null: " + name);
        foreach (string path in paths) ws.Resolve(path);
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(id, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("Task id must contain 1-64 ASCII letters, digits, underscores or hyphens and start with a letter or digit.");
    }
}
