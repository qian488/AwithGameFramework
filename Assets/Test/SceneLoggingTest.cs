using UnityEngine;
using AwithGameFrame.Logging;

namespace Test
{
    /// <summary>
    /// 场景日志测试脚本 - 用于在SampleScene中测试日志系统
    /// </summary>
    public class SceneLoggingTest : MonoBehaviour
    {
        [Header("测试设置")]
        [SerializeField] private bool enableBasicTest = true;
        [SerializeField] private bool enableFileLoggingTest = true;
        [SerializeField] private bool enableFrameworkValidationTest = true;
        [SerializeField] private bool enablePerformanceTest = true;
        [SerializeField] private float testInterval = 3f;
        
        [Header("日志模式设置")]
        [SerializeField] private LogMode logMode = LogMode.FrameworkLog;
        [SerializeField] private bool enableValidation = true;
        [SerializeField] private string customLogPath = "D:/GameLogs";
        
        private float testTimer = 0f;
        private int testCounter = 0;
        
        void Start()
        {
            FrameworkLogger.Info("=== 场景日志测试开始 ===");
            
            // 初始化日志模式管理器
            InitializeLoggingSystem();
            
            // 执行基础测试
            if (enableBasicTest)
            {
                TestBasicLogging();
            }
            
            // 执行文件日志测试
            if (enableFileLoggingTest)
            {
                TestFileLogging();
            }
            
            // 执行框架验证测试
            if (enableFrameworkValidationTest)
            {
                TestFrameworkValidation();
            }
        }
        
        void Update()
        {
            testTimer += Time.deltaTime;
            
            if (testTimer >= testInterval)
            {
                testTimer = 0f;
                testCounter++;
                
                // 定期执行性能测试
                if (enablePerformanceTest)
                {
                    TestPerformanceMonitoring();
                }
                
                // 定期执行框架验证测试
                if (enableFrameworkValidationTest)
                {
                    TestFrameworkValidation();
                }
            }
            
            // 更新日志管理器
            LoggingManager.GetInstance().Update();
        }
        
        /// <summary>
        /// 初始化日志系统
        /// </summary>
        private void InitializeLoggingSystem()
        {
            // 初始化日志模式管理器
            LoggingManager.GetInstance().Initialize(logMode, enableValidation, customLogPath);
            
            FrameworkLogger.Info($"日志系统初始化完成 - 模式: {logMode}, 验证: {enableValidation}");
            
            // 显示配置信息
            ShowLoggingConfiguration();
        }
        
        /// <summary>
        /// 测试基础日志功能
        /// </summary>
        private void TestBasicLogging()
        {
            FrameworkLogger.Info("=== 测试基础日志功能 ===");
            
            // 测试不同级别的日志
            FrameworkLogger.Trace("这是Trace级别的日志");
            FrameworkLogger.Debug("这是Debug级别的日志");
            FrameworkLogger.Info("这是Info级别的日志");
            FrameworkLogger.Warn("这是Warn级别的日志");
            FrameworkLogger.Error("这是Error级别的日志");
            FrameworkLogger.Fatal("这是Fatal级别的日志");
            
            // 测试分类日志
            FrameworkLogger.LogUI("UI系统测试");
            FrameworkLogger.LogAudio("音频系统测试");
            FrameworkLogger.LogNetwork("网络系统测试");
            FrameworkLogger.LogPerformance("性能系统测试");
            
            // 测试格式化日志
            string playerName = "测试玩家";
            int score = 1000;
            float health = 85.5f;
            FrameworkLogger.LogInfoFormat("玩家 {0} 得分: {1}, 生命值: {2:F1}%", playerName, score, health);
        }
        
        /// <summary>
        /// 测试文件日志功能
        /// </summary>
        private void TestFileLogging()
        {
            FrameworkLogger.Info("=== 测试文件日志功能 ===");
            
            // 配置文件日志
            if (LoggingManager.GetInstance().FileLogger != null)
            {
                var fileLogger = LoggingManager.GetInstance().FileLogger;
                fileLogger.MaxFileSize = 1024; // 1KB，便于测试轮转
                fileLogger.MaxFiles = 3;
                
                FrameworkLogger.Info("文件日志配置完成 - 最大文件大小: 1KB, 最大文件数: 3");
            }
            
            // 生成一些测试日志
            for (int i = 0; i < 5; i++)
            {
                FrameworkLogger.Info($"文件日志测试消息 {i + 1} - 时间: {System.DateTime.Now:HH:mm:ss.fff}");
            }
        }
        
