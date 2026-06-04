using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using AwithGameFrame.Core;
using AwithGameFrame.Core.Config;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Event;
using AwithGameFrame.Core.Logging;

namespace AwithGameFrame.Extensions.Localization
{
    /// <summary>
    /// 本地化条目
    /// </summary>
    [Serializable]
    public struct LocalizationEntry
    {
        /// <summary>本地化键</summary>
        public string Key;
        /// <summary>当前语言对应的文本值</summary>
        public string Value;
    }

    /// <summary>
    /// 本地化数据容器 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "LocalizationData", menuName = "AwithGameFrame/Localization Data")]
    public class LocalizationData : ScriptableObject
    {
        public string LanguageCode;
        public List<LocalizationEntry> Entries = new List<LocalizationEntry>();
    }

    /// <summary>
    /// 预定义的语言切换事件
    /// </summary>
    public static class LocalizationEvents
    {
        public static readonly EventId LanguageChanged = new EventId("LanguageChanged");
    }

    /// <summary>
    /// 多语言本地化管理器
    /// 支持CSV/JSON数据源、语言切换、动态字符串注入
    /// 放在Extensions包中
    /// </summary>
    public class LocalizationManager : BaseManager<LocalizationManager>
    {
        private readonly Dictionary<string, string> _currentLocale = new Dictionary<string, string>();
        private Dictionary<string, LocalizationData> _loadedDatasets = new Dictionary<string, LocalizationData>();
        private string _currentLanguage;
        private bool _initialized;

        public string CurrentLanguage => _currentLanguage;
        public override int Priority => (int)ModulePriority.GameSpecific;

        public override void Initialize()
        {
            base.Initialize();
            ServiceLocator.Register<LocalizationManager>(this);

            // 从框架配置获取默认语言
            _currentLanguage = ConfigAPI.GetDefaultLanguage();
            _initialized = true;
            LoggingAPI.Info(LogCategory.Core, $"LocalizationManager 初始化完成 (语言: {_currentLanguage})");
        }

        public override void Shutdown()
        {
            _currentLocale.Clear();
            _loadedDatasets.Clear();
            _initialized = false;
            base.Shutdown();
        }

        #region 数据加载

        /// <summary>
        /// 从Resources加载本地化数据集
        /// </summary>
        public void LoadDataset(string resourcePath)
        {
            var dataset = Resources.Load<LocalizationData>(resourcePath);
            if (dataset == null)
            {
                FrameworkLogger.Warn($"未找到本地化数据: Resources/{resourcePath}", LogCategory.Core);
                return;
            }
            _loadedDatasets[dataset.LanguageCode] = dataset;
            FrameworkLogger.Info($"已加载本地化数据: {dataset.LanguageCode}, 条目数: {dataset.Entries.Count}", LogCategory.Core);
        }

        /// <summary>
        /// 加载所有内置数据集(从 Resources/Localization/ 路径)
        /// </summary>
        public void LoadBuiltInDatasets()
        {
            var datasets = Resources.LoadAll<LocalizationData>("Localization");
            foreach (var dataset in datasets)
            {
                _loadedDatasets[dataset.LanguageCode] = dataset;
                FrameworkLogger.Info($"已加载内置本地化数据: {dataset.LanguageCode}, 条目数: {dataset.Entries.Count}", LogCategory.Core);
            }

            // 加载完成后切换到当前语言
            if (_loadedDatasets.Count > 0)
            {
                ApplyLanguage(_currentLanguage);
            }
        }

        #endregion

        #region 语言切换

        /// <summary>
        /// 切换到指定语言
        /// </summary>
        public void SwitchLanguage(string languageCode)
        {
            if (_currentLanguage == languageCode && _currentLocale.Count > 0) return;

            ApplyLanguage(languageCode);

            // 触发语言切换事件
            EventCenter.GetInstance().EventTrigger(LocalizationEvents.LanguageChanged, languageCode);
            LoggingAPI.Info(LogCategory.Core, $"语言已切换: {languageCode}");
        }

        private void ApplyLanguage(string languageCode)
        {
            _currentLocale.Clear();

            if (_loadedDatasets.TryGetValue(languageCode, out var dataset))
            {
                foreach (var entry in dataset.Entries)
                {
                    _currentLocale[entry.Key] = entry.Value;
                }
                _currentLanguage = languageCode;
            }
            else
            {
                FrameworkLogger.Warn($"未找到语言数据: {languageCode}，使用默认语言", LogCategory.Core);
                TryFallbackLanguage();
            }
        }

        private void TryFallbackLanguage()
        {
            foreach (var lang in _loadedDatasets.Keys)
            {
                ApplyLanguage(lang);
                return;
            }
            _currentLocale.Clear();
        }

        #endregion

        #region 文本获取

        /// <summary>
        /// 获取本地化文本
        /// </summary>
        /// <param name="key">本地化键</param>
        /// <param name="fallback">未找到时的回退文本</param>
        public string GetText(string key, string fallback = null)
        {
            if (_currentLocale.TryGetValue(key, out var value))
                return value;

            return fallback ?? key;
        }

        /// <summary>
        /// 获取格式化本地化文本 (string.Format)
        /// </summary>
        /// <param name="key">本地化键</param>
        /// <param name="args">格式化参数</param>
        public string GetFormat(string key, params object[] args)
        {
            var format = GetText(key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }

        /// <summary>
        /// 获取本地化文本（索引器快捷访问）
        /// </summary>
        public string this[string key] => GetText(key);

        /// <summary>
        /// 检查本地化键是否存在
        /// </summary>
        public bool HasKey(string key) => _currentLocale.ContainsKey(key);

        /// <summary>
        /// 获取当前本地化条目总数
        /// </summary>
        public int EntryCount => _currentLocale.Count;

        #endregion

        #region 可用语言

        /// <summary>
        /// 获取所有已加载的语言代码
        /// </summary>
        public IReadOnlyList<string> GetAvailableLanguages()
        {
            var list = new List<string>(_loadedDatasets.Keys);
            return list.AsReadOnly();
        }

        #endregion
    }
}
