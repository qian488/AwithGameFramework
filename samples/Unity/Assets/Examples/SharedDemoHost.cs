using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Foundation;
using AwithGameFrame.Foundation.DataPersistence;
using AwithGameFrame.Samples.Shared;
using AwithGameFrame.Unity.Audio;
using AwithGameFrame.Unity.Hosting;
using AwithGameFrame.Unity.Input;
using AwithGameFrame.Unity.Presentation;
using AwithGameFrame.Unity.Scenes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AwithGameFrame.Samples
{
    public sealed class SharedDemoHost : GameHostBehaviour
    {
        private DemoExperience experience;
        private byte[] configuration;
        private string smokeDirectory;

        protected override Task OnFailureAsync(Exception exception)
        {
            if (Environment.GetCommandLineArgs().Contains("-awith-shared-smoke"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(1);
#else
                Application.Quit(1);
#endif
            }
            return Task.CompletedTask;
        }

        protected override void ConfigureHost(GameHost host)
        {
            var asset = Resources.Load<TextAsset>("AwithDemo/LevelConfig");
            if (asset == null) throw new FileNotFoundException("Generate shared configuration first.");
            configuration = asset.bytes;
            Resources.UnloadAsset(asset);
            var cameraObject = new GameObject("DemoCamera");
            cameraObject.transform.SetParent(transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
            cameraObject.AddComponent<AudioListener>();
            var canvas = new GameObject("Demo Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1000, 700);
            var viewRoot = new GameObject("Demo Views", typeof(RectTransform)); viewRoot.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)viewRoot.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            if (EventSystem.current == null)
            {
                var events = new GameObject("Demo Events", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
            bool smoke = Environment.GetCommandLineArgs().Contains("-awith-shared-smoke");
            if (smoke) smokeDirectory = Path.Combine(Application.temporaryCachePath, "AwithComposedValidation", Guid.NewGuid().ToString("N"));
            var directory = smokeDirectory ?? Path.Combine(Application.persistentDataPath, "AwithSharedDemo");
            var resources = new ResourceStore(new UnityResourceLoader(), host.Dispatcher);
            var keys = new DemoAssetKeys("AwithDemo/Menu", "AwithDemo/Playing", "AwithDemo/Settings", "Assets/AwithDemoLevel.unity", "AwithDemo/Tone");
            var factory = new UnityViewFactory(rect, (request, content) =>
            {
                var hooks = content.GetComponent<DemoUnityView>();
                if (hooks == null) throw new InvalidOperationException("Prepare the complete sample prefabs first.");
                hooks.Configure(experience, request.Key == keys.Menu ? "menu" : request.Key == keys.Settings ? "settings" : "playing");
            });
            experience = DemoExperience.Install(host, configuration, directory, resources, factory, new UnitySceneBackend(),
                new UnityAudioBackend(transform), new UnityInputBackend(), keys);
            if (smoke) experience.InputBackend.ScenarioFrame = new System.Collections.Generic.Dictionary<string, float>();
        }

        protected override async Task OnStartedAsync()
        {
            await using (var scope = new ResourceScope(Host.Services.Get<ResourceStore>()))
            {
                var first = await scope.LoadAsync<TextAsset>("AwithDemo/LevelConfig");
                var second = await scope.LoadAsync<TextAsset>("AwithDemo/LevelConfig");
                if (!ReferenceEquals(first.Value, second.Value)) throw new InvalidOperationException("Resource leases did not share a load.");
            }
            if (!Environment.GetCommandLineArgs().Contains("-awith-shared-smoke")) return;
            var directory = Path.Combine(Application.temporaryCachePath, "AwithValidation", Guid.NewGuid().ToString("N"));
            int exitCode = 0;
            try
            {
                FoundationAPI.Initialize();
                if (ProviderManager.AnimationProvider.GetType().FullName != "AwithGameFrame.Foundation.Providers.DOTweenProvider")
                    throw new InvalidOperationException("The optional DOTween provider was not registered in this build.");
                var database = DatabaseFactory.CreateProvider(DatabaseType.SQLite);
                if (database == null) throw new InvalidOperationException("The optional SQLite provider was not registered in this build.");
                try
                {
                    if (await database.InitializeAsync("Data Source=:memory:;Version=3;") != DataOperationResult.Success ||
                        await database.ExecuteAsync("CREATE TABLE adapter_probe (id INTEGER PRIMARY KEY)") != DataOperationResult.Success ||
                        !await database.TableExistsAsync("adapter_probe"))
                        throw new InvalidOperationException("The optional SQLite provider could not open and query a native database.");
                }
                finally { await database.CloseAsync(); }
                Debug.Log("UNITY_OPTIONAL_PROVIDERS_PASSED");
                await PresentationSmoke.RunAsync(Host);
                await InputAudioSmoke.RunAsync(Host);
                await AssetBundleSmoke.RunAsync(Host);
                await DemoExperienceScenario.RunAsync(experience, message => Debug.Log(message));
                await DemoScenario.RunAsync(directory, configuration, message => Debug.Log(message));
                await ShutdownAsync();
                Debug.Log("UNITY_SHARED_HOST_PASSED");
            }
            catch (Exception exception) { Debug.LogException(exception); exitCode = 1; }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                if (smokeDirectory != null && Directory.Exists(smokeDirectory)) Directory.Delete(smokeDirectory, true);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(exitCode);
#else
                Application.Quit(exitCode);
#endif
            }
        }

    }
}
