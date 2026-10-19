using System;

namespace AwithGameFrame.Gameplay.Input
{
    /// <summary>Runs before gameplay publication. Return true to consume the action; callbacks run on the input thread.</summary>
    public interface IInputRouter
    {
        bool Route(InputActionEvent input);
    }

    public sealed class InputBindingSnapshot
    {
        public InputBindingSnapshot(string contextId, string action, string control, float scale = 1, float deadZone = 0.1f)
        {
            ContextId = contextId; Action = action; Control = control; Scale = scale; DeadZone = deadZone;
        }
        public string ContextId { get; }
        public string Action { get; }
        public string Control { get; }
        public float Scale { get; }
        public float DeadZone { get; }
    }
}
