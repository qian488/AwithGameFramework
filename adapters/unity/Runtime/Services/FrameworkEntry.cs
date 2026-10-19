using UnityEngine;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Bootstrap;
using AwithGameFrame.Core.Config;
using AwithGameFrame.Foundation.Systems.Audio;
using AwithGameFrame.Foundation.Systems.InputSystem;
using AwithGameFrame.Foundation.Systems.UI;
using AwithGameFrame.Foundation.Pool;

namespace AwithGameFrame.Foundation
{
    /// <summary>
    /// 框架入口组件
    /// 挂载到场景中的GameObject上，在Awake中完成框架启动
    /// </summary>
    public class FrameworkEntry : MonoBehaviour
    {
        [SerializeField] private bool _autoBootstrap = true;
        private bool _ownsBootstrap;
        private bool _applicationPaused;
        private bool _inputWasEnabled;

        private void Awake()
        {
            if (_autoBootstrap)
            {
                Bootstrap();
            }
        }

        public void Bootstrap()
        {
            if (_ownsBootstrap) return;
            foreach (var module in FrameworkBootstrap.RegisteredModules)
                if (module.State == ModuleState.Running) return;
            // 按优先级注册模块
            // Critical - 配置系统必须先初始化
            FrameworkBootstrap.Register<ConfigManager>();

            // Infrastructure - 核心服务
            FrameworkBootstrap.Register<MonoManager>();

            // CoreSystems - 对象池和资源
            var config = ConfigManager.GetInstance().FrameworkConfig;
            if (config.enablePoolModule) FrameworkBootstrap.Register<PoolManager>();
            FrameworkBootstrap.Register<ResourcesManager>();

            // Features - 游戏系统
            if (config.enableAudioModule) FrameworkBootstrap.Register<MusicManager>();
            if (config.enableInputModule) FrameworkBootstrap.Register<InputManager>();
            if (config.enableUIModule) FrameworkBootstrap.Register<UIManager>();

            // 启动
            FrameworkBootstrap.Bootstrap();
            _ownsBootstrap = true;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_ownsBootstrap)
            {
                _ownsBootstrap = false;
                FrameworkBootstrap.Shutdown();
            }
        }

        private void OnApplicationPause(bool pause)
        {
            SetApplicationPaused(pause);
        }

        /// <summary>前后台切换保留运行中的模块；最终释放由 OnDestroy 负责。</summary>
        public void SetApplicationPaused(bool pause)
        {
            if (!_ownsBootstrap || _applicationPaused == pause) return;
            _applicationPaused = pause;
            foreach (var module in FrameworkBootstrap.RegisteredModules)
            {
                if (module.State != ModuleState.Running) continue;
                if (module is InputManager input)
                {
                    if (pause) { _inputWasEnabled = input.IsEnabled; input.SetEnabled(false); }
                    else input.SetEnabled(_inputWasEnabled);
                }
                if (module is MusicManager music) music.SetApplicationPaused(pause);
            }
        }
    }
}
