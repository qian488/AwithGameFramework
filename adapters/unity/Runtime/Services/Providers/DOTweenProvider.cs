using System.Threading.Tasks;
using UnityEngine;
using AwithGameFrame.Core.Interfaces;
using AwithGameFrame.Core.Logging;
using DG.Tweening;

namespace AwithGameFrame.Foundation.Providers
{
    /// <summary>
    /// DOTween动画操作提供者
    /// 使用 TaskCompletionSource 将 DOTween 回调转为 Task
    /// </summary>
    public class DOTweenProvider : IAnimationProvider
    {
        public Task MoveTo(Transform target, Vector3 endValue, float duration)
        {
            if (target == null)
            {
                FrameworkLogger.Error("DOTweenProvider: Target transform is null for Move operation.", LogCategory.Core);
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            target.DOMove(endValue, duration)
                .OnComplete(() => tcs.TrySetResult(true))
                .OnKill(() => tcs.TrySetResult(false));
            return tcs.Task;
        }

        public Task ScaleTo(Transform target, Vector3 endValue, float duration)
        {
            if (target == null)
            {
                FrameworkLogger.Error("DOTweenProvider: Target transform is null for Scale operation.", LogCategory.Core);
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            target.DOScale(endValue, duration)
                .OnComplete(() => tcs.TrySetResult(true))
                .OnKill(() => tcs.TrySetResult(false));
            return tcs.Task;
        }

        public Task RotateTo(Transform target, Vector3 endValue, float duration)
        {
            if (target == null)
            {
                FrameworkLogger.Error("DOTweenProvider: Target transform is null for Rotate operation.", LogCategory.Core);
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            target.DORotate(endValue, duration)
                .OnComplete(() => tcs.TrySetResult(true))
                .OnKill(() => tcs.TrySetResult(false));
            return tcs.Task;
        }

        public Task FadeTo(CanvasGroup target, float endValue, float duration)
        {
            if (target == null)
            {
                FrameworkLogger.Error("DOTweenProvider: Target CanvasGroup is null for Fade operation.", LogCategory.Core);
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            DOTween.To(() => target.alpha, x => target.alpha = x, endValue, duration)
                .OnComplete(() => tcs.TrySetResult(true))
                .OnKill(() => tcs.TrySetResult(false));
            return tcs.Task;
        }

        public void Kill(Transform target)
        {
            target.DOKill();
        }

        public void KillAll()
        {
            DOTween.KillAll();
        }
    }
}