        /// <summary>
        /// 测试框架验证功能
        /// </summary>
        private void TestFrameworkValidation()
        {
            FrameworkLogger.Info("=== 测试框架验证功能 ===");
            
            // 测试各种验证类型
            FrameworkLogger.InfoWithValidation("测试单例使用", ValidationType.Singleton, this);
            FrameworkLogger.InfoWithValidation("测试事件监听", ValidationType.Event, this);
            FrameworkLogger.InfoWithValidation("测试资源加载", ValidationType.Resource, this);
            FrameworkLogger.InfoWithValidation("测试UI面板", ValidationType.UI, this);
            FrameworkLogger.InfoWithValidation("测试网络连接", ValidationType.Network, this);
            FrameworkLogger.InfoWithValidation("测试性能监控", ValidationType.Performance, this);
            FrameworkLogger.InfoWithValidation("测试内存使用", ValidationType.Memory, this);
        }
        
        /// <summary>
        /// 测试性能监控功能
        /// </summary>
        private void TestPerformanceMonitoring()
        {
            FrameworkLogger.Info("=== 测试性能监控功能 ===");
            
            // 测试计时器
            PerformanceMonitor.GetInstance().StartTimer("场景更新");
            PerformanceMonitor.GetInstance().EndTimer("场景更新");
            
            // 测试计数器
            PerformanceMonitor.GetInstance().IncrementCounter("测试计数");
            PerformanceMonitor.GetInstance().LogCounter("测试计数");
            
            // 测试平均值
            PerformanceMonitor.GetInstance().RecordAverage("测试平均值", Random.Range(1f, 10f));
            float avgValue = PerformanceMonitor.GetInstance().GetAverage("测试平均值");
            FrameworkLogger.LogPerformance($"测试平均值: {avgValue:F3}");
            
            // 测试FPS和内存
            PerformanceMonitor.GetInstance().LogFrameRate();
            PerformanceMonitor.GetInstance().LogMemoryUsage();
        }
        
        /// <summary>
        /// 显示日志配置信息
        /// </summary>
        private void ShowLoggingConfiguration()
        {
            FrameworkLogger.Info("=== 日志配置信息 ===");
            string configInfo = LoggingSystem.GetStatus();
            FrameworkLogger.Info($"配置信息:\n{configInfo}");
        }
        
        /// <summary>
        /// 显示框架验证统计
        /// </summary>
        [ContextMenu("显示框架验证统计")]
        public void ShowValidationStats()
        {
            FrameworkLogger.Info("=== 框架验证统计 ===");
            var stats = LoggingManager.GetInstance().FrameworkValidator.GetValidationStats();
            FrameworkLogger.Info(stats);
        }
        
        /// <summary>
        /// 手动轮转日志文件
        /// </summary>
        [ContextMenu("手动轮转日志文件")]
        public void ManualRotateLogFile()
        {
            FrameworkLogger.Info("手动轮转日志文件");
            LoggingManager.GetInstance().RotateLogFile();
        }
        
        /// <summary>
        /// 清理旧日志文件
        /// </summary>
        [ContextMenu("清理旧日志文件")]
        public void CleanupOldFiles()
        {
            FrameworkLogger.Info("清理旧日志文件");
            LoggingManager.GetInstance().CleanupOldFiles();
        }
        
        /// <summary>
        /// 切换日志模式
        /// </summary>
        [ContextMenu("切换日志模式")]
        public void SwitchLogMode()
        {
            var currentMode = LoggingManager.GetInstance().CurrentMode;
            var newMode = (LogMode)(((int)currentMode + 1) % 4);
            
            FrameworkLogger.Info($"切换日志模式: {currentMode} -> {newMode}");
            LoggingManager.GetInstance().SwitchMode(newMode);
        }
        
        void OnDestroy()
        {
            FrameworkLogger.Info("=== 场景日志测试结束 ===");
            
            // 显示最终统计
            if (enableFrameworkValidationTest)
            {
                ShowValidationStats();
            }
            
            // 关闭文件日志
            if (LoggingManager.GetInstance().EnableFileLogging)
            {
                LoggingManager.GetInstance().ShutdownFileLogger();
            }
        }
    }
}
