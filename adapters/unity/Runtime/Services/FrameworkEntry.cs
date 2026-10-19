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

        private void Awake()
        {
            if (_autoBootstrap)
            {
                Bootstrap();
            }
        }

        private void Bootstrap()
        {
            // 按优先级注册模块
            // Critical - 配置系统必须先初始化
            FrameworkBootstrap.Register<ConfigManager>();

            // Infrastructure - 核心服务
            FrameworkBootstrap.Register<MonoManager>();

            // CoreSystems - 对象池和资源
            FrameworkBootstrap.Register<PoolManager>();
            FrameworkBootstrap.Register<ResourcesManager>();

            // Features - 游戏系统
            FrameworkBootstrap.Register<MusicManager>();
            FrameworkBootstrap.Register<InputManager>();
            FrameworkBootstrap.Register<UIManager>();

            // 启动
            FrameworkBootstrap.Bootstrap();
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            FrameworkBootstrap.Shutdown();
        }

        private void OnApplicationPause(bool pause)
        {
            foreach (var module in FrameworkBootstrap.RegisteredModules)
            {
                if (pause && module.State == ModuleState.Running)
                {
                    module.Shutdown();
                }
            }
        }
    }
}
