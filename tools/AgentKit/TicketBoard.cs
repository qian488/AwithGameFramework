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

public sealed record TicketHandoff(string Status, string EvidenceDigest, string Report, DateTimeOffset AtUtc);
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
            case "ticket-handoff":
                if (!state.Started) throw new InvalidOperationException("Start the ticket before handing it off.");
                var task = ReadMatchingTask(workspace, ticket, taskPath);
                string directory = Path.GetDirectoryName(workspace.Resolve(taskPath, true))!;
                using (Lock(workspace, Path.Combine(directory, "task.json")))
                {
                    foreach (string file in new[] { "baseline.json", "context.json", "context.md", "events.jsonl", "runs", "review.json", "evaluation.json", "report.md" })
                        workspace.Resolve(Path.GetRelativePath(workspace.Root, Path.Combine(directory, file)));
                    var result = Evaluation.Evaluate(workspace, task, directory);
                    string report = Evaluation.Report(result, directory);
                    state.Handoff = new TicketHandoff(result.Status, result.EvidenceDigest, Path.GetRelativePath(workspace.Root, report), DateTimeOffset.UtcNow);
                }
                break;
            default: throw new ArgumentException("Unknown ticket command: " + command);
        }
        TaskFiles.Save(boardPath, board);
        return new TicketResult(new { ticket.Id, task = state.Started ? taskPath : null, status = board.Status(ticket), state.Handoff },
            command == "ticket-handoff" ? state.Handoff : null);
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
