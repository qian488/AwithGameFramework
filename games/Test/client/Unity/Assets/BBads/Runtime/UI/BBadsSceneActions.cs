using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Unity.Hosting;
using AwithGameFrame.Unity.Scenes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[assembly: InternalsVisibleTo("Test.Client.PlayMode.Tests")]

namespace Test.Client.BBads
{
    public static class BBadsSceneActions
    {
        public const string BattleScene = "Assets/BBads/Scenes/BBadsBattle.unity";
        public const string ExitScene = "Assets/Scenes/SampleScene.unity";
        internal const string RestartButton = "BBads Restart";
        internal const string ExitButton = "BBads Exit";
        internal const string EnterButton = "Open BBads";

        static readonly Dictionary<int, NavigationSession> ownedScenes = new Dictionary<int, NavigationSession>();

        internal static Func<ISceneBackend> CreateSceneBackend { get; set; } = CreateUnitySceneBackend;
        internal static GameHost GetNavigationHost(Scene scene) =>
            ownedScenes.TryGetValue(scene.handle, out NavigationSession session) ? session.Host : null;

        public static bool IsTransitioning { get; private set; }

        public static bool TryEnterBattle() =>
            SceneManager.GetActiveScene().path != BattleScene && TryLoad(BattleScene);

        public static bool TryRestart() =>
            SceneManager.GetActiveScene().path == BattleScene && TryLoad(BattleScene);

        public static bool TryExit() =>
            SceneManager.GetActiveScene().path == BattleScene && TryLoad(ExitScene);

        static bool TryLoad(string path)
        {
            if (IsTransitioning || !UnityEngine.Application.CanStreamedLevelBeLoaded(path))
                return false;

            IsTransitioning = true;
            _ = TransitionAsync(SceneManager.GetActiveScene(), path);
            return true;
        }

        static async Task TransitionAsync(Scene source, string path)
        {
            NavigationSession next = null;
            var suspendedControls = new List<Behaviour>();
            try
            {
                next = new NavigationSession(CreateSceneBackend());
                await next.Host.StartAsync();
                SuspendSceneControls(source, suspendedControls);
                // Each loaded scene owns a service. Restart can load the same path as a new
                // instance while the source remains available for rollback.
                next.Handle = await next.Scenes.LoadAsync(path);
                Scene destination = SceneManager.GetActiveScene();
                if (destination.path != path || destination.handle == source.handle)
                    throw new InvalidOperationException($"Unity did not activate a fresh scene for '{path}'.");

                if (ownedScenes.TryGetValue(source.handle, out NavigationSession previous))
                {
                    ownedScenes.Remove(source.handle);
                    try
                    {
                        await previous.Handle.UnloadAsync();
                    }
                    catch (Exception error) when (!source.isLoaded)
                    {
                        Debug.LogError($"BBads unloaded '{source.path}' but its scene cleanup failed: {error}");
                    }
                    catch
                    {
                        ownedScenes.Add(source.handle, previous);
                        throw;
                    }
                    await StopNavigationAsync(previous);
                }
                else if (source.IsValid() && source.isLoaded)
                {
                    // A scene opened directly in Play Mode has no SceneService handle.
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(source);
                    if (unload == null)
                        throw new InvalidOperationException($"Unity refused to unload '{source.path}'.");
                    await AwaitUnloadAsync(unload);
                }

                ownedScenes.Add(destination.handle, next);
                next = null;
            }
            catch (Exception error)
            {
                if (source.IsValid() && source.isLoaded)
                    SceneManager.SetActiveScene(source);
                Debug.LogError($"BBads transition to '{path}' failed: {error}");
            }
            finally
            {
                if (next != null)
                    await StopNavigationAsync(next);
                foreach (Behaviour control in suspendedControls)
                    if (control != null) control.enabled = true;
                IsTransitioning = false;
            }
        }

        static void SuspendSceneControls(Scene source, List<Behaviour> suspended)
        {
            foreach (GameObject root in source.GetRootGameObjects())
            {
                Suspend(root.GetComponentsInChildren<EventSystem>());
                Suspend(root.GetComponentsInChildren<AudioListener>());
            }

            void Suspend(Behaviour[] controls)
            {
                foreach (Behaviour control in controls)
                {
                    if (!control.isActiveAndEnabled)
                        continue;
                    suspended.Add(control);
                    control.enabled = false;
                }
            }
        }

