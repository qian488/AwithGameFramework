using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using AwithGameFrame.Core;
using AwithGameFrame.Foundation;
using AwithGameFrame.Foundation.Logging;
using AwithGameFrame.Foundation.DataPersistence;

namespace AwithGameFrame.Foundation.Systems.UI
{
    /// <summary>
    /// UI层级枚举
    /// </summary>
    public enum UILayer
    {
        /// <summary>底层</summary>
        Bot,
        /// <summary>中层</summary>
        Mid,
        /// <summary>顶层</summary>
        Top,
        /// <summary>系统层</summary>
        System,
    }

    /// <summary>
    /// UI管理器
    /// 负责UI面板的显示、隐藏和层级管理
    /// </summary>
    public class UIManager : BaseManager<UIManager>
    {
        #region 字段
        /// <summary>面板字典</summary>
        private Dictionary<string,BasePanel> panelDictionary = new Dictionary<string,BasePanel>();

        private Transform bot;
        private Transform mid;
        private Transform top;
        private Transform system;

        public RectTransform canvas;
        #endregion

        #region 构造函数
        public UIManager()
        {
            FrameworkLogger.LogUI("UIManager初始化开始");
            
            GameObject go = ResourcesManager.GetInstance().Load<GameObject>(GameConstants.UI_CANVAS_PATH);
            canvas = go.transform as RectTransform;
            GameObject.DontDestroyOnLoad(go);

            bot = canvas.Find("Bot");
            mid = canvas.Find("Mid");
            top = canvas.Find("Top");
            system = canvas.Find("System");

            go = ResourcesManager.GetInstance().Load<GameObject>(GameConstants.UI_EVENTSYSTEM_PATH);
            GameObject.DontDestroyOnLoad(go);
            
            FrameworkLogger.LogUI("UIManager初始化完成");
        }
        #endregion

        #region 公共方法
        public Transform GetUILayerFather(UILayer layer)
        {
            switch(layer)
            {
                case UILayer.Bot:
                    return this.bot;
                case UILayer.Mid:
                    return this.mid;
                case UILayer.Top:
                    return this.top;
                case UILayer.System:
                    return this.system;
            }
            return null;
        }

        /// <summary>
        /// 显示面板
        /// </summary>
        /// <typeparam name="T">面板脚本类型</typeparam>
        /// <param name="panelName">面板名字</param>
        /// <param name="layer">面板所在层级</param>
        /// <param name="callback">面板创建后所作的事</param>
        public void ShowPanel<T>(string panelName, UILayer layer = UILayer.Mid, UnityAction<T> callback = null) where T : BasePanel 
        {
            FrameworkLogger.LogUI($"显示面板: {panelName}, 层级: {layer}");
            
            if (panelDictionary.ContainsKey(panelName))
            {
                panelDictionary[panelName].ShowMe();
                if (callback != null)
                {
                    callback(panelDictionary[panelName] as T);
                }
                FrameworkLogger.LogUI($"面板已存在，直接显示: {panelName}");
                return;
            }

            ResourcesManager.GetInstance().LoadAsync<GameObject>("UI/" + panelName, (go) =>
            {
                Transform father = bot;
                switch(layer)
                {
                    case UILayer.Mid:
                        father = mid;
                        break;
                    case UILayer.Top:
                        father = top;
                        break;
                    case UILayer.System:
                        father = system;
                        break;
                }
                go.transform.SetParent(father);
                go.transform.localPosition = Vector3.zero;
                go.transform.localScale = Vector3.one;
                (go.transform as RectTransform).offsetMax = Vector2.one;
                (go.transform as RectTransform).offsetMin = Vector2.one;

                T panel = go.GetComponent<T>();
                if(callback != null) callback(panel);
                panel.ShowMe();
                panelDictionary.Add(panelName, panel);
                FrameworkLogger.LogUI($"面板加载完成并显示: {panelName}");
            });
        }

