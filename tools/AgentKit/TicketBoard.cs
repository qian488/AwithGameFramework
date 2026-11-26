namespace AwithGameFrame.AgentKit;

public sealed class TicketSpec
{
    public string Id { get; set; } = "";
    public string Owner { get; set; } = "";
    public int Priority { get; set; }
    public string[] DependsOn { get; set; } = Array.Empty<string>();
    public string? ExternalRef { get; set; }
    public TaskSpec Scope { get; set; } = new();
}

public sealed class BoardSpec
{
    public int Version { get; set; }
    public string Id { get; set; } = "";
    public string Brief { get; set; } = "";
    public string DecisionSource { get; set; } = "";
    public TicketSpec[] Tickets { get; set; } = Array.Empty<TicketSpec>();
}

public sealed class TicketState
{
    public string TaskId { get; set; } = "";
    public bool Started { get; set; }
    public string? TriageNote { get; set; }
    public TicketHandoff? Handoff { get; set; }
}

public sealed record TicketHandoff(string Status, string EvidenceDigest, string Report, DateTimeOffset AtUtc)
{
    public string[] ChangedPaths { get; init; } = Array.Empty<string>();
    public string BaseCommit { get; init; } = "";
}
public sealed record TicketResult(object View, TicketHandoff? Handoff = null);

public sealed class TicketBoard
{
    public BoardSpec Spec { get; set; } = new();
    public string BriefHash { get; set; } = "";
    public Dictionary<string, TicketState> State { get; set; } = new(StringComparer.Ordinal);

    public static TicketBoard Import(Workspace workspace, string path)
    {
        var spec = TaskFiles.Load<BoardSpec>(workspace.Resolve(path, true));
        TaskFiles.ValidateId(spec.Id);
        if (spec.Version != 1 || string.IsNullOrWhiteSpace(spec.DecisionSource) || spec.Tickets == null || spec.Tickets.Length is < 1 or > 100)
            throw new InvalidDataException("Supply a version 1 board, the actual specification decision source, and 1 to 100 tickets.");
        string brief = workspace.Resolve(spec.Brief, true);
        if (!File.Exists(brief)) throw new InvalidDataException("The brief must be a repository file.");

        var board = new TicketBoard { Spec = spec, BriefHash = Workspace.HashFile(brief) };
        var catalog = ToolRunner.Catalog(workspace);
        var remaining = new Dictionary<string, TicketSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var ticket in spec.Tickets)
        {
            if (ticket == null || ticket.Scope == null || ticket.DependsOn == null || ticket.Priority is < 0 or > 3)
                throw new InvalidDataException("Each ticket needs a scope, dependencies and a priority from 0 to 3.");
            TaskFiles.ValidateId(ticket.Id);
            if (!remaining.TryAdd(ticket.Id, ticket)) throw new InvalidDataException("Duplicate ticket ID: " + ticket.Id);
            ticket.Scope.Id = "ticket-" + Guid.NewGuid().ToString("N");
            TaskFiles.Validate(workspace, ticket.Scope);
            ticket.Scope.ReadPaths = ticket.Scope.ReadPaths.Append(spec.Brief).Distinct(StringComparer.Ordinal).ToArray();
            if (ticket.Scope.RequiredChecks.Length == 0 || ticket.Scope.AllowedTools.Any(tool => !catalog.Tools.Any(recipe => recipe.Id == tool)))
                throw new InvalidDataException("Each slice needs a required catalog check and only known tools: " + ticket.Id);
            board.State.Add(ticket.Id, new TicketState { TaskId = ticket.Scope.Id });
        }

        foreach (var ticket in spec.Tickets)
            if (ticket.DependsOn.Distinct(StringComparer.Ordinal).Count() != ticket.DependsOn.Length ||
                ticket.DependsOn.Any(parent => parent == null || !board.State.ContainsKey(parent)))
                throw new InvalidDataException("Unknown or duplicate dependency: " + ticket.Id);
        while (remaining.Count > 0)
        {
            var unblocked = remaining.Values.Where(ticket => ticket.DependsOn.All(parent => !remaining.ContainsKey(parent))).ToArray();
            if (unblocked.Length == 0) throw new InvalidDataException("Ticket dependencies contain a cycle.");
            foreach (var ticket in unblocked) remaining.Remove(ticket.Id);
        }

