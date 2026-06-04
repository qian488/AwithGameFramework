using UnityEngine;
using Cysharp.Threading.Tasks;
using AwithGameFrame.Foundation;

namespace AwithGameFrame.Foundation
{
    /// <summary>
    /// Foundation API
    /// 提供统一的异步、动画、序列化操作接口
    /// </summary>
    public static class FoundationAPI
    {
        /// <summary>
        /// 初始化Foundation包
        /// </summary>
        public static void Initialize()
        {
            ProviderManager.InitializeDefaultProviders();
        }

        #region 异步操作

        /// <summary>
        /// 创建已完成的任务
        /// </summary>
        public static async UniTask<T> FromResult<T>(T result)
        {
            return await ProviderManager.AsyncProvider.FromResult(result);
        }

        /// <summary>
        /// 延迟指定时间
        /// </summary>
        public static async UniTask Delay(int milliseconds)
        {
            await ProviderManager.AsyncProvider.Delay(milliseconds);
        }

        /// <summary>
        /// 等待所有任务完成
        /// </summary>
        public static async UniTask WhenAll(params UniTask[] tasks)
        {
            await UniTask.WhenAll(tasks);
        }

        #endregion

        #region 动画操作

        /// <summary>
        /// 移动对象到指定位置
        /// </summary>
        public static async UniTask MoveTo(Transform target, Vector3 endValue, float duration)
        {
            await ProviderManager.AnimationProvider.MoveTo(target, endValue, duration);
        }

        /// <summary>
        /// 缩放对象
        /// </summary>
        public static async UniTask ScaleTo(Transform target, Vector3 endValue, float duration)
        {
            await ProviderManager.AnimationProvider.ScaleTo(target, endValue, duration);
        }

        /// <summary>
        /// 旋转对象
        /// </summary>
        public static async UniTask RotateTo(Transform target, Vector3 endValue, float duration)
        {
            await ProviderManager.AnimationProvider.RotateTo(target, endValue, duration);
        }

        /// <summary>
        /// 淡入淡出效果
        /// </summary>
        public static async UniTask FadeTo(CanvasGroup target, float endValue, float duration)
        {
            await ProviderManager.AnimationProvider.FadeTo(target, endValue, duration);
        }

        #endregion

        #region 序列化操作

        /// <summary>
        /// 序列化对象为字符串
        /// </summary>
        public static async UniTask<string> SerializeAsync<T>(T obj)
        {
            return await ProviderManager.SerializationProvider.SerializeAsync(obj);
        }

        /// <summary>
        /// 反序列化字符串为对象
        /// </summary>
        public static async UniTask<T> DeserializeAsync<T>(string json)
        {
            return await ProviderManager.SerializationProvider.DeserializeAsync<T>(json);
        }

        /// <summary>
        /// 检查字符串是否为有效的序列化数据
        /// </summary>
        public static bool IsValidJson(string json)
        {
            return ProviderManager.SerializationProvider.IsValidJson(json);
        }

        #endregion
    }
}