        public void HidePanel(string panelName)
        {
            FrameworkLogger.LogUI($"隐藏面板: {panelName}");
            
            if (panelDictionary.ContainsKey(panelName))
            {
                panelDictionary[panelName].HideMe();
                ResourcesManager.GetInstance().Recycle("UI/" + panelName, panelDictionary[panelName].gameObject);
                panelDictionary.Remove(panelName);
                FrameworkLogger.LogUI($"面板已隐藏并回收: {panelName}");
            }
            else
            {
                FrameworkLogger.Warn($"尝试隐藏不存在的面板: {panelName}");
            }
        }

        public T GetPanel<T>(string panelName) where T : BasePanel
        {
            if (panelDictionary.ContainsKey(panelName))
            {
                return panelDictionary[panelName] as T;
            }
            return null;
        }

        /// <summary>
        /// 给控件增添自定义事件
        /// </summary>
        /// <param name="UIComponent">控件对象</param>
        /// <param name="type">事件类型</param>
        /// <param name="callback">事件的回调</param>
        public static void AddCustomEventListener(UIBehaviour UIComponent, EventTriggerType type, UnityAction<BaseEventData> callback)
        {
            EventTrigger eventTrigger = UIComponent.GetComponent<EventTrigger>();
            if (eventTrigger == null)
            {
                eventTrigger = UIComponent.gameObject.AddComponent<EventTrigger>();
            }
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = type;
            entry.callback.AddListener(callback);

            eventTrigger.triggers.Add(entry);
        }
        
        /// <summary>
        /// 获取设置键名
        /// </summary>
        protected string GetSettingsKey()
        {
            return "UISettings";
        }
        
        /// <summary>
        /// 获取存储类型
        /// </summary>
        protected StorageType GetStorageType()
        {
            return StorageType.PlayerPrefs;
        }
        
        /// <summary>
        /// 设置加载完成回调
        /// </summary>
        protected void OnSettingsLoaded()
        {
            // 应用设置到UI系统
            ApplySettingsToUI();
            FrameworkLogger.LogUI("UI设置已应用");
        }
        
        /// <summary>
        /// 设置变更回调
        /// </summary>
        protected void OnSettingsChangedInternal(UISettings settings)
        {
            // 设置变更时自动应用
            ApplySettingsToUI();
            FrameworkLogger.LogUI("UI设置已更新并应用");
        }
        
        /// <summary>
        /// 应用设置到UI系统
        /// </summary>
        private void ApplySettingsToUI()
        {
            // 这里可以添加从数据持久化系统加载设置的逻辑
            // 暂时使用默认值，后续可以集成SettingsHelper
            
            FrameworkLogger.LogUI("UI设置已应用");
        }
        
        /// <summary>
        /// 更新UI设置
        /// </summary>
        /// <param name="updateAction">更新委托</param>
        public async System.Threading.Tasks.Task<bool> UpdateUISettingsAsync(System.Action<UISettings> updateAction)
        {
            try
            {
                // 使用SettingsHelper更新设置
                var settings = new UISettings();
                updateAction?.Invoke(settings);
                
                var result = await DataPersistenceAPI.SaveAsync<UISettings>(GetSettingsKey(), settings, GetStorageType());
                if (result == DataOperationResult.Success)
                {
                    OnSettingsChangedInternal(settings);
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                FrameworkLogger.Error($"更新UI设置失败: {ex.Message}", LogCategory.Core);
                return false;
            }
        }
        
        /// <summary>
        /// 重置UI设置
        /// </summary>
        public async System.Threading.Tasks.Task<bool> ResetUISettingsAsync()
        {
            try
            {
                // 使用SettingsHelper重置设置
                var result = await DataPersistenceAPI.DeleteAsync(GetSettingsKey(), GetStorageType());
                if (result == DataOperationResult.Success)
                {
                    var defaultSettings = new UISettings();
                    OnSettingsChangedInternal(defaultSettings);
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                FrameworkLogger.Error($"重置UI设置失败: {ex.Message}", LogCategory.Core);
                return false;
            }
        }
        #endregion
    }
    
    /// <summary>
    /// UI设置数据类
    /// </summary>
    [System.Serializable]
    public class UISettings
    {
        public float UIScale = 1.0f;
        public bool ShowFPS = false;
        public bool ShowDebugInfo = false;
        public string Language = "zh-CN";
        public bool EnableAnimations = true;
        public float AnimationSpeed = 1.0f;
    }
}
