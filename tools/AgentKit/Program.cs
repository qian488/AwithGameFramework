using System.Text.Json;

namespace AwithGameFrame.AgentKit;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancelled = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancelled.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return await Execute(args, cancelled.Token); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        finally { Console.CancelKeyPress -= handler; }
    }

    public static async Task<int> Execute(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is "help" or "--help")
        {
            Console.WriteLine("AgentKit: new --template <repo path> --id <id> [--objective <text>]\n" +
                "context|check|evaluate|report|monitor --task <repo-relative task.json>\n" +
                "plan --task <path> --tool <catalog id>\n" +
                "approve --task <path> --plan <id> --by <human> --decision approve|deny --reason <text>\n" +
                "run --task <path> --plan <id>\n" +
                "review --task <path> --by <human> --decision accept|reject --reason <text>\n" +
                "modules [--id <module id>] (list maintained package entry points)\n" +
                "contracts (validate maintained module/tool/task/template contracts)");
            return 0;
        }
        string command = args[0];
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length || !options.TryAdd(args[i][2..], args[i + 1]))
                throw new ArgumentException("Use unique --name value options.");
        }
        string Get(string name) => options.TryGetValue(name, out string? value) ? value : throw new ArgumentException("Missing --" + name);
        var workspace = new Workspace(Workspace.FindRoot());
        if (command == "contracts") { Contracts(workspace); return 0; }
        if (command == "modules")
        {
            var modules = ModuleCatalog.Read(workspace);
            if (options.TryGetValue("id", out string? moduleId)) Print(modules.Get(moduleId));
            else Print(modules);
            return 0;
        }
        if (command == "new")
        {
            string path = TaskFiles.Create(workspace, Get("template"), Get("id"), options.GetValueOrDefault("objective"));
            Console.WriteLine(path);
            return 0;
        }
        string taskPath = workspace.Resolve(Get("task"), true);
        var task = TaskFiles.ReadTask(workspace, taskPath);
        string expected = workspace.Resolve("artifacts/agent/tasks/" + task.Id + "/task.json", true);
        if (!string.Equals(taskPath, expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Operate on a task created by new under artifacts/agent/tasks/<id>/task.json.");
        string directory = Path.GetDirectoryName(taskPath)!;
        foreach (string name in new[] { ".lock", "context.json", "context.md", "plans", "runs", "events.jsonl", "review.json", "baseline.json", "report.md", "evaluation.json" })
            workspace.Resolve(Path.GetRelativePath(workspace.Root, Path.Combine(directory, name)));
        using var taskLock = new FileStream(Path.Combine(directory, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        switch (command)
        {
            case "context": Print(ContextBundle.Build(workspace, task, directory)); return 0;
            case "check":
                var baseline = TaskFiles.Load<Baseline>(Path.Combine(directory, "baseline.json"));
                var changes = workspace.Changes(task, baseline);
                Print(changes);
                return workspace.Head() == baseline.Head && changes.All(c => c.InScope && !c.Protected) ? 0 : 1;
            case "plan":
                var plan = ToolRunner.Plan(workspace, task, directory, Get("tool"));
                TaskFiles.Save(Path.Combine(directory, "plans", plan.Id + ".json"), plan);
                Evidence.Event(directory, "plan", plan);
                Print(plan); return 0;
            case "approve":
            case "run":
                string id = Get("plan");
                if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Plan ID must be a generated 32-character GUID.");
                string planPath = workspace.Resolve(Path.GetRelativePath(workspace.Root, Path.Combine(directory, "plans", id + ".json")), true);
                var existing = TaskFiles.Load<ExecutionPlan>(planPath);
                if (existing.Id != id) throw new InvalidOperationException("Plan identity mismatch.");
                if (command == "approve")
                {
                    if (existing.Digest != ToolRunner.Plan(workspace, task, directory, existing.Tool, id).Digest || ToolRunner.PlanDigest(existing) != existing.Digest)
                        throw new InvalidOperationException("Plan changed; create a fresh plan before approval.");
                    ToolRunner.RecordApproval(directory, existing, Get("by"), Get("decision"), Get("reason")); return 0;
                }
                var run = await ToolRunner.RunAsync(workspace, task, directory, existing, cancellationToken);
                Print(run); return run.Status == "passed" ? 0 : 1;
            case "evaluate":
            case "report":
            case "review":
                var evaluation = Evaluation.Evaluate(workspace, task, directory);
                if (command == "review")
                {
                    Evaluation.Review(directory, evaluation, Get("by"), Get("decision"), Get("reason"));
                    evaluation = Evaluation.Evaluate(workspace, task, directory);
                }
                if (command == "report") Evaluation.Report(evaluation, directory);
                Print(evaluation); return evaluation.ChecksPassed && evaluation.Status != "rejected" ? 0 : 1;
            case "monitor":
                string eventsPath = Path.Combine(directory, "events.jsonl");
                var events = File.Exists(eventsPath) ? File.ReadLines(eventsPath).Select(line =>
                {
                    using var document = JsonDocument.Parse(line);
                    return document.RootElement.Clone();
                }).ToList() : new();
                string runsPath = Path.Combine(directory, "runs");
                var results = Directory.Exists(runsPath) ? Directory.EnumerateFiles(runsPath, "*.json").Select(TaskFiles.Load<ToolResult>).ToList() : new();
                Print(new { task.Id, events = events.Count,
                    counts = events.GroupBy(e => e.GetProperty("kind").GetString()!).ToDictionary(g => g.Key, g => g.Count()),
                    tools = results.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count()),
                    totalToolSeconds = results.Sum(r => r.DurationSeconds),
                    unfinished = results.Where(r => r.Status == "running").Select(r => new { r.PlanId, r.Tool, r.StartedUtc }),
                    latest = events.TakeLast(10) }); return 0;
            default: throw new ArgumentException("Unknown command: " + command);
        }
    }

    public static void Contracts(Workspace workspace)
    {
        var catalog = ToolRunner.Catalog(workspace);
        ModuleCatalog.Read(workspace);
        foreach (string path in Directory.EnumerateFiles(workspace.Resolve("agent/templates/tasks", true), "*.json"))
        {
            var task = TaskFiles.ReadTask(workspace, path);
            foreach (string tool in task.AllowedTools.Concat(task.RequiredChecks))
                if (!catalog.Tools.Any(t => t.Id == tool)) throw new InvalidOperationException("Unknown template tool: " + tool);
            if (task.RequiredChecks.Except(task.AllowedTools, StringComparer.Ordinal).Any())
                throw new InvalidOperationException("Required checks must be allowed by the template.");
        }
        foreach (string path in new[] { "agent/README.md", "agent/templates/handoff.md", "agent/templates/review.md", "agent/templates/task-brief.md", "templates/portable-feature/README.md" })
            workspace.Resolve(path, true);
        Console.WriteLine("AGENT_CONTRACTS_PASSED");
    }

    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, TaskFiles.JsonOptions));
}
