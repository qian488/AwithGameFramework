using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Samples.Shared;
using AwithGameFrame.Unity.Hosting;
using UnityEngine;

namespace AwithGameFrame.Samples
{
    public sealed class SharedDemoHost : GameHostBehaviour
    {
        private DemoApplication application;
        private byte[] configuration;
        private bool busy;
        private string error;

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
            host.AddModule(new ResourceModule(new ResourceStore(new UnityResourceLoader(), host.Dispatcher)));
            var asset = Resources.Load<TextAsset>("AwithDemo/LevelConfig");
            if (asset == null) throw new FileNotFoundException("Generate shared configuration first.");
            configuration = asset.bytes;
            Resources.UnloadAsset(asset);
            application = new DemoApplication(Path.Combine(Application.persistentDataPath, "AwithSharedDemo"), configuration);
            host.AddModule(application);
            var cameraObject = new GameObject("DemoCamera");
            cameraObject.transform.SetParent(transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
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
                await DemoScenario.RunAsync(directory, configuration, message => Debug.Log(message));
                await ShutdownAsync();
                Debug.Log("UNITY_SHARED_HOST_PASSED");
            }
            catch (Exception exception) { Debug.LogException(exception); exitCode = 1; }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(exitCode);
#else
                Application.Quit(exitCode);
#endif
            }
        }

        private void OnGUI()
        {
            if (application == null) return;
            GUILayout.BeginArea(new Rect(40, 40, 600, 350), GUI.skin.box);
            GUILayout.Label("AwithGameFrame / Unity");
            GUILayout.Label("Phase: " + application.Phase + " / Coins: " + application.Coins + " / Completed: " + application.CompletedLevels);
            GUILayout.Label("Frames: " + application.Frames);
            GUI.enabled = IsReady && !busy && application.Phase == "Menu";
            if (GUILayout.Button("Enter level")) RunCommand(() => application.EnterLevelAsync());
            GUI.enabled = IsReady && !busy && application.Phase == "Playing";
            if (GUILayout.Button("Complete level and save")) RunCommand(() => application.CompleteLevelAsync());
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(error)) GUILayout.Label(error);
            if (Failure != null) GUILayout.Label(Failure.Message);
            GUILayout.Label("Progress is restored when this scene is reopened.");
            GUILayout.EndArea();
        }

        private async void RunCommand(Func<Task> command)
        {
            if (busy) return;
            busy = true;
            try { error = null; await command(); }
            catch (Exception exception) { error = exception.Message; Debug.LogException(exception); }
            finally { busy = false; }
        }
    }
}
