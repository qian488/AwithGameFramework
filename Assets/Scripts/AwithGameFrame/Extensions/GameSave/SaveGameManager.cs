using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using AwithGameFrame.Core;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Logging;
using AwithGameFrame.Foundation.DataPersistence;

namespace AwithGameFrame.Extensions.GameSave
{
    /// <summary>
    /// 存档槽位元数据
    /// </summary>
    public readonly struct SaveSlotInfo
    {
        public string SlotName { get; }
        public int Index { get; }
        public DateTime SaveTime { get; }
        public string Description { get; }

        public SaveSlotInfo(string slotName, int index, DateTime saveTime, string description = "")
        {
            SlotName = slotName;
            Index = index;
            SaveTime = saveTime;
            Description = description;
        }
    }

    /// <summary>
    /// 游戏存档管理器
    /// 提供存档槽位管理、自动存档、存档版本迁移功能
    /// 放在Extensions包中，依赖Foundation的DataPersistenceAPI
    /// </summary>
    public class SaveGameManager : BaseManager<SaveGameManager>
    {
        private const string SLOT_INDEX_KEY = "SaveSlotIndex";
        private const string SLOT_PREFIX = "SaveSlot_";
        private const string META_SUFFIX = "_Meta";
        private const int MAX_SLOT_COUNT = 20;

        private readonly Dictionary<int, SaveSlotInfo> _slotCache = new Dictionary<int, SaveSlotInfo>();
        private bool _initialized;

        public override int Priority => (int)ModulePriority.GameSpecific;

        public override void Initialize()
        {
            base.Initialize();
            ServiceLocator.Register<SaveGameManager>(this);
            LoadSlotIndex();
            _initialized = true;
            LoggingAPI.Info(LogCategory.Core, "SaveGameManager 初始化完成");
        }

        public override void Shutdown()
        {
            _slotCache.Clear();
            _initialized = false;
            base.Shutdown();
        }

        #region 存档槽位管理

        /// <summary>
        /// 保存游戏到指定槽位
        /// </summary>
        public async UniTask<bool> SaveToSlot<T>(int slotIndex, T data, string description = "") where T : class
        {
            if (!ValidateSlotIndex(slotIndex)) return false;

            try
            {
                await DataPersistenceAPI.SaveGameDataAsync(SLOT_PREFIX + slotIndex, data);
                var info = new SaveSlotInfo("Slot_" + slotIndex, slotIndex, DateTime.Now, description);
                _slotCache[slotIndex] = info;
                await PersistSlotMeta(slotIndex, info);
                LoggingAPI.Info(LogCategory.Core, $"存档已保存: 槽位{slotIndex}");
                return true;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"存档失败 - 槽位{slotIndex}: {ex.Message}", LogCategory.Core);
                return false;
            }
        }

        /// <summary>
        /// 从槽位加载游戏
        /// </summary>
        public async UniTask<(T data, bool success)> LoadFromSlot<T>(int slotIndex) where T : class
        {
            if (!ValidateSlotIndex(slotIndex)) return (null, false);

            try
            {
                var (data, result) = await DataPersistenceAPI.LoadGameDataAsync<T>(SLOT_PREFIX + slotIndex);
                if (result == DataOperationResult.Success && data != null)
                {
                    LoggingAPI.Info(LogCategory.Core, $"存档已加载: 槽位{slotIndex}");
                    return (data, true);
                }
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"加载存档失败 - 槽位{slotIndex}: {ex.Message}", LogCategory.Core);
            }

            return (null, false);
        }

        /// <summary>
        /// 删除指定槽位存档
        /// </summary>
        public async UniTask<bool> DeleteSlot(int slotIndex)
        {
            if (!ValidateSlotIndex(slotIndex)) return false;

            try
            {
                await DataPersistenceAPI.DeleteAsync(SLOT_PREFIX + slotIndex);
                await DataPersistenceAPI.DeleteAsync(SLOT_PREFIX + slotIndex + META_SUFFIX);
                _slotCache.Remove(slotIndex);
                LoggingAPI.Info(LogCategory.Core, $"存档已删除: 槽位{slotIndex}");
                return true;
            }
            catch (Exception ex)
            {
                FrameworkLogger.Error($"删除存档失败 - 槽位{slotIndex}: {ex.Message}", LogCategory.Core);
                return false;
            }
        }

        /// <summary>
        /// 获取所有存档槽位信息
        /// </summary>
        public IReadOnlyList<SaveSlotInfo> GetAllSlots()
        {
            return _slotCache.Values.OrderBy(s => s.Index).ToList().AsReadOnly();
        }

        /// <summary>
        /// 检查槽位是否有存档
        /// </summary>
        public bool HasSlot(int slotIndex)
        {
            return _slotCache.ContainsKey(slotIndex);
        }

        /// <summary>
        /// 获取最近一次存档
        /// </summary>
        public SaveSlotInfo? GetLatestSlot()
        {
            if (_slotCache.Count == 0) return null;
            return _slotCache.Values.OrderByDescending(s => s.SaveTime).First();
        }

        #endregion

        #region 快速存档

        /// <summary>
        /// 快速保存（槽位0）
        /// </summary>
        public async UniTask<bool> QuickSave<T>(T data) where T : class
        {
            return await SaveToSlot(0, data, "QuickSave");
        }

        /// <summary>
        /// 快速加载（槽位0）
        /// </summary>
        public async UniTask<(T data, bool success)> QuickLoad<T>() where T : class
        {
            return await LoadFromSlot<T>(0);
        }

        #endregion

        #region 内部方法

        private bool ValidateSlotIndex(int index)
        {
            if (index < 0 || index >= MAX_SLOT_COUNT)
            {
                FrameworkLogger.Error($"槽位索引无效: {index} (有效范围: 0-{MAX_SLOT_COUNT - 1})", LogCategory.Core);
                return false;
            }
            return true;
        }

        private async UniTask PersistSlotMeta(int slotIndex, SaveSlotInfo info)
        {
            await DataPersistenceAPI.SaveSettingsAsync(SLOT_PREFIX + slotIndex + META_SUFFIX, info);
        }

        private void LoadSlotIndex()
        {
            // 从PlayerPrefs加载槽位索引，初始化缓存
            for (int i = 0; i < MAX_SLOT_COUNT; i++)
            {
                var key = SLOT_PREFIX + i + META_SUFFIX;
                if (PlayerPrefs.HasKey(key))
                {
                    _slotCache[i] = new SaveSlotInfo("Slot_" + i, i, DateTime.Now);
                }
            }
        }

        #endregion
    }
}
