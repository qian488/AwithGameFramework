using UnityEngine;
using Cysharp.Threading.Tasks;

namespace AwithGameFrame.Core.Interfaces
{
    /// <summary>
    /// 动画操作提供者接口
    /// 为不同的动画实现提供统一抽象
    /// </summary>
    public interface IAnimationProvider
    {
        /// <summary>
        /// 移动对象到指定位置
        /// </summary>
        /// <param name="target">目标对象</param>
        /// <param name="endValue">目标位置</param>
        /// <param name="duration">动画时长</param>
        /// <returns>动画任务</returns>
        UniTask MoveTo(Transform target, Vector3 endValue, float duration);

        /// <summary>
        /// 缩放对象
        /// </summary>
        /// <param name="target">目标对象</param>
        /// <param name="endValue">目标缩放</param>
        /// <param name="duration">动画时长</param>
        /// <returns>动画任务</returns>
        UniTask ScaleTo(Transform target, Vector3 endValue, float duration);

        /// <summary>
        /// 旋转对象
        /// </summary>
        /// <param name="target">目标对象</param>
        /// <param name="endValue">目标旋转</param>
        /// <param name="duration">动画时长</param>
        /// <returns>动画任务</returns>
        UniTask RotateTo(Transform target, Vector3 endValue, float duration);

        /// <summary>
        /// 淡入淡出效果
        /// </summary>
        /// <param name="target">目标对象</param>
        /// <param name="endValue">目标透明度</param>
        /// <param name="duration">动画时长</param>
        /// <returns>动画任务</returns>
        UniTask FadeTo(CanvasGroup target, float endValue, float duration);

        /// <summary>
        /// 停止指定对象的所有动画
        /// </summary>
        /// <param name="target">目标对象</param>
        void Kill(Transform target);

        /// <summary>
        /// 停止所有动画
        /// </summary>
        void KillAll();
    }
}
