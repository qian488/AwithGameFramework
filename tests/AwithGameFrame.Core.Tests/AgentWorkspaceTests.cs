using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.AgentKit;
using NUnit.Framework;

namespace AwithGameFrame.Tests
{
    public sealed class AgentWorkspaceTests
    {
        private string fixtureDirectory;
        private string repository;
        private Workspace workspace;
        private readonly List<string> links = new List<string>();

        [SetUp]
        public void SetUp()
        {
            fixtureDirectory = Path.Combine(Path.GetTempPath(), "AwithAgentWorkspaceTests", Guid.NewGuid().ToString("N"));
            repository = Path.Combine(fixtureDirectory, "repository");
            Directory.CreateDirectory(repository);
            Git("init");
            Write(".gitignore", "artifacts/\n");
            Write("owned/initial.txt", "initial\n");
            Write("owned/rename.txt", "rename me\n");
            Write("protected.txt", "original\n");
            Git("add", ".");
            Git("-c", "user.name=AgentKit Tests", "-c", "user.email=agentkit-tests@example.invalid", "-c", "commit.gpgsign=false",
                "-c", "core.hooksPath=nonexistent-test-hooks", "commit", "-m", "Initial fixture");
            workspace = new Workspace(repository);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string link in links)
                if (Directory.Exists(link)) Directory.Delete(link);
            links.Clear();
            string allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AwithAgentWorkspaceTests")) + Path.DirectorySeparatorChar;
            if (fixtureDirectory != null && Directory.Exists(fixtureDirectory))
            {
                if (!Path.GetFullPath(fixtureDirectory).StartsWith(allowed, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidOperationException("Refuse cleanup outside the test directory.");
                foreach (string file in Directory.EnumerateFiles(fixtureDirectory, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(fixtureDirectory, true);
            }
        }

        [Test]
        public void BaselinePreservesExistingDirtyFilesAndAttributesOnlyLaterDrift()
        {
            Write("protected.txt", "existing user edit\n");
            Write("preexisting untracked.txt", "existing untracked\n");
            var baseline = workspace.CaptureBaseline();
            var task = Spec();
            Assert.That(workspace.Changes(task, baseline), Is.Empty);

            Write("owned/initial.txt", "task edit\n");
            var changes = workspace.Changes(task, baseline);
            Assert.That(changes.Select(change => change.Path), Is.EqualTo(new[] { "owned/initial.txt" }));
            Assert.That(changes[0].InScope, Is.True);
            Assert.That(changes[0].Protected, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(repository, "protected.txt")), Is.EqualTo("existing user edit\n"));

            Write("protected.txt", "later edit\n");
            Assert.That(workspace.Changes(task, baseline).Single(change => change.Path == "protected.txt").Protected, Is.True);
        }

        [Test]
        public void RestoringAnOriginallyDirtyFileStillCountsAsAChange()
        {
            Write("protected.txt", "existing user edit\n");
            var baseline = workspace.CaptureBaseline();
            Git("checkout", "--", "protected.txt");
            var change = workspace.Changes(Spec(), baseline).Single();
            Assert.That(change.Path, Is.EqualTo("protected.txt"));
            Assert.That(change.Protected, Is.True);
        }

        [Test]
        public void RenameReportsBothPathsAndChecksEachScopeSeparately()
        {
            var baseline = workspace.CaptureBaseline();
            Directory.CreateDirectory(Path.Combine(repository, "outside"));
            Git("mv", "owned/rename.txt", "outside/renamed 文件.txt");
            var changes = workspace.Changes(Spec(), baseline);
            Assert.That(changes.Select(change => change.Path), Is.EquivalentTo(new[] { "owned/rename.txt", "outside/renamed 文件.txt" }));
            Assert.That(changes.Single(change => change.Path == "owned/rename.txt").InScope, Is.True);
            Assert.That(changes.Single(change => change.Path == "outside/renamed 文件.txt").InScope, Is.False);
        }

        [Test]
        public void IndexChangesAreDetectedWhenStatusAndWorkingBytesRemainIdentical()
        {
            Write("owned/initial.txt", "index one\n");
            Git("add", "owned/initial.txt");
            Write("owned/initial.txt", "stable working bytes\n");
            var baseline = workspace.CaptureBaseline();
            Write("owned/initial.txt", "index two\n");
            Git("add", "owned/initial.txt");
            Write("owned/initial.txt", "stable working bytes\n");
            var after = workspace.CaptureBaseline();
            var first = baseline.Files["owned/initial.txt"];
            var second = after.Files["owned/initial.txt"];
            Assert.That(second.Status, Is.EqualTo(first.Status));
            Assert.That(second.Hash, Is.EqualTo(first.Hash));
            Assert.That(second.IndexHash, Is.Not.EqualTo(first.IndexHash));
            Assert.That(workspace.Changes(Spec(), baseline).Single().Path, Is.EqualTo("owned/initial.txt"));
        }

        [Test]
        public void ConflictStagesAreIncludedInTheIndexSnapshot()
        {
            string ancestor = GitInput("ancestor\n", "hash-object", "-w", "--stdin").Trim();
            string ours = GitInput("ours\n", "hash-object", "-w", "--stdin").Trim();
            string theirs = GitInput("theirs\n", "hash-object", "-w", "--stdin").Trim();
            SetConflict(ancestor, ours, theirs);
            var baseline = workspace.CaptureBaseline();
            SetConflict(ancestor, theirs, ours);
            var after = workspace.CaptureBaseline();
            Assert.That(after.Files["owned/initial.txt"].Hash, Is.EqualTo(baseline.Files["owned/initial.txt"].Hash));
            Assert.That(after.Files["owned/initial.txt"].Status, Is.EqualTo(baseline.Files["owned/initial.txt"].Status));
            Assert.That(workspace.Changes(Spec(), baseline).Single().Path, Is.EqualTo("owned/initial.txt"));
        }

        [TestCase(".")]
        [TestCase("../outside")]
        [TestCase("owned/../protected.txt")]
        [TestCase("owned\\..\\protected.txt")]
        [TestCase("/absolute")]
        [TestCase("C:\\absolute")]
        [TestCase(".git/config")]
        [TestCase("owned/.GIT/config")]
        public void ResolveRejectsEscapesAndGitMetadata(string path)
        {
            Assert.Throws<ArgumentException>(() => workspace.Resolve(path));
        }

        [Test]
        public void ScopeMatchesWholeSegmentsAndAllowsUncreatedDirectories()
        {
            Assert.That(workspace.Resolve("new/future/"), Does.EndWith(Path.Combine("new", "future")));
            Assert.That(Workspace.Matches("owned/child.txt", new[] { "owned/" }), Is.True);
            Assert.That(Workspace.Matches("ownedness/child.txt", new[] { "owned" }), Is.False);
            Assert.That(Workspace.Matches("owned/initial.txt", new[] { "owned/initial.txt" }), Is.True);
            Assert.That(Workspace.FindRoot(Path.Combine(repository, "owned")), Is.EqualTo(repository));
        }

        [Test]
        public void LinksAreRejectedForContextAndTaskOutputsWithoutReadingTheirTargets()
        {
            string outside = Path.Combine(fixtureDirectory, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside contents");
            string link = Path.Combine(repository, "link");
            try { Directory.CreateSymbolicLink(link, outside); links.Add(link); }
            catch (Exception error) when (error is UnauthorizedAccessException || error is PlatformNotSupportedException || error is IOException)
            { Assert.Ignore("This host cannot create symbolic links: " + error.Message); }
            Assert.Throws<IOException>(() => workspace.Resolve("link/secret.txt", true));
            Assert.Throws<IOException>(() => TaskFiles.Save(Path.Combine(link, "evidence.json"), new { value = 1 }));
            var baseline = workspace.CaptureBaseline();
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "changed outside contents");
            Assert.That(workspace.Changes(Spec(), baseline), Is.Empty);
        }

        [Test]
        public void TaskCreationCopiesTheTemplateCapturesBaselineAndRejectsAnExistingId()
        {
            string template = WriteTemplate();
            string path = TaskFiles.Create(workspace, template, "task-one", "A concrete objective");
            var task = TaskFiles.ReadTask(workspace, path);
            Assert.That(task.Id, Is.EqualTo("task-one"));
            Assert.That(task.Objective, Is.EqualTo("A concrete objective"));
            Assert.That(TaskFiles.ReadTask(workspace, template).Id, Is.EqualTo("template"));
            string baselinePath = Path.Combine(repository, Path.GetDirectoryName(path), "baseline.json");
            var baseline = TaskFiles.Load<Baseline>(baselinePath);
            Assert.That(baseline.Head, Is.EqualTo(workspace.Head()));
            Assert.That(workspace.Changes(task, baseline), Is.Empty);
            Assert.Throws<IOException>(() => TaskFiles.Create(workspace, template, "task-one", "Must not overwrite"));
            Assert.That(TaskFiles.ReadTask(workspace, path).Objective, Is.EqualTo("A concrete objective"));
            Assert.That(File.ReadAllBytes(baselinePath).Take(3), Is.Not.EqualTo(new byte[] { 0xef, 0xbb, 0xbf }));
        }

        [Test]
        public async Task ConcurrentCreationPublishesExactlyOneCompleteTask()
        {
            string template = WriteTemplate();
            using var ready = new Barrier(2);
            var attempts = Enumerable.Range(0, 2).Select(index => Task.Run(() =>
            {
                ready.SignalAndWait();
                try { return (Path: TaskFiles.Create(workspace, template, "same-id", "Winner " + index), Error: (Exception)null); }
                catch (Exception error) { return (Path: (string)null, Error: error); }
            })).ToArray();
            var results = await Task.WhenAll(attempts);
            Assert.That(results.Count(result => result.Error == null), Is.EqualTo(1));
            Assert.That(results.Single(result => result.Error != null).Error, Is.InstanceOf<IOException>());
            string path = results.Single(result => result.Error == null).Path;
            var task = TaskFiles.ReadTask(workspace, path);
            var baseline = TaskFiles.Load<Baseline>(Path.Combine(repository, Path.GetDirectoryName(path), "baseline.json"));
            Assert.That(task.Objective, Does.StartWith("Winner "));
            Assert.That(workspace.Changes(task, baseline), Is.Empty);
            Assert.That(Directory.EnumerateDirectories(Path.Combine(repository, "artifacts", "agent", "tasks")).Count(), Is.EqualTo(1));
        }

        [Test]
        public void TaskValidationRejectsPathInjectionBeforeCreatingTaskArtifacts()
        {
            var task = Spec();
            task.WritePaths = new[] { "../outside" };
            TaskFiles.Save(Path.Combine(repository, "template.json"), task);
            Assert.Throws<ArgumentException>(() => TaskFiles.Create(workspace, "template.json", "bad-task", null));
            Assert.That(Directory.Exists(Path.Combine(repository, "artifacts")), Is.False);
        }

        private static TaskSpec Spec() => new TaskSpec
        {
            Id = "template", Objective = "Validate task changes", ReadPaths = new[] { "owned" },
            AllowedTools = new[] { "probe" }, RequiredChecks = new[] { "probe" },
            WritePaths = new[] { "owned" }, ProtectedPaths = new[] { "protected.txt" }, Acceptance = new[] { "Changes stay in scope" }
        };

        private string WriteTemplate()
        {
            const string path = "agent/template.json";
            TaskFiles.Save(Path.Combine(repository, path), Spec());
            return path;
        }

        private void SetConflict(string ancestor, string ours, string theirs)
        {
            const string path = "owned/initial.txt";
            GitInput("0 " + new string('0', ancestor.Length) + "\t" + path + "\n" +
                "100644 " + ancestor + " 1\t" + path + "\n100644 " + ours + " 2\t" + path + "\n100644 " + theirs + " 3\t" + path + "\n",
                "update-index", "--index-info");
        }

        private void Write(string relative, string contents)
        {
            string path = Path.Combine(repository, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents, new UTF8Encoding(false));
        }

        private string Git(params string[] arguments) => GitInput(null, arguments);

        private string GitInput(string input, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = repository, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input != null,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (input != null) { process.StandardInput.Write(input); process.StandardInput.Close(); }
            process.WaitForExit();
            string result = output.GetAwaiter().GetResult();
            string diagnostic = error.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException("Fixture Git failed: " + diagnostic);
            return result;
        }
    }
}
