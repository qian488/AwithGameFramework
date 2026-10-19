using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Data.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AwithGameFrame.Tests
{
    public class HostEventJournalTests
    {
        [Test]
        public async Task JournalRecordsIndependentSessionsAndUnsubscribesOnDispose()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AwithJournal", Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "host.jsonl");
            try
            {
                var first = new GameHost();
                var second = new GameHost();
                using (var journal = new HostEventJournal(path, first, second))
                {
                    journal.Attach(first);
                    await first.StartAsync();
                    await first.StopAsync();
                    await second.StartAsync();
                    Assert.That(journal.LastError, Is.Null);
                }
                var lines = File.ReadAllLines(path);
                var entries = lines.Select(JObject.Parse).ToArray();
                Assert.That(entries.Select(e => (string)e["hostId"]).Distinct().Count(), Is.EqualTo(2));
                Assert.That(entries.Count(e => (string)e["hostId"] == first.Id && (string)e["phase"] == "Start"), Is.EqualTo(2));
                Assert.That(entries.All(e => e["timestampUtc"] != null && (double)e["elapsedSeconds"] >= 0), Is.True);
                await second.StopAsync();
                Assert.That(File.ReadAllLines(path), Is.EqualTo(lines));
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
