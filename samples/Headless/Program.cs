using System;
using System.IO;
using AwithGameFrame.Samples.Shared;

namespace AwithGameFrame.Samples.Headless
{
    internal static class Program
    {
        public static int Main()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AwithGameFrame.Headless", Guid.NewGuid().ToString("N"));
            try
            {
                var configuration = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Configuration", "LevelConfig.json"));
                DemoScenario.RunBlocking(directory, configuration, Console.WriteLine);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
