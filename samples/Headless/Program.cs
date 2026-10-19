using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Event;
using AwithGameFrame.Core.Logging;
using AwithGameFrame.Extensions.Localization;
using AwithGameFrame.Foundation.DataPersistence;

namespace AwithGameFrame.Samples.Headless
{
    internal static class Program
    {
        public static async Task<int> Main()
        {
            var dataPath = Path.Combine(Path.GetTempPath(), "AwithGameFrame.Headless", Guid.NewGuid().ToString("N"));
            try
            {
                LoggingHost.DataPath = dataPath;
                LoggingAPI.Initialize(LoggingAPI.Preset.Minimal);
                var clock = new HeadlessTimeSource();
                TimeHelper.TimeSource = clock;

                var events = new EventCenter();
                var progressChanged = new EventId("ProgressChanged");
                int receivedLevel = 0;
                events.AddEventListener<int>(progressChanged, level => receivedLevel = level);
                events.EventTrigger(progressChanged, 7);

                var locale = new LocalizationManager { DefaultLanguage = "zh-CN" };
                locale.Initialize();
                locale.LoadDataset(new LocalizationDataset
                {
                    LanguageCode = "zh-CN",
                    Entries = new List<LocalizationEntry>
                    {
                        new LocalizationEntry { Key = "ready", Value = "共享逻辑运行成功" }
                    }
                });
                locale.SwitchLanguage("zh-CN");

                var config = new DataPersistenceConfig { DataRootPath = dataPath };
                if (await DataPersistenceAPI.InitializeAsync(config) != DataOperationResult.Success) return 1;
                if (await DataPersistenceAPI.SaveGameDataAsync("progress", new Progress { level = receivedLevel }) != DataOperationResult.Success) return 1;
                var (progress, result) = await DataPersistenceAPI.LoadGameDataAsync<Progress>("progress");
                if (result != DataOperationResult.Success || progress.level != 7) return 1;

                for (int i = 0; i < 60; i++)
                {
                    clock.Advance(1f / 60f);
                    PerformanceMonitor.GetInstance().Tick(clock.UnscaledDeltaTime);
                }

                Console.WriteLine($"{locale.GetText("ready")}: level={progress.level}, frames=60, Unity dependencies=0");
                locale.Shutdown();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                await DataPersistenceAPI.ShutdownAsync();
                TimeHelper.TimeSource = null;
                LoggingManager.GetInstance().Shutdown();
                if (Directory.Exists(dataPath)) Directory.Delete(dataPath, true);
            }
        }

        [Serializable]
        private sealed class Progress
        {
            public int level;
        }

        private sealed class HeadlessTimeSource : ITimeSource
        {
            public float GameTime { get; private set; }
            public float UnscaledGameTime { get; private set; }
            public float DeltaTime { get; private set; }
            public float UnscaledDeltaTime { get; private set; }
            public float FixedDeltaTime => 1f / 60f;
            public float TimeScale { get; set; } = 1f;
            public int TargetFrameRate { get; set; } = 60;

            public void Advance(float elapsed)
            {
                UnscaledDeltaTime = elapsed;
                DeltaTime = elapsed * TimeScale;
                UnscaledGameTime += UnscaledDeltaTime;
                GameTime += DeltaTime;
            }
        }
    }
}
