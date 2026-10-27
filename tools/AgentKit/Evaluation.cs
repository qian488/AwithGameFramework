using System.Text;

namespace AwithGameFrame.AgentKit;

public sealed class FinalReview
{
    public string EvidenceDigest { get; set; } = "";
    public string Decision { get; set; } = "";
    public string By { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class EvaluationResult
{
    public string TaskId { get; set; } = "";
    public string Status { get; set; } = "failed";
    public bool ChecksPassed { get; set; }
    public string EvidenceDigest { get; set; } = "";
    public List<string> Failures { get; set; } = new();
    public List<Change> Changes { get; set; } = new();
    public List<ToolResult> Checks { get; set; } = new();
    public string[] ManualAcceptance { get; set; } = Array.Empty<string>();
}

public static class Evaluation
{
    public static EvaluationResult Evaluate(Workspace workspace, TaskSpec task, string directory)
    {
        var result = new EvaluationResult { TaskId = task.Id, ManualAcceptance = task.Acceptance };
        var baseline = TaskFiles.Load<Baseline>(Path.Combine(directory, "baseline.json"));
        if (workspace.Head() != baseline.Head) result.Failures.Add("HEAD changed since task creation; finish/rebase the task explicitly.");
        result.Changes = workspace.Changes(task, baseline);
        foreach (var change in result.Changes)
            if (!change.InScope || change.Protected) result.Failures.Add("Scope violation: " + change.Path);
        string state;
        try { state = Evidence.State(workspace, task, directory); }
        catch (Exception error) { result.Failures.Add(error.Message); return Finish(result, directory); }
        var catalog = ToolRunner.Catalog(workspace);
        string runs = Path.Combine(directory, "runs");
        var all = Directory.Exists(runs) ? Directory.EnumerateFiles(runs, "*.json").Select(TaskFiles.Load<ToolResult>).ToList() : new();
        foreach (string required in task.RequiredChecks)
        {
            var recipe = catalog.Tools.SingleOrDefault(t => t.Id == required);
            if (recipe == null || !task.AllowedTools.Contains(required, StringComparer.Ordinal))
            { result.Failures.Add("Required check is not an allowed catalog tool: " + required); continue; }
            var check = all.Where(r => r.Tool == required).OrderByDescending(r => r.StartedUtc).FirstOrDefault();
            if (check == null) { result.Failures.Add("Required check not executed: " + required); continue; }
            result.Checks.Add(check);
            if (check.Status != "passed" || check.StateDigest != state || check.RecipeDigest != ToolRunner.RecipeDigest(workspace, recipe))
                result.Failures.Add("Check failed, unfinished, or stale: " + required);
            foreach (var log in new[] { ("stdout", check.StdoutHash), ("stderr", check.StderrHash) })
            {
                string path = Path.Combine(runs, check.PlanId + "." + log.Item1 + ".log");
                workspace.Resolve(Path.GetRelativePath(workspace.Root, path));
                if (check.Status == "passed" && (!File.Exists(path) || Workspace.HashFile(path) != log.Item2))
                    result.Failures.Add("Check log changed or missing: " + required + "/" + log.Item1);
            }
        }
        result.EvidenceDigest = Evidence.Digest(new { state, result.Checks });
        return Finish(result, directory);
    }

    private static EvaluationResult Finish(EvaluationResult result, string directory)
    {
        result.ChecksPassed = result.Failures.Count == 0;
        result.Status = result.ChecksPassed ? "ready-for-review" : "failed";
        string reviewPath = Path.Combine(directory, "review.json");
        if (result.ChecksPassed && File.Exists(reviewPath))
        {
            var review = TaskFiles.Load<FinalReview>(reviewPath);
            if (review.EvidenceDigest == result.EvidenceDigest)
                result.Status = review.Decision == "accept" ? "accepted" : "rejected";
        }
        TaskFiles.Save(Path.Combine(directory, "evaluation.json"), result);
        Evidence.Event(directory, "evaluation", new { result.Status, result.ChecksPassed, result.EvidenceDigest, result.Failures });
        return result;
    }

    public static void Review(string directory, EvaluationResult result, string by, string decision, string reason)
    {
        if (!result.ChecksPassed) throw new InvalidOperationException("Fix failed checks before final review.");
        if (string.IsNullOrWhiteSpace(by) || string.IsNullOrWhiteSpace(reason) || (decision != "accept" && decision != "reject"))
            throw new ArgumentException("Review requires --by, --reason and --decision accept|reject.");
        var review = new FinalReview { EvidenceDigest = result.EvidenceDigest, By = by, Reason = reason, Decision = decision };
        TaskFiles.Save(Path.Combine(directory, "review.json"), review);
        Evidence.Event(directory, "review", review);
    }

    public static string Report(EvaluationResult result, string directory)
    {
        var report = new StringBuilder("# " + result.TaskId + "\n\nStatus: **" + result.Status + "**\n\n");
        report.AppendLine("Machine checks and human acceptance are recorded separately. Paths below are changes since task creation, including pre-existing dirty files only when they drifted.\n");
        report.AppendLine("## Changes\n");
        foreach (var change in result.Changes) report.AppendLine("- `" + change.Path + "`: " + change.Kind + (change.Protected || !change.InScope ? " (scope violation)" : ""));
        report.AppendLine("\n## Checks\n");
        foreach (var check in result.Checks) report.AppendLine("- " + check.Tool + ": " + check.Status + " (exit " + check.ExitCode + ", " + check.DurationSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "s)");
        foreach (string failure in result.Failures) report.AppendLine("- FAILED: " + failure);
        report.AppendLine("\n## Human acceptance\n");
        foreach (string criterion in result.ManualAcceptance) report.AppendLine("- " + criterion);
        string path = Path.Combine(directory, "report.md");
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        return path;
    }
}
