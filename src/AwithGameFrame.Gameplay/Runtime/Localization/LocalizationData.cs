using System;
using System.Collections.Generic;

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
    /// 可由任意宿主加载的本地化数据。
    /// </summary>
    [Serializable]
    public class LocalizationDataset
    {
        public string LanguageCode;
        public List<LocalizationEntry> Entries = new List<LocalizationEntry>();
    }

    /// <summary>宿主提供数据加载，不在通用模块中读取引擎资源。</summary>
    public interface ILocalizationSource
    {
        LocalizationDataset Load(string path);
        IEnumerable<LocalizationDataset> LoadAll();
    }
}
