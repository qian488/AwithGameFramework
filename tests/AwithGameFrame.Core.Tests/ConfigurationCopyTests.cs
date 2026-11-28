using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace AwithGameFrame.Tests
{
    public sealed class ConfigurationCopyTests
    {
        [Test]
        public async Task CheckReportsFirstStaleLineAndRefreshPreservesUnityMetadata()
        {
            string root = Path.Combine(Path.GetTempPath(), "AwithConfigurationCopy", Guid.NewGuid().ToString("N"));
            var location = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (location != null && !File.Exists(Path.Combine(location.FullName, "tools", "game.ps1")))
                location = location.Parent;
            Assert.That(location, Is.Not.Null);
            try
            {
                var tools = Directory.CreateDirectory(Path.Combine(root, "tools"));
                foreach (string name in new[] { "game.ps1", "build-identity.ps1" })
                    File.Copy(Path.Combine(location.FullName, "tools", name), Path.Combine(tools.FullName, name));
                var game = Directory.CreateDirectory(Path.Combine(root, "games", "Pilot"));
                File.WriteAllText(Path.Combine(game.FullName, "game.json"),
                    "{\"schemaVersion\":1,\"name\":\"Pilot\",\"engine\":\"unity\"," +
                    "\"applicationProject\":\"src/Pilot.Application/Pilot.Application.csproj\"," +
                    "\"serverProject\":\"server/Pilot.Server/Pilot.Server.csproj\"}");
                var configuration = game.CreateSubdirectory("configuration");
                string source = Path.Combine(configuration.FullName, "items.csv");
                const string destination = "client/Unity/Assets/Resources/items.csv";
                string copy = Path.Combine(game.FullName, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                File.WriteAllText(Path.Combine(configuration.FullName, "copies.json"),
                    "[{\"source\":\"configuration/items.csv\",\"destination\":\"" + destination + "\"}]");
                File.WriteAllText(source, "Id,Name\r\n1,New\r\n");
                File.WriteAllText(copy, "Id,Name\r\n1,Old\r\n");
                File.WriteAllText(copy + ".meta", "guid: 8af3b138f7794e11ac83dcc95677d603\n");

                var stale = await Run(tools.FullName, "-Check");
                Assert.That(stale.ExitCode, Is.Not.Zero, stale.Output);
                Assert.That(stale.Output, Does.Contain(destination + ":2"));
                Assert.That(File.ReadAllText(copy), Does.Contain("Old"));

                var refreshed = await Run(tools.FullName);
                Assert.That(refreshed.ExitCode, Is.Zero, refreshed.Output);
                Assert.That(File.ReadAllBytes(copy), Is.EqualTo(File.ReadAllBytes(source)));
                Assert.That(File.ReadAllText(copy + ".meta"), Is.EqualTo("guid: 8af3b138f7794e11ac83dcc95677d603\n"));

                var current = await Run(tools.FullName, "-Check");
                Assert.That(current.ExitCode, Is.Zero, current.Output);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static async Task<(int ExitCode, string Output)> Run(string tools, params string[] options)
        {
            var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(tools, "game.ps1"), "config", "-Name", "Pilot" })
                start.ArgumentList.Add(argument);
            foreach (string option in options) start.ArgumentList.Add(option);
            using var process = Process.Start(start);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw; }
            return (process.ExitCode, await output + await error);
        }
    }
}
