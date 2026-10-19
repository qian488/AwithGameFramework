using System;
using System.IO;
using System.Threading.Tasks;
using AwithGameFrame.Samples.Shared;
using NUnit.Framework;

namespace AwithGameFrame.Tests
{
    public class SharedScenarioTests
    {
        [Test]
        public void SameApplicationRestoresProgressAfterHostReopen()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AwithSharedScenario", Guid.NewGuid().ToString("N"));
            try
            {
                string result = null;
                DemoScenario.RunBlocking(directory, File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Configuration/LevelConfig.json")), text => result = text);
                Assert.That(result, Does.Contain("SHARED_SCENARIO_PASSED"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
