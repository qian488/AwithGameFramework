using UnityEngine;
using Cysharp.Threading.Tasks;
using AwithGameFrame.Foundation;

namespace AwithGameFrame
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
        /// <typeparam name="T">结果类型</typeparam>
        /// <param name="result">结果值</param>
        /// <returns>已完成的任务</returns>
        public static UniTask<T> FromResult<T>(T result)
        {
            return ProviderManager.AsyncProvider.FromResult(result);
        }

        /// <summary>
        /// 延迟指定时间
        /// </summary>
        /// <param name="milliseconds">延迟毫秒数</param>
        /// <returns>延迟任务</returns>
        public static UniTask Delay(int milliseconds)
        {
            return ProviderManager.AsyncProvider.Delay(milliseconds);
        }

        /// <summary>
        /// 等待所有任务完成
        /// </summary>
        /// <param name="tasks">任务数组</param>
        /// <returns>等待任务</returns>
        public static UniTask WhenAll(params UniTask[] tasks)
        {
            return ProviderManager.AsyncProvider.WhenAll(tasks);
        }

        #endregion

        #region 动画操作

        /// <summary>
        /// 移动对象到指定位置
        /// </summary>
        /// <param name="target">目标对象</param>
        /// <param name="endValue">目标位置</param>
        /// <param name="duration">动画时长</param>
        /// <returns>动画任务</returns>
        public static UniTask MoveTo(Transform target, Vector3 endValue, float duration)
        {
            return ProviderManager.AnimationProvider.MoveTo(target, endValue, duration);
        }

        /// <summary>
        /// 缩放对象
        /// </summary>
        /// <param name="target">目标对象</param>
        /// <param name="endValue">目标缩放</param>
        /// <param name="duration">动画时长</param>
        /// <returns>动画任务</returns>
        public static UniTask ScaleTo(Transform target, Vector3 endValue, float duration)
        {
            return ProviderManager.AnimationProvider.ScaleTo(target, endValue, duration);
        }

        /// <summary>
        /// 旋转对象
        /// </summary>
        /// <param name="target">目标对象</param>
        /// <param name="endValue">目标旋转</param>
        /// <param name="duration">动画时长</param>
        /// <returns>动画任务</returns>
        public static UniTask RotateTo(Transform target, Vector3 endValue, float duration)
        {
            return ProviderManager.AnimationProvider.RotateTo(target, endValue, duration);
        }

        /// <summary>
        /// 淡入淡出效果
        /// </summary>
        /// <param name="target">目标对象</param>
        /// <param name="endValue">目标透明度</param>
        /// <param name="duration">动画时长</param>
        /// <returns>动画任务</returns>
        public static UniTask FadeTo(CanvasGroup target, float endValue, float duration)
        {
            return ProviderManager.AnimationProvider.FadeTo(target, endValue, duration);
        }

        #endregion

        #region 序列化操作

        /// <summary>
        /// 序列化对象为字符串
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字符串</returns>
        public static UniTask<string> SerializeAsync<T>(T obj)
        {
            return ProviderManager.SerializationProvider.SerializeAsync(obj);
        }

        /// <summary>
        /// 反序列化字符串为对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="json">序列化的字符串</param>
        /// <returns>反序列化后的对象</returns>
        public static UniTask<T> DeserializeAsync<T>(string json)
        {
            return ProviderManager.SerializationProvider.DeserializeAsync<T>(json);
        }

        /// <summary>
        /// 检查字符串是否为有效的序列化数据
        /// </summary>
        /// <param name="json">要检查的字符串</param>
        /// <returns>是否有效</returns>
        public static bool IsValidJson(string json)
        {
            return ProviderManager.SerializationProvider.IsValidJson(json);
        }

        #endregion
    }
}
