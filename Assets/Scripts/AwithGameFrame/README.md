# AwithGameFrame - 简化版Unity游戏框架

## 📁 目录结构

```
Assets/Scripts/AwithGameFrame/
├── Core/                    # 核心系统
│   ├── Base/               # 基础类
│   │   ├── BaseManager.cs
│   │   ├── SingletonMono.cs
│   │   └── SingletonAutoMono.cs
│   ├── Event/              # 事件系统
│   │   └── EventCenter.cs
│   ├── Mono/               # Mono管理
│   │   ├── MonoManager.cs
│   │   └── MonoControl.cs
│   ├── Resource/           # 资源管理
│   │   ├── ResourcesManager.cs
│   │   ├── PoolManager.cs
│   │   └── PoolData.cs
│   └── Scene/              # 场景管理
│       └── ScenesManager.cs
│
├── Systems/                # 功能系统
│   ├── Input/              # 输入系统 (AwithGameFrame.InputSystem)
│   │   └── InputManager.cs
│   ├── Audio/              # 音频系统 (AwithGameFrame.Audio)
│   │   └── MusicManager.cs
│   └── UI/                 # UI系统 (AwithGameFrame.UI)
│       ├── UIManager.cs
│       └── BasePanel.cs
│
├── Utils/                  # 工具类
│   └── GameConstants.cs
│
└── Tests/                  # 框架测试工具（暂时为空）
    ├── FrameworkTestRunner.cs    # 框架功能自动测试（待开发）
    ├── PerformanceTester.cs      # 性能测试工具（待开发）
    └── ExampleUsage.cs          # 使用示例（待开发）
```

## 🎯 设计理念

- **简单实用** - 避免过度设计，专注核心功能
- **易于扩展** - 模块化设计，便于添加新功能
- **清晰结构** - 按功能分类，职责明确

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

## 📝 注意事项

1. **命名空间简化** - 当前使用简单的命名空间结构
2. **后续重构** - 随着框架复杂度增加，会考虑更严格的命名空间
3. **Unity兼容** - 所有功能都兼容Unity的MonoBehaviour系统
4. **测试目录区分** - `Assets/Test/` 用于项目测试，`Assets/Scripts/AwithGameFrame/Tests/` 用于框架测试工具

## 🔄 迁移完成

- ✅ 所有文件已从ProjectBase迁移到新结构
- ✅ 旧文件已清理
- ✅ 目录结构已优化
- ✅ 命名空间已简化

框架现在更加简洁和易于使用！