using System.Collections.Generic;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Core.Config
{
    /// <summary>
    /// 配置系统API门面
    /// 提供简洁的静态API，内部委托给ConfigManager实例
    /// </summary>
    public static class ConfigAPI
    {
        #region 框架配置API
        /// <summary>
        /// 获取框架配置
        /// </summary>
        /// <returns>框架配置</returns>
        public static FrameworkConfig GetFrameworkConfig()
        {
            return ConfigManager.GetInstance().FrameworkConfig;
        }

        /// <summary>
        /// 获取资源根路径
        /// </summary>
        /// <returns>资源根路径</returns>
        public static string GetResourceRootPath()
        {
            return GetFrameworkConfig().resourceRootPath;
        }

        /// <summary>
        /// 获取默认语言
        /// </summary>
        /// <returns>默认语言</returns>
        public static string GetDefaultLanguage()
        {
            return GetFrameworkConfig().defaultLanguage;
        }

        /// <summary>
        /// 获取默认音量
        /// </summary>
        /// <returns>默认音量</returns>
        public static float GetDefaultVolume()
        {
            return GetFrameworkConfig().defaultVolume;
        }

        /// <summary>
        /// 获取日志级别
        /// </summary>
        /// <returns>日志级别</returns>
        public static LogLevel GetLogLevel()
        {
            return GetFrameworkConfig().logLevel;
        }

        /// <summary>
        /// 获取日志模式
        /// </summary>
        /// <returns>日志模式</returns>
        public static LogMode GetLogMode()
        {
            return GetFrameworkConfig().logMode;
        }

        /// <summary>
        /// 获取日志配置
        /// </summary>
        /// <returns>日志配置对象</returns>
        public static LoggingConfig GetLoggingConfig()
        {
            return GetFrameworkConfig().GetLoggingConfig();
        }

        /// <summary>
        /// 重新加载框架配置
        /// </summary>
        public static void ReloadFrameworkConfig()
        {
            ConfigManager.GetInstance().ReloadFrameworkConfig();
        }

        /// <summary>
        /// 创建默认FrameworkConfig资源文件
        /// </summary>
        public static void CreateDefaultFrameworkConfigAsset()
        {
            ConfigManager.GetInstance().CreateDefaultFrameworkConfigAsset();
        }

        /// <summary>
        /// 配置变更事件数据
        /// </summary>
        [System.Serializable]
        public class ConfigChangedEventData
        {
            public string key;
            public object value;
        }


        /// <summary>
        /// 检查指定模块是否启用
        /// </summary>
        /// <param name="moduleName">模块名称</param>
        /// <returns>是否启用</returns>
        public static bool IsModuleEnabled(string moduleName)
        {
            var config = GetFrameworkConfig();
            switch (moduleName.ToLower())
            {
                case "audio": return config.enableAudioModule;
                case "ui": return config.enableUIModule;
                case "input": return config.enableInputModule;
                case "pool": return config.enablePoolModule;
                case "logging": return config.enableLoggingModule;
                default: return false;
            }
        }
        #endregion

        #region 游戏配置API
        /// <summary>
        /// 加载游戏配置
        /// </summary>
        /// <typeparam name="T">配置数据类型</typeparam>
        /// <param name="configName">配置名称</param>
        /// <param name="filePath">文件路径</param>
        public static void LoadGameConfig<T>(string configName, string filePath) where T : GameConfigData
        {
            ConfigManager.GetInstance().LoadGameConfig<T>(configName, filePath);
        }

        /// <summary>
        /// 获取游戏配置数据
        /// </summary>
        /// <typeparam name="T">配置数据类型</typeparam>
        /// <param name="configName">配置名称</param>
        /// <param name="id">数据ID</param>
        /// <returns>配置数据</returns>
        public static T GetGameConfigData<T>(string configName, int id) where T : GameConfigData
        {
            return ConfigManager.GetInstance().GetGameConfigData<T>(configName, id);
        }

        /// <summary>
        /// 获取所有游戏配置数据
        /// </summary>
        /// <typeparam name="T">配置数据类型</typeparam>
        /// <param name="configName">配置名称</param>
        /// <returns>所有配置数据</returns>
        public static T[] GetAllGameConfigData<T>(string configName) where T : GameConfigData
        {
            return ConfigManager.GetInstance().GetAllGameConfigData<T>(configName);
        }
        #endregion

        #region 运行时配置API
        /// <summary>
        /// 获取运行时配置值
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>配置值</returns>
        public static T Get<T>(string key, T defaultValue = default(T))
        {
            return ConfigManager.GetInstance().Get(key, defaultValue);
        }

        /// <summary>
        /// 设置运行时配置值
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="value">配置值</param>
        public static void Set<T>(string key, T value)
        {
            ConfigManager.GetInstance().Set(key, value);
        }

        /// <summary>
        /// 检查运行时配置是否存在
        /// </summary>
        /// <param name="key">配置键</param>
        /// <returns>是否存在</returns>
        public static bool Has(string key)
        {
            return ConfigManager.GetInstance().Has(key);
        }

        /// <summary>
        /// 移除运行时配置
        /// </summary>
        /// <param name="key">配置键</param>
        public static void Remove(string key)
        {
            ConfigManager.GetInstance().Remove(key);
        }

        /// <summary>
        /// 清空所有运行时配置
        /// </summary>
        public static void Clear()
        {
            ConfigManager.GetInstance().Clear();
        }
        #endregion

        #region 系统管理
        /// <summary>
        /// 初始化配置系统
        /// </summary>
        public static void Initialize()
        {
            ConfigManager.GetInstance().Initialize();
        }

        /// <summary>
        /// 检查配置系统是否已初始化
        /// </summary>
        /// <returns>是否已初始化</returns>
        public static bool IsInitialized()
        {
            return ConfigManager.GetInstance().IsInitialized();
        }
        #endregion
    }
}
