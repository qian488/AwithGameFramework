using System;
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
        private static readonly List<IModule> _startedModules = new List<IModule>();
        private static bool _initialized;
        private static bool _bootstrapping;
        private static bool _shuttingDown;

        /// <summary>
        /// 注册模块实例
        /// </summary>
        public static void Register(IModule module)
        {
            if (module == null) return;
            if (_modules.Contains(module)) return;
            if (_bootstrapping || _shuttingDown || _initialized)
                throw new InvalidOperationException("Register modules before starting the framework, or after shutting it down.");

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
            if (_bootstrapping || _shuttingDown)
                throw new InvalidOperationException("A framework lifecycle transition is already in progress.");
            if (_initialized) return;
            _bootstrapping = true;
            try
            {
                // 按优先级降序排列（高优先级先初始化）
                _modules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                FrameworkLogger.Info("=== FrameworkBootstrap 开始 ===", LogCategory.Core);

                foreach (var module in _modules)
                {
                    FrameworkLogger.Info($"[Bootstrap] 初始化: {module.ModuleName}", LogCategory.Core);
                    // Track before calling user code so partial initialization is cleaned up too.
                    _startedModules.Add(module);
                    module.Initialize();
                }

                foreach (var module in _startedModules)
                {
                    FrameworkLogger.Info($"[Bootstrap] 后初始化: {module.ModuleName}", LogCategory.Core);
                    module.PostInitialize();
                }

                FrameworkLogger.Info("=== FrameworkBootstrap 完成 ===", LogCategory.Core);
                _initialized = true;
            }
            catch (Exception startupFailure)
            {
                var cleanupFailures = ShutdownStartedModules();
                _initialized = false;
                if (cleanupFailures.Count > 0)
                {
                    cleanupFailures.Insert(0, startupFailure);
                    throw new AggregateException("Framework startup failed and some modules could not be shut down.", cleanupFailures);
                }
                // Keep registrations for a retry while preserving the original exception and stack.
                throw;
            }
            finally
            {
                _bootstrapping = false;
            }
        }

        /// <summary>
        /// 优雅关闭所有模块（按初始化逆序）
        /// </summary>
        public static void Shutdown()
        {
            if (_shuttingDown) return;
            if (_bootstrapping)
                throw new InvalidOperationException("The framework cannot shut down during initialization.");

            List<Exception> failures;
            try
            {
                failures = ShutdownStartedModules();
            }
            finally
            {
                _modules.Clear();
                _initialized = false;
            }

            if (failures.Count > 0)
                throw new AggregateException("Some framework modules could not be shut down.", failures);
        }

        private static List<Exception> ShutdownStartedModules()
        {
            var failures = new List<Exception>();
            _shuttingDown = true;
            try
            {
                for (int i = _startedModules.Count - 1; i >= 0; i--)
                {
                    var module = _startedModules[i];
                    try
                    {
                        FrameworkLogger.Info($"[Bootstrap] 关闭: {module.ModuleName}", LogCategory.Core);
                    }
                    catch (Exception loggingFailure)
                    {
                        failures.Add(loggingFailure);
                    }
                    try
                    {
                        module.Shutdown();
                    }
                    catch (Exception moduleFailure)
                    {
                        failures.Add(moduleFailure);
                    }
                }
            }
            finally
            {
                _startedModules.Clear();
                _shuttingDown = false;
            }
            return failures;
        }

        /// <summary>
        /// 获取已注册的模块列表
        /// </summary>
        public static IReadOnlyList<IModule> RegisteredModules => _modules.AsReadOnly();
    }
}
