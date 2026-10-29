using System.Text.RegularExpressions;

namespace AwithGameFrame.AgentKit;

public sealed class ModuleDescriptor
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string[] SourcePaths { get; set; } = Array.Empty<string>();
    public string[] EntryPoints { get; set; } = Array.Empty<string>();
    public string Readme { get; set; } = "";
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public string[] VerificationTools { get; set; } = Array.Empty<string>();
}

public sealed class ModuleEntry
{
    public string Path { get; init; } = "";
    public string Root { get; init; } = "";
    public ModuleDescriptor Definition { get; init; } = new();
}

/// <summary>Maintained package entry points, discovered without walking engine projects or caches.</summary>
public sealed class ModuleCatalog
{
    public ModuleEntry[] Modules { get; private init; } = Array.Empty<ModuleEntry>();
    public string Digest { get; private init; } = "";

    public ModuleEntry Get(string id) => Modules.FirstOrDefault(m => m.Definition.Id == id)
        ?? throw new InvalidDataException("Unknown module: " + id);

    public static ModuleCatalog Read(Workspace workspace)
    {
        var modules = new List<ModuleEntry>();
        foreach (string area in new[] { "src", "adapters", "samples", "templates" })
        {
            string directory = workspace.Resolve(area);
            if (!Directory.Exists(directory)) continue;
            foreach (string child in Directory.EnumerateDirectories(directory).OrderBy(p => p, StringComparer.Ordinal))
            {
                string root = Path.GetRelativePath(workspace.Root, child).Replace('\\', '/');
                string descriptorPath = root + "/module.json";
                string descriptor = workspace.Resolve(descriptorPath);
                if (!File.Exists(descriptor)) continue;
                var definition = TaskFiles.Load<ModuleDescriptor>(descriptor);
                Validate(workspace, root, definition);
                modules.Add(new ModuleEntry { Path = descriptorPath, Root = root, Definition = definition });
            }
        }
        var duplicate = modules.GroupBy(m => m.Definition.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new InvalidDataException("Duplicate module id: " + duplicate.Key);
        var known = modules.ToDictionary(m => m.Definition.Id, StringComparer.Ordinal);
        var tools = ToolRunner.Catalog(workspace).Tools.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            foreach (string dependency in module.Definition.Dependencies)
                if (!known.ContainsKey(dependency)) throw new InvalidDataException("Unknown dependency for module " + module.Definition.Id + ": " + dependency);
            foreach (string tool in module.Definition.VerificationTools)
                if (!tools.Contains(tool)) throw new InvalidDataException("Unknown verification tool for module " + module.Definition.Id + ": " + tool);
        }
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in known.Keys) Visit(id);
        return new ModuleCatalog
        {
            Modules = modules.OrderBy(m => m.Definition.Id, StringComparer.Ordinal).ToArray(),
            Digest = Evidence.Digest(modules.OrderBy(m => m.Path, StringComparer.Ordinal)
                .Select(m => new { m.Path, Hash = Workspace.HashFile(workspace.Resolve(m.Path, true)) }).ToArray())
        };

        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidDataException("Module dependency cycle at: " + id);
            foreach (string dependency in known[id].Definition.Dependencies) Visit(dependency);
            visiting.Remove(id);
            visited.Add(id);
        }
    }

    private static void Validate(Workspace workspace, string root, ModuleDescriptor module)
    {
        if (module.SchemaVersion != 1) throw new InvalidDataException("Unsupported module schemaVersion: " + module.SchemaVersion);
        if (string.IsNullOrEmpty(module.Id) || !Regex.IsMatch(module.Id, "^[a-z][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Module id must use lowercase ASCII letters, digits and hyphens: " + root);
        if (string.IsNullOrWhiteSpace(module.Purpose)) throw new InvalidDataException("Module purpose is required: " + root);
        ValidateList(module.SourcePaths, "sourcePaths", true);
        ValidateList(module.EntryPoints, "entryPoints", true);
        ValidateList(module.Dependencies, "dependencies", false);
        ValidateList(module.VerificationTools, "verificationTools", true);
        foreach (string path in module.SourcePaths) CheckPath(path, directory: true);
        foreach (string path in module.EntryPoints) CheckPath(path, directory: false);
        CheckPath(module.Readme, directory: false);

        void CheckPath(string path, bool directory)
        {
            string full = workspace.Resolve(path);
            if (!Workspace.Matches(path, new[] { root }))
                throw new InvalidDataException("Module paths must stay within " + root + ": " + path);
            if (directory ? !Directory.Exists(full) : !File.Exists(full))
                throw new InvalidDataException("Module " + (directory ? "directory" : "file") + " is invalid: " + path);
        }
    }

    private static void ValidateList(string[]? values, string name, bool required)
    {
        if (values == null || (required && values.Length == 0) || values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException("Module " + name + " must contain unique nonempty values" + (required ? " and cannot be empty." : "."));
    }
}
