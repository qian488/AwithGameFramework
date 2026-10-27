using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AwithGameFrame.AgentKit;

public sealed class Workspace
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public Workspace(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A repository root is required.", nameof(root));
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException("Repository root does not exist: " + Root);
        RejectLinks(Root);
    }

    public string Root { get; }

    public string Resolve(string relative, bool mustExist = false)
    {
        string normalized = NormalizeRelative(relative);
        string path = Path.GetFullPath(Path.Combine(Root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsBelow(path, Root)) throw new ArgumentException("Path escapes the repository: " + relative, nameof(relative));
        RejectLinks(path);
        if (mustExist && !File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("Repository path does not exist: " + normalized, path);
        return path;
    }

    public static string FindRoot(string? start = null)
    {
        string directory = Path.GetFullPath(start ?? Environment.CurrentDirectory);
        if (File.Exists(directory)) directory = Path.GetDirectoryName(directory)!;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(RunGit(directory, "rev-parse", "--show-toplevel").TrimEnd('\r', '\n')));
    }

    public string Head() => RunGit(Root, "rev-parse", "--verify", "HEAD").TrimEnd('\r', '\n');

    public Baseline CaptureBaseline()
    {
        var baseline = new Baseline { Head = Head() };
        var dirty = DirtyPaths();
        var indexHashes = IndexHashes(dirty.Keys);
        foreach (var pair in dirty) baseline.Files.Add(pair.Key, State(pair.Key, pair.Value, indexHashes.GetValueOrDefault(pair.Key, "")));
        return baseline;
    }

    public List<Change> Changes(TaskSpec task, Baseline baseline)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Files == null) throw new InvalidDataException("Baseline files are required.");
        var dirty = DirtyPaths();
        // Git paths are case-sensitive identities even on Windows; preserve both ends of a case-only rename.
        var paths = new HashSet<string>(baseline.Files.Keys, StringComparer.Ordinal);
        paths.UnionWith(dirty.Keys);
        var indexHashes = IndexHashes(paths);
        var changes = new List<Change>();
        foreach (string path in paths.OrderBy(value => value, StringComparer.Ordinal))
        {
            baseline.Files.TryGetValue(path, out var before);
            var after = State(path, dirty.TryGetValue(path, out var status) ? status : "", indexHashes.GetValueOrDefault(path, ""));
            if (before != null && before.Exists == after.Exists && before.Hash == after.Hash && before.IndexHash == after.IndexHash && before.Status == after.Status) continue;
            if (before == null && !dirty.ContainsKey(path)) continue;
            string kind = before == null
                ? (!after.Exists ? "deleted" : after.Status == "??" || after.Status.Contains('A') ? "added" : "modified")
                : before.Exists && !after.Exists ? "deleted"
                : !before.Exists && after.Exists ? "added"
                : before.Hash != after.Hash || before.IndexHash != after.IndexHash ? "modified" : "status";
            changes.Add(new Change
            {
                Path = path,
                Kind = kind,
                InScope = Matches(path, task.WritePaths),
                Protected = Matches(path, task.ProtectedPaths)
            });
        }
        return changes;
    }

    public static bool Matches(string path, IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        string normalized = NormalizeRelative(path);
        foreach (string root in roots)
        {
            string candidate = NormalizeRelative(root);
            if (string.Equals(normalized, candidate, PathComparison) || normalized.StartsWith(candidate + "/", PathComparison)) return true;
        }
        return false;
    }

    public static string HashFile(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
            string target = entry.LinkTarget ?? throw new IOException("Cannot snapshot this reparse point: " + path);
            return "link:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target))).ToLowerInvariant();
        }
        if ((attributes & FileAttributes.Directory) != 0)
            throw new IOException("Cannot snapshot a dirty directory or submodule without recursively reading it: " + path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static string NormalizeRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) throw new ArgumentException("A nonempty repository-relative path is required.", nameof(relative));
        string path = relative.Replace('\\', '/');
        if (Path.IsPathRooted(relative) || path.StartsWith('/') || path.Contains(':') || path.IndexOf('\0') >= 0)
            throw new ArgumentException("Only repository-relative paths are allowed: " + relative, nameof(relative));
        path = path.TrimEnd('/');
        string[] parts = path.Split('/');
        if (parts.Any(part => part.Length == 0 || part == "." || part == ".." ||
            part.Equals(".git", StringComparison.OrdinalIgnoreCase) || part.EndsWith('.') || part.EndsWith(' ')))
            throw new ArgumentException("Path contains a forbidden segment: " + relative, nameof(relative));
        return path;
    }

    internal static void RejectLinks(string path, bool allowFinalLink = false)
    {
        string absolute = Path.GetFullPath(path);
        string filesystemRoot = Path.GetPathRoot(absolute)!;
        string current = filesystemRoot;
        string[] parts = absolute.Substring(filesystemRoot.Length).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < parts.Length; index++)
        {
            current = Path.Combine(current, parts[index]);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            if ((attributes & FileAttributes.ReparsePoint) != 0 && !(allowFinalLink && index == parts.Length - 1))
                throw new IOException("Symbolic links and reparse points are not allowed in repository paths: " + current);
        }
    }

    private FileState State(string relative, string status, string indexHash)
    {
        string normalized = NormalizeRelative(relative);
        string path = Path.Combine(Root, normalized.Replace('/', Path.DirectorySeparatorChar));
        RejectLinks(path, allowFinalLink: true);
        bool exists;
        try { File.GetAttributes(path); exists = true; }
        catch (FileNotFoundException) { exists = false; }
        catch (DirectoryNotFoundException) { exists = false; }
        return new FileState { Status = status, Exists = exists, Hash = exists ? HashFile(path) : "", IndexHash = indexHash };
    }

    private Dictionary<string, string> IndexHashes(IEnumerable<string> paths)
    {
        var records = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var batch in paths.Select(NormalizeRelative).Distinct(StringComparer.Ordinal).Chunk(32))
        {
            var arguments = new List<string> { "--literal-pathspecs", "ls-files", "--stage", "-z", "--" };
            arguments.AddRange(batch);
            foreach (string record in RunGit(Root, arguments.ToArray()).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = record.IndexOf('\t');
                if (separator < 0) throw new InvalidDataException("Unexpected Git index record.");
                string path = NormalizeRelative(record.Substring(separator + 1));
                if (!records.TryGetValue(path, out var entries)) records.Add(path, entries = new List<string>());
                entries.Add(record.Substring(0, separator));
            }
        }
        return records.ToDictionary(pair => pair.Key,
            pair => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", pair.Value.OrderBy(value => value, StringComparer.Ordinal))))).ToLowerInvariant(),
            StringComparer.Ordinal);
    }

    private Dictionary<string, string> DirtyPaths()
    {
        string output = RunGit(Root, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        string[] records = output.Split('\0');
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < records.Length; index++)
        {
            string record = records[index];
            if (record.Length == 0 && index == records.Length - 1) continue;
            if (record.Length < 4 || record[2] != ' ') throw new InvalidDataException("Unexpected Git porcelain record.");
            string status = record.Substring(0, 2);
            paths[NormalizeRelative(record.Substring(3))] = status;
            if (status.Contains('R') || status.Contains('C'))
            {
                if (++index >= records.Length || records[index].Length == 0) throw new InvalidDataException("Git rename/copy source is missing.");
                paths[NormalizeRelative(records[index])] = status;
            }
        }
        return paths;
    }

    private static bool IsBelow(string path, string root)
    {
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathComparison);
    }

    private static string RunGit(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true)
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start Git.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string text = output.GetAwaiter().GetResult();
        string diagnostic = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new IOException("Git failed: " + diagnostic.Trim());
        return text;
    }
}
