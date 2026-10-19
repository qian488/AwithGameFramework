using System.Collections.Generic;
using UnityEngine;

namespace AwithGameFrame.Extensions.Localization
{
    /// <summary>Unity Inspector 中的本地化资源。</summary>
    [CreateAssetMenu(fileName = "LocalizationData", menuName = "AwithGameFrame/Localization Data")]
    public class LocalizationData : ScriptableObject
    {
        public string LanguageCode;
        public List<LocalizationEntry> Entries = new List<LocalizationEntry>();

        public LocalizationDataset ToDataset()
        {
            return new LocalizationDataset { LanguageCode = LanguageCode, Entries = Entries };
        }
    }

    /// <summary>将 Unity Resources 转换成通用的本地化数据源。</summary>
    public sealed class UnityLocalizationSource : ILocalizationSource
    {
        public LocalizationDataset Load(string path)
        {
            var asset = Resources.Load<LocalizationData>(path);
            return asset == null ? null : asset.ToDataset();
        }

        public IEnumerable<LocalizationDataset> LoadAll()
        {
            foreach (var asset in Resources.LoadAll<LocalizationData>("Localization"))
                yield return asset.ToDataset();
        }
    }
}
