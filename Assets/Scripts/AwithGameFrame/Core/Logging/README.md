# AwithGameFrame 日志系统

## 概述

AwithGameFrame日志系统是一个基于Unity Console的轻量级日志解决方案，提供统一的日志记录、分类管理和性能监控功能。

## 🚀 快速开始

### 一键初始化（推荐）
```csharp
using AwithGameFrame.Logging;

// 开发环境 - 显示所有日志，启用文件输出和验证
LoggingSystem.Initialize(LoggingSystem.Preset.Development);

// 生产环境 - 只显示重要日志，启用文件输出
LoggingSystem.Initialize(LoggingSystem.Preset.Production);

// 调试模式 - 显示所有日志，启用性能监控
LoggingSystem.Initialize(LoggingSystem.Preset.Debug);

// 快速初始化
LoggingSystem.InitializeDevelopment();  // 开发环境
LoggingSystem.InitializeProduction();   // 生产环境
LoggingSystem.InitializeDebug();        // 调试模式
```

### 基础使用
```csharp
// 基础日志记录
LoggingSystem.Info("游戏开始");
LoggingSystem.Warn("检测到警告");
LoggingSystem.Error("发生错误");
LoggingSystem.Debug("调试信息");

// 分类日志记录
LoggingAPI.Info(LogCategory.UI, "UI面板打开");
LoggingAPI.Info(LogCategory.Audio, "播放背景音乐");
LoggingAPI.Info(LogCategory.Input, "用户按下空格键");
LoggingAPI.Info(LogCategory.Resource, "加载游戏资源");
LoggingAPI.Info(LogCategory.Performance, "帧率: 60fps");
```

### 查看系统状态
```csharp
// 查看当前配置状态
Debug.Log(LoggingSystem.GetStatus());

// 切换日志模式
LoggingSystem.SwitchMode(LogModeManager.LogMode.Both);

// 设置日志级别
LoggingSystem.SetLogLevel(LoggingManager.LogLevel.Info);
```

## 特性

- ✅ **多级别日志**: 支持Trace、Debug、Info、Warn、Error、Fatal六个级别
- ✅ **分类管理**: 支持Core、UI、Audio、Input、Network、Performance、Resource七个分类
- ✅ **性能监控**: 内置计时器、计数器、平均值统计等功能
- ✅ **异常处理**: 支持异常日志记录和堆栈跟踪
- ✅ **格式化日志**: 支持字符串格式化日志记录
- ✅ **灵活配置**: 支持运行时配置日志级别和分类开关
- ✅ **文件输出**: 支持日志写入文件，按时间命名
- ✅ **文件轮转**: 支持按大小自动轮转日志文件
- ✅ **文件清理**: 支持自动清理旧日志文件
- ✅ **框架验证**: 自动检测框架使用中的不规范行为
- ✅ **模式选择**: 支持Unity Debug、框架日志或两者同时使用
- ✅ **自定义路径**: 支持指定自定义日志文件路径
- ✅ **错误检测**: 提供详细的错误检测和提示功能
- ✅ **无外部依赖**: 基于Unity Console，无需额外依赖
- ✅ **一键配置**: 提供预设配置，简化初始化过程
- ✅ **统一入口**: LoggingSystem提供统一的访问接口

## 📊 系统评估与改进

### 当前优势
1. **功能完整**: 涵盖了日志系统的基本需求
2. **分类清晰**: 按模块分类，便于过滤和管理
3. **性能监控**: 内置性能监控功能
4. **文件输出**: 支持文件日志和自动管理
5. **框架验证**: 提供开发时的使用规范检查

### 已解决的问题
1. **✅ 封装不足**: 新增LoggingSystem统一入口，提供一键配置
2. **✅ 配置复杂**: 提供预设配置，简化初始化过程
3. **✅ 缺少便捷方法**: 新增快速初始化方法
4. **✅ 状态查询困难**: 新增GetStatus()方法查看当前配置

