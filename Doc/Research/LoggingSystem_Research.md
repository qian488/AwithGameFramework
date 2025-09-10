# 日志系统模块调研报告

## 调研概述

**调研时间**: 2024年9月10日  
**调研目标**: 为AwithGameFrame框架选择合适的日志系统解决方案  
**调研范围**: 日志框架、性能监控、调试工具  

## 1. 需求分析

### 1.1 功能需求
- **多级别日志**: 支持Debug、Info、Warning、Error等级别
- **分类记录**: 支持按模块、功能分类记录日志
- **性能监控**: 支持性能指标记录和分析
- **文件输出**: 支持日志文件输出和管理
- **实时查看**: 支持运行时日志查看

### 1.2 技术需求
- **性能**: 不影响游戏性能，异步记录
- **跨平台**: 支持PC、移动端、WebGL等平台
- **可配置**: 支持运行时配置日志级别
- **扩展性**: 支持自定义日志处理器
- **易用性**: 提供简洁的API接口

## 2. 市场调研结果

### 2.1 Unity内置解决方案

#### Unity Console
- **开发商**: Unity Technologies
- **功能**: 基础日志输出
- **特点**: 集成在Unity编辑器中
- **平台支持**: 编辑器内查看

**优点**:
- 无需额外配置
- 与Unity编辑器集成
- 支持日志过滤和搜索
- 支持堆栈跟踪

**缺点**:
- 仅在编辑器中可用
- 功能相对基础
- 不支持文件输出
- 性能监控功能有限

#### Unity Profiler
- **开发商**: Unity Technologies
- **功能**: 性能分析工具
- **特点**: 实时性能监控
- **平台支持**: 编辑器 + 运行时

**优点**:
- 功能强大，性能分析全面
- 实时监控
- 支持多种性能指标
- 与Unity深度集成

**缺点**:
- 仅在编辑器中可用
- 学习成本较高
- 不适合生产环境

### 2.2 第三方开源解决方案

#### NLog
- **开发商**: NLog Team
- **功能**: 企业级日志框架
- **特点**: 高性能、可配置、可扩展
- **许可证**: BSD

**优点**:
- 性能优秀，异步记录
- 配置灵活，支持多种输出目标
- 社区活跃，文档完善
- 支持结构化日志
- 支持日志过滤和路由

**缺点**:
- 学习成本较高
- 配置相对复杂
- 需要额外依赖

#### Serilog
- **开发商**: Serilog Team
- **功能**: 结构化日志框架
- **特点**: 结构化日志、丰富的事件数据
- **许可证**: Apache 2.0

**优点**:
- 结构化日志，易于分析
- 丰富的输出目标
- 性能优秀
- 易于扩展和定制

**缺点**:
- 学习成本较高
- 配置相对复杂
- 需要额外依赖

#### log4net
- **开发商**: Apache Software Foundation
- **功能**: 企业级日志框架
- **特点**: 成熟稳定、功能丰富
- **许可证**: Apache 2.0

**优点**:
- 成熟稳定，经过大量项目验证
- 功能丰富，支持多种输出目标
- 社区活跃，文档完善
- 支持异步记录

**缺点**:
- 配置复杂
- 性能相对较低
- 学习成本高

### 2.3 Unity专用解决方案

#### Unity Console Pro
- **开发商**: Unity Asset Store
- **功能**: 增强版控制台
- **特点**: 支持运行时日志查看
- **费用**: 付费插件

**优点**:
- 支持运行时查看日志
- 功能丰富，界面美观
- 支持日志过滤和搜索
- 支持日志导出

**缺点**:
- 需要付费
- 依赖Unity Asset Store
- 功能相对固定

#### Unity Log Viewer
- **开发商**: 社区开源
- **功能**: 运行时日志查看器
- **特点**: 轻量级、易集成
- **许可证**: MIT

**优点**:
- 轻量级，易于集成
- 支持运行时查看
- 开源免费
- 易于定制

