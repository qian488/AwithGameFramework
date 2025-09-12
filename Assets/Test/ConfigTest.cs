using UnityEngine;
using UnityEngine.UI;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Config;
using AwithGameFrame.Core.Logging;
using UnityEngine.EventSystems;

namespace AwithGameFrame.Test
{
    /// <summary>
    /// 配置系统测试脚本
    /// 挂载在空物体上，自动创建UI进行测试
    /// </summary>
    public class ConfigTest : MonoBehaviour
    {
        [Header("测试设置")]
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool showUI = true;
        
        [Header("UI设置")]
        [SerializeField] private Font uiFont;
        [SerializeField] private int fontSize = 14;
        
        private Canvas testCanvas;
        private ScrollRect scrollRect;
        private Text logText;
        private Button[] testButtons;
        
        private string logContent = "";
        
        void Start()
        {
            if (autoStart)
            {
                InitializeTest();
            }
        }
        
        /// <summary>
        /// 初始化测试
        /// </summary>
        private void InitializeTest()
        {
            LoggingAPI.Info(LogCategory.Config, "=== 配置系统测试开始 ===");
            
            if (showUI)
            {
                CreateTestUI();
            }
            
            // 运行所有测试
            RunAllTests();
        }
        
        /// <summary>
        /// 创建测试UI
        /// </summary>
        private void CreateTestUI()
        {
            // 创建Canvas
            GameObject canvasObj = new GameObject("ConfigTestCanvas");
            canvasObj.transform.SetParent(transform);
            testCanvas = canvasObj.AddComponent<Canvas>();
            testCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            testCanvas.sortingOrder = 1000;
            
            // 添加CanvasScaler
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            
            // 添加GraphicRaycaster
            canvasObj.AddComponent<GraphicRaycaster>();
            
            // 添加EventSystem（如果没有的话）
            if (FindObjectOfType<EventSystem>() == null)
            {
                GameObject eventSystemObj = new GameObject("EventSystem");
                eventSystemObj.AddComponent<EventSystem>();
                eventSystemObj.AddComponent<StandaloneInputModule>();
            }
            
            // 创建主面板
            GameObject panelObj = new GameObject("MainPanel");
            panelObj.transform.SetParent(canvasObj.transform);
            RectTransform panelRect = panelObj.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            
            Image panelImage = panelObj.AddComponent<Image>();
            panelImage.color = new Color(0, 0, 0, 0.8f);
            
            // 创建标题
            CreateText("ConfigTestTitle", "配置系统测试", new Vector2(0, 400), new Vector2(400, 50), 
                panelObj.transform, Color.white, 24, TextAnchor.MiddleCenter);
            
            // 创建按钮区域
            GameObject buttonPanel = new GameObject("ButtonPanel");
            buttonPanel.transform.SetParent(panelObj.transform);
            RectTransform buttonRect = buttonPanel.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0, 0.8f);
            buttonRect.anchorMax = new Vector2(1, 1);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;
            
            // 创建测试按钮
            CreateTestButtons(buttonPanel.transform);
            
            // 创建日志区域
            CreateLogArea(panelObj.transform);
        }
        
        /// <summary>
        /// 创建测试按钮
        /// </summary>
        private void CreateTestButtons(Transform parent)
        {
            string[] buttonTexts = {
                "测试UI", "测试配置", "配置变更测试", "运行时修改", "Inspector测试", "清空日志"
            };
            
            testButtons = new Button[buttonTexts.Length];
            
            for (int i = 0; i < buttonTexts.Length; i++)
            {
                int index = i;
                // 调整按钮位置，确保在屏幕范围内
                float x = 50 + (i % 3) * 180;
                float y = 100 + (i / 3) * 60;
                testButtons[i] = CreateButton(buttonTexts[i], new Vector2(x, y), 
                    new Vector2(160, 40), parent, () => OnTestButtonClick(index));
            }
        }
        
