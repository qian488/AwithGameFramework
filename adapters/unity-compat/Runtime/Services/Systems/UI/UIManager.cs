using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using AwithGameFrame.Core;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Logging;
using Object = UnityEngine.Object;

namespace AwithGameFrame.Foundation.Systems.UI
{
    /// <summary>
    /// UI层级枚举
    /// </summary>
    public enum UILayer
    {
        Bot,
        Mid,
        Top,
        System,
    }

    /// <summary>
    /// UI管理器
    /// 负责UI面板的显示、隐藏和层级管理
    /// </summary>
    public class UIManager : BaseManager<UIManager>, IUIManager
    {
        private readonly Dictionary<string, BasePanel> _panelDict = new Dictionary<string, BasePanel>();
        private Transform _bot, _mid, _top, _system;
        private bool _canvasReady;
        private GameObject _eventSystem;
        private readonly Dictionary<string, PendingPanel> _pendingPanels = new Dictionary<string, PendingPanel>();

        private sealed class PendingPanel
        {
            public Type PanelType;
            public readonly List<Action<BasePanel>> Callbacks = new List<Action<BasePanel>>();
        }

        public RectTransform Canvas { get; private set; }

        public override int Priority => (int)ModulePriority.Features;

        public override void Initialize()
        {
            base.Initialize();
            ServiceLocator.Register<IUIManager>(this);
            SetupCanvas();
        }

        public override void PostInitialize()
        {
            base.PostInitialize();
            // 确保Canvas在PostInit阶段一定可用
            if (!_canvasReady) SetupCanvas();
        }

        public override void Shutdown()
        {
            base.Shutdown();
            var pendingPanels = new List<PendingPanel>(_pendingPanels.Values);
            _pendingPanels.Clear();
            foreach (var pending in pendingPanels) CompleteCallbacks(pending, null);
            foreach (var panel in _panelDict.Values)
                if (panel != null) Object.Destroy(panel.gameObject);
            _panelDict.Clear();
            if (Canvas != null) Object.Destroy(Canvas.gameObject);
            if (_eventSystem != null) Object.Destroy(_eventSystem);
            Canvas = null;
            _eventSystem = null;
            _bot = _mid = _top = _system = null;
            _canvasReady = false;
            ServiceLocator.Unregister<IUIManager>();
        }

        private void SetupCanvas()
        {
            if (_canvasReady) return;

            var canvasGo = ResourcesManager.GetInstance().Load<GameObject>(GameConstants.UI_CANVAS_PATH);
            if (canvasGo == null) throw new InvalidOperationException("The UI Canvas prefab is missing from Resources.");
            Canvas = canvasGo.transform as RectTransform;
            if (Canvas == null)
            {
                Object.Destroy(canvasGo);
                throw new InvalidOperationException("The UI Canvas requires a RectTransform.");
            }
            Object.DontDestroyOnLoad(canvasGo);

            _bot = Canvas.Find("Bot");
            _mid = Canvas.Find("Mid");
            _top = Canvas.Find("Top");
            _system = Canvas.Find("System");

            _eventSystem = ResourcesManager.GetInstance().Load<GameObject>(GameConstants.UI_EVENTSYSTEM_PATH);
            if (_eventSystem != null) Object.DontDestroyOnLoad(_eventSystem);

            _canvasReady = true;
            LoggingAPI.Info(LogCategory.UI, "UIManager Canvas初始化完成");
        }

        public Transform GetLayerRoot(UILayer layer)
        {
            if (!_canvasReady) SetupCanvas();

            return layer switch
            {
                UILayer.Bot => _bot,
                UILayer.Mid => _mid,
                UILayer.Top => _top,
                UILayer.System => _system,
                _ => null
            };
        }

        public void ShowPanel<T>(string panelName, UILayer layer = UILayer.Mid, UnityAction<T> callback = null) where T : BasePanel
        {
            if (State == ModuleState.Shutdown) { callback?.Invoke(null); return; }
            if (string.IsNullOrWhiteSpace(panelName)) throw new ArgumentException("Panel name is required.", nameof(panelName));
            if (!_canvasReady) SetupCanvas();

            if (_panelDict.TryGetValue(panelName, out var existing))
            {
                if (existing != null)
                {
                    existing.ShowMe();
                    callback?.Invoke(existing as T);
                    return;
                }
                _panelDict.Remove(panelName);
            }

            if (_pendingPanels.TryGetValue(panelName, out var pending))
            {
                if (pending.PanelType != typeof(T)) throw new InvalidOperationException("A panel name must use one component type while loading.");
                if (callback != null) pending.Callbacks.Add(panel => callback(panel as T));
                return;
            }
            pending = new PendingPanel { PanelType = typeof(T) };
            if (callback != null) pending.Callbacks.Add(panel => callback(panel as T));
            _pendingPanels.Add(panelName, pending);

            ResourcesManager.GetInstance().LoadAsync<GameObject>("UI/" + panelName, (go) =>
            {
                if (!_pendingPanels.TryGetValue(panelName, out var current) || !ReferenceEquals(current, pending))
                {
                    if (go != null) ResourcesManager.GetInstance().Recycle("UI/" + panelName, go);
                    return;
                }
                _pendingPanels.Remove(panelName);
                if (go == null) { CompleteCallbacks(pending, null); return; }
                var panel = go.GetComponent<T>();
                var rt = go.transform as RectTransform;
                if (panel == null || rt == null)
                {
                    ResourcesManager.GetInstance().Recycle("UI/" + panelName, go);
                    CompleteCallbacks(pending, null);
                    LoggingAPI.Error(LogCategory.UI, $"Panel '{panelName}' requires {typeof(T).Name} and RectTransform.");
                    return;
                }
                var father = GetLayerRoot(layer);
                go.transform.SetParent(father, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localScale = Vector3.one;

                rt.offsetMax = Vector2.zero;
                rt.offsetMin = Vector2.zero;

                _panelDict[panelName] = panel;
                panel.ShowMe();
                CompleteCallbacks(pending, panel);
            });
        }

        private static void CompleteCallbacks(PendingPanel pending, BasePanel panel)
        {
            var callbacks = pending.Callbacks.ToArray();
            pending.Callbacks.Clear();
            foreach (var callback in callbacks)
            {
                try { callback(panel); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }

        public void HidePanel(string panelName)
        {
            if (_pendingPanels.TryGetValue(panelName, out var pending))
            {
                _pendingPanels.Remove(panelName);
                CompleteCallbacks(pending, null);
            }
            if (_panelDict.TryGetValue(panelName, out var panel))
            {
                _panelDict.Remove(panelName);
                if (panel == null) return;
                panel.HideMe();
                ResourcesManager.GetInstance().Recycle("UI/" + panelName, panel.gameObject);
            }
        }

        public T GetPanel<T>(string panelName) where T : BasePanel
        {
            return _panelDict.TryGetValue(panelName, out var panel) ? panel as T : null;
        }

        public static void AddCustomEventListener(UIBehaviour comp, EventTriggerType type, UnityAction<BaseEventData> callback)
        {
            var trigger = comp.GetComponent<EventTrigger>() ?? comp.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(callback);
            trigger.triggers.Add(entry);
        }
    }
}