**缺点**:
- 功能相对基础
- 社区支持有限
- 文档相对较少

### 2.4 商业解决方案

#### Sentry
- **开发商**: Sentry
- **功能**: 错误监控和性能分析
- **特点**: 云端服务、实时监控
- **费用**: 免费额度 + 按使用量计费

**优点**:
- 云端服务，无需自建
- 实时监控和告警
- 支持多种平台
- 提供详细的分析报告

**缺点**:
- 需要网络连接
- 费用较高
- 依赖第三方服务

#### LogRocket
- **开发商**: LogRocket
- **功能**: 前端日志和性能监控
- **特点**: 会话重放、性能分析
- **费用**: 免费额度 + 按使用量计费

**优点**:
- 会话重放功能
- 性能分析详细
- 支持多种平台
- 易于集成

**缺点**:
- 主要针对Web应用
- 费用较高
- 依赖第三方服务

## 3. 日志框架对比

### 3.1 功能对比

| 框架 | 性能 | 配置 | 扩展性 | 学习成本 | 社区支持 | 推荐度 |
|------|------|------|--------|----------|----------|--------|
| Unity Console | 高 | 简单 | 低 | 低 | 高 | ⭐⭐ |
| NLog | 高 | 复杂 | 高 | 中 | 高 | ⭐⭐⭐⭐⭐ |
| Serilog | 高 | 中 | 高 | 中 | 高 | ⭐⭐⭐⭐ |
| log4net | 中 | 复杂 | 高 | 高 | 高 | ⭐⭐⭐ |
| Unity Console Pro | 高 | 简单 | 中 | 低 | 中 | ⭐⭐⭐ |

### 3.2 适用场景

| 场景 | 推荐方案 | 理由 |
|------|----------|------|
| 开发调试 | Unity Console + 自定义 | 简单易用，集成度高 |
| 生产环境 | NLog + 自定义 | 性能优秀，功能丰富 |
| 结构化日志 | Serilog | 结构化日志，易于分析 |
| 企业应用 | log4net | 成熟稳定，功能全面 |
| 运行时查看 | Unity Console Pro | 支持运行时日志查看 |

## 4. 技术选型建议

### 4.1 日志框架选择
**推荐方案**: Unity Console + 框架封装 (第一阶段)

**选择理由**:
1. **简单实用**: 符合框架"简单实用"的设计理念
2. **无外部依赖**: 不依赖第三方库，保持框架轻量
3. **易于集成**: 与Unity深度集成，学习成本低
4. **快速实现**: 可以快速提供日志功能
5. **后续扩展**: 为后续集成高级日志框架预留接口

### 4.2 日志级别设计
**推荐方案**: 多级别日志系统

**级别定义**:
- **Trace**: 最详细的日志，用于调试
- **Debug**: 调试信息，开发时使用
- **Info**: 一般信息，记录重要事件
- **Warn**: 警告信息，可能的问题
- **Error**: 错误信息，需要关注
- **Fatal**: 致命错误，程序无法继续

### 4.3 日志分类设计
**推荐方案**: 按模块分类

**分类定义**:
- **Core**: 核心模块日志
- **UI**: 用户界面日志
- **Audio**: 音频系统日志
- **Input**: 输入系统日志
- **Network**: 网络通信日志
- **Performance**: 性能监控日志

## 5. 架构设计

### 5.1 日志系统架构

```
LoggingManager (第一阶段)
├── LogLevel (Trace, Debug, Info, Warn, Error, Fatal)
├── LogCategory (Core, UI, Audio, Input, Network, Performance)
├── UnityConsoleTarget (Unity Console封装)
│   ├── ConsoleLog (控制台日志)
│   ├── ConsoleFilter (控制台过滤)
│   └── ConsoleFormatter (控制台格式化)
└── LoggingInterface (扩展接口)
    ├── ILogTarget (日志目标接口)
    ├── ILogFilter (日志过滤接口)
    └── ILogFormatter (日志格式化接口)

LoggingManager (第二阶段 - 未来扩展)
├── AdvancedTargets
│   ├── FileTarget (文件输出)
│   ├── NetworkTarget (网络传输)
│   └── DatabaseTarget (数据库存储)
└── AdvancedFeatures
    ├── LogRotation (日志轮转)
    ├── LogCompression (日志压缩)
    └── LogAnalysis (日志分析)
```

