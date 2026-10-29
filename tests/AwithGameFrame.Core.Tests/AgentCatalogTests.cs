using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AwithGameFrame.AgentKit;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class AgentCatalogTests
    {
        private string root;
        private Workspace workspace;
        private TaskSpec task;
        private string directory;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "awith-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            workspace = new Workspace(root);
            Write(".gitignore", "artifacts/\n");
            Write("README.md", "Repository entry");
            Write("GUIDE.md", "Repository map");
            Write("AGENTS.md", "Root instructions");
            Write("src/AGENTS.md", "Runtime instructions");
            Write("notes.md", "Explicit task context");
            WriteModule("core");
            WriteModule("gameplay", "core");
            Write("src/gameplay/AGENTS.md", "Package instructions");
            Write("src/gameplay/Runtime/AGENTS.md", "Source instructions");
            TaskFiles.Save(workspace.Resolve("agent/tools.json"), new ToolCatalog
            {
                Tools = new[] { new ToolRecipe { Id = "probe", Executable = "dotnet", Arguments = new[] { "--version" } } }
            });
            Git("init");
            Git("config", "user.email", "tests@example.invalid");
            Git("config", "user.name", "Agent catalog tests");
            Git("add", ".");
            Git("commit", "-m", "initial");
            task = new TaskSpec
            {
                Id = "catalog-test", Objective = "Check bounded module context",
                ReadModules = new[] { "gameplay" }, ReadPaths = new[] { "notes.md" },
                WritePaths = new[] { "src", "notes.md" },
                AllowedTools = new[] { "probe" }, RequiredChecks = new[] { "probe" },
                Acceptance = new[] { "A human reviews selected context" }
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (root == null || !Directory.Exists(root)) return;
            Assert.That(Path.GetDirectoryName(root), Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetTempPath())).IgnoreCase);
            Assert.That(Path.GetFileName(root), Does.StartWith("awith-catalog-"));
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }

        [Test]
        public void SelectedModuleAddsBoundedEntriesAndAncestorInstructionsToExplicitPaths()
        {
            Start();
            var manifest = ContextBundle.ReadFresh(workspace, directory, task);
            var paths = manifest.Files.Select(f => f.Path).ToArray();
            Assert.That(paths, Does.Contain("src/gameplay/module.json"));
            Assert.That(paths, Does.Contain("src/gameplay/README.md"));
            Assert.That(paths, Does.Contain("src/gameplay/Runtime/Entry.cs"));
            Assert.That(paths, Does.Contain("src/AGENTS.md"));
            Assert.That(paths, Does.Contain("src/gameplay/AGENTS.md"));
            Assert.That(paths, Does.Contain("src/gameplay/Runtime/AGENTS.md"));
            Assert.That(paths, Does.Contain("notes.md"));
            Assert.That(paths, Does.Not.Contain("src/gameplay/Runtime/Hidden.cs"));
            Assert.That(paths, Does.Not.Contain("src/core/README.md"));
            Assert.That(manifest.ModuleCatalogDigest, Is.EqualTo(ModuleCatalog.Read(workspace).Digest));
            Assert.That(paths, Is.Ordered.Using<string>(StringComparer.Ordinal));
        }

        [TestCase("entry")]
        [TestCase("readme")]
        [TestCase("descriptor")]
        [TestCase("selection")]
        [TestCase("catalog-addition")]
        [TestCase("catalog-removal")]
        public void ModuleSelectionAndCatalogChangesInvalidateContext(string change)
        {
            Start();
            switch (change)
            {
                case "entry": Write("src/gameplay/Runtime/Entry.cs", "changed source"); break;
                case "readme": Write("src/gameplay/README.md", "changed documentation"); break;
                case "descriptor": Alter(m => m.Purpose = "New package purpose"); break;
                case "selection": task.ReadModules = new[] { "core" }; break;
                case "catalog-addition": WriteModule("extra"); break;
                case "catalog-removal":
                    // Remove an unselected, unreferenced descriptor from a freshly captured catalog.
                    WriteModule("extra");
                    ContextBundle.Build(workspace, task, directory);
                    File.Delete(workspace.Resolve("src/extra/module.json", true));
                    break;
            }
            Assert.Throws<InvalidOperationException>(() => ContextBundle.ReadFresh(workspace, directory, task));
        }

        [Test]
        public void UnselectedSourceAndDependencyReadmeAreNotCollectedImplicitly()
        {
            Start();
            Write("src/gameplay/Runtime/Hidden.cs", "changed nonentry source");
            Write("src/core/README.md", "changed dependency docs");
            Assert.DoesNotThrow(() => ContextBundle.ReadFresh(workspace, directory, task));
        }

        [Test]
        public void DescriptorChangePreventsAnExistingPlanFromLaunching()
        {
            Start();
            var plan = ToolRunner.Plan(workspace, task, directory, "probe");
            Alter(m => m.EntryPoints = new[] { "src/gameplay/Runtime/Hidden.cs" });
            var error = Assert.ThrowsAsync<InvalidOperationException>(() => ToolRunner.RunAsync(workspace, task, directory, plan, CancellationToken.None));
            Assert.That(error.Message, Does.Contain("context"));
            Assert.That(Directory.Exists(Path.Combine(directory, "runs")), Is.False);
        }

        [Test]
        public void LegacyTasksKeepSerializationAndContextDigestWithoutRequiringACatalog()
        {
            task.ReadModules = null;
            File.Delete(workspace.Resolve("agent/tools.json", true));
            Write("src/gameplay/module.json", "invalid JSON unused by a legacy task");
            var legacyShape = new
            {
                task.SchemaVersion, task.Id, task.Objective, task.ReadPaths, task.WritePaths,
                task.ProtectedPaths, task.AllowedTools, task.RequiredChecks, task.Acceptance, task.Parameters
            };
            Assert.That(Evidence.Digest(task), Is.EqualTo(Evidence.Digest(legacyShape)));
            Start();
            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "task.json")));
            Assert.That(saved.RootElement.TryGetProperty("readModules", out _), Is.False);
            var manifest = ContextBundle.ReadFresh(workspace, directory);
            Assert.That(manifest.ModuleCatalogDigest, Is.Null);
            Assert.That(manifest.Digest, Is.EqualTo(Evidence.Digest(new { manifest.TaskDigest, manifest.Files })));
        }

        [TestCase("unknown")]
        [TestCase("duplicate")]
        [TestCase("blank")]
        public void InvalidReadModulesCannotCreateTask(string reason)
        {
            task.ReadModules = reason == "unknown" ? new[] { "absent" } : reason == "duplicate" ? new[] { "core", "core" } : new[] { "" };
            TaskFiles.Save(workspace.Resolve("template.json"), task);
            Assert.Throws<InvalidDataException>(() => TaskFiles.Create(workspace, "template.json", task.Id, null));
            Assert.That(Directory.Exists(workspace.Resolve("artifacts/agent/tasks/" + task.Id)), Is.False);
        }

        [TestCase("schema")]
        [TestCase("id")]
        [TestCase("id-newline")]
        [TestCase("purpose")]
        [TestCase("source-missing")]
        [TestCase("source-file")]
        [TestCase("entry-missing")]
        [TestCase("entry-directory")]
        [TestCase("readme-missing")]
        [TestCase("other-package")]
        [TestCase("unknown-dependency")]
        [TestCase("cycle")]
        [TestCase("unknown-tool")]
        [TestCase("null-list")]
        [TestCase("empty-entries")]
        [TestCase("duplicate-entries")]
        public void InvalidModuleContractsFailBeforeContextCollection(string reason)
        {
            Alter(m =>
            {
                switch (reason)
                {
                    case "schema": m.SchemaVersion = 2; break;
                    case "id": m.Id = "Invalid ID"; break;
                    case "id-newline": m.Id = "gameplay\n"; break;
                    case "purpose": m.Purpose = ""; break;
                    case "source-missing": m.SourcePaths = new[] { "src/gameplay/Missing" }; break;
                    case "source-file": m.SourcePaths = new[] { "src/gameplay/Runtime/Entry.cs" }; break;
                    case "entry-missing": m.EntryPoints = new[] { "src/gameplay/Runtime/Missing.cs" }; break;
                    case "entry-directory": m.EntryPoints = new[] { "src/gameplay/Runtime" }; break;
                    case "readme-missing": m.Readme = "src/gameplay/Missing.md"; break;
                    case "other-package": m.Readme = "src/core/README.md"; break;
                    case "unknown-dependency": m.Dependencies = new[] { "absent" }; break;
                    case "cycle": m.Dependencies = new[] { "gameplay" }; break;
                    case "unknown-tool": m.VerificationTools = new[] { "missing-check" }; break;
                    case "null-list": m.SourcePaths = null; break;
                    case "empty-entries": m.EntryPoints = Array.Empty<string>(); break;
                    case "duplicate-entries": m.EntryPoints = new[] { m.EntryPoints[0], m.EntryPoints[0] }; break;
                }
            });
            Assert.Throws<InvalidDataException>(() => ModuleCatalog.Read(workspace));
        }

        [TestCase("../outside.md")]
        [TestCase("E:/outside.md")]
        public void ModulePathsCannotEscapeRepository(string path)
        {
            Alter(m => m.Readme = path);
            Assert.Throws<ArgumentException>(() => ModuleCatalog.Read(workspace));
        }

        [Test]
        public void DuplicateModuleIdsAndTransitiveCyclesAreRejected()
        {
            var core = TaskFiles.Load<ModuleDescriptor>(workspace.Resolve("src/core/module.json"));
            core.Id = "gameplay";
            TaskFiles.Save(workspace.Resolve("src/core/module.json"), core);
            Assert.That(Assert.Throws<InvalidDataException>(() => ModuleCatalog.Read(workspace)).Message, Does.Contain("Duplicate"));
            core.Id = "core";
            core.Dependencies = new[] { "gameplay" };
            TaskFiles.Save(workspace.Resolve("src/core/module.json"), core);
            Assert.That(Assert.Throws<InvalidDataException>(() => ModuleCatalog.Read(workspace)).Message, Does.Contain("cycle"));
        }

        [Test]
        public void DiscoveryOnlyUsesImmediatePackageDirectoriesAndIsSorted()
        {
            Write("samples/Unity/Assets/Plugins/Ignore/module.json", "not a module descriptor");
            Write("artifacts/Fake/module.json", "not a module descriptor");
            Write("src/gameplay/Runtime/Nested/module.json", "not a module descriptor");
            var catalog = ModuleCatalog.Read(workspace);
            Assert.That(catalog.Modules.Select(m => m.Definition.Id), Is.EqualTo(new[] { "core", "gameplay" }));
            Assert.That(catalog.Get("gameplay").Path, Is.EqualTo("src/gameplay/module.json"));
        }

        private void Start()
        {
            TaskFiles.Save(workspace.Resolve("template.json"), task);
            string path = TaskFiles.Create(workspace, "template.json", task.Id, null);
            directory = Path.GetDirectoryName(workspace.Resolve(path));
            ContextBundle.Build(workspace, task, directory);
        }

        private void Alter(Action<ModuleDescriptor> change)
        {
            string path = workspace.Resolve("src/gameplay/module.json", true);
            var module = TaskFiles.Load<ModuleDescriptor>(path);
            change(module);
            TaskFiles.Save(path, module);
        }

        private void WriteModule(string id, params string[] dependencies)
        {
            string package = "src/" + id;
            Write(package + "/Runtime/Entry.cs", "// public entry point");
            Write(package + "/Runtime/Hidden.cs", "// not collected implicitly");
            Write(package + "/README.md", "Package documentation: " + id);
            TaskFiles.Save(workspace.Resolve(package + "/module.json"), new ModuleDescriptor
            {
                Id = id, Purpose = "Fixture package " + id, SourcePaths = new[] { package + "/Runtime" },
                EntryPoints = new[] { package + "/Runtime/Entry.cs" }, Readme = package + "/README.md",
                Dependencies = dependencies, VerificationTools = new[] { "probe" }
            });
        }

        private void Write(string path, string content)
        {
            string full = workspace.Resolve(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
        }

        private void Git(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(errors.GetAwaiter().GetResult());
            output.GetAwaiter().GetResult();
        }
    }
}
