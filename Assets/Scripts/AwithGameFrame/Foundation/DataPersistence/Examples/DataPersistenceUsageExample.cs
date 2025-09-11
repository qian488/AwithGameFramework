using System.Collections;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace AwithGameFrame.Foundation.DataPersistence
{
    /// <summary>
    /// 数据持久化使用示例
    /// 展示如何使用不同的存储方式
    /// </summary>
    public class DataPersistenceUsageExample : MonoBehaviour
    {
        [System.Serializable]
        public class PlayerData
        {
            public string playerName;
            public int level;
            public float experience;
            public Vector3 position;
        }

        [System.Serializable]
        public class GameSettings
        {
            public float masterVolume = 1.0f;
            public float musicVolume = 0.8f;
            public float sfxVolume = 0.9f;
            public bool fullscreen = true;
        }

        async void Start()
        {
            Debug.Log("=== 数据持久化使用示例开始 ===");

            // 初始化数据持久化系统
            var config = new DataPersistenceConfig
            {
                DefaultStorageType = StorageType.JsonFile,
                DefaultSerializationFormat = SerializationFormat.Json
            };
            
            var initResult = await DataPersistenceAPI.InitializeAsync(config);
            Debug.Log($"数据持久化系统初始化结果: {initResult}");

            if (initResult == DataOperationResult.Success)
            {
                await RunAllExamples();
            }

            Debug.Log("=== 数据持久化使用示例完成 ===");
        }

        private async UniTask RunAllExamples()
        {
            // 1. 用户设置存储（PlayerPrefs）
            await TestUserSettings();

            // 2. 游戏数据存储（JSON文件）
            await TestGameData();

            // 3. 缓存数据存储（二进制文件）
            await TestCacheData();

            // 4. 复杂数据存储（数据库）
            await TestComplexData();

            // 5. 管理功能测试
            await TestManagementFeatures();
        }

        private async UniTask TestUserSettings()
        {
            Debug.Log("\n--- 测试用户设置存储（PlayerPrefs） ---");

            var settings = new GameSettings
            {
                masterVolume = 0.7f,
                musicVolume = 0.6f,
                sfxVolume = 0.8f,
                fullscreen = false
            };

            // 保存设置
            var saveResult = await DataPersistenceAPI.SaveSettingsAsync("game_settings", settings);
            Debug.Log($"保存用户设置结果: {saveResult}");

            // 加载设置
            var (loadedSettings, loadResult) = await DataPersistenceAPI.LoadSettingsAsync<GameSettings>("game_settings");
            if (loadResult == DataOperationResult.Success)
            {
                Debug.Log($"加载用户设置成功: 音量={loadedSettings.masterVolume}, 全屏={loadedSettings.fullscreen}");
            }
        }

        private async UniTask TestGameData()
        {
            Debug.Log("\n--- 测试游戏数据存储（JSON文件） ---");

            var playerData = new PlayerData
            {
                playerName = "TestPlayer",
                level = 5,
                experience = 1250.5f,
                position = new Vector3(10, 0, 20)
            };

            // 保存游戏数据
            var saveResult = await DataPersistenceAPI.SaveGameDataAsync("player_data", playerData);
            Debug.Log($"保存游戏数据结果: {saveResult}");

            // 加载游戏数据
            var (loadedData, loadResult) = await DataPersistenceAPI.LoadGameDataAsync<PlayerData>("player_data");
            if (loadResult == DataOperationResult.Success)
            {
                Debug.Log($"加载游戏数据成功: 玩家={loadedData.playerName}, 等级={loadedData.level}, 位置={loadedData.position}");
            }
        }

        private async UniTask TestCacheData()
        {
            Debug.Log("\n--- 测试缓存数据存储（二进制文件） ---");

            var cacheData = new { timestamp = System.DateTime.Now, data = "cached_data_12345" };

            // 保存缓存数据
            var saveResult = await DataPersistenceAPI.SaveCacheDataAsync("cache_data", cacheData);
            Debug.Log($"保存缓存数据结果: {saveResult}");

            // 加载缓存数据
            var (loadedData, loadResult) = await DataPersistenceAPI.LoadCacheDataAsync<object>("cache_data");
            if (loadResult == DataOperationResult.Success)
            {
                Debug.Log($"加载缓存数据成功: {loadedData}");
            }
        }

        private async UniTask TestComplexData()
        {
            Debug.Log("\n--- 测试复杂数据存储（数据库） ---");

            var complexData = new PlayerData
            {
                playerName = "ComplexPlayer",
                level = 10,
                experience = 5000.0f,
                position = new Vector3(100, 50, 200)
            };

            // 保存复杂数据（使用SQLite）
            var saveResult = await DataPersistenceAPI.SaveComplexDataAsync("complex_player_data", complexData, DatabaseType.SQLite);
            Debug.Log($"保存复杂数据结果: {saveResult}");

            // 加载复杂数据
            var (loadedData, loadResult) = await DataPersistenceAPI.LoadComplexDataAsync<PlayerData>("complex_player_data", DatabaseType.SQLite);
            if (loadResult == DataOperationResult.Success)
            {
                Debug.Log($"加载复杂数据成功: 玩家={loadedData.playerName}, 等级={loadedData.level}");
            }
        }

        private async UniTask TestManagementFeatures()
        {
            Debug.Log("\n--- 测试管理功能 ---");

            // 获取所有数据键
            var allKeys = await DataPersistenceAPI.GetAllKeysAsync();
            Debug.Log($"所有数据键: {string.Join(", ", allKeys)}");

            // 获取统计信息
            var stats = await DataPersistenceAPI.GetStatisticsAsync();
            Debug.Log($"存储统计信息: 总键数={stats.ItemCount}, 总大小={stats.TotalSize} bytes");

            // 检查数据是否存在
            var exists = await DataPersistenceAPI.ExistsAsync("player_data");
            Debug.Log($"player_data是否存在: {exists}");

            // 删除特定数据
            var deleteResult = await DataPersistenceAPI.DeleteAsync("cache_data");
            Debug.Log($"删除cache_data结果: {deleteResult}");

            // 验证删除
            var existsAfterDelete = await DataPersistenceAPI.ExistsAsync("cache_data");
            Debug.Log($"删除后cache_data是否存在: {existsAfterDelete}");
        }
    }
}
