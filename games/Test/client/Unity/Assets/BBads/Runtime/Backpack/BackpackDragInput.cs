using System;
using System.Collections.Generic;
using AwithGameFrame.Core.Events;
using AwithGameFrame.Gameplay.Input;

namespace BBads.Backpack
{
    internal sealed class BackpackDragInput : IDisposable
    {
        const string Backpack = "bbads.backpack";
        const string Drag = "drag";
        const string PointerDrag = "ui/pointer-drag";

        static readonly IReadOnlyDictionary<string, float> dragFrame =
            new Dictionary<string, float> { [PointerDrag] = 1f };
        static readonly IReadOnlyDictionary<string, float> releasedFrame =
            new Dictionary<string, float>();

        readonly EventBus events = new EventBus();
        readonly InputContext pause;

        public BackpackDragInput()
        {
            Service = new InputService(events);
            Service.CreateContext(Backpack).Bind(Drag, PointerDrag);
            pause = Service.CreateContext("bbads.pause", priority: 10, modal: true);
            pause.SetEnabled(false);
        }

        public InputService Service { get; }

        public void SetPaused(bool paused) => pause.SetEnabled(paused);

        // Unity supplies the gesture edge and owns the pointer. The context decides whether it reaches gameplay.
        public bool AllowsDrag()
        {
            var frame = Service.ProcessFrame(dragFrame);
            frame.ThrowIfFailed();
            foreach (var action in frame.Events)
            {
                if (action.ContextId == Backpack && action.Action == Drag)
                    return action.Phase == InputActionPhase.Pressed || action.Phase == InputActionPhase.Held;
            }

            return false;
        }

        public void Release() => Service.ProcessFrame(releasedFrame).ThrowIfFailed();

        public void Dispose()
        {
            try { Service.Dispose(); }
            finally { events.Dispose(); }
        }
    }
}

