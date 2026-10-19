using System;
using System.Collections.Generic;
using System.Threading;
using AwithGameFrame.Gameplay.Input;
using Godot;

namespace AwithGameFrame.Godot
{
    public sealed class GodotInputBackend : IInputBackend
    {
        private readonly Window _window;
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<string, Func<float>> _controls = new Dictionary<string, Func<float>>(StringComparer.Ordinal);

        public GodotInputBackend(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            MapKey("keyboard/space", Key.Space); MapKey("keyboard/enter", Key.Enter);
            MapKey("keyboard/escape", Key.Escape);
            MapKey("keyboard/w", Key.W); MapKey("keyboard/a", Key.A);
            MapKey("keyboard/s", Key.S); MapKey("keyboard/d", Key.D);
            MapMouse("mouse/left", MouseButton.Left); MapMouse("mouse/right", MouseButton.Right);
            MapJoyButton("joy/a", JoyButton.A);
            MapJoyAxis("joy/left-x", JoyAxis.LeftX); MapJoyAxis("joy/left-y", JoyAxis.LeftY);
        }

        public bool IsFocused { get { VerifyThread(); return GodotObject.IsInstanceValid(_window) && _window.HasFocus(); } }
        public void MapKey(string control, Key key, bool physical = true)
        { Map(control, () => (physical ? global::Godot.Input.IsPhysicalKeyPressed(key) : global::Godot.Input.IsKeyPressed(key)) ? 1 : 0); }
        public void MapMouse(string control, MouseButton button) { Map(control, () => global::Godot.Input.IsMouseButtonPressed(button) ? 1 : 0); }
        public void MapJoyButton(string control, JoyButton button, int device = 0)
        { if (device < 0) throw new ArgumentOutOfRangeException(nameof(device)); Map(control, () => global::Godot.Input.IsJoyButtonPressed(device, button) ? 1 : 0); }
        public void MapJoyAxis(string control, JoyAxis axis, int device = 0)
        { if (device < 0) throw new ArgumentOutOfRangeException(nameof(device)); Map(control, () => global::Godot.Input.GetJoyAxis(device, axis)); }
        public float ReadControl(string control)
        {
            VerifyThread();
            if (!_controls.TryGetValue(control, out var read)) throw new ArgumentException("Unmapped Godot control: " + control, nameof(control));
            return read();
        }
        private void Map(string control, Func<float> read)
        {
            VerifyThread();
            if (string.IsNullOrWhiteSpace(control)) throw new ArgumentException("Control name cannot be empty.", nameof(control));
            _controls[control] = read;
        }
        private void VerifyThread()
        { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Poll Godot input on its creating engine thread."); }
    }
}
