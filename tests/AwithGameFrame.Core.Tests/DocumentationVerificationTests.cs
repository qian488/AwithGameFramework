using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace AwithGameFrame.Tests
{
    public sealed class DocumentationVerificationTests
    {
        private string directory;
        private string script;

        [SetUp]
        public async Task SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "AwithDocsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var location = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (location != null && !File.Exists(Path.Combine(location.FullName, "tools", "verify-docs.ps1")))
                location = location.Parent;
            Assert.That(location, Is.Not.Null, "Repository documentation checker must be available.");
            script = Path.Combine(location.FullName, "tools", "verify-docs.ps1");
            var git = await Run("git", "init", directory);
            Assert.That(git.ExitCode, Is.Zero, git.Output);
        }

        [TearDown]
        public void TearDown()
        {
            if (directory == null || !Directory.Exists(directory)) return;
            string allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AwithDocsTests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(directory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refuse cleanup outside documentation test directory.");
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(directory, true);
        }

        [Test]
        public async Task ChecksNewDocumentsRelativeAndRootLinksWhileIgnoringCodeAndArchives()
        {
            Write("README.md", "[guide](docs/guide.md)\n[reference]: <docs/空 格.md>\n");
            Write("docs/guide.md", "[root](/README.md#heading)\n[encoded](%E7%A9%BA%20%E6%A0%BC.md)\n`[sample](missing.md)`\n```md\n[example](absent.md)\n```\n~~~md\n[example](absent.md)\n~~~\n");
            Write("docs/空 格.md", "# A document\n");
            Write("docs/legacy/old.md", "[obsolete](missing.md)\n");
            Write("samples/Unity/Assets/Packages/package/README.md", "[vendored](missing.md)\n");
            Write(".gitignore", "artifacts/\n");
            Write("artifacts/report.md", "[generated](missing.md)\n");
            var result = await Check();
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That(result.Output, Does.Contain("3 documents, 4 local targets"));
        }

        [Test]
        public async Task MissingLinksInCursorRulesAndReferenceDefinitionsFailWithSourceLocation()
        {
            Write("README.md", "[read][guide]\n[guide]: docs/removed.md\n");
            Write(".cursor/rules/repo.mdc", "[entry](../../missing.md)\n");
            var result = await Check();
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("README.md:2"));
            Assert.That(result.Output, Does.Contain(".cursor/rules/repo.mdc:1"));
        }

        [Test]
        public async Task RelativeLinksCannotEscapeRepositoryEvenWhenTargetExists()
        {
            Write("README.md", "[outside](../)\n");
            var result = await Check();
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("local link leaves repository"));
        }

        private void Write(string path, string text)
        {
            string fullPath = Path.Combine(directory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, text, new UTF8Encoding(false));
        }

        private Task<(int ExitCode, string Output)> Check() =>
            Run(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh", "-NoProfile", "-File", script, "-RepositoryRoot", directory);

        private async Task<(int ExitCode, string Output)> Run(string executable, params string[] arguments)
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(true); throw; }
            return (process.ExitCode, await output + await error);
        }
    }
}
