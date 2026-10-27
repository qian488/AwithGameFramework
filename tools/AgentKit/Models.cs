using System;
using System.Collections.Generic;

namespace AwithGameFrame.AgentKit;

public sealed class TaskSpec
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Objective { get; set; } = "";
    public string[] ReadPaths { get; set; } = Array.Empty<string>();
    public string[] WritePaths { get; set; } = Array.Empty<string>();
    public string[] ProtectedPaths { get; set; } = Array.Empty<string>();
    public string[] AllowedTools { get; set; } = Array.Empty<string>();
    public string[] RequiredChecks { get; set; } = Array.Empty<string>();
    public string[] Acceptance { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
}

public sealed class FileState
{
    public string Status { get; set; } = "";
    public string Hash { get; set; } = "";
    public string IndexHash { get; set; } = "";
    public bool Exists { get; set; }
}

public sealed class Baseline
{
    public string Head { get; set; } = "";
    public Dictionary<string, FileState> Files { get; set; } = new(StringComparer.Ordinal);
}

public sealed class Change
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool InScope { get; set; }
    public bool Protected { get; set; }
}