### 5.2 性能监控设计

```
PerformanceMonitor
├── FrameRate (帧率监控)
├── MemoryUsage (内存使用)
├── GarbageCollection (GC监控)
├── NetworkLatency (网络延迟)
├── LoadTime (加载时间)
└── CustomMetrics (自定义指标)
```

## 6. 实施计划

### 6.1 第一阶段 (1-2周) - Unity Console封装
- 设计日志系统架构
- 封装Unity Console API
- 实现基础日志功能
- 添加日志级别和分类

### 6.2 第二阶段 (1-2周) - 功能完善
- 实现日志过滤功能
- 添加日志格式化
- 集成性能监控
- 完善错误处理

### 6.3 第三阶段 (未来扩展) - 高级功能
- 集成NLog或其他高级日志框架
- 添加文件输出功能
- 实现网络日志传输
- 开发日志分析工具

### 6.4 第四阶段 (未来扩展) - 优化完善
- 性能优化和测试
- 完善文档和示例
- 部署和上线准备

## 7. 风险评估

### 7.1 技术风险
- **性能风险**: 日志记录可能影响游戏性能
- **存储风险**: 日志文件可能占用大量存储空间
- **网络风险**: 网络日志传输可能失败

### 7.2 缓解措施
- **异步记录**: 使用异步方式记录日志
- **日志轮转**: 实现日志文件轮转和清理
- **网络重试**: 实现网络传输重试机制

## 8. Unity Console封装实现示例

### 8.1 基础日志管理器

```csharp
namespace AwithGameFrame.Logging
{
    public class LoggingManager : BaseManager<LoggingManager>
    {
        public enum LogLevel
        {
            Trace = 0,
            Debug = 1,
            Info = 2,
            Warn = 3,
            Error = 4,
            Fatal = 5
        }
        
        public enum LogCategory
        {
            Core,
            UI,
            Audio,
            Input,
            Network,
            Performance
        }
        
        private LogLevel _currentLevel = LogLevel.Debug;
        private Dictionary<LogCategory, bool> _categoryEnabled = new Dictionary<LogCategory, bool>();
        
        public void SetLogLevel(LogLevel level)
        {
            _currentLevel = level;
        }
        
        public void SetCategoryEnabled(LogCategory category, bool enabled)
        {
            _categoryEnabled[category] = enabled;
        }
        
        public void Log(LogLevel level, LogCategory category, string message, object context = null)
        {
            if (level < _currentLevel) return;
            if (!_categoryEnabled.GetValueOrDefault(category, true)) return;
            
            string formattedMessage = FormatMessage(level, category, message, context);
            
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    Debug.Log(formattedMessage, context as UnityEngine.Object);
                    break;
                case LogLevel.Info:
                    Debug.Log(formattedMessage, context as UnityEngine.Object);
                    break;
                case LogLevel.Warn:
                    Debug.LogWarning(formattedMessage, context as UnityEngine.Object);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Debug.LogError(formattedMessage, context as UnityEngine.Object);
                    break;
            }
        }
        
        private string FormatMessage(LogLevel level, LogCategory category, string message, object context)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string levelStr = level.ToString().ToUpper();
            string categoryStr = category.ToString();
            
            return $"[{timestamp}] [{levelStr}] [{categoryStr}] {message}";
        }
    }
}
```

### 8.2 便捷日志方法

