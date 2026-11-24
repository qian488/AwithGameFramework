using System.Text.RegularExpressions;

namespace AwithGameFrame.AgentKit;

public sealed class CapabilityCatalogDocument
{
    public int SchemaVersion { get; set; } = 1;
    public CapabilityDescriptor[] Capabilities { get; set; } = Array.Empty<CapabilityDescriptor>();
}

public sealed class CapabilityDescriptor
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public string Owner { get; set; } = "";
    public string EntryPoint { get; set; } = "";
    public string[] Modes { get; set; } = Array.Empty<string>();
    public string[] Parameters { get; set; } = Array.Empty<string>();
    public string[] Checks { get; set; } = Array.Empty<string>();
    public bool MutatesProject { get; set; }
    public bool RequiresApproval { get; set; }
}

public sealed class CapabilityQuery
{
    public string? Kind { get; init; }
    public string? Mode { get; init; }
    public string? Query { get; init; }
}

public static class CapabilityCatalog
{
    public const string CatalogPath = "agent/capabilities.json";

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "workflow", "editor", "build", "verification", "content"
    };

    private static readonly HashSet<string> Modes = new(StringComparer.Ordinal)
    {
        "conversation", "background", "live-editor", "manual"
    };

    public static CapabilityCatalogDocument Read(Workspace workspace)
    {
        var catalog = TaskFiles.Load<CapabilityCatalogDocument>(workspace.Resolve(CatalogPath, true));
        if (catalog.SchemaVersion != 1 || catalog.Capabilities.Length == 0)
            throw new InvalidDataException("Invalid capability catalog version or empty capabilities.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var tools = ToolRunner.Catalog(workspace).Tools.Select(tool => tool.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var capability in catalog.Capabilities)
        {
            ValidateDescriptor(workspace, capability, ids, tools);
        }

        return catalog;
    }

    public static CapabilityDescriptor[] Find(Workspace workspace, CapabilityQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var catalog = Read(workspace);
        return catalog.Capabilities
            .Where(capability => string.IsNullOrWhiteSpace(query.Kind) || capability.Kind == query.Kind)
            .Where(capability => string.IsNullOrWhiteSpace(query.Mode) || capability.Modes.Contains(query.Mode, StringComparer.Ordinal))
            .Where(capability => string.IsNullOrWhiteSpace(query.Query) || Matches(capability, query.Query))
            .OrderBy(capability => capability.Kind, StringComparer.Ordinal)
            .ThenBy(capability => capability.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool Matches(CapabilityDescriptor capability, string query)
    {
        return capability.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            capability.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            capability.Owner.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            capability.EntryPoint.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateDescriptor(Workspace workspace, CapabilityDescriptor capability,
        HashSet<string> ids, HashSet<string> tools)
    {
        if (string.IsNullOrWhiteSpace(capability.Id) ||
            !Regex.IsMatch(capability.Id, "^[a-z][a-z0-9.-]*\\z", RegexOptions.CultureInvariant) ||
            !ids.Add(capability.Id))
            throw new InvalidDataException("Capability ids must be unique lowercase names: " + capability.Id);
        if (!Kinds.Contains(capability.Kind)) throw new InvalidDataException("Unknown capability kind: " + capability.Id);
        if (string.IsNullOrWhiteSpace(capability.Description)) throw new InvalidDataException("Capability description is required: " + capability.Id);
        if (string.IsNullOrWhiteSpace(capability.Owner)) throw new InvalidDataException("Capability owner is required: " + capability.Id);
        if (string.IsNullOrWhiteSpace(capability.EntryPoint)) throw new InvalidDataException("Capability entryPoint is required: " + capability.Id);
        if (capability.Modes.Length == 0 || capability.Modes.Any(mode => !Modes.Contains(mode)) ||
            capability.Modes.Distinct(StringComparer.Ordinal).Count() != capability.Modes.Length)
            throw new InvalidDataException("Capability modes must be known and unique: " + capability.Id);
        if (capability.Parameters.Any(string.IsNullOrWhiteSpace) ||
            capability.Parameters.Distinct(StringComparer.Ordinal).Count() != capability.Parameters.Length)
            throw new InvalidDataException("Capability parameters must be unique and nonempty: " + capability.Id);
        if (capability.Checks.Any(check => !tools.Contains(check)))
            throw new InvalidDataException("Capability references an unknown check: " + capability.Id);

        workspace.Resolve(capability.Owner, true);
    }
}
