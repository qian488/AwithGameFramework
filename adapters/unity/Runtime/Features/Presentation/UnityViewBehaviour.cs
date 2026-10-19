using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.Presentation;
using UnityEngine;

namespace AwithGameFrame.Unity.Presentation
{
    /// <summary>Optional prefab-root hooks. Queue navigation instead of awaiting service reentry inside these callbacks.</summary>
    public class UnityViewBehaviour : MonoBehaviour
    {
        public virtual Task ShowAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task HideAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual void OnPresentationChanged(ViewState state) { }
        public virtual bool HandleInput(ViewInput input) => false;
    }
}
