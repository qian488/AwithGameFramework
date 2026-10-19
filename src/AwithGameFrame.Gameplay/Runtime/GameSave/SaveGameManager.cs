using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwithGameFrame.Core;
using AwithGameFrame.Core.DI;
using AwithGameFrame.Core.Logging;
using AwithGameFrame.Foundation.DataPersistence;

namespace AwithGameFrame.Extensions.GameSave
{
    /// <summary>
    /// 存档槽位元数据
    /// </summary>
    public struct SaveSlotInfo
    {
        public string SlotName { get; set; }
        public int Index { get; set; }
        public DateTime SaveTime { get; set; }
        public string Description { get; set; }

        public SaveSlotInfo(string slotName, int index, DateTime saveTime, string description = "")
        {
            SlotName = slotName;
            Index = index;
            SaveTime = saveTime;
            Description = description;
        }
    }

    /// <summary>
    /// 兼容的存档槽位管理器，依赖静态 DataPersistenceAPI；支持手动和快速存取。
    /// 数据和元信息分别保存，不提供原子提交、自动存档或版本迁移。
    /// 新项目可使用显式注入路径与序列化器的 VersionedSaveService&lt;T&gt;。
    /// </summary>
    public class SaveGameManager : BaseManager<SaveGameManager>
    {
        private const string SLOT_PREFIX = "SaveSlot_";
        private const string META_SUFFIX = "_Meta";
        private const int MAX_SLOT_COUNT = 20;

        private readonly Dictionary<int, SaveSlotInfo> _slotCache = new Dictionary<int, SaveSlotInfo>();
        private bool _initialized;
        public bool IsInitialized => _initialized;
        public Task Initialization { get; private set; } = Task.CompletedTask;

        public override int Priority => (int)ModulePriority.GameSpecific;

        public override void Initialize()
        {
            base.Initialize();
            ServiceLocator.Register<SaveGameManager>(this);
            Initialization = ReloadSlotsAsync();
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
        public async Task<bool> SaveToSlot<T>(int slotIndex, T data, string description = "") where T : class
        {
            await Initialization;
            if (!ValidateSlotIndex(slotIndex)) return false;

            try
            {
                var result = await DataPersistenceAPI.SaveGameDataAsync(SLOT_PREFIX + slotIndex, data);
                if (result != DataOperationResult.Success) return false;
                var info = new SaveSlotInfo("Slot_" + slotIndex, slotIndex, DateTime.Now, description);
                if (await PersistSlotMeta(slotIndex, info) != DataOperationResult.Success) return false;
                _slotCache[slotIndex] = info;
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
        public async Task<(T data, bool success)> LoadFromSlot<T>(int slotIndex) where T : class
        {
            await Initialization;
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
        public async Task<bool> DeleteSlot(int slotIndex)
        {
            await Initialization;
            if (!ValidateSlotIndex(slotIndex)) return false;

            try
            {
                var dataResult = await DataPersistenceAPI.DeleteAsync(SLOT_PREFIX + slotIndex, StorageType.JsonFile);
                var metaResult = await DataPersistenceAPI.DeleteAsync(SLOT_PREFIX + slotIndex + META_SUFFIX);
                if (dataResult != DataOperationResult.Success && dataResult != DataOperationResult.NotFound) return false;
                if (metaResult != DataOperationResult.Success && metaResult != DataOperationResult.NotFound) return false;
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
        public async Task<bool> QuickSave<T>(T data) where T : class
        {
            return await SaveToSlot(0, data, "QuickSave");
        }

        /// <summary>
        /// 快速加载（槽位0）
        /// </summary>
        public async Task<(T data, bool success)> QuickLoad<T>() where T : class
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

        private async Task<DataOperationResult> PersistSlotMeta(int slotIndex, SaveSlotInfo info)
        {
            return await DataPersistenceAPI.SaveSettingsAsync(SLOT_PREFIX + slotIndex + META_SUFFIX, info);
        }

        /// <summary>从配置的存储中恢复槽位及真实保存时间。</summary>
        public async Task ReloadSlotsAsync()
        {
            _slotCache.Clear();
            for (int i = 0; i < MAX_SLOT_COUNT; i++)
            {
                var key = SLOT_PREFIX + i + META_SUFFIX;
                var (info, result) = await DataPersistenceAPI.LoadSettingsAsync<SaveSlotInfo>(key);
                if (result == DataOperationResult.Success)
                {
                    _slotCache[i] = info;
                }
            }
        }

        #endregion
    }
}
