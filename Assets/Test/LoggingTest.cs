using UnityEngine;
using AwithGameFrame.Core.Logging;


namespace Test
{
    /// <summary>
    /// 日志系统测试脚本
    /// </summary>
    public class LoggingTest : MonoBehaviour
    {
        [Header("测试设置")]
        public bool enablePerformanceTest = true;
        public bool enableExceptionTest = true;
        public bool enableFileLoggingTest = true;
        public bool enableFrameworkValidation = true;
        public bool enableCustomLogPath = true;
        public LogMode testMode = LogMode.FrameworkLog;
        public string customLogPath = "D:/GameLogs";
        public float testInterval = 2f;
        
        private float _testTimer = 0f;
        private int _testCounter = 0;
        
        void Start()
        {
            FrameworkLogger.Info("日志系统测试开始");
            
            // 初始化日志模式管理器
            InitializeLogModeManager();
            
            TestBasicLogging();
            TestCategoryLogging();
            TestFormatLogging();
            
            if (enableFileLoggingTest)
            {
                TestFileLogging();
            }
            
            if (enableFrameworkValidation)
            {
                TestFrameworkValidation();
            }
        }
        
        void Update()
        {
            _testTimer += Time.deltaTime;
            
            if (_testTimer >= testInterval)
            {
                _testTimer = 0f;
                _testCounter++;
                
                if (enablePerformanceTest)
                {
                    TestPerformanceMonitoring();
                }
                
                if (enableExceptionTest && _testCounter % 3 == 0)
                {
                    TestExceptionLogging();
                }
            }
            
            // 更新日志管理器
            LoggingManager.GetInstance().Update();
        }
        
        /// <summary>
        /// 测试基础日志功能
        /// </summary>
        private void TestBasicLogging()
        {
            FrameworkLogger.Trace("这是Trace级别的日志");
            FrameworkLogger.Debug("这是Debug级别的日志");
            FrameworkLogger.Info("这是Info级别的日志");
            FrameworkLogger.Warn("这是Warn级别的日志");
            FrameworkLogger.Error("这是Error级别的日志");
            FrameworkLogger.Fatal("这是Fatal级别的日志");
        }
        
        /// <summary>
        /// 测试分类日志功能
        /// </summary>
        private void TestCategoryLogging()
        {
            FrameworkLogger.LogUI("UI面板打开");
            FrameworkLogger.LogAudio("播放背景音乐");
            FrameworkLogger.LogInput("玩家按下空格键");
            FrameworkLogger.LogNetwork("连接到服务器");
            FrameworkLogger.LogPerformance("游戏帧率: 60fps");
        }
        
        /// <summary>
        /// 测试格式化日志功能
        /// </summary>
        private void TestFormatLogging()
        {
            string playerName = "Player1";
            int score = 1000;
            float health = 85.5f;
            
            FrameworkLogger.LogInfoFormat("玩家 {0} 得分: {1}, 生命值: {2:F1}%", playerName, score, health);
            FrameworkLogger.LogErrorFormat("错误代码: {0}, 描述: {1}", 404, "资源未找到");
        }
        
        /// <summary>
        /// 测试性能监控功能
        /// </summary>
        private void TestPerformanceMonitoring()
        {
            // 测试计时器
            PerformanceMonitor.GetInstance().StartTimer("TestOperation");
            
            // 模拟一些操作
            for (int i = 0; i < 1000; i++)
            {
                // 空循环模拟计算
            }
            
            PerformanceMonitor.GetInstance().EndTimer("TestOperation");
            
            // 测试计数器
            PerformanceMonitor.GetInstance().IncrementCounter("TestCounter");
            PerformanceMonitor.GetInstance().LogCounter("TestCounter");
            
            // 测试平均值
            float randomValue = Random.Range(1f, 10f);
            PerformanceMonitor.GetInstance().RecordAverage("TestAverage", randomValue);
            
            // 测试系统性能
            PerformanceMonitor.GetInstance().LogFrameRate();
            PerformanceMonitor.GetInstance().LogMemoryUsage();
        }
        
        /// <summary>
        /// 测试异常日志功能
        /// </summary>
        private void TestExceptionLogging()
        {
            try
            {
                // 故意抛出异常
                throw new System.Exception("这是一个测试异常");
            }
            catch (System.Exception ex)
            {
                FrameworkLogger.LogException("捕获到测试异常", ex, this);
            }
        }
        
