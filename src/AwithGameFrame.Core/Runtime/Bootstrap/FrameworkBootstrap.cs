using System.Collections.Generic;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Core.Bootstrap
{
    /// <summary>
    /// 框架启动编排器
    /// 负责按优先级顺序初始化所有已注册模块
    /// </summary>
    public static class FrameworkBootstrap
    {
        private static readonly List<IModule> _modules = new List<IModule>();
        private static bool _initialized;

        /// <summary>
        /// 注册模块实例
        /// </summary>
        public static void Register(IModule module)
        {
            if (module == null) return;
            if (_modules.Contains(module)) return;

            _modules.Add(module);
            FrameworkLogger.Info($"[Bootstrap] 注册模块: {module.ModuleName} (优先级:{module.Priority})", LogCategory.Core);
        }

        /// <summary>
        /// 注册模块（通过单例获取）
        /// </summary>
        public static void Register<T>() where T : BaseManager<T>, new()
        {
            Register(BaseManager<T>.GetInstance());
        }

        /// <summary>
        /// 启动框架
        /// Phase 1: 按优先级降序依次调用 Initialize()
        /// Phase 2: 再次遍历调用 PostInitialize() 完成跨模块关联
        /// </summary>
        public static void Bootstrap()
        {
            if (_initialized) return;

            // 按优先级降序排列（高优先级先初始化）
            _modules.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            FrameworkLogger.Info("=== FrameworkBootstrap 开始 ===", LogCategory.Core);

            // Phase 1: 初始化
            foreach (var module in _modules)
            {
                FrameworkLogger.Info($"[Bootstrap] 初始化: {module.ModuleName}", LogCategory.Core);
                module.Initialize();
            }

            // Phase 2: 后初始化（跨模块关联）
            foreach (var module in _modules)
            {
                FrameworkLogger.Info($"[Bootstrap] 后初始化: {module.ModuleName}", LogCategory.Core);
                module.PostInitialize();
            }

            _initialized = true;
            FrameworkLogger.Info("=== FrameworkBootstrap 完成 ===", LogCategory.Core);
        }

        /// <summary>
        /// 优雅关闭所有模块（按初始化逆序）
        /// </summary>
        public static void Shutdown()
        {
            for (int i = _modules.Count - 1; i >= 0; i--)
            {
                FrameworkLogger.Info($"[Bootstrap] 关闭: {_modules[i].ModuleName}", LogCategory.Core);
                _modules[i].Shutdown();
            }
            _modules.Clear();
            _initialized = false;
        }

        /// <summary>
        /// 获取已注册的模块列表
        /// </summary>
        public static IReadOnlyList<IModule> RegisteredModules => _modules.AsReadOnly();
    }
}