        /// <summary>
        /// 创建日志区域
        /// </summary>
        private void CreateLogArea(Transform parent)
        {
            // 创建滚动视图
            GameObject scrollObj = new GameObject("ScrollView");
            scrollObj.transform.SetParent(parent);
            RectTransform scrollRect = scrollObj.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.05f, 0.05f);
            scrollRect.anchorMax = new Vector2(0.95f, 0.75f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            
            Image scrollImage = scrollObj.AddComponent<Image>();
            scrollImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            
            this.scrollRect = scrollObj.AddComponent<ScrollRect>();
            
            // 创建内容区域
            GameObject contentObj = new GameObject("Content");
            contentObj.transform.SetParent(scrollObj.transform);
            RectTransform contentRect = contentObj.AddComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            
            this.scrollRect.content = contentRect;
            this.scrollRect.horizontal = false;
            this.scrollRect.vertical = true;
            
            // 创建日志文本
            logText = CreateText("LogText", "", Vector2.zero, Vector2.zero, 
                contentObj.transform, Color.white, fontSize, TextAnchor.UpperLeft);
            
            logText.rectTransform.anchorMin = Vector2.zero;
            logText.rectTransform.anchorMax = Vector2.one;
            logText.rectTransform.offsetMin = new Vector2(10, 10);
            logText.rectTransform.offsetMax = new Vector2(-10, -10);
        }
        
        /// <summary>
        /// 创建按钮
        /// </summary>
        private Button CreateButton(string text, Vector2 position, Vector2 size, Transform parent, System.Action onClick)
        {
            GameObject buttonObj = new GameObject(text + "Button");
            buttonObj.transform.SetParent(parent, false);
            
            RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
            buttonRect.anchoredPosition = position;
            buttonRect.sizeDelta = size;
            // 使用左下角锚点，这样位置更容易控制
            buttonRect.anchorMin = new Vector2(0, 0);
            buttonRect.anchorMax = new Vector2(0, 0);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            
            Image buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.6f, 1f, 0.8f);
            
            Button button = buttonObj.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            
            // 创建按钮文本
            CreateText(text + "Text", text, Vector2.zero, Vector2.zero, 
                buttonObj.transform, Color.white, fontSize, TextAnchor.MiddleCenter);
            
            // 添加点击事件
            button.onClick.AddListener(() => {
                Debug.Log($"[ConfigTest] 按钮被点击: {text}");
                onClick();
            });
            