        string boardPath = BoardPath(workspace, spec.Id);
        using var boardLock = Lock(workspace, boardPath);
        if (File.Exists(boardPath)) throw new IOException("Board already exists: " + spec.Id);
        TaskFiles.Save(boardPath, board);
        return board;
    }

    public static TicketResult Execute(Workspace workspace, string command, string id, Func<string, string> get)
    {
        string boardPath = BoardPath(workspace, id);
        using var boardLock = Lock(workspace, boardPath);
        var board = TaskFiles.Load<TicketBoard>(boardPath);
        if (board.Spec.Id != id) throw new InvalidDataException("Board ID did not match its file.");
        if (command == "board-show") return new TicketResult(board.View());
        if (command == "board-tracker") return new TicketResult(board.CompareTracker(workspace, get("snapshot")));

        string ticketId = get("ticket");
        var ticket = board.Spec.Tickets.SingleOrDefault(item => item.Id == ticketId) ?? throw new ArgumentException("Unknown ticket: " + ticketId);
        var state = board.State[ticket.Id];
        string taskPath = "artifacts/agent/tasks/" + state.TaskId + "/task.json";
        switch (command)
        {
            case "ticket-triage":
                string owner = get("owner");
                string note = get("note");
                if (!int.TryParse(get("priority"), out int priority) || priority is < 0 or > 3 || string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(note))
                    throw new ArgumentException("Supply an owner, a priority from 0 to 3 and a triage note.");
                ticket.Owner = owner;
                ticket.Priority = priority;
                state.TriageNote = note;
                break;
            case "ticket-start":
                if (state.Started) break;
                if (board.Status(ticket) != "ready" || string.IsNullOrWhiteSpace(ticket.Owner))
                    throw new InvalidOperationException("Assign an owner and complete the prerequisite tickets before starting.");
                if (Workspace.HashFile(workspace.Resolve(board.Spec.Brief, true)) != board.BriefHash)
                    throw new InvalidOperationException("The source brief changed after slicing. Import the revised scope as a new board.");
                if (File.Exists(workspace.Resolve(taskPath))) ReadMatchingTask(workspace, ticket, taskPath);
                else TaskFiles.Create(workspace, ticket.Scope);
                state.Started = true;
                break;
            case "ticket-prepare":
                if (!state.Started) throw new InvalidOperationException("Start the ticket before preparing its worktree.");
                var worker = ResolveWorker(workspace, get("worktree"));
                PrepareWorker(workspace, worker, board, ticket, taskPath);
                return new TicketResult(new { ticket.Id, worktree = worker.Root, task = taskPath, status = board.Status(ticket) });
            case "ticket-handoff":
            case "ticket-collect":
                if (!state.Started) throw new InvalidOperationException("Start the ticket before handing it off.");
                var source = command == "ticket-collect" ? ResolveWorker(workspace, get("worktree")) : workspace;
                var task = ReadMatchingTask(source, ticket, taskPath);
                string directory = Path.GetDirectoryName(source.Resolve(taskPath, true))!;
                using (Lock(source, Path.Combine(directory, "task.json")))
                {
                    foreach (string file in new[] { "baseline.json", "context.json", "context.md", "events.jsonl", "runs", "review.json", "evaluation.json", "report.md" })
                        source.Resolve(Path.GetRelativePath(source.Root, Path.Combine(directory, file)));
                    string baselineFile = "artifacts/agent/tasks/" + state.TaskId + "/baseline.json";
                    if (source != workspace)
                    {
                        if (Workspace.HashFile(source.Resolve(baselineFile, true)) != Workspace.HashFile(workspace.Resolve(baselineFile, true)))
                            throw new InvalidOperationException("The worktree baseline differs from the original ticket.");
                    }
                    var baseline = TaskFiles.Load<Baseline>(source.Resolve(baselineFile, true));
                    var result = Evaluation.Evaluate(source, task, directory);
                    if (source != workspace)
                        CheckCollectedPaths(board, ticket.Id, baseline.Head, result);
                    string report = Evaluation.Report(result, directory);
                    if (source != workspace)
                    {
                        string delivery = workspace.Resolve("artifacts/agent/boards/" + id + "/deliveries/" + ticket.Id);
                        Directory.CreateDirectory(delivery);
                        string target = workspace.Resolve(Path.GetRelativePath(workspace.Root, Path.Combine(delivery, "report.md")));
                        File.Copy(report, target, overwrite: true);
                        TaskFiles.Save(workspace.Resolve(Path.GetRelativePath(workspace.Root, Path.Combine(delivery, "evaluation.json"))), result);
                        report = target;
                    }
                    state.Handoff = new TicketHandoff(result.Status, result.EvidenceDigest,
                        Path.GetRelativePath(workspace.Root, report), DateTimeOffset.UtcNow)
                    {
                        ChangedPaths = result.Changes.Select(change => change.Path).ToArray(),
                        BaseCommit = baseline.Head
                    };
                }
                break;
            default: throw new ArgumentException("Unknown ticket command: " + command);
        }
        TaskFiles.Save(boardPath, board);
        return new TicketResult(new { ticket.Id, task = state.Started ? taskPath : null, status = board.Status(ticket), state.Handoff },
            command is "ticket-handoff" or "ticket-collect" ? state.Handoff : null);
    }

    private static Workspace ResolveWorker(Workspace workspace, string path)
    {
        var worker = new Workspace(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Workspace.FindRoot(worker.Root), worker.Root, comparison) ||
            string.Equals(workspace.Root, worker.Root, comparison) ||
            !string.Equals(workspace.GitCommonDirectory(), worker.GitCommonDirectory(), comparison))
            throw new InvalidOperationException("Collect from a separate Git worktree linked to this repository.");
        return worker;
    }

    private static void PrepareWorker(Workspace workspace, Workspace worker, TicketBoard board, TicketSpec ticket, string taskPath)
    {
        ReadMatchingTask(workspace, ticket, taskPath);
        string directory = Path.GetDirectoryName(taskPath)!;
        string baselinePath = directory + "/baseline.json";
        var baseline = TaskFiles.Load<Baseline>(workspace.Resolve(baselinePath, true));
        if (worker.Head() != baseline.Head)
            throw new InvalidOperationException("The worktree must be on the original ticket HEAD.");
        if (Workspace.HashFile(worker.Resolve(board.Spec.Brief, true)) != board.BriefHash)
            throw new InvalidOperationException("The worktree brief differs from the board specification.");

        using var preparation = Lock(worker, worker.Resolve(taskPath));
        var sources = new[] { workspace.Resolve(taskPath, true), workspace.Resolve(baselinePath, true) };
        var destinations = new[] { worker.Resolve(taskPath), worker.Resolve(baselinePath) };
        for (int index = 0; index < destinations.Length; index++)
            if (File.Exists(destinations[index]) && Workspace.HashFile(destinations[index]) != Workspace.HashFile(sources[index]))
                throw new InvalidOperationException("The worktree already has a different task package: " + destinations[index]);
        for (int index = 0; index < destinations.Length; index++)
            if (!File.Exists(destinations[index])) File.Copy(sources[index], destinations[index]);
    }

    private static void CheckCollectedPaths(TicketBoard board, string ticketId, string baseCommit, EvaluationResult result)
    {
        foreach (var sibling in board.State)
        {
            var handoff = sibling.Value.Handoff;
            if (sibling.Key == ticketId || handoff?.Status is not ("ready-for-review" or "accepted") ||
                handoff.BaseCommit != baseCommit) continue;
            foreach (var change in result.Changes.Where(change => Workspace.Matches(change.Path, handoff.ChangedPaths)))
                result.Failures.Add("Overlapping worktree delivery with " + sibling.Key + ": " + change.Path);
        }
        if (result.Failures.Count > 0)
        {
            result.Status = "failed";
            result.ChecksPassed = false;
        }
    }

    private static TaskSpec ReadMatchingTask(Workspace workspace, TicketSpec ticket, string taskPath)
    {
        var task = TaskFiles.ReadTask(workspace, taskPath);
        if (Evidence.Digest(task) != Evidence.Digest(ticket.Scope))
            throw new InvalidOperationException("The task differs from the ticket scope. Use the accepted slice when handing off.");
        return task;
    }

    private object View() => new { Spec.Id, Spec.Brief, BriefHash, Spec.DecisionSource,
        tickets = Spec.Tickets.OrderBy(ticket => ticket.Priority).ThenBy(ticket => ticket.Id, StringComparer.Ordinal).Select(ticket => new
        {
            ticket.Id, ticket.Scope.Objective, ticket.Owner, ticket.Priority, ticket.ExternalRef, ticket.DependsOn, ticket.Scope.Acceptance,
            ticket.Scope.RequiredChecks, status = Status(ticket), state = State[ticket.Id]
        }).ToArray() };

    private object CompareTracker(Workspace workspace, string snapshotPath)
    {
        var issues = TaskFiles.Load<TrackerIssue[]>(workspace.Resolve(snapshotPath, true));
        if (issues.Any(issue => issue == null || string.IsNullOrWhiteSpace(issue.Url) || issue.State is not ("OPEN" or "CLOSED")))
            throw new InvalidDataException("Use a GitHub issue export with url, title and OPEN/CLOSED state.");
        var byUrl = issues.ToDictionary(issue => issue.Url, StringComparer.Ordinal);
        return new { Spec.Id, snapshot = snapshotPath, tickets = Spec.Tickets.Select(ticket =>
        {
            var status = Status(ticket);
            var issue = ticket.ExternalRef == null ? null : byUrl.GetValueOrDefault(ticket.ExternalRef);
            var comparison = string.IsNullOrWhiteSpace(ticket.ExternalRef) ? "unlinked" : issue == null ? "missing" :
                (status == "accepted") == (issue.State == "CLOSED") ? "aligned" : "review-state-difference";
            return new { ticket.Id, ticket.ExternalRef, localStatus = status, issue, comparison };
        }).ToArray() };
    }

    private sealed class TrackerIssue
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string State { get; set; } = "";
    }

    private string Status(TicketSpec ticket) => State[ticket.Id].Handoff?.Status switch
    {
        "accepted" => "accepted",
        "ready-for-review" => "ready-for-review",
        "rejected" => "changes-requested",
        _ when State[ticket.Id].Started => "in-progress",
        _ when ticket.DependsOn.Any(parent => State[parent].Handoff?.Status != "accepted") => "blocked",
        _ => "ready"
    };

    private static string BoardPath(Workspace workspace, string id)
    {
        TaskFiles.ValidateId(id);
        return workspace.Resolve("artifacts/agent/boards/" + id + "/board.json");
    }

    private static FileStream Lock(Workspace workspace, string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        string lockPath = workspace.Resolve(Path.GetRelativePath(workspace.Root, Path.Combine(directory, ".lock")));
        Directory.CreateDirectory(directory);
        return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
