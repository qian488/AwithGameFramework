using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.AgentKit;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class AgentKitTests
    {
        private string root;
        private Workspace workspace;
        private TaskSpec task;
        private string directory;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "awith-agent-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Git("init"); Git("config", "user.email", "tests@example.invalid"); Git("config", "user.name", "Agent tests");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "artifacts/\n");
            File.WriteAllText(Path.Combine(root, "README.md"), "original context");
            Directory.CreateDirectory(Path.Combine(root, "work"));
            File.WriteAllText(Path.Combine(root, "work", "source.md"), "source");
            Directory.CreateDirectory(Path.Combine(root, "agent"));
            WriteCatalog(false);
            Git("add", "."); Git("commit", "-m", "initial");
            workspace = new Workspace(root);
            task = new TaskSpec { Id = "test", Objective = "Exercise an isolated agent task", ReadPaths = new[] { "README.md" },
                WritePaths = new[] { "work" }, AllowedTools = new[] { "probe" }, RequiredChecks = new[] { "probe" },
                Acceptance = new[] { "A human checks the result" } };
            TaskFiles.Save(Path.Combine(root, "template.json"), task);
            string path = TaskFiles.Create(workspace, "template.json", "test", null);
            directory = Path.GetDirectoryName(workspace.Resolve(path))!;
            ContextBundle.Build(workspace, task, directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (!Directory.Exists(root)) return;
            Assert.That(Path.GetDirectoryName(root), Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetTempPath())).IgnoreCase);
            Assert.That(Path.GetFileName(root), Does.StartWith("awith-agent-"));
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }

        [Test]
        public async Task SuccessfulRunProducesEvidenceButStillNeedsExplicitHumanAcceptance()
        {
            var plan = ToolRunner.Plan(workspace, task, directory, "probe");
            var run = await ToolRunner.RunAsync(workspace, task, directory, plan, CancellationToken.None);
            Assert.That(run.Status, Is.EqualTo("passed"));
            Assert.That(run.ExitCode, Is.Zero);
            Assert.That(run.StdoutHash, Is.Not.Empty);
            var result = Evaluation.Evaluate(workspace, task, directory);
            Assert.That(result.Status, Is.EqualTo("ready-for-review"));
            Evaluation.Review(directory, result, "human reviewer", "accept", "Reviewed the stated acceptance criteria");
            Assert.That(Evaluation.Evaluate(workspace, task, directory).Status, Is.EqualTo("accepted"));

            File.WriteAllText(Path.Combine(root, "work", "source.md"), "new revision");
            var stale = Evaluation.Evaluate(workspace, task, directory);
            Assert.That(stale.ChecksPassed, Is.False);
            Assert.That(stale.Status, Is.EqualTo("failed"));
        }

        [Test]
        public void MissingChecksAndScopeViolationsCannotBeReportedAsPassed()
        {
            var missing = Evaluation.Evaluate(workspace, task, directory);
            Assert.That(missing.ChecksPassed, Is.False);
            Assert.That(missing.Failures, Has.Some.Contains("not executed"));
            File.WriteAllText(Path.Combine(root, "outside.md"), "outside scope");
            Assert.Throws<InvalidOperationException>(() => ToolRunner.Plan(workspace, task, directory, "probe"));
            var outside = Evaluation.Evaluate(workspace, task, directory);
            Assert.That(outside.Failures, Has.Some.Contains("Scope violation"));
            Assert.That(File.ReadAllText(Evaluation.Report(outside, directory)), Does.Contain("FAILED"));
        }

        [Test]
        public void ContextBudgetsAndDriftAreExplicitAndNeverSilentlyTruncated()
        {
            File.WriteAllText(Path.Combine(root, "README.md"), new string('x', 65537));
            Assert.Throws<InvalidOperationException>(() => ContextBundle.Build(workspace, task, directory));
            Assert.Throws<InvalidOperationException>(() => ContextBundle.ReadFresh(workspace, directory));
            Assert.That(Evaluation.Evaluate(workspace, task, directory).ChecksPassed, Is.False);
        }

        [Test]
        public void NewFilesAndChangedTaskSelectionInvalidateTheContextManifest()
        {
            task.ReadPaths = new[] { "work" };
            ContextBundle.Build(workspace, task, directory);
            File.WriteAllText(Path.Combine(root, "work", "new.md"), "new context");
            Assert.Throws<InvalidOperationException>(() => ContextBundle.ReadFresh(workspace, directory, task));
            ContextBundle.Build(workspace, task, directory);
            task.ReadPaths = new[] { "README.md" };
            Assert.Throws<InvalidOperationException>(() => ContextBundle.ReadFresh(workspace, directory, task));
        }

        [Test]
        public async Task RequiredApprovalDeniesWithoutLaunchingAndCannotBeReplayed()
        {
            WriteCatalog(true);
            task.WritePaths = new[] { "work", "agent" };
            ContextBundle.Build(workspace, task, directory);
            var deniedPlan = ToolRunner.Plan(workspace, task, directory, "probe");
            var denied = await ToolRunner.RunAsync(workspace, task, directory, deniedPlan, CancellationToken.None);
            Assert.That(denied.Status, Is.EqualTo("denied"));
            Assert.That(denied.ExitCode, Is.Null);
            Assert.That(File.Exists(Path.Combine(directory, "runs", deniedPlan.Id + ".stdout.log")), Is.False);
            var plan = ToolRunner.Plan(workspace, task, directory, "probe");
            ToolRunner.RecordApproval(directory, plan, "human", "approve", "Reviewed exact command");
            Assert.That((await ToolRunner.RunAsync(workspace, task, directory, plan, CancellationToken.None)).Status, Is.EqualTo("passed"));
            Assert.ThrowsAsync<InvalidOperationException>(() => ToolRunner.RunAsync(workspace, task, directory, plan, CancellationToken.None));
        }

        [Test]
        public void ChangingWorkOrArgumentsInvalidatesPreviouslyApprovedPlan()
        {
            WriteCatalog(true);
            task.WritePaths = new[] { "work", "agent" };
            ContextBundle.Build(workspace, task, directory);
            var plan = ToolRunner.Plan(workspace, task, directory, "probe");
            ToolRunner.RecordApproval(directory, plan, "human", "approve", "Reviewed exact command");
            File.WriteAllText(Path.Combine(root, "work", "source.md"), "edited after approval");
            Assert.ThrowsAsync<InvalidOperationException>(() => ToolRunner.RunAsync(workspace, task, directory, plan, CancellationToken.None));
            plan = ToolRunner.Plan(workspace, task, directory, "probe");
            plan.Arguments = new[] { "--different-command" };
            Assert.ThrowsAsync<InvalidOperationException>(() => ToolRunner.RunAsync(workspace, task, directory, plan, CancellationToken.None));
        }

        [TestCase("deny")]
        [TestCase("expired")]
        public async Task RejectedOrExpiredApprovalCannotStartProcess(string condition)
        {
            WriteCatalog(true);
            task.WritePaths = new[] { "work", "agent" };
            ContextBundle.Build(workspace, task, directory);
            var plan = ToolRunner.Plan(workspace, task, directory, "probe");
            ToolRunner.RecordApproval(directory, plan, "human", condition == "deny" ? "deny" : "approve", "Explicit decision");
            if (condition == "expired")
            {
                string path = Path.Combine(directory, "plans", plan.Id + ".approval.json");
                var approval = TaskFiles.Load<Approval>(path); approval.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
                TaskFiles.Save(path, approval);
            }
            Assert.That((await ToolRunner.RunAsync(workspace, task, directory, plan, CancellationToken.None)).Status, Is.EqualTo("denied"));
        }

        [Test]
        public async Task FailedProcessAndModifiedEvidenceCannotPassEvaluation()
        {
            WriteCatalog(false, "dotnet", new[] { "--this-command-does-not-exist" });
            task.WritePaths = new[] { "work", "agent" };
            ContextBundle.Build(workspace, task, directory);
            var plan = ToolRunner.Plan(workspace, task, directory, "probe");
            var result = await ToolRunner.RunAsync(workspace, task, directory, plan, CancellationToken.None);
            Assert.That(result.Status, Is.EqualTo("failed"));
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(Evaluation.Evaluate(workspace, task, directory).ChecksPassed, Is.False);
            WriteCatalog(false);
            plan = ToolRunner.Plan(workspace, task, directory, "probe");
            await ToolRunner.RunAsync(workspace, task, directory, plan, CancellationToken.None);
            File.AppendAllText(Path.Combine(directory, "runs", plan.Id + ".stdout.log"), "tampered");
            Assert.That(Evaluation.Evaluate(workspace, task, directory).Failures, Has.Some.Contains("log changed"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TimeoutAndCancellationLeaveDistinctFailedEvidence(bool cancel)
        {
            WriteCatalog(false, "$powershell", new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 20" }, cancel ? 30 : 1);
            task.WritePaths = new[] { "work", "agent" };
            ContextBundle.Build(workspace, task, directory);
            var plan = ToolRunner.Plan(workspace, task, directory, "probe");
            using var cancellation = new CancellationTokenSource();
            if (cancel) cancellation.CancelAfter(TimeSpan.FromSeconds(1));
            var result = await ToolRunner.RunAsync(workspace, task, directory, plan, cancellation.Token);
            Assert.That(result.Status, Is.EqualTo(cancel ? "cancelled" : "timed-out"));
            Assert.That(result.DurationSeconds, Is.LessThan(15));
            Assert.That(result.ExitCode, Is.Null);
            Assert.That(Evaluation.Evaluate(workspace, task, directory).ChecksPassed, Is.False);
        }

        private void WriteCatalog(bool approval, string executable = "dotnet", string[] arguments = null, int seconds = 30)
        {
            TaskFiles.Save(Path.Combine(root, "agent", "tools.json"), new ToolCatalog { Tools = new[] {
                new ToolRecipe { Id = "probe", Executable = executable, Arguments = arguments ?? new[] { "--version" },
                    TimeoutSeconds = seconds, RequiresApproval = approval } } });
        }

        private void Git(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(errors.GetAwaiter().GetResult());
            output.GetAwaiter().GetResult();
        }
    }
}
