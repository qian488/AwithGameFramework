using System;
using System.Collections.Generic;
using System.Threading;
using AwithGameFrame.Gameplay.Input;
using UnityEngine;

namespace AwithGameFrame.Unity.Input
{
    /// <summary>Explicit mappings over Unity's built-in key/mouse/axis APIs; poll on the creating engine thread.</summary>
    public sealed class UnityInputBackend : IInputBackend
    {
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<string, Func<float>> _controls = new Dictionary<string, Func<float>>(StringComparer.Ordinal);

        public UnityInputBackend()
        {
            MapKey("keyboard/space", KeyCode.Space);
            MapKey("keyboard/enter", KeyCode.Return);
            MapKey("keyboard/escape", KeyCode.Escape);
            MapKey("keyboard/w", KeyCode.W); MapKey("keyboard/a", KeyCode.A);
            MapKey("keyboard/s", KeyCode.S); MapKey("keyboard/d", KeyCode.D);
            MapMouse("mouse/left", 0); MapMouse("mouse/right", 1);
        }

        public bool IsFocused { get { VerifyThread(); return Application.isFocused; } }

        public void MapKey(string control, KeyCode key) { Map(control, () => UnityEngine.Input.GetKey(key) ? 1 : 0); }
        public void MapMouse(string control, int button)
        {
            if (button < 0 || button > 6) throw new ArgumentOutOfRangeException(nameof(button));
            Map(control, () => UnityEngine.Input.GetMouseButton(button) ? 1 : 0);
        }
        public void MapAxis(string control, string unityAxis)
        {
            if (string.IsNullOrWhiteSpace(unityAxis)) throw new ArgumentException("Unity axis name cannot be empty.", nameof(unityAxis));
            Map(control, () => UnityEngine.Input.GetAxisRaw(unityAxis));
        }

        public float ReadControl(string control)
        {
            VerifyThread();
            if (!_controls.TryGetValue(control, out var read)) throw new ArgumentException("Unmapped Unity control: " + control, nameof(control));
            return read();
        }

        private void Map(string control, Func<float> read)
        {
            VerifyThread();
            if (string.IsNullOrWhiteSpace(control)) throw new ArgumentException("Control name cannot be empty.", nameof(control));
            _controls[control] = read;
        }
        private void VerifyThread()
        { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Poll Unity input on its creating engine thread."); }
    }
}