        /// <summary>
        /// 测试日志级别设置
        /// </summary>
        [ContextMenu("测试日志级别设置")]
        public void TestLogLevelSettings()
        {
            FrameworkLogger.Info("=== 测试日志级别设置 ===");
            
            // 设置为Info级别
            LoggingManager.GetInstance().SetLogLevel(LogLevel.Info);
            FrameworkLogger.Debug("这条Debug日志应该不会显示");
            FrameworkLogger.Info("这条Info日志应该会显示");
            FrameworkLogger.Warn("这条Warn日志应该会显示");
            
            // 设置为Error级别
            LoggingManager.GetInstance().SetLogLevel(LogLevel.Error);
            FrameworkLogger.Info("这条Info日志应该不会显示");
            FrameworkLogger.Warn("这条Warn日志应该不会显示");
            FrameworkLogger.Error("这条Error日志应该会显示");
        }
        
        /// <summary>
        /// 测试分类开关设置
        /// </summary>
        [ContextMenu("测试分类开关设置")]
        public void TestCategorySettings()
        {
            FrameworkLogger.Info("=== 测试分类开关设置 ===");
            
            // 关闭UI分类
            LoggingManager.GetInstance().SetCategoryEnabled(LogCategory.UI, false);
            FrameworkLogger.LogUI("这条UI日志应该不会显示");
            FrameworkLogger.LogAudio("这条Audio日志应该会显示");
            
            // 重新开启UI分类
            LoggingManager.GetInstance().SetCategoryEnabled(LogCategory.UI, true);
            FrameworkLogger.LogUI("这条UI日志现在应该会显示");
        }
        
        /// <summary>
        /// 测试性能监控统计
        /// </summary>
        [ContextMenu("显示性能监控统计")]
        public void ShowPerformanceStats()
        {
            FrameworkLogger.Info("=== 性能监控统计 ===");
            
            var timers = PerformanceMonitor.GetInstance().GetActiveTimers();
            var counters = PerformanceMonitor.GetInstance().GetActiveCounters();
            
            FrameworkLogger.Info($"活跃计时器数量: {timers.Length}");
            FrameworkLogger.Info($"活跃计数器数量: {counters.Length}");
            
            foreach (var counter in counters)
            {
                int value = PerformanceMonitor.GetInstance().GetCounterValue(counter);
                FrameworkLogger.Info($"计数器 '{counter}': {value}");
            }
        }
        
        /// <summary>
        /// 测试文件日志功能
        /// </summary>
        private void TestFileLogging()
        {
            FrameworkLogger.Info("=== 测试文件日志功能 ===");
            
            // 启用文件日志
            LoggingManager.GetInstance().EnableFileLogging = true;
            LoggingManager.GetInstance().InitializeFileLogger();
            
            // 测试文件日志记录
            FrameworkLogger.Info("这是一条文件日志测试消息");
            FrameworkLogger.LogUI("UI测试消息");
            FrameworkLogger.LogPerformance("性能测试消息");
            
            // 测试文件日志配置
            if (LoggingManager.GetInstance().FileLogger != null)
            {
                LoggingManager.GetInstance().FileLogger.MaxFileSize = 1024; // 1KB，便于测试轮转
                LoggingManager.GetInstance().FileLogger.MaxFiles = 3;
                
                FrameworkLogger.Info("文件日志配置完成 - 最大文件大小: 1KB, 最大文件数: 3");
            }
        }
        
        /// <summary>
        /// 测试文件日志轮转
        /// </summary>
        [ContextMenu("测试文件日志轮转")]
        public void TestFileLogRotation()
        {
            if (LoggingManager.GetInstance().FileLogger == null)
            {
                FrameworkLogger.Warn("文件日志器未初始化");
                return;
            }
            
            FrameworkLogger.Info("=== 测试文件日志轮转 ===");
            
            // 生成大量日志以触发文件轮转
            for (int i = 0; i < 50; i++)
            {
                FrameworkLogger.Info($"轮转测试消息 {i + 1} - 这是一条很长的日志消息，用于测试文件大小限制和自动轮转功能");
            }
            
            FrameworkLogger.Info("文件轮转测试完成");
        }
        
        /// <summary>
        /// 测试文件日志清理
        /// </summary>
        [ContextMenu("测试文件日志清理")]
        public void TestFileLogCleanup()
        {
            if (LoggingManager.GetInstance().FileLogger == null)
            {
                FrameworkLogger.Warn("文件日志器未初始化");
                return;
            }
            
            FrameworkLogger.Info("=== 测试文件日志清理 ===");
            
            // 手动清理旧文件
            LoggingManager.GetInstance().CleanupOldFiles();
            
            FrameworkLogger.Info("文件清理测试完成");
        }
        