```csharp
namespace AwithGameFrame.Logging
{
    public static class Logger
    {
        public static void Trace(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LoggingManager.LogLevel.Trace, LoggingManager.LogCategory.Core, message, context);
        }
        
        public static void Debug(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LoggingManager.LogLevel.Debug, LoggingManager.LogCategory.Core, message, context);
        }
        
        public static void Info(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LoggingManager.LogLevel.Info, LoggingManager.LogCategory.Core, message, context);
        }
        
        public static void Warn(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LoggingManager.LogLevel.Warn, LoggingManager.LogCategory.Core, message, context);
        }
        
        public static void Error(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LoggingManager.LogLevel.Error, LoggingManager.LogCategory.Core, message, context);
        }
        
        public static void Fatal(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LoggingManager.LogLevel.Fatal, LoggingManager.LogCategory.Core, message, context);
        }
        
        // 分类日志方法
        public static void LogUI(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LoggingManager.LogLevel.Info, LoggingManager.LogCategory.UI, message, context);
        }
        
        public static void LogAudio(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LoggingManager.LogLevel.Info, LoggingManager.LogCategory.Audio, message, context);
        }
        
        public static void LogNetwork(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LoggingManager.LogLevel.Info, LoggingManager.LogCategory.Network, message, context);
        }
        
        public static void LogPerformance(string message, object context = null)
        {
            LoggingManager.GetInstance().Log(LoggingManager.LogLevel.Info, LoggingManager.LogCategory.Performance, message, context);
        }
    }
}
```

### 8.3 性能监控封装

```csharp
namespace AwithGameFrame.Logging
{
    public class PerformanceMonitor : BaseManager<PerformanceMonitor>
    {
        private Dictionary<string, float> _timers = new Dictionary<string, float>();
        private Dictionary<string, int> _counters = new Dictionary<string, int>();
        
        public void StartTimer(string name)
        {
            _timers[name] = Time.realtimeSinceStartup;
        }
        
        public void EndTimer(string name)
        {
            if (_timers.ContainsKey(name))
            {
                float duration = Time.realtimeSinceStartup - _timers[name];
                Logger.LogPerformance($"Timer '{name}': {duration:F3}s");
                _timers.Remove(name);
            }
        }
        
        public void IncrementCounter(string name)
        {
            _counters[name] = _counters.GetValueOrDefault(name, 0) + 1;
        }
        
        public void LogCounter(string name)
        {
            if (_counters.ContainsKey(name))
            {
                Logger.LogPerformance($"Counter '{name}': {_counters[name]}");
            }
        }
        
        public void LogFrameRate()
        {
            float fps = 1.0f / Time.deltaTime;
            Logger.LogPerformance($"FPS: {fps:F1}");
        }
        
        public void LogMemoryUsage()
        {
            long memory = GC.GetTotalMemory(false);
            Logger.LogPerformance($"Memory: {memory / 1024 / 1024}MB");
        }
    }
}
```

### 8.4 使用示例

```csharp
// 基础日志使用
Logger.Info("游戏开始");
Logger.Error("网络连接失败");
Logger.LogUI("UI面板打开");
Logger.LogNetwork("发送消息到服务器");

// 性能监控使用
PerformanceMonitor.GetInstance().StartTimer("加载场景");
// ... 加载场景的代码 ...
PerformanceMonitor.GetInstance().EndTimer("加载场景");

PerformanceMonitor.GetInstance().IncrementCounter("敌人击杀");
PerformanceMonitor.GetInstance().LogCounter("敌人击杀");

// 运行时配置
LoggingManager.GetInstance().SetLogLevel(LoggingManager.LogLevel.Info);
LoggingManager.GetInstance().SetCategoryEnabled(LoggingManager.LogCategory.Network, false);
```

## 9. 总结

通过全面的市场调研和技术分析，我们选择了Unity Console + 框架封装的方案作为AwithGameFrame框架的日志系统解决方案。该方案在简单性、易用性和无外部依赖方面表现优秀，符合框架的设计理念，能够满足框架的日志记录和性能监控需求。

第一阶段专注于Unity Console的封装，为后续集成高级日志框架预留了扩展接口，确保框架的灵活性和可扩展性。

下一步将按照实施计划，逐步完成日志系统模块的开发工作。