### 设计改进
1. **统一入口**: LoggingSystem作为主要访问接口
2. **预设配置**: 提供开发/生产/调试等预设
3. **自动获取调用者**: 无需手动传递context参数
4. **状态查询**: 可以查看当前系统配置状态

### 学习成本分析
- **🟢 极低 (90%使用场景)**: 使用LoggingSystem.Initialize()和基础日志方法
- **🟡 中等 (10%使用场景)**: 需要自定义配置或使用高级功能
- **🔴 高 (框架维护者)**: 需要了解所有组件的详细配置

## 使用指南

### 1. 系统初始化

```csharp
using AwithGameFrame.Logging;

// 方式1: 使用预设配置（推荐）
LoggingSystem.Initialize(LoggingSystem.Preset.Development);

// 方式2: 快速初始化
LoggingSystem.InitializeDevelopment();  // 开发环境
LoggingSystem.InitializeProduction();   // 生产环境
LoggingSystem.InitializeDebug();        // 调试模式

// 方式3: 自定义路径
LoggingSystem.Initialize(LoggingSystem.Preset.Development, "D:/MyGame/Logs");
```

### 2. 基础日志记录

```csharp
// 使用LoggingSystem（推荐）
LoggingSystem.Info("游戏开始");
LoggingSystem.Debug("调试信息");
LoggingSystem.Warn("警告信息");
LoggingSystem.Error("错误信息");

// 使用FrameworkLogger（分类日志）
LoggingAPI.Info(LogCategory.UI, "UI面板打开");
LoggingAPI.Info(LogCategory.Audio, "播放背景音乐");
LoggingAPI.Info(LogCategory.Input, "用户按下空格键");
LoggingAPI.Info(LogCategory.Resource, "加载游戏资源");
LoggingAPI.Info(LogCategory.Performance, "帧率: 60fps");
```

### 3. 系统管理

```csharp
// 查看当前状态
Debug.Log(LoggingSystem.GetStatus());

// 切换日志模式
LoggingSystem.SwitchMode(LogModeManager.LogMode.Both);

// 设置日志级别
LoggingSystem.SetLogLevel(LoggingManager.LogLevel.Info);

// 控制分类开关
LoggingSystem.SetCategoryEnabled(LoggingManager.LogCategory.UI, false);

// 重置到默认状态
LoggingSystem.Reset();
```

### 性能监控

```csharp
// 计时器使用
PerformanceMonitor.GetInstance().StartTimer("加载场景");
// ... 执行加载操作 ...
PerformanceMonitor.GetInstance().EndTimer("加载场景");

// 计数器使用
PerformanceMonitor.GetInstance().IncrementCounter("敌人击杀");
PerformanceMonitor.GetInstance().LogCounter("敌人击杀");

// 平均值统计
PerformanceMonitor.GetInstance().RecordAverage("加载时间", 2.5f);

// 系统性能监控
PerformanceMonitor.GetInstance().LogFrameRate();
PerformanceMonitor.GetInstance().LogMemoryUsage();
```

### 异常处理

```csharp
try
{
    // 可能出错的代码
    riskyOperation();
}
catch (Exception ex)
{
    FrameworkLogger.LogException("操作失败", ex, this);
}
```

### 格式化日志

```csharp
string playerName = "Player1";
int score = 1000;
float health = 85.5f;

FrameworkLogger.LogInfoFormat("玩家 {0} 得分: {1}, 生命值: {2:F1}%", playerName, score, health);
```

### 日志模式选择

```csharp
// 初始化日志模式管理器
LogModeManager.GetInstance().Initialize(
    LogModeManager.LogMode.FrameworkLog,  // 使用框架日志
    true,                                 // 启用框架验证
    "D:/MyGame/Logs"                     // 自定义日志路径
);

// 切换日志模式
LogModeManager.GetInstance().SwitchMode(LogModeManager.LogMode.Both);

// 设置自定义日志路径
LogModeManager.GetInstance().SetCustomLogPath("D:/CustomLogs");
```

