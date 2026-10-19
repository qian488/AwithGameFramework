using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Bootstrap;
using AwithGameFrame.Core.Config;
using AwithGameFrame.Foundation;
using AwithGameFrame.Foundation.Pool;
using AwithGameFrame.Foundation.Systems.Audio;
using AwithGameFrame.Foundation.Systems.InputSystem;
using AwithGameFrame.Foundation.Systems.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AwithGameFrame.Samples.Editor
{
    /// <summary>Batch entry: -executeMethod AwithGameFrame.Samples.Editor.UnityAdapterSmoke.Run (without -quit).</summary>
    [InitializeOnLoad]
    public static class UnityAdapterSmoke
    {
        private const string Key = "AwithGameFrame.UnityAdapterSmoke";
        private const string FixtureRoot = "Assets/__AwithAdapterSmoke";
        private static IEnumerator checks;
        private static int assertions;
        private static int lastFrame = -1;
        private static string unexpectedError;

        static UnityAdapterSmoke()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Update;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (SessionState.GetBool(Key, false) && SessionState.GetInt(Key + ".result", -1) < 0 &&
                    (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) unexpectedError = message;
            };
        }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run the smoke from edit mode.");
            if (AssetDatabase.IsValidFolder(FixtureRoot)) throw new InvalidOperationException("Smoke fixture directory already exists.");
            var config = Resources.Load<FrameworkConfig>("FrameworkConfig");
            if (config == null) throw new InvalidOperationException("The sample requires Resources/FrameworkConfig.asset.");
            SessionState.SetString(Key + ".config", JsonUtility.ToJson(config));
            CreateFixtures();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetInt(Key + ".result", -1);
            SessionState.SetFloat(Key + ".started", (float)EditorApplication.timeSinceStartup);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                lastFrame = -1;
                checks = RunChecks();
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetInt(Key + ".result", -1) >= 0)
            {
                var config = Resources.Load<FrameworkConfig>("FrameworkConfig");
                if (config != null)
                {
                    JsonUtility.FromJsonOverwrite(SessionState.GetString(Key + ".config", "{}"), config);
                    EditorUtility.ClearDirty(config);
                }
                AssetDatabase.DeleteAsset(FixtureRoot);
                var result = SessionState.GetInt(Key + ".result", 1);
                SessionState.EraseBool(Key);
                if (Application.isBatchMode) EditorApplication.Exit(result);
            }
        }

        private static void Update()
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (SessionState.GetInt(Key + ".result", -1) >= 0) return;
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".started", 0) > 120)
            {
                Finish(new TimeoutException("Unity adapter smoke exceeded 120 seconds."));
                return;
            }
            if (!EditorApplication.isPlaying || checks == null) return;
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                if (unexpectedError != null) throw new InvalidOperationException("Unity reported an error during smoke: " + unexpectedError);
                if (!checks.MoveNext()) Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            checks = null;
            SessionState.SetInt(Key + ".result", error == null ? 0 : 1);
            if (error == null) Debug.Log($"Unity adapter smoke passed: {assertions} assertions with real Unity objects.");
            else Debug.LogException(error);
            EditorApplication.isPlaying = false;
        }

        private static IEnumerator RunChecks()
        {
            assertions = 0;
            FrameworkBootstrap.Shutdown();
            var config = ConfigManager.GetInstance().FrameworkConfig;
            config.enableAudioModule = config.enableInputModule = config.enablePoolModule = config.enableUIModule = true;
            config.enableLoggingModule = false;
            config.poolEnableAutoCleanup = false;
            config.poolDefaultMaxSize = 2;
            config.defaultVolume = 0.25f;
            config.maxConcurrentSFX = 1;
            config.targetFrameRate = 45;
            var entry = CreateEntry();
            entry.Bootstrap();
            var pool = PoolManager.GetInstance();
            var ui = UIManager.GetInstance();
            var music = MusicManager.GetInstance();
            var input = InputManager.GetInstance();
            Check(Application.targetFrameRate == 45, "Configured frame rate is applied.");
            Check(PoolManagerAPI.IsInitialized(), "Bootstrap registers the pool facade.");
            var canvas = ui.Canvas;
            entry.SetApplicationPaused(true);
            Check(!input.IsEnabled && FrameworkBootstrap.RegisteredModules.All(module => module.State == ModuleState.Running), "Backgrounding retains running modules and suspends input.");
            entry.SetApplicationPaused(false);
            Check(input.IsEnabled && ui.Canvas == canvas, "Foregrounding restores input and retains the canvas.");
            input.SetEnabled(false);
            entry.SetApplicationPaused(true);
            entry.SetApplicationPaused(false);
            Check(!input.IsEnabled, "Foregrounding preserves deliberately disabled input.");
            input.SetEnabled(true);

            var templateParent = new GameObject("Smoke template parent");
            var template = new GameObject("Smoke template");
            template.transform.SetParent(templateParent.transform);
            pool.WarmupPool("smoke-warmup", template, 2);
            Check(template.activeSelf && template.transform.parent == templateParent.transform, "Warmup does not deactivate or reparent the supplied template.");
            Check(pool.GetPoolCount("smoke-warmup") == 2, "Warmup creates exactly the requested number of instances.");
            var first = pool.GetGameObjectSync("smoke-warmup");
            var second = pool.GetGameObjectSync("smoke-warmup");
            Check(first != null && second != null && first != second, "Separate rentals receive separate objects.");
            pool.PushGameObject("smoke-warmup", first);
            pool.PushGameObject("smoke-warmup", first);
            Check(pool.GetPoolCount("smoke-warmup") == 1 && PoolData(pool, "smoke-warmup").currentActive == 1, "Duplicate return does not duplicate availability or decrement another lease.");
            Check(pool.GetGameObjectSync("smoke-warmup") == first && pool.GetGameObjectSync("smoke-warmup") == null, "An available object is delivered only once.");
            pool.PushGameObject("smoke-warmup", first);
            pool.PushGameObject("smoke-warmup", second);
            Check(PoolData(pool, "smoke-warmup").currentActive == 0, "All returns leave zero active rentals.");
            Object.Destroy(templateParent);

            GameObject asyncFirst = null, asyncSecond = null;
            var deliveries = 0;
            pool.GetGameObject("__AwithSmokeCube", value => { asyncFirst = value; deliveries++; });
            pool.GetGameObject("__AwithSmokeCube", value => { asyncSecond = value; deliveries++; });
            while (deliveries < 2) yield return null;
            Check(asyncFirst != null && asyncSecond != null && asyncFirst != asyncSecond, "Concurrent asynchronous rentals are distinct.");
            Check(pool.GetPoolCount("__AwithSmokeCube") == 0 && PoolData(pool, "__AwithSmokeCube").currentActive == 2, "New asynchronous deliveries remain rented rather than also available.");
            pool.PushGameObject("__AwithSmokeCube", asyncFirst);
            pool.PushGameObject("__AwithSmokeCube", asyncSecond);

            BasePanel panelA = null, panelB = null;
            var callbacks = 0;
            ui.ShowPanel<BasePanel>("__AwithSmokePanel", callback: value => { panelA = value; callbacks++; });
            ui.ShowPanel<BasePanel>("__AwithSmokePanel", callback: value => { panelB = value; callbacks++; });
            while (callbacks < 2) yield return null;
            Check(panelA != null && panelA == panelB && ui.GetLayerRoot(UILayer.Mid).childCount == 1, "Repeated asynchronous opens share one panel instance.");
            ui.HidePanel("__AwithSmokePanel");
            Check(ui.GetPanel<BasePanel>("__AwithSmokePanel") == null && !panelA.gameObject.activeSelf, "Hide removes and recycles the panel.");
            var canceled = 0;
            ui.ShowPanel<BasePanel>("__AwithSmokeLatePanel", callback: value => { Check(value == null, "Closing an in-flight panel completes its callback with null."); canceled++; });
            ui.HidePanel("__AwithSmokeLatePanel");
            Check(canceled == 1, "In-flight close completes once.");
            BasePanel reopened = null;
            ui.ShowPanel<BasePanel>("__AwithSmokeLatePanel", callback: value => reopened = value);
            while (reopened == null) yield return null;
            Check(ui.GetPanel<BasePanel>("__AwithSmokeLatePanel") == reopened && canceled == 1, "An old canceled load cannot overwrite a subsequent open.");
            ui.HidePanel("__AwithSmokeLatePanel");

            config.targetFrameRate = 72;
            config.poolDefaultMaxSize = 4;
            config.defaultVolume = 0.4f;
            var changeDeadline = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < changeDeadline) yield return null;
            Check(Application.targetFrameRate == 72 && PoolData(pool, "smoke-warmup").maxSize == 4, "The Unity frame driver applies programmatic configuration changes.");

            AudioSource sfx = null;
            var rejected = false;
            music.PlaySFX("__AwithSmokeTone", true, value => sfx = value);
            music.PlaySFX("__AwithSmokeTone", true, value => rejected = value == null);
            Check(rejected, "SFX concurrency includes pending loads.");
            while (sfx == null) yield return null;
            Check(Mathf.Approximately(sfx.volume, 0.4f), "Configured volume reaches a real AudioSource.");
            var wasPlaying = sfx.isPlaying;
            entry.SetApplicationPaused(true);
            yield return null;
            Check(sfx != null && !sfx.isPlaying && GetField<List<AudioSource>>(music, "_sfxList").Count == 1, "Application pause retains audio sources rather than recycling paused playback.");
            entry.SetApplicationPaused(false);
            yield return null;
            Check(!wasPlaying || sfx.isPlaying, "Audio that was playing resumes on foreground.");

            input.Initialize();
            music.Initialize();
            Check(UpdateSubscriptionCount(input) == 1 && UpdateSubscriptionCount(music) == 1, "Repeated initialization does not duplicate update subscriptions.");
            input.Shutdown();
            music.Shutdown();
            Check(UpdateSubscriptionCount(input) == 0 && UpdateSubscriptionCount(music) == 0, "Shutdown removes input and audio update subscriptions.");
            yield return null;
            Check(sfx == null && GetField<Dictionary<string, AudioClip>>(music, "_clipCache").Count == 0, "Audio shutdown destroys sources and releases cached references.");
            input.Initialize(); input.PostInitialize();
            music.Initialize(); music.PostInitialize();
            Check(UpdateSubscriptionCount(input) == 1 && UpdateSubscriptionCount(music) == 1, "A restarted session installs one subscription per service.");

            Object.Destroy(entry.gameObject);
            yield return null;
            yield return null;
            Check(canvas == null && !PoolManagerAPI.IsInitialized(), "Final shutdown destroys UI roots and releases the pool facade.");
            config.enableAudioModule = config.enableInputModule = config.enablePoolModule = config.enableUIModule = false;
            entry = CreateEntry();
            entry.Bootstrap();
            Check(!FrameworkBootstrap.RegisteredModules.Any(module => module is MusicManager || module is InputManager || module is PoolManager || module is UIManager), "Disabled modules are omitted from startup.");
            Object.Destroy(entry.gameObject);
            yield return null;
        }

        private static FrameworkEntry CreateEntry()
        {
            var go = new GameObject("Smoke framework entry");
            go.SetActive(false);
            var entry = go.AddComponent<FrameworkEntry>();
            typeof(FrameworkEntry).GetField("_autoBootstrap", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(entry, false);
            go.SetActive(true);
            return entry;
        }

        private static T GetField<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static PoolData PoolData(PoolManager manager, string name) => GetField<Dictionary<string, PoolData>>(manager, "poolDictionary")[name];
        private static int UpdateSubscriptionCount(object target)
        {
            var controller = GetField<MonoControl>(MonoManager.GetInstance(), "controller");
            var listeners = GetField<Delegate>(controller, "updateEvent");
            return listeners?.GetInvocationList().Count(listener => ReferenceEquals(listener.Target, target)) ?? 0;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Smoke assertion failed: " + message);
            assertions++;
        }

        private static void CreateFixtures()
        {
            Directory.CreateDirectory(FixtureRoot + "/Resources/UI");
            Directory.CreateDirectory(FixtureRoot + "/Resources/Music/SFX");
            AssetDatabase.Refresh();
            var cube = new GameObject("__AwithSmokeCube");
            PrefabUtility.SaveAsPrefabAsset(cube, FixtureRoot + "/Resources/__AwithSmokeCube.prefab");
            Object.DestroyImmediate(cube);
            foreach (var name in new[] { "__AwithSmokePanel", "__AwithSmokeLatePanel" })
            {
                var panel = new GameObject(name, typeof(RectTransform), typeof(BasePanel));
                PrefabUtility.SaveAsPrefabAsset(panel, FixtureRoot + "/Resources/UI/" + name + ".prefab");
                Object.DestroyImmediate(panel);
            }
            const int sampleRate = 22050, samples = sampleRate * 8;
            using (var writer = new BinaryWriter(File.Create(FixtureRoot + "/Resources/Music/SFX/__AwithSmokeTone.wav")))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate * 2);
                writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * Math.PI * 440 / sampleRate) * 1000));
            }
            AssetDatabase.Refresh();
        }
    }
}
