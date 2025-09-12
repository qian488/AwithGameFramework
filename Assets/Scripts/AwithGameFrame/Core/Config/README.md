# AwithGameFrame 配置系统使用指南

## 概述

AwithGameFrame配置系统提供了统一的配置管理功能，支持框架配置、游戏配置和运行时配置的统一管理。

## 系统架构

### 核心组件

- **FrameworkConfig**: ScriptableObject，存储框架内部配置
- **GameConfigData**: 基类，用于游戏配置数据
- **ConfigManager**: 核心管理器，继承BaseManager
- **ConfigAPI**: 静态API，提供便捷访问接口

### 配置类型

1. **框架配置**: 相对稳定，编辑器友好，使用ScriptableObject
2. **游戏配置**: 频繁变更，数据驱动，使用JSON文件
3. **运行时配置**: 临时设置，内存存储，使用Dictionary

## 快速开始

### 1. 创建FrameworkConfig资源

1. 在Unity编辑器中右键
2. 选择 `Create > AwithGameFrame > Framework Config`
3. 命名为 `FrameworkConfig`
4. 放置在 `Assets/Resources/` 目录下
5. 在Inspector中配置各项参数

### 2. 初始化配置系统

```csharp
// 在游戏启动时初始化
ConfigAPI.Initialize();
```

### 3. 使用配置

```csharp
// 获取框架配置
var frameworkConfig = ConfigAPI.GetFrameworkConfig();

// 使用便捷方法
string resourcePath = ConfigAPI.GetResourceRootPath();
string language = ConfigAPI.GetDefaultLanguage();
float volume = ConfigAPI.GetDefaultVolume();

// 检查模块状态
bool audioEnabled = ConfigAPI.IsModuleEnabled("audio");
bool uiEnabled = ConfigAPI.IsModuleEnabled("ui");
```

## 详细使用说明

### 框架配置

#### 创建配置资源

1. 在Unity编辑器中创建FrameworkConfig资源
2. 配置各项参数：
   - **路径配置**: 资源根路径、存档路径等
   - **模块开关**: 控制各模块的启用状态
   - **默认设置**: 语言、音量、帧率等
   - **日志系统配置**: 日志级别、模式、文件设置等
   - **性能配置**: 对象池、音频等性能相关设置

#### 使用框架配置

```csharp
// 获取完整配置
var config = ConfigAPI.GetFrameworkConfig();

// 使用便捷方法
LogLevel logLevel = ConfigAPI.GetLogLevel();
LogMode logMode = ConfigAPI.GetLogMode();
LoggingConfig loggingConfig = ConfigAPI.GetLoggingConfig();

// 重新加载配置
ConfigAPI.ReloadFrameworkConfig();
```

### 游戏配置

#### 创建游戏配置数据类

```csharp
[Serializable]
public class CharacterConfig : GameConfigData
{
    public float health;
    public float attack;
    
    public override bool IsValid()
    {
        return base.IsValid() && health > 0 && attack > 0;
    }
}
```

#### 创建JSON配置文件

```json
{
    "data": [
        {
            "id": 1,
            "name": "骑士",
            "health": 100.0,
            "attack": 15.0
        }
    ]
}
```

#### 使用游戏配置

```csharp
// 加载配置
ConfigAPI.LoadGameConfig<CharacterConfig>("Characters", "Configs/Characters");

// 获取单个配置
var knight = ConfigAPI.GetGameConfigData<CharacterConfig>("Characters", 1);

// 获取所有配置
var allCharacters = ConfigAPI.GetAllGameConfigData<CharacterConfig>("Characters");
```

### 运行时配置

#### 设置运行时配置

```csharp
// 设置各种类型的配置
ConfigAPI.Set("PlayerName", "TestPlayer");
ConfigAPI.Set("PlayerLevel", 10);
ConfigAPI.Set("PlayerScore", 1500);
ConfigAPI.Set("IsVIP", true);
```

#### 获取运行时配置

```csharp
// 获取配置（带默认值）
string playerName = ConfigAPI.Get<string>("PlayerName", "Unknown");
int playerLevel = ConfigAPI.Get<int>("PlayerLevel", 1);
bool isVIP = ConfigAPI.Get<bool>("IsVIP", false);

// 检查配置是否存在
bool exists = ConfigAPI.Has("PlayerName");

// 删除配置
ConfigAPI.Remove("PlayerName");
```

### 配置变更通知

#### 订阅配置变更事件

```csharp
// 订阅事件
ConfigAPI.OnConfigChanged += OnConfigChanged;

// 事件处理
private void OnConfigChanged(string key, object value)
{
    Debug.Log($"配置变更: {key} = {value}");
}

// 取消订阅
ConfigAPI.OnConfigChanged -= OnConfigChanged;
```

## 测试和调试

### 使用ConfigSystemTester

1. 在场景中创建一个GameObject
2. 添加 `ConfigSystemTester` 组件
3. 配置测试参数
4. 运行测试或使用Context Menu

### 测试功能

- ✅ 配置系统初始化测试
- ✅ 框架配置测试
- ✅ 日志配置测试
- ✅ 游戏配置测试
- ✅ 运行时配置测试
- ✅ 配置变更通知测试

### 手动测试

```csharp
// 在Inspector中右键点击ConfigSystemTester组件
// 选择 "运行配置系统测试" 或 "重新加载配置"
```

## 最佳实践

### 1. 配置分类

- **框架配置**: 用于框架内部设置，相对稳定
- **游戏配置**: 用于游戏数据，频繁变更
- **运行时配置**: 用于临时设置，会话级别

### 2. 性能优化

- 框架配置使用ScriptableObject，性能优秀
- 游戏配置使用JSON，支持热更新
- 运行时配置使用Dictionary，访问快速

### 3. 错误处理

- 配置加载失败时使用默认值
- 提供配置验证方法
- 记录配置加载日志

### 4. 扩展性

- 继承GameConfigData创建新的配置类型
- 使用ConfigAPI的便捷方法
- 支持配置变更通知

## 常见问题

### Q: 如何添加新的配置项？

A: 在FrameworkConfig中添加新的字段，并在ConfigAPI中添加对应的便捷方法。

### Q: 如何支持热更新？

A: 游戏配置使用JSON文件，支持运行时重新加载。框架配置需要重启应用。

### Q: 如何调试配置问题？

A: 使用ConfigSystemTester进行测试，查看Console日志输出。

### Q: 如何优化配置性能？

A: 框架配置使用ScriptableObject，游戏配置使用Dictionary缓存，运行时配置使用内存存储。

## 示例项目

参考 `ConfigExample.cs` 和 `ConfigSystemTester.cs` 获取完整的使用示例。

## 更新日志

- v1.0.0: 初始版本，支持基础配置管理
- v1.1.0: 集成日志系统配置
- v1.2.0: 添加配置变更通知
- v1.3.0: 优化性能和易用性
