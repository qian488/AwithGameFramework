# AwithGameFrame - 简化版Unity游戏框架

## 📦 三包架构设计

AwithGameFrame采用三包架构设计，从核心到扩展，功能逐渐丰富：

```
AwithGameFrame/
├── Core/           # 核心包 - 零外部依赖的基础功能
├── Foundation/     # 基础包 - 核心包 + 基础功能实现
└── Extensions/     # 扩展包 - 基础包 + 扩展功能
```

## 📁 目录结构

```
Assets/Scripts/AwithGameFrame/
├── Core/                    # 核心包 - 零外部依赖
│   ├── Base/               # 基础类
│   │   ├── BaseManager.cs
│   │   ├── SingletonMono.cs
│   │   └── SingletonAutoMono.cs
│   ├── Event/              # 事件系统
│   │   └── EventCenter.cs
│   ├── Interfaces/         # 接口定义
│   │   ├── IAnimationProvider.cs
│   │   ├── IAsyncProvider.cs
│   │   └── ISerializationProvider.cs
│   ├── Mono/               # Mono管理
│   │   ├── MonoManager.cs
│   │   └── MonoControl.cs
│   ├── Pool/               # 对象池管理
│   │   ├── PoolManager.cs
│   │   └── PoolData.cs
│   ├── Scene/              # 场景管理
│   │   └── ScenesManager.cs
│   └── Utils/              # 核心工具
│       └── GameConstants.cs
│
├── Foundation/              # 基础包 - 核心包 + 基础功能
│   ├── DataPersistence/    # 数据持久化模块
│   │   ├── Database/       # 数据库支持
│   │   ├── Serialization/  # 序列化支持
│   │   ├── Storage/        # 存储支持
│   │   └── Utils/          # 工具类
│   ├── Logging/            # 日志系统模块
│   │   ├── LoggingManager.cs
│   │   ├── FileLogger.cs
│   │   └── PerformanceMonitor.cs
│   ├── Providers/          # 提供者管理
│   │   ├── DOTweenProvider.cs
│   │   ├── NewtonsoftJsonProvider.cs
│   │   └── UniTaskProvider.cs
│   ├── Resource/           # 资源管理模块
│   │   └── ResourcesManager.cs
│   ├── Systems/            # 基础系统模块
│   │   ├── Audio/          # 音频系统
│   │   │   └── MusicManager.cs
│   │   ├── Input/          # 输入系统
│   │   │   └── InputManager.cs
│   │   └── UI/             # UI系统
│   │       ├── UIManager.cs
│   │       └── BasePanel.cs
│   └── Examples/           # 使用示例
│       └── FoundationUsageExample.cs
│
└── Extensions/              # 扩展包 - 基础包 + 扩展功能
    └── Tests/              # 测试工具模块
        ├── FrameworkTestRunner.cs
        ├── PerformanceTester.cs
        └── ExampleUsage.cs
```

## 🎯 设计理念

- **三包架构** - 从核心到扩展，功能逐渐丰富
- **零外部依赖** - 核心包只使用Unity内置API
- **模块化设计** - 按功能模块组织，职责明确
- **易于扩展** - 支持按需安装和功能扩展

## 🚀 使用方式

### 基础使用
```csharp
// 获取管理器实例
var uiManager = UIManager.GetInstance();
var audioManager = MusicManager.GetInstance();

// 显示UI面板
uiManager.ShowPanel<TestPanel>("testpanel");

// 播放音频
audioManager.PlayBGM("background_music");
```

### 事件系统
```csharp
// 监听事件
EventCenter.GetInstance().AddEventListener<KeyCode>("KeyDown", OnKeyDown);

// 触发事件
EventCenter.GetInstance().EventTrigger<KeyCode>("KeyDown", KeyCode.W);
```

## 📝 包依赖关系

### 依赖层次
```
Core包 (零依赖)
    ↑
Foundation包 (依赖Core + 核心依赖)
    ↑
Extensions包 (依赖Foundation + 扩展功能)
```

### 命名空间规范
- **Core包**: `AwithGameFrame.Core`
- **Foundation包**: `AwithGameFrame.Foundation`
- **Extensions包**: `AwithGameFrame.Extensions`

## 📝 注意事项

1. **三包架构** - 采用渐进式功能设计，从核心到扩展
2. **零外部依赖** - 核心包只使用Unity内置API
3. **模块化设计** - 每个包内按功能模块组织
4. **Unity兼容** - 所有功能都兼容Unity的MonoBehaviour系统

## 🔄 架构更新完成

- ✅ 三包架构设计完成
- ✅ 目录结构重新组织
- ✅ 模块划分更加清晰
- ✅ 依赖关系明确

框架现在采用清晰的三包架构设计！