### 框架验证使用

```csharp
// 带验证的日志记录
FrameworkLogger.InfoWithValidation("操作完成", "singleton", this);
FrameworkLogger.WarnWithValidation("检测到问题", "event", this);
FrameworkLogger.ErrorWithValidation("操作失败", "resource", this);

// 手动验证框架使用
LogModeManager.GetInstance().ValidateFrameworkUsage("singleton", "测试消息", this);
LogModeManager.GetInstance().ValidateFrameworkUsage("event", "事件名称", this);
LogModeManager.GetInstance().ValidateFrameworkUsage("performance", "操作名称", this);
```

### 文件日志使用

```csharp
// 启用文件日志
LoggingManager.GetInstance().EnableFileLogging = true;
LoggingManager.GetInstance().InitializeFileLogger();

// 配置文件日志
var fileLogger = LoggingManager.GetInstance().FileLogger;
fileFrameworkLogger.MaxFileSize = 10 * 1024 * 1024; // 10MB
fileFrameworkLogger.MaxFiles = 10; // 最多保留10个文件
fileFrameworkLogger.EnableTimestamp = true;
fileFrameworkLogger.EnableStackTrace = true;

// 手动轮转日志文件
LoggingManager.GetInstance().RotateLogFile();

// 清理旧日志文件
LoggingManager.GetInstance().CleanupOldFiles();
```

## 配置管理

### 设置日志级别

```csharp
// 设置为Info级别，只显示Info及以上级别的日志
LoggingManager.GetInstance().SetLogLevel(LoggingManager.LogLevel.Info);

// 设置为Error级别，只显示Error和Fatal级别的日志
LoggingManager.GetInstance().SetLogLevel(LoggingManager.LogLevel.Error);
```

### 控制分类开关

```csharp
// 关闭UI分类的日志
LoggingManager.GetInstance().SetCategoryEnabled(LoggingManager.LogCategory.UI, false);

// 关闭网络分类的日志
LoggingManager.GetInstance().SetCategoryEnabled(LoggingManager.LogCategory.Network, false);
```

### 高级配置

```csharp
// 启用时间戳
LoggingManager.GetInstance().EnableTimestamp = true;

// 启用堆栈跟踪（用于异常日志）
LoggingManager.GetInstance().EnableStackTrace = true;

// 启用文件日志
LoggingManager.GetInstance().EnableFileLogging = true;

// 启用性能监控自动日志
PerformanceMonitor.GetInstance().EnableAutoLogging = true;

// 设置FPS更新间隔
PerformanceMonitor.GetInstance().FpsUpdateInterval = 2f;
```

## 日志级别说明

| 级别 | 用途 | 示例 |
|------|------|------|
| Trace | 最详细的调试信息 | 函数进入/退出 |
| Debug | 调试信息 | 变量值、状态变化 |
| Info | 一般信息 | 重要事件、状态更新 |
| Warn | 警告信息 | 潜在问题、异常情况 |
| Error | 错误信息 | 操作失败、异常 |
| Fatal | 致命错误 | 程序无法继续 |

## 日志分类说明

| 分类 | 用途 | 示例 |
|------|------|------|
| Core | 核心系统 | 框架初始化、管理器状态 |
| UI | 用户界面 | 面板打开/关闭、按钮点击 |
| Audio | 音频系统 | 音乐播放、音效触发 |
| Input | 输入系统 | 按键输入、触摸事件 |
| Network | 网络通信 | 连接状态、消息收发 |
| Performance | 性能监控 | 帧率、内存、计时器 |

## 文件日志功能

### 文件日志特性

