using UnityEngine;
using Cysharp.Threading.Tasks;
using AwithGameFrame.Foundation.DataPersistence;

namespace AwithGameFrame.Foundation.Examples
{
    /// <summary>
    /// Foundation包使用示例
    /// 展示如何使用统一的API进行异步、动画、序列化操作
    /// </summary>
    public class FoundationUsageExample : MonoBehaviour
    {
        [System.Serializable]
        public class PlayerData
        {
            public string name;
            public int level;
            public float experience;
        }

        async void Start()
        {
            Debug.Log("=== Foundation包使用示例开始 ===");

            // 初始化Foundation包
            FoundationAPI.Initialize();

            // 测试异步操作
            await TestAsyncOperations();

            // 测试动画操作
            await TestAnimationOperations();

            // 测试序列化操作
            await TestSerializationOperations();

            // 测试数据持久化
            await TestDataPersistence();

            Debug.Log("=== Foundation包使用示例完成 ===");
        }

        private async UniTask TestAsyncOperations()
        {
            Debug.Log("\n--- 测试异步操作 ---");

            // 创建已完成的任务
            var result = await FoundationAPI.FromResult("Hello Foundation!");
            Debug.Log($"异步结果: {result}");

            // 延迟操作
            Debug.Log("开始延迟2秒...");
            await FoundationAPI.Delay(2000);
            Debug.Log("延迟完成!");

            // 等待多个任务
            var task1 = FoundationAPI.Delay(1000);
            var task2 = FoundationAPI.Delay(1500);
            var task3 = FoundationAPI.Delay(2000);

            Debug.Log("开始等待多个任务...");
            await FoundationAPI.WhenAll(task1, task2, task3);
            Debug.Log("所有任务完成!");
        }

        private async UniTask TestAnimationOperations()
        {
            Debug.Log("\n--- 测试动画操作 ---");

            // 移动动画
            Debug.Log("开始移动动画...");
            await FoundationAPI.MoveTo(transform, Vector3.up * 2, 1.0f);
            Debug.Log("移动动画完成!");

            // 缩放动画
            Debug.Log("开始缩放动画...");
            await FoundationAPI.ScaleTo(transform, Vector3.one * 1.5f, 1.0f);
            Debug.Log("缩放动画完成!");

            // 旋转动画
            Debug.Log("开始旋转动画...");
            await FoundationAPI.RotateTo(transform, Vector3.up * 360, 2.0f);
            Debug.Log("旋转动画完成!");

            // 恢复原始状态
            await FoundationAPI.MoveTo(transform, Vector3.zero, 0.5f);
            await FoundationAPI.ScaleTo(transform, Vector3.one, 0.5f);
        }

        private async UniTask TestSerializationOperations()
        {
            Debug.Log("\n--- 测试序列化操作 ---");

            var playerData = new PlayerData
            {
                name = "TestPlayer",
                level = 10,
                experience = 1250.5f
            };

            // 序列化
            Debug.Log("开始序列化...");
            var json = await FoundationAPI.SerializeAsync(playerData);
            Debug.Log($"序列化结果: {json}");

            // 验证JSON
            bool isValid = FoundationAPI.IsValidJson(json);
            Debug.Log($"JSON是否有效: {isValid}");

            // 反序列化
            Debug.Log("开始反序列化...");
            var deserializedData = await FoundationAPI.DeserializeAsync<PlayerData>(json);
            Debug.Log($"反序列化结果: 姓名={deserializedData.name}, 等级={deserializedData.level}, 经验={deserializedData.experience}");
        }

        private async UniTask TestDataPersistence()
        {
            Debug.Log("\n--- 测试数据持久化 ---");

            // 初始化数据持久化系统
            var initResult = await DataPersistenceAPI.InitializeAsync();
            Debug.Log($"数据持久化初始化结果: {initResult}");

            if (initResult == DataOperationResult.Success)
            {
                var playerData = new PlayerData
                {
                    name = "FoundationPlayer",
                    level = 15,
                    experience = 2500.0f
                };

                // 保存数据
                var saveResult = await DataPersistenceAPI.SaveAsync("player_data", playerData);
                Debug.Log($"保存数据结果: {saveResult}");

                // 加载数据
                var (loadedData, loadResult) = await DataPersistenceAPI.LoadAsync<PlayerData>("player_data");
                if (loadResult == DataOperationResult.Success)
                {
                    Debug.Log($"加载数据成功: 姓名={loadedData.name}, 等级={loadedData.level}, 经验={loadedData.experience}");
                }
                else
                {
                    Debug.LogError($"加载数据失败: {loadResult}");
                }
            }
        }
    }
}
