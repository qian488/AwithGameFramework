using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using AwithGameFrame.Core;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Logging;

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

        private void SetupCanvas()
        {
            if (_canvasReady) return;

            var canvasGo = ResourcesManager.GetInstance().Load<GameObject>(GameConstants.UI_CANVAS_PATH);
            Canvas = canvasGo.transform as RectTransform;
            Object.DontDestroyOnLoad(canvasGo);

            _bot = Canvas.Find("Bot");
            _mid = Canvas.Find("Mid");
            _top = Canvas.Find("Top");
            _system = Canvas.Find("System");

            var eventGo = ResourcesManager.GetInstance().Load<GameObject>(GameConstants.UI_EVENTSYSTEM_PATH);
            Object.DontDestroyOnLoad(eventGo);

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
            if (!_canvasReady) SetupCanvas();

            if (_panelDict.TryGetValue(panelName, out var existing))
            {
                existing.ShowMe();
                callback?.Invoke(existing as T);
                return;
            }

            ResourcesManager.GetInstance().LoadAsync<GameObject>("UI/" + panelName, (go) =>
            {
                var father = GetLayerRoot(layer);
                go.transform.SetParent(father);
                go.transform.localPosition = Vector3.zero;
                go.transform.localScale = Vector3.one;

                var rt = go.transform as RectTransform;
                rt.offsetMax = Vector2.zero;
                rt.offsetMin = Vector2.zero;

                var panel = go.GetComponent<T>();
                callback?.Invoke(panel);
                panel.ShowMe();
                _panelDict[panelName] = panel;
            });
        }

        public void HidePanel(string panelName)
        {
            if (_panelDict.TryGetValue(panelName, out var panel))
            {
                panel.HideMe();
                ResourcesManager.GetInstance().Recycle("UI/" + panelName, panel.gameObject);
                _panelDict.Remove(panelName);
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
