using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.Presentation;
using Godot;

namespace AwithGameFrame.Godot
{
    /// <summary>Optional PackedScene root hooks for application-specific animations and logical actions.</summary>
    public partial class GodotView : Control
    {
        public virtual Task ShowAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task HideAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual void OnPresentationChanged(ViewState state) { }
        public virtual bool HandleInput(ViewInput input) => false;
    }
}
