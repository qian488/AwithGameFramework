using System;
using AwithGameFrame.Gameplay.Input;

namespace AwithGameFrame.Extensions.Presentation
{
    /// <summary>Routes actions through views before InputService publishes unconsumed gameplay events.</summary>
    public sealed class ViewInputRouter : IInputRouter
    {
        private readonly ViewService views;
        private readonly int thread = Environment.CurrentManagedThreadId;
        public ViewInputRouter(ViewService views) => this.views = views ?? throw new ArgumentNullException(nameof(views));
        public bool Route(InputActionEvent input)
        {
            if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("View input routing requires the creating host thread.");
            if (input == null) throw new ArgumentNullException(nameof(input));
            return views.RouteInput(new ViewInput(input.Action, input));
        }
    }
}