        static Task AwaitUnloadAsync(AsyncOperation unload)
        {
            if (unload.isDone)
                return Task.CompletedTask;

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            unload.completed += _ => completion.TrySetResult(true);
            return completion.Task;
        }

        static async Task StopNavigationAsync(NavigationSession session)
        {
            try
            {
                await session.Host.StopAsync();
            }
            catch (Exception error)
            {
                Debug.LogError($"BBads navigation host shutdown failed: {error}");
            }
        }

        static void OnSceneUnloaded(Scene scene)
        {
            if (ownedScenes.TryGetValue(scene.handle, out NavigationSession session))
            {
                ownedScenes.Remove(scene.handle);
                _ = StopNavigationAsync(session);
            }
        }

        static ISceneBackend CreateUnitySceneBackend() => new UnitySceneBackend();

        sealed class NavigationSession
        {
            public NavigationSession(ISceneBackend backend)
            {
                Host = new GameHost();
                var resources = new ResourceStore(new UnityResourceLoader(), Host.Dispatcher);
                Scenes = new SceneService(Host.Context, backend, resources);
                Host.AddModule(new ResourceModule(resources));
                Host.AddModule(new SceneModule(Scenes));
            }

            public GameHost Host { get; }
            public SceneService Scenes { get; }
            public SceneHandle Handle { get; set; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            IsTransitioning = false;
            ownedScenes.Clear();
            CreateSceneBackend = CreateUnitySceneBackend;
            SceneManager.sceneLoaded -= InstallSceneButtons;
            SceneManager.sceneLoaded += InstallSceneButtons;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        static void InstallSceneButtons(Scene scene, LoadSceneMode mode)
        {
            if (scene.path == ExitScene && UnityEngine.Application.CanStreamedLevelBeLoaded(BattleScene))
                InstallEntryButton(scene);

            if (scene.path != BattleScene)
                return;

            var wave = FindInScene<global::BBads.UI.TimeWavePresenter>(scene);
            var waveText = wave != null ? wave.GetComponent<TMP_Text>() : null;
            var hud = waveText != null ? waveText.transform.parent as RectTransform : null;
            var frame = hud != null ? hud.Find("WaveLabelFrame")?.GetComponent<Image>() : null;
            if (frame == null || hud.Find(RestartButton) != null)
                return;

            AddButton(hud, waveText, frame, RestartButton, "Restart", -306f, () => TryRestart());
            Button exit = AddButton(hud, waveText, frame, ExitButton, "Exit", 306f, () => TryExit());
            exit.interactable = UnityEngine.Application.CanStreamedLevelBeLoaded(ExitScene);
        }

        static void InstallEntryButton(Scene scene)
        {
            var overlay = new GameObject("BBads Entry", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(overlay, scene);
            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = overlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            if (FindInScene<EventSystem>(scene) == null)
            {
                var events = new GameObject("BBads Entry Input", typeof(EventSystem), typeof(StandaloneInputModule));
                SceneManager.MoveGameObjectToScene(events, scene);
            }

            Button enter = AddButton((RectTransform)overlay.transform, null, null, EnterButton, "Open BBads", 0f,
                () => TryEnterBattle());
            var rect = (RectTransform)enter.transform;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.sizeDelta = new Vector2(156f, 44f);
            rect.anchoredPosition = new Vector2(-20f, -20f);
            enter.GetComponentInChildren<TMP_Text>().fontSize = 20f;
        }

        static Button AddButton(RectTransform hud, TMP_Text style, Image frame, string name, string caption,
            float x, UnityAction click)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.layer = hud.gameObject.layer;
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(hud, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(216f, 56f);
            rect.anchoredPosition = new Vector2(x, frame != null ? frame.rectTransform.anchoredPosition.y : 0f);

            var background = buttonObject.GetComponent<Image>();
            background.sprite = frame != null ? frame.sprite : null;
            background.color = frame != null ? frame.color : new Color(0.14f, 0.17f, 0.23f, 0.94f);
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(click);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.layer = buttonObject.layer;
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(rect, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            if (style != null)
            {
                label.font = style.font;
                label.fontSharedMaterial = style.fontSharedMaterial;
                label.color = style.color;
                label.fontStyle = style.fontStyle;
            }
            label.fontSize = 30f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.text = caption;
            return button;
        }

        internal static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>();
                if (component != null)
                    return component;
            }

            return null;
        }
    }
}