- **按时间命名**: 日志文件按创建时间命名，格式为 `game_yyyy-MM-dd_HH-mm-ss.log`
- **自动轮转**: 当文件大小超过限制时自动创建新文件
- **自动清理**: 定期清理旧日志文件，保持文件数量在限制内
- **线程安全**: 支持多线程环境下的安全写入
- **配置灵活**: 支持自定义文件大小、数量、目录等配置

### 文件日志配置

```csharp
var fileLogger = LoggingManager.GetInstance().FileLogger;

// 基本配置
fileFrameworkLogger.MaxFileSize = 10 * 1024 * 1024; // 10MB
fileFrameworkLogger.MaxFiles = 10; // 最多保留10个文件
fileFrameworkLogger.EnableTimestamp = true; // 启用时间戳
fileFrameworkLogger.EnableStackTrace = true; // 启用堆栈跟踪

// 设置最小日志级别
fileFrameworkLogger.MinLevel = LoggingManager.LogLevel.Info;

// 控制分类开关
fileFrameworkLogger.SetCategoryEnabled(LoggingManager.LogCategory.UI, false);
```

### 文件日志管理

```csharp
// 手动轮转日志文件
LoggingManager.GetInstance().RotateLogFile();

// 清理旧日志文件
LoggingManager.GetInstance().CleanupOldFiles();

// 关闭文件日志
LoggingManager.GetInstance().EnableFileLogging = false;
```

### 文件日志位置

- **默认位置**: `Application.persistentDataPath/Logs/`
- **自定义位置**: 通过 `InitializeFileLogger(logDirectory)` 指定
- **文件格式**: UTF-8编码的文本文件

## 框架验证功能

### 验证类型

| 验证类型 | 描述 | 检测内容 |
|----------|------|----------|
| singleton | 单例使用验证 | 检查在Awake中调用GetInstance等不当使用 |
| event | 事件监听验证 | 检查事件监听是否在OnDestroy中移除 |
| resource | 资源加载验证 | 检查资源路径是否有效 |
| ui | UI面板验证 | 检查UI面板名称是否有效 |
| network | 网络连接验证 | 检查网络连接状态 |
| performance | 性能验证 | 检查操作耗时是否超过阈值 |
| memory | 内存验证 | 检查内存使用是否过高 |

### 验证配置

```csharp
// 获取框架验证器
var validator = FrameworkValidator.GetInstance();

// 启用/禁用验证
validator.EnableValidation = true;

// 启用/禁用警告
validator.EnableWarnings = true;

// 启用/禁用错误检测
validator.EnableErrors = true;

// 获取验证统计
var stats = validator.GetValidationStats();

// 重置验证统计
validator.ResetValidationStats();
```

### 日志模式说明

| 模式 | 描述 | 适用场景 |
|------|------|----------|
| UnityDebug | 只使用Unity Debug | 简单调试，不需要文件日志 |
| FrameworkLog | 只使用框架日志 | 生产环境，需要文件日志和验证 |
| Both | 同时使用两种模式 | 开发调试，需要对比两种日志 |
| None | 禁用所有日志 | 性能测试，不需要日志输出 |

## 性能监控功能

### 计时器

```csharp
// 开始计时
PerformanceMonitor.GetInstance().StartTimer("操作名称");

// 结束计时（自动记录日志）
PerformanceMonitor.GetInstance().EndTimer("操作名称");

// 获取当前耗时（不结束计时）
float elapsed = PerformanceMonitor.GetInstance().GetTimerElapsed("操作名称");
```

### 计数器

```csharp
// 增加计数
PerformanceMonitor.GetInstance().IncrementCounter("计数器名称", 1);

// 记录计数器值
PerformanceMonitor.GetInstance().LogCounter("计数器名称");

// 获取计数器值
int count = PerformanceMonitor.GetInstance().GetCounterValue("计数器名称");

// 重置计数器
PerformanceMonitor.GetInstance().ResetCounter("计数器名称");
```

### 平均值统计

