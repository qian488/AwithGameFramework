using UnityEngine;
using Cysharp.Threading.Tasks;
using AwithGameFrame.Core.Interfaces;
using AwithGameFrame.Core.Logging;
using DG.Tweening;

namespace AwithGameFrame.Foundation.Providers
{
    /// <summary>
    /// DOTween动画操作提供者
    /// 基于DOTween的高性能动画实现
    /// </summary>
    public class DOTweenProvider : IAnimationProvider
    {
        public async UniTask MoveTo(Transform target, Vector3 endValue, float duration)
        {
            if (target == null)
            {
                FrameworkLogger.Error("DOTweenProvider: Target transform is null for Move operation.", LogCategory.Core);
                return;
            }
            await target.DOMove(endValue, duration).AsyncWaitForCompletion().AsUniTask();
        }

        public async UniTask ScaleTo(Transform target, Vector3 endValue, float duration)
        {
            if (target == null)
            {
                FrameworkLogger.Error("DOTweenProvider: Target transform is null for Scale operation.", LogCategory.Core);
                return;
            }
            await target.DOScale(endValue, duration).AsyncWaitForCompletion().AsUniTask();
        }

        public async UniTask RotateTo(Transform target, Vector3 endValue, float duration)
        {
            if (target == null)
            {
                FrameworkLogger.Error("DOTweenProvider: Target transform is null for Rotate operation.", LogCategory.Core);
                return;
            }
            await target.DORotate(endValue, duration).AsyncWaitForCompletion().AsUniTask();
        }

        public async UniTask FadeTo(CanvasGroup target, float endValue, float duration)
        {
            if (target == null)
            {
                FrameworkLogger.Error("DOTweenProvider: Target CanvasGroup is null for Fade operation.", LogCategory.Core);
                return;
            }
            await target.DOFade(endValue, duration).AsyncWaitForCompletion().AsUniTask();
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
