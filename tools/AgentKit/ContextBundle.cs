using System.Text;

namespace AwithGameFrame.AgentKit;

public sealed class ContextEntry
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public long Bytes { get; set; }
}

public sealed class ContextManifest
{
    public List<ContextEntry> Files { get; set; } = new();
    public string TaskDigest { get; set; } = "";
    public string Digest { get; set; } = "";
}

public static class ContextBundle
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".md", ".cs", ".json", ".csproj", ".props", ".targets", ".ps1", ".yml", ".yaml", ".asmdef", ".txt" };
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "artifacts", "bin", "obj", ".godot", "Library", "Temp", "node_modules", "Plugins", "Packages" };

    public static ContextManifest Build(Workspace workspace, TaskSpec task, string directory)
    {
        var manifest = Capture(workspace, task, out string bundle);
        TaskFiles.Save(Path.Combine(directory, "context.json"), manifest);
        File.WriteAllText(Path.Combine(directory, "context.md"), bundle, new UTF8Encoding(false));
        Evidence.Event(directory, "context", new { manifest.Digest, files = manifest.Files.Count, bytes = manifest.Files.Sum(f => f.Bytes) });
        return manifest;
    }

    private static ContextManifest Capture(Workspace workspace, TaskSpec task, out string textBundle)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string required in new[] { "AGENTS.md", "README.md", "GUIDE.md" })
            if (File.Exists(workspace.Resolve(required))) paths.Add(required);
        foreach (string requested in task.ReadPaths)
        {
            string full = workspace.Resolve(requested, true);
            foreach (string parent in Ancestors(requested.Replace('\\', '/')))
            {
                string instructions = parent + "/AGENTS.md";
                if (File.Exists(workspace.Resolve(instructions))) paths.Add(instructions);
            }
            Collect(workspace, full, paths);
        }
        var manifest = new ContextManifest { TaskDigest = Evidence.Digest(task) };
        var bundle = new StringBuilder("# Task context\n\n" + task.Objective + "\n");
        long total = 0;
        foreach (string path in paths)
        {
            string full = workspace.Resolve(path, true);
            long size = new FileInfo(full).Length;
            total += size;
            if (size > 65536 || total > 524288 || paths.Count > 128)
                throw new InvalidOperationException("Context budget exceeded (128 files, 64 KiB/file, 512 KiB total). Narrow readPaths; nothing was silently truncated.");
            var content = File.ReadAllBytes(full);
            string text = new UTF8Encoding(false, true).GetString(content);
            if (text.Contains('\0')) throw new InvalidOperationException("Binary context is unsupported: " + path);
            manifest.Files.Add(new ContextEntry { Path = path, Hash = Evidence.Hash(content), Bytes = size });
            bundle.Append("\n## ").Append(path).Append("\n\n").Append(text).Append('\n');
        }
        manifest.Digest = Evidence.Digest(new { manifest.TaskDigest, manifest.Files });
        textBundle = bundle.ToString();
        return manifest;
    }

    public static ContextManifest ReadFresh(Workspace workspace, string directory, TaskSpec? task = null)
    {
        var manifest = TaskFiles.Load<ContextManifest>(Path.Combine(directory, "context.json"));
        task ??= TaskFiles.ReadTask(workspace, Path.Combine(directory, "task.json"));
        var current = Capture(workspace, task, out _);
        if (manifest.Digest != Evidence.Digest(new { manifest.TaskDigest, manifest.Files }) || manifest.Digest != current.Digest)
            throw new InvalidOperationException("Task or context files changed. Run context again before planning verification.");
        return manifest;
    }

    private static void Collect(Workspace workspace, string full, SortedSet<string> paths)
    {
        string relative = Path.GetRelativePath(workspace.Root, full).Replace('\\', '/');
        if (relative.Split('/').Any(Excluded.Contains)) throw new InvalidOperationException("Excluded context path: " + relative);
        if (Directory.Exists(full))
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(full).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (Excluded.Contains(Path.GetFileName(child))) continue;
                workspace.Resolve(Path.GetRelativePath(workspace.Root, child));
                if (Directory.Exists(child) || IsText(child)) Collect(workspace, child, paths);
            }
        }
        else
        {
            if (!IsText(full)) throw new InvalidOperationException("Unsupported or sensitive context file: " + relative);
            paths.Add(relative);
            if (paths.Count > 128) throw new InvalidOperationException("Context file budget exceeded; narrow readPaths.");
        }
    }

    private static bool IsText(string path) => Extensions.Contains(Path.GetExtension(path)) &&
        !Path.GetFileName(path).Contains("secret", StringComparison.OrdinalIgnoreCase) &&
        !Path.GetFileName(path).Contains("credential", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Ancestors(string path)
    {
        string? parent = Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar));
        while (!string.IsNullOrEmpty(parent))
        {
            yield return parent.Replace('\\', '/');
            parent = Path.GetDirectoryName(parent);
        }
    }
}