```csharp
// 记录值到平均值统计
PerformanceMonitor.GetInstance().RecordAverage("平均值名称", value);

// 获取平均值
float average = PerformanceMonitor.GetInstance().GetAverage("平均值名称");

// 重置平均值
PerformanceMonitor.GetInstance().ResetAverage("平均值名称");
```

### 系统监控

```csharp
// 记录帧率
PerformanceMonitor.GetInstance().LogFrameRate();

// 记录内存使用
PerformanceMonitor.GetInstance().LogMemoryUsage();

// 记录GC信息
PerformanceMonitor.GetInstance().LogGCInfo();

// 记录渲染统计
PerformanceMonitor.GetInstance().LogRenderStats();
```

## 最佳实践

### 1. 合理使用日志级别

```csharp
// 好的做法
FrameworkLogger.Debug("玩家位置: {0}", playerPosition);  // 调试信息
FrameworkLogger.Info("玩家升级到等级 {0}", newLevel);    // 重要事件
FrameworkLogger.Warn("资源加载超时: {0}", resourceName); // 警告
FrameworkLogger.Error("无法连接到服务器: {0}", error);   // 错误

// 避免的做法
FrameworkLogger.Info("进入Update方法");  // 过于频繁的日志
FrameworkLogger.Error("用户点击了按钮");  // 错误级别使用不当
```

### 2. 使用分类组织日志

```csharp
// 好的做法
LoggingAPI.Info(LogCategory.UI, "打开设置面板");
LoggingAPI.Info(LogCategory.Network, "发送登录请求");
LoggingAPI.Info(LogCategory.Performance, "场景加载完成，耗时: {0}s", loadTime);

// 避免的做法
FrameworkLogger.Info("UI: 打开设置面板");  // 应该使用LogUI
FrameworkLogger.Info("Network: 发送登录请求");  // 应该使用LogNetwork
```

### 3. 性能监控使用

```csharp
// 好的做法
PerformanceMonitor.GetInstance().StartTimer("复杂操作");
try
{
    // 执行复杂操作
    ComplexOperation();
}
finally
{
    PerformanceMonitor.GetInstance().EndTimer("复杂操作");
}

// 避免的做法
// 不要为简单操作使用计时器
PerformanceMonitor.GetInstance().StartTimer("简单赋值");
int x = 1;
PerformanceMonitor.GetInstance().EndTimer("简单赋值");
```

### 4. 异常处理

```csharp
// 好的做法
try
{
    riskyOperation();
}
catch (Exception ex)
{
    FrameworkLogger.LogException("操作失败", ex, this);
    // 处理异常
}

// 避免的做法
try
{
    riskyOperation();
}
catch (Exception ex)
{
    FrameworkLogger.Error("操作失败: " + ex.Message);  // 丢失堆栈信息
}
```

## 测试

项目包含完整的测试脚本 `LoggingTest.cs`，演示了所有功能的使用方法。

### 运行测试

1. 将 `LoggingTest.cs` 脚本添加到场景中的GameObject
2. 运行游戏
3. 查看Console窗口的日志输出
4. 使用Context Menu测试各种功能

### 测试功能

- 基础日志记录
- 分类日志记录
- 格式化日志记录
- 性能监控功能
- 异常日志记录
- 配置管理测试

## 扩展

日志系统设计为可扩展的，未来可以轻松添加：

- 文件输出功能
- 网络日志传输
- 日志分析工具
- 更多性能监控指标

## 注意事项

1. **性能影响**: 虽然日志系统经过优化，但大量日志仍可能影响性能
2. **内存使用**: 性能监控数据会占用内存，定期清理不需要的数据
3. **线程安全**: 当前实现不是线程安全的，多线程环境需要额外处理
4. **Unity Console限制**: 基于Unity Console，受Unity Console功能限制

## 更新日志

### v1.0.0
- 初始版本发布
- 支持基础日志记录和性能监控
- 提供完整的测试和文档
