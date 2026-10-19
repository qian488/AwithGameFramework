using System;
using System.Collections;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Unity.Hosting;
using AwithGameFrame.Unity.Presentation;
using AwithGameFrame.Unity.Scenes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AwithGameFrame.Samples
{
    /// <summary>Real prefab and native scene checks, called by the shared host smoke entry.</summary>
    public sealed class PresentationSmoke : MonoBehaviour
    {
        public static async Task RunAsync(GameHost host)
        {
            var bootstrap = SceneManager.GetActiveScene();
            var owner = new GameObject("Presentation smoke");
            var clock = owner.AddComponent<PresentationSmoke>();
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(owner.transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var viewRoot = new GameObject("Views", typeof(RectTransform));
            viewRoot.transform.SetParent(canvasObject.transform, false);
            var eventObject = EventSystem.current == null ? new GameObject("Smoke events", typeof(EventSystem)) : null;
            if (eventObject != null) eventObject.transform.SetParent(owner.transform, false);
            var store = new ResourceStore(new UnityResourceLoader(), host.Dispatcher);
            var views = new ViewService(host.Context, new UnityViewFactory((RectTransform)viewRoot.transform), store);
            var scenes = new SceneService(host.Context, new UnitySceneBackend(), store);
            try
            {
                var modal = await views.OpenAsync(new ViewRequest("AwithSmoke/Modal", 10, modal: true));
                var background = await views.OpenAsync(new ViewRequest("AwithSmoke/Background"));
                var lower = viewRoot.transform.GetChild(0).gameObject;
                var upper = viewRoot.transform.GetChild(1).gameObject;
                Require(lower.name.StartsWith("AwithSmoke/Background", StringComparison.Ordinal), "View layer order");
                Require(!lower.GetComponent<CanvasGroup>().interactable && !lower.GetComponent<CanvasGroup>().blocksRaycasts, "Modal blocks native background input");
                Require(upper.transform.Find("Modal shield") != null, "Native modal shield");
                Require(EventSystem.current.currentSelectedGameObject.transform.IsChildOf(upper.transform), "Native modal focus");
                Require(views.Focused == modal && views.RouteInput(new ViewInput("unhandled")), "Modal logical input barrier");
                var model = new ObservableValue<string>("initial");
                var nativeTarget = upper.GetComponentInChildren<Button>().gameObject;
                using (var bindings = new ViewBindingScope())
                {
                    bindings.Bind(model, nameof(model.Value), () => model.Value, text => nativeTarget.name = text);
                    model.Value = "updated";
                    Require(nativeTarget.name == "updated", "Explicit model binding updates a native view");
                }
                model.Value = "detached";
                Require(nativeTarget.name == "updated", "Binding disposal detaches the native setter");
                await modal.CloseAsync();
                await clock.NextFrame();
                Require(upper == null && viewRoot.transform.childCount == 1, "Closed native view destroyed");
                Require(views.Focused == background && lower.GetComponent<CanvasGroup>().interactable, "Focus and input restored");
                Require(EventSystem.current.currentSelectedGameObject.transform.IsChildOf(lower.transform), "Native background focus restored");
                await background.CloseAsync();
                await clock.NextFrame();
                Require(lower == null && viewRoot.transform.childCount == 0, "All native views destroyed");
                bool scopeClosed = false;
                try { await background.Resources.LoadAsync<GameObject>("AwithSmoke/Background"); }
                catch (ObjectDisposedException) { scopeClosed = true; }
                Require(scopeClosed, "Closed view releases its resource scope");

                var a = await scenes.LoadAsync("Assets/AwithSmokeSceneA.unity");
                var nativeA = SceneManager.GetSceneByPath("Assets/AwithSmokeSceneA.unity");
                Require(nativeA.isLoaded && SceneManager.GetActiveScene() == nativeA, "Native single scene activation");
                var b = await scenes.LoadAsync("Assets/AwithSmokeSceneB.unity", SceneLoadMode.Additive);
                var nativeB = SceneManager.GetSceneByPath("Assets/AwithSmokeSceneB.unity");
                var markerB = nativeB.GetRootGameObjects()[0];
                Require(nativeA.isLoaded && nativeB.isLoaded && scenes.Loaded.Count == 2, "Native additive scenes coexist");
                Require(await scenes.LoadAsync(a.Key) == a, "Single reuses an owned native scene");
                Require(!b.IsLoaded && !nativeB.isLoaded && markerB == null && scenes.Loaded.Count == 1, "Single unloads other owned native scenes");
                await a.UnloadAsync();
                Require(!nativeA.isLoaded && scenes.Loaded.Count == 0, "Native scene explicitly unloaded");
                Require(bootstrap.isLoaded && owner != null && host.State == GameHostState.Running, "Bootstrap and host survive scene replacement");
                Debug.Log("UNITY_PRESENTATION_SCENES_PASSED");
            }
            finally
            {
                try { await views.StopAsync(); }
                finally
                {
                    try { await scenes.StopAsync(); }
                    finally { await store.DisposeAsync(); Destroy(owner); }
                }
            }
        }

        private Task NextFrame()
        {
            var completion = new TaskCompletionSource<bool>();
            StartCoroutine(CompleteNextFrame(completion));
            return completion.Task;
        }
        private static IEnumerator CompleteNextFrame(TaskCompletionSource<bool> completion)
        { yield return null; completion.SetResult(true); }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Presentation smoke: " + message); }
    }
}