            return button;
        }
        
        /// <summary>
        /// 创建文本
        /// </summary>
        private Text CreateText(string name, string text, Vector2 position, Vector2 size, 
            Transform parent, Color color, int fontSize, TextAnchor alignment)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(parent);
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchoredPosition = position;
            textRect.sizeDelta = size;
            
            Text textComponent = textObj.AddComponent<Text>();
            textComponent.text = text;
            textComponent.color = color;
            textComponent.fontSize = fontSize;
            textComponent.alignment = alignment;
            textComponent.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            
            return textComponent;
        }
        
        /// <summary>
        /// 测试按钮点击事件
        /// </summary>
        private void OnTestButtonClick(int index)
        {
            switch (index)
            {
                case 0: TestUI(); break;
                case 1: TestConfig(); break;
                case 2: TestConfigChange(); break;
                case 3: RuntimeModify(); break;
                case 4: InspectorTest(); break;
                case 5: ClearLog(); break;
            }
        }
        
        /// <summary>
        /// 测试UI功能
        /// </summary>
        private void TestUI()
        {
            AddLog("--- 测试UI功能 ---");
            AddLog("✅ UI按钮点击测试成功！");
            AddLog("💡 如果你看到这条消息，说明UI按钮可以正常点击");
        }

        /// <summary>
        /// 运行所有测试
        /// </summary>
        private void RunAllTests()
        {
            AddLog("=== 配置系统测试开始 ===");
            
            // 在测试开始前就订阅事件（持续监听，不取消订阅）
            EventCenter.GetInstance().AddEventListener<ConfigAPI.ConfigChangedEventData>("ConfigChanged", OnConfigChanged);
            
            TestConfig();
            
            // 不取消订阅，保持持续监听
            AddLog("=== 配置系统测试完成 ===");
            AddLog("💡 事件监听已保持，现在可以在运行时修改配置并看到反应");
        }
        
        /// <summary>
        /// 测试配置功能
        /// </summary>
        private void TestConfig()
        {
            AddLog("--- 测试配置功能 ---");
            
            // 初始化配置系统
            ConfigAPI.Initialize();
            AddLog($"配置系统初始化: {ConfigAPI.IsInitialized()}");
            
            // 测试框架配置
            var frameworkConfig = ConfigAPI.GetFrameworkConfig();
            AddLog($"资源路径: {frameworkConfig.resourceRootPath}");
            AddLog($"默认语言: {frameworkConfig.defaultLanguage}");
            AddLog($"默认音量: {frameworkConfig.defaultVolume}");
            
            // 测试运行时配置
            ConfigAPI.Set("PlayerName", "TestPlayer");
            ConfigAPI.Set("PlayerLevel", 10);
            ConfigAPI.Set("IsVIP", true);
            
            string playerName = ConfigAPI.Get<string>("PlayerName", "Unknown");
            int playerLevel = ConfigAPI.Get<int>("PlayerLevel", 1);
            bool isVIP = ConfigAPI.Get<bool>("IsVIP", false);
            
            AddLog($"玩家名称: {playerName}");
            AddLog($"玩家等级: {playerLevel}");
            AddLog($"是否VIP: {isVIP}");
            
            AddLog("✅ 配置功能测试完成");
        }
        
        /// <summary>
        /// 测试配置变更
        /// </summary>
        private void TestConfigChange()
        {
            AddLog("--- 测试配置变更 ---");
            
            // 确保事件已订阅
            EventCenter.GetInstance().AddEventListener<ConfigAPI.ConfigChangedEventData>("ConfigChanged", OnConfigChanged);
            
            // 触发配置变更
            AddLog("触发配置变更...");
            ConfigAPI.Set("TestKey", "TestValue1");
            ConfigAPI.Set("TestKey", "TestValue2");
            ConfigAPI.Set("PlayerName", "Hero");
            ConfigAPI.Set("PlayerLevel", 99);
            
            AddLog("✅ 配置变更测试完成");
            AddLog("💡 如果看到配置变更日志，说明事件系统正常工作");
        }
        
        /// <summary>
        /// 运行时修改配置
        /// </summary>
        private void RuntimeModify()
        {
            AddLog("--- 运行时修改配置 ---");
            
            // 确保事件已订阅
            EventCenter.GetInstance().AddEventListener<ConfigAPI.ConfigChangedEventData>("ConfigChanged", OnConfigChanged);
            
            // 随机修改一些配置
            int randomValue = Random.Range(1, 100);
            ConfigAPI.Set("RandomValue", randomValue);
            ConfigAPI.Set("CurrentTime", System.DateTime.Now.ToString("HH:mm:ss"));
            ConfigAPI.Set("PlayerScore", Random.Range(1000, 9999));
            ConfigAPI.Set("IsOnline", Random.Range(0, 2) == 1);
            
            AddLog($"随机值: {randomValue}");
            AddLog($"当前时间: {System.DateTime.Now:HH:mm:ss}");
            AddLog("✅ 运行时配置修改完成");
            AddLog("💡 查看上方是否有配置变更日志");
        }
        
        /// <summary>
        /// Inspector修改测试
        /// </summary>
        private void InspectorTest()
        {
            AddLog("--- Inspector修改测试 ---");
            AddLog("💡 现在请在Inspector中修改FrameworkConfig的配置");
            AddLog("💡 例如：修改默认音量、日志级别、目标帧率等");
            AddLog("💡 修改后应该会看到配置变更日志");
            AddLog("✅ Inspector修改测试说明完成");
        }
        
        /// <summary>
        /// 清空日志
        /// </summary>
        private void ClearLog()
        {
            logContent = "";
            if (logText != null)
            {
                logText.text = "";
            }
        }
        
        /// <summary>
        /// 配置变更事件处理
        /// </summary>
        private void OnConfigChanged(ConfigAPI.ConfigChangedEventData eventData)
        {
            Debug.Log($"[ConfigTest] 收到配置变更事件: {eventData.key} = {eventData.value}");
            AddLog($"配置变更: {eventData.key} = {eventData.value}");
        }
        
        /// <summary>
        /// 添加日志
        /// </summary>
        private void AddLog(string message)
        {
            logContent += message + "\n";
            if (logText != null)
            {
                logText.text = logContent;
                // 滚动到底部
                if (scrollRect != null)
                {
                    scrollRect.verticalNormalizedPosition = 0f;
                }
            }
            
            // 同时输出到Unity Console
            Debug.Log($"[ConfigTest] {message}");
        }
        
    }
}
