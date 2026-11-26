using System;
using AwithGameFrame.Gameplay.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal sealed class RainPassGamepad : IInputBackend
    {
        private readonly IInputBackend keyboard;

        internal RainPassGamepad(IInputBackend keyboard) { this.keyboard = keyboard; }

        internal string PauseControl { get; set; } = "keyboard/escape";
        internal static bool HasPad => Gamepad.current != null;
        internal static bool HasFocus => UnityEngine.Application.isFocused || UnityEngine.Application.isBatchMode && HasPad;
        public bool IsFocused => HasFocus;

        public float ReadControl(string control)
        {
            var pad = Gamepad.current;
            float keyboardValue = keyboard.ReadControl(control);
            if (pad == null) return keyboardValue;
            if (control == PauseControl) return Mathf.Max(keyboardValue, pad.startButton.ReadValue());
            float padValue = control switch
            {
                "keyboard/w" => pad.leftStick.up.ReadValue(),
                "keyboard/s" => pad.leftStick.down.ReadValue(),
                "keyboard/a" => pad.leftStick.left.ReadValue(),
                "keyboard/d" => pad.leftStick.right.ReadValue(),
                "mouse/left" => pad.rightShoulder.ReadValue(),
                "mouse/right" => pad.leftShoulder.ReadValue(),
                "mouse/middle" => pad.rightStickButton.ReadValue(),
                "keyboard/space" => pad.buttonWest.ReadValue(),
                "keyboard/1" => pad.dpad.left.ReadValue(),
                "keyboard/2" => pad.dpad.right.ReadValue(),
                _ => 0
            };
            return Mathf.Max(keyboardValue, padValue);
        }

        internal static Vector2 Look => Gamepad.current?.rightStick.ReadValue() ?? Vector2.zero;

        internal static void Navigate(MonoBehaviour menu, Action back)
        {
            var pad = Gamepad.current;
            var events = EventSystem.current;
            if (pad == null || events == null || !HasFocus) return;
            if (pad.buttonEast.wasPressedThisFrame && back != null)
            {
                back();
                return;
            }
            MoveDirection move = pad.dpad.up.wasPressedThisFrame ? MoveDirection.Up :
                pad.dpad.down.wasPressedThisFrame ? MoveDirection.Down :
                pad.dpad.left.wasPressedThisFrame ? MoveDirection.Left :
                pad.dpad.right.wasPressedThisFrame ? MoveDirection.Right : MoveDirection.None;
            bool submit = pad.buttonSouth.wasPressedThisFrame;
            if (move == MoveDirection.None && !submit) return;

            var selection = events.currentSelectedGameObject;
            if (selection == null || !selection.transform.IsChildOf(menu.transform) ||
                !selection.TryGetComponent<Selectable>(out var selectable) || !selectable.IsInteractable())
            {
                selection = null;
                foreach (var candidate in menu.GetComponentsInChildren<Selectable>())
                {
                    if (!candidate.IsInteractable()) continue;
                    selection = candidate.gameObject;
                    events.SetSelectedGameObject(selection);
                    break;
                }
            }
            if (selection == null) return;
            if (move != MoveDirection.None)
                ExecuteEvents.Execute(selection, new AxisEventData(events) { moveDir = move }, ExecuteEvents.moveHandler);
            else
                ExecuteEvents.Execute(selection, new BaseEventData(events), ExecuteEvents.submitHandler);
        }
    }
}