        /// <summary>
        /// 显示文件日志状态
        /// </summary>
        [ContextMenu("显示文件日志状态")]
        public void ShowFileLogStatus()
        {
            FrameworkLogger.Info("=== 文件日志状态 ===");
            FrameworkLogger.Info($"文件日志启用: {LoggingManager.GetInstance().EnableFileLogging}");
            
            if (LoggingManager.GetInstance().FileLogger != null)
            {
                var fileLogger = LoggingManager.GetInstance().FileLogger;
                FrameworkLogger.Info($"文件日志器启用: {fileLogger.IsEnabled}");
                FrameworkLogger.Info($"最小日志级别: {fileLogger.MinLevel}");
                FrameworkLogger.Info($"最大文件大小: {fileLogger.MaxFileSize / 1024}KB");
                FrameworkLogger.Info($"最大文件数量: {fileLogger.MaxFiles}");
                FrameworkLogger.Info($"启用时间戳: {fileLogger.EnableTimestamp}");
                FrameworkLogger.Info($"启用堆栈跟踪: {fileLogger.EnableStackTrace}");
            }
            else
            {
                FrameworkLogger.Warn("文件日志器未初始化");
            }
        }
        
        /// <summary>
        /// 手动轮转日志文件
        /// </summary>
        [ContextMenu("手动轮转日志文件")]
        public void ManualRotateLogFile()
        {
            if (LoggingManager.GetInstance().FileLogger == null)
            {
                FrameworkLogger.Warn("文件日志器未初始化");
                return;
            }
            
            FrameworkLogger.Info("手动轮转日志文件");
            LoggingManager.GetInstance().RotateLogFile();
        }
        
        /// <summary>
        /// 初始化日志模式管理器
        /// </summary>
        private void InitializeLogModeManager()
        {
            string logPath = enableCustomLogPath ? customLogPath : null;
            LoggingManager.GetInstance().Initialize(testMode, enableFrameworkValidation, logPath);
            
            FrameworkLogger.Info($"日志模式管理器初始化完成 - 模式: {testMode}, 验证: {enableFrameworkValidation}");
        }
        
        /// <summary>
        /// 测试框架验证功能
        /// </summary>
        private void TestFrameworkValidation()
        {
            FrameworkLogger.Info("=== 测试框架验证功能 ===");
            
            // 测试单例验证
            FrameworkLogger.InfoWithValidation("测试单例使用", ValidationType.Singleton, this);
            
            // 测试事件验证
            FrameworkLogger.InfoWithValidation("添加事件监听", ValidationType.Event, this);
            
            // 测试资源验证
            FrameworkLogger.InfoWithValidation("测试资源加载", ValidationType.Resource, this);
            
            // 测试UI验证
            FrameworkLogger.InfoWithValidation("测试UI面板", ValidationType.UI, this);
        }
        
        /// <summary>
        /// 测试日志模式切换
        /// </summary>
        [ContextMenu("测试日志模式切换")]
        public void TestLogModeSwitching()
        {
            FrameworkLogger.Info("=== 测试日志模式切换 ===");
            
            var modes = new[] { 
                LogMode.UnityDebug, 
                LogMode.FrameworkLog, 
                LogMode.Both, 
                LogMode.None 
            };
            
            foreach (var mode in modes)
            {
                LoggingManager.GetInstance().SwitchMode(mode);
                FrameworkLogger.Info($"切换到模式: {mode}");
            }
            
            // 切换回原始模式
            LoggingManager.GetInstance().SwitchMode(testMode);
        }
        
        /// <summary>
        /// 显示日志配置信息
        /// </summary>
        [ContextMenu("显示日志配置信息")]
        public void ShowLogConfiguration()
        {
            FrameworkLogger.Info("=== 日志配置信息 ===");
            string configInfo = LoggingAPI.GetStatus();
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
        /// 重置验证统计
        /// </summary>
        [ContextMenu("重置验证统计")]
        public void ResetValidationStats()
        {
            FrameworkValidator.GetInstance().ResetValidationStats();
            FrameworkLogger.Info("验证统计已重置");
        }
        
        /// <summary>
        /// 测试错误检测
        /// </summary>
        [ContextMenu("测试错误检测")]
        public void TestErrorDetection()
        {
            FrameworkLogger.Info("=== 测试错误检测 ===");
            
            // 测试各种错误情况
            FrameworkLogger.ErrorWithValidation("测试错误检测", ValidationType.Singleton, this);
            FrameworkLogger.WarnWithValidation("测试警告检测", ValidationType.Event, this);
            FrameworkLogger.InfoWithValidation("测试信息验证", ValidationType.Resource, this);
        }
        
        void OnDestroy()
        {
            FrameworkLogger.Info("日志系统测试结束");
            
            // 显示最终统计
            if (enableFrameworkValidation)
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
