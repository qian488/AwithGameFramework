using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AwithGameFrame.AgentKit;

public sealed class ToolCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public ToolRecipe[] Tools { get; set; } = Array.Empty<ToolRecipe>();
}

public sealed class ToolRecipe
{
    public string Id { get; set; } = "";
    public string Executable { get; set; } = "";
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public string[] Inputs { get; set; } = Array.Empty<string>();
    public string[] Parameters { get; set; } = Array.Empty<string>();
    public bool RequiresApproval { get; set; }
    public int TimeoutSeconds { get; set; } = 600;
    public string Description { get; set; } = "";
}

public sealed class ExecutionPlan
{
    public string Id { get; set; } = "";
    public string Tool { get; set; } = "";
    public string Executable { get; set; } = "";
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public string WorkingDirectory { get; set; } = "";
    public bool RequiresApproval { get; set; }
    public int TimeoutSeconds { get; set; }
    public string StateDigest { get; set; } = "";
    public string RecipeDigest { get; set; } = "";
    public string Digest { get; set; } = "";
}

public sealed class Approval
{
    public string PlanDigest { get; set; } = "";
    public string Decision { get; set; } = "";
    public string By { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset ExpiresUtc { get; set; }
}

public sealed class ToolResult
{
    public string PlanId { get; set; } = "";
    public string Tool { get; set; } = "";
    public string StateDigest { get; set; } = "";
    public string RecipeDigest { get; set; } = "";
    public string Status { get; set; } = "running";
    public int? ExitCode { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public double DurationSeconds { get; set; }
    public string StdoutHash { get; set; } = "";
    public string StderrHash { get; set; } = "";
    public bool LogsTruncated { get; set; }
    public string? Error { get; set; }
}

public static class ToolRunner
{
    public const string CatalogPath = "agent/tools.json";

    public static ToolCatalog Catalog(Workspace workspace)
    {
        var catalog = TaskFiles.Load<ToolCatalog>(workspace.Resolve(CatalogPath, true));
        if (catalog.SchemaVersion != 1 || catalog.Tools.Length == 0 ||
            catalog.Tools.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Tools.Length)
            throw new InvalidOperationException("Invalid tool catalog version or duplicate tool IDs.");
        foreach (var tool in catalog.Tools)
        {
            if (!Regex.IsMatch(tool.Id, "^[a-z][a-z0-9-]*$") || tool.TimeoutSeconds < 1 || tool.TimeoutSeconds > 3600 ||
                (tool.Executable != "$powershell" && tool.Executable != "dotnet"))
                throw new InvalidOperationException("Invalid tool recipe: " + tool.Id);
            foreach (string input in tool.Inputs) workspace.Resolve(input, true);
        }
        return catalog;
    }

