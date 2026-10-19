using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AwithGameFrame.Unity.Presentation
{
    /// <summary>Instantiate Resources prefab keys through a ResourceScope under a dedicated uGUI view root.</summary>
    public sealed class UnityViewFactory : IViewFactory
    {
        private readonly RectTransform parent;
        private readonly Action<ViewRequest, GameObject> initialize;
        private readonly int thread = Environment.CurrentManagedThreadId;
        public UnityViewFactory(RectTransform parent) : this(parent, null) { }
        public UnityViewFactory(RectTransform parent, Action<ViewRequest, GameObject> initialize)
        { this.parent = parent != null ? parent : throw new ArgumentNullException(nameof(parent)); this.initialize = initialize; }

        public async Task<IView> CreateAsync(ViewRequest request, ResourceScope resources, CancellationToken cancellationToken)
        {
            VerifyThread();
            var prefab = await resources.LoadAsync<GameObject>(request.Key, cancellationToken);
            VerifyThread();
            cancellationToken.ThrowIfCancellationRequested();
            GameObject root = null;
            try
            {
                if (parent == null) throw new ObjectDisposedException("Unity view parent");
                root = new GameObject(request.Key + " [view]", typeof(RectTransform), typeof(CanvasGroup));
                root.SetActive(false);
                var rect = (RectTransform)root.transform;
                rect.SetParent(parent, false); Stretch(rect);
                if (request.Modal)
                {
                    var shield = new GameObject("Modal shield", typeof(RectTransform), typeof(Image));
                    shield.transform.SetParent(rect, false); Stretch((RectTransform)shield.transform);
                    shield.GetComponent<Image>().color = new Color(0, 0, 0, 0.35f);
                    shield.GetComponent<Image>().raycastTarget = true;
                }
                var content = UnityEngine.Object.Instantiate(prefab.Value, rect, false);
                if (!(content.transform is RectTransform)) throw new InvalidOperationException("A Unity UI view prefab must have a RectTransform root.");
                initialize?.Invoke(request, content);
                return new View(root, content.GetComponent<UnityViewBehaviour>(), thread);
            }
            catch { if (root != null) UnityEngine.Object.Destroy(root); throw; }
        }

        private void VerifyThread() { if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Unity views must run on the creating Unity thread."); }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }

        private sealed class View : IView
        {
            private readonly GameObject root;
            private readonly UnityViewBehaviour hooks;
            private readonly CanvasGroup group;
            private readonly int thread;
            private bool disposed;
            public View(GameObject root, UnityViewBehaviour hooks, int thread) { this.root = root; this.hooks = hooks; this.thread = thread; group = root.GetComponent<CanvasGroup>(); }
            private void Verify() { if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Unity view lifecycle requires the creating thread."); }
            public async Task ShowAsync(CancellationToken token)
            { Verify(); token.ThrowIfCancellationRequested(); root.SetActive(true); if (hooks != null) await hooks.ShowAsync(token); }
            public async Task HideAsync(CancellationToken token)
            {
                Verify();
                try { if (!disposed && hooks != null) await hooks.HideAsync(token); }
                finally { Verify(); if (root != null) root.SetActive(false); }
            }
            public void ApplyState(ViewState state)
            {
                Verify(); if (disposed || root == null) return;
                root.transform.SetSiblingIndex(state.Order);
                group.interactable = state.IsInputEnabled; group.blocksRaycasts = state.IsInputEnabled;
                var system = EventSystem.current;
                if (system != null)
                {
                    var selected = system.currentSelectedGameObject;
                    bool ownsSelection = selected != null && selected.transform.IsChildOf(root.transform);
                    if (!state.IsFocused && ownsSelection) system.SetSelectedGameObject(null);
                    else if (state.IsFocused && !ownsSelection)
                    {
                        foreach (var selectable in root.GetComponentsInChildren<Selectable>())
                        {
                            if (!selectable.IsActive() || !selectable.IsInteractable()) continue;
                            system.SetSelectedGameObject(selectable.gameObject); break;
                        }
                    }
                }
                if (hooks != null) hooks.OnPresentationChanged(state);
            }
            public bool HandleInput(ViewInput input) { Verify(); return !disposed && hooks != null && hooks.HandleInput(input); }
            public ValueTask DisposeAsync()
            {
                Verify(); if (disposed) return default;
                disposed = true;
                if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
                return default;
            }
        }
    }
}