    public static ExecutionPlan Plan(Workspace workspace, TaskSpec task, string directory, string toolId, string? id = null)
    {
        var baseline = TaskFiles.Load<Baseline>(Path.Combine(directory, "baseline.json"));
        if (workspace.Head() != baseline.Head || workspace.Changes(task, baseline).Any(c => !c.InScope || c.Protected))
            throw new InvalidOperationException("Task HEAD or write boundary violated. Resolve the task scope before running tools.");
        if (!task.AllowedTools.Contains(toolId, StringComparer.Ordinal)) throw new InvalidOperationException("Tool not allowed by task: " + toolId);
        var recipe = Catalog(workspace).Tools.Single(t => t.Id == toolId);
        string[] arguments = recipe.Arguments.Select(argument => Regex.Replace(argument, @"\{([a-zA-Z][a-zA-Z0-9]*)\}", match =>
        {
            string name = match.Groups[1].Value;
            if (!recipe.Parameters.Contains(name, StringComparer.Ordinal) || !task.Parameters.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Missing declared tool parameter: " + name);
            return value;
        })).ToArray();
        var plan = new ExecutionPlan
        {
            Id = id ?? Guid.NewGuid().ToString("N"), Tool = toolId,
            Executable = recipe.Executable == "$powershell" ? (OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh") : "dotnet",
            Arguments = arguments, WorkingDirectory = workspace.Root, RequiresApproval = recipe.RequiresApproval,
            TimeoutSeconds = recipe.TimeoutSeconds, StateDigest = Evidence.State(workspace, task, directory),
            RecipeDigest = RecipeDigest(workspace, recipe)
        };
        plan.Digest = PlanDigest(plan);
        return plan;
    }

    public static string PlanDigest(ExecutionPlan plan) => Evidence.Digest(new
    {
        plan.Id, plan.Tool, plan.Executable, plan.Arguments, plan.WorkingDirectory, plan.RequiresApproval,
        plan.TimeoutSeconds, plan.StateDigest, plan.RecipeDigest
    });

    public static string RecipeDigest(Workspace workspace, ToolRecipe recipe) => Evidence.Digest(new
    {
        catalog = Workspace.HashFile(workspace.Resolve(CatalogPath, true)), recipe,
        inputs = recipe.Inputs.Select(path => new { path, hash = Workspace.HashFile(workspace.Resolve(path, true)) }).ToArray()
    });

    public static void RecordApproval(string directory, ExecutionPlan plan, string by, string decision, string reason)
    {
        if (string.IsNullOrWhiteSpace(by) || string.IsNullOrWhiteSpace(reason) || (decision != "approve" && decision != "deny"))
            throw new ArgumentException("Approval requires --by, --reason and --decision approve|deny.");
        var approval = new Approval { PlanDigest = plan.Digest, Decision = decision, By = by, Reason = reason,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        TaskFiles.Save(Path.Combine(directory, "plans", plan.Id + ".approval.json"), approval);
        Evidence.Event(directory, "approval", approval);
    }

    public static async Task<ToolResult> RunAsync(Workspace workspace, TaskSpec task, string directory, ExecutionPlan plan, CancellationToken cancellationToken)
    {
        var fresh = Plan(workspace, task, directory, plan.Tool, plan.Id);
        if (fresh.Digest != plan.Digest || PlanDigest(plan) != plan.Digest)
            throw new InvalidOperationException("Plan is stale or modified; create a new plan and review it.");
        string resultsDirectory = Path.Combine(directory, "runs");
        Directory.CreateDirectory(resultsDirectory);
        string resultPath = Path.Combine(resultsDirectory, plan.Id + ".json");
        if (File.Exists(resultPath)) throw new InvalidOperationException("Plan already consumed; create a new plan for each run.");
        var result = new ToolResult { PlanId = plan.Id, Tool = plan.Tool, StateDigest = plan.StateDigest,
            RecipeDigest = plan.RecipeDigest, StartedUtc = DateTimeOffset.UtcNow };
        if (plan.RequiresApproval)
        {
            string approvalPath = Path.Combine(directory, "plans", plan.Id + ".approval.json");
            Approval? approval = File.Exists(approvalPath) ? TaskFiles.Load<Approval>(approvalPath) : null;
            if (approval == null || approval.PlanDigest != plan.Digest || approval.Decision != "approve" || approval.ExpiresUtc <= DateTimeOffset.UtcNow)
            {
                result.Status = "denied";
                TaskFiles.Save(resultPath, result);
                Evidence.Event(directory, "tool-denied", result);
                return result;
            }
        }
        TaskFiles.Save(resultPath, result);
        Evidence.Event(directory, "tool-started", result);
        var clock = Stopwatch.StartNew();
        string stdout = Path.Combine(resultsDirectory, plan.Id + ".stdout.log");
        string stderr = Path.Combine(resultsDirectory, plan.Id + ".stderr.log");
        Workspace.RejectLinks(stdout);
        Workspace.RejectLinks(stderr);
        using var process = new Process { StartInfo = new ProcessStartInfo(plan.Executable)
            { WorkingDirectory = workspace.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        foreach (string argument in plan.Arguments) process.StartInfo.ArgumentList.Add(argument);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(plan.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        Task<bool>? output = null, errors = null;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            process.Start();
            output = Capture(process.StandardOutput, stdout, linked.Token);
            errors = Capture(process.StandardError, stderr, linked.Token);
            await process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(output, errors).WaitAsync(linked.Token);
            result.ExitCode = process.ExitCode;
            result.Status = process.ExitCode == 0 ? "passed" : "failed";
        }
        catch (OperationCanceledException)
        {
            result.Status = cancellationToken.IsCancellationRequested ? "cancelled" : "timed-out";
            await Stop(process);
        }
        catch (Exception error)
        {
            result.Status = "failed";
            result.Error = error.Message;
            linked.Cancel();
            await Stop(process);
        }
        finally
        {
            foreach (var capture in new[] { output, errors })
            {
                if (capture == null) continue;
                try { result.LogsTruncated |= await capture; }
                catch (OperationCanceledException) { result.LogsTruncated = true; }
                catch (Exception error) { result.Status = "failed"; result.Error = error.Message; }
            }
            result.DurationSeconds = clock.Elapsed.TotalSeconds;
            if (File.Exists(stdout)) result.StdoutHash = Workspace.HashFile(stdout);
            if (File.Exists(stderr)) result.StderrHash = Workspace.HashFile(stderr);
            TaskFiles.Save(resultPath, result);
            Evidence.Event(directory, "tool-completed", result);
        }
        return result;
    }

    private static async Task Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        catch (InvalidOperationException) { /* Process never started. */ }
    }

    private static async Task<bool> Capture(StreamReader reader, string path, CancellationToken token)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        var buffer = new char[4096];
        int kept = 0, count;
        bool truncated = false;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            int writing = Math.Min(count, Math.Max(0, 1048576 - kept));
            if (writing > 0) { await writer.WriteAsync(buffer.AsMemory(0, writing)); kept += writing; }
            if (writing != count) truncated = true;
        }
        return truncated;
    }
}
