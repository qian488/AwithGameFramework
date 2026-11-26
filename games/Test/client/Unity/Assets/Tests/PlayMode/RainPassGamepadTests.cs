using System.Collections;
using AwithGameFrame.Gameplay.Input;
using NUnit.Framework;
using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using InputActionPhase = AwithGameFrame.Gameplay.Input.InputActionPhase;

namespace Test.Client.Tests
{
    public sealed partial class RainPassContractTests
    {
        [UnityTest]
        public IEnumerator GamepadCompletesAndReturnsFromTheCommission()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            var keyboard = new KeyboardInput();
            var backend = new Combat.RainPassGamepad(keyboard);
            int previousFrameRate = UnityEngine.Application.targetFrameRate;
            try
            {
                UnityEngine.Application.targetFrameRate = CombatRun.StepsPerSecond;
                yield return OpenCamp();
                yield return PadFrame(pad, new GamepadState(GamepadButton.South));
                yield return PadFrame(pad, new GamepadState());
                yield return Await(() => camp.Phase == ContractPhase.Battle, "gamepad accepted the contract");
                var battle = camp.ActiveShowcase;
                var run = battle.Run;
                float originalYaw = battle.CameraYaw;
                yield return PadFrame(pad, new GamepadState { rightStick = Vector2.right });
                Assert.That(battle.CameraYaw, Is.GreaterThan(originalYaw));

                var origin = battle.Space.Position(0);
                yield return PadFrame(pad, new GamepadState { leftStick = Vector2.up });
                yield return Await(() => battle.Space.Position(0) != origin, "left stick moves the player during simulation");
                Assert.That(battle.ForwardHeld, Is.True);
                Assert.That(battle.Space.Position(0), Is.Not.EqualTo(origin));

                var events = new System.Collections.Generic.List<InputActionPhase>();
                using var attackEvents = battle.Host.Events.Subscribe<InputActionEvent>(action =>
                {
                    if (action.Action == "attack") events.Add(action.Phase);
                });
                yield return PadFrame(pad, new GamepadState(GamepadButton.RightShoulder));
                yield return PadFrame(pad, new GamepadState(GamepadButton.RightShoulder, GamepadButton.Start));
                yield return PadFrame(pad, new GamepadState(GamepadButton.RightShoulder));
                Assert.That(run.Status, Is.EqualTo(RunStatus.Paused));
                Assert.That(events, Does.Contain(InputActionPhase.Released));
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(FindButton("Retreat to camp").gameObject));
                yield return PadFrame(pad, new GamepadState(GamepadButton.RightShoulder, GamepadButton.DpadUp));
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(FindButton("Settings").gameObject));
                yield return PadFrame(pad, new GamepadState(GamepadButton.RightShoulder, GamepadButton.East));
                Assert.That(run.Status, Is.Not.EqualTo(RunStatus.Paused));
                events.Clear();
                yield return PadFrame(pad, new GamepadState(GamepadButton.RightShoulder));
                Assert.That(events, Is.Empty, "Held attack must stay suppressed after resume.");
                yield return PadFrame(pad, new GamepadState());
                yield return PadFrame(pad, new GamepadState(GamepadButton.RightShoulder));
                Assert.That(events, Does.Contain(InputActionPhase.Pressed));
                yield return PadFrame(pad, new GamepadState { leftStick = Vector2.up });
                Assert.That(backend.ReadControl("keyboard/w"), Is.GreaterThan(0));
                InputSystem.RemoveDevice(pad);
                yield return null;
                yield return null;
                Assert.That(battle.ForwardHeld, Is.False, "Unplugging must release movement.");
                Assert.That(run.Status, Is.EqualTo(RunStatus.Fighting), "Removing the pad must not pause the commission.");
                keyboard.Forward = 1;
                Assert.That(backend.ReadControl("keyboard/w"), Is.EqualTo(1), "Keyboard remains available.");
                pad = InputSystem.AddDevice<Gamepad>();

                int deflects = 0, finishes = 0;
                run.Cue += cue =>
                {
                    if (cue.Result == ContactResult.Deflect) deflects++;
                    if (cue.Result == ContactResult.Finish) finishes++;
                };
                yield return FightWithGamepad(pad, battle);
                Assert.That(deflects, Is.GreaterThan(0));
                Assert.That(finishes, Is.GreaterThan(0));
                yield return Await(() => HasButton("Return to camp"), "saved settlement");
                yield return PadFrame(pad, new GamepadState(GamepadButton.South));
                yield return PadFrame(pad, new GamepadState());
                yield return Await(() => camp.Phase == ContractPhase.Camp, "gamepad returned to camp");
                Assert.That(camp.Completed, Is.True);
                AssertCampReleased();
            }
            finally
            {
                UnityEngine.Application.targetFrameRate = previousFrameRate;
                if (pad.added) InputSystem.RemoveDevice(pad);
            }
        }

        private static IEnumerator FightWithGamepad(Gamepad pad, Combat.CombatShowcase battle)
        {
            var run = battle.Run;
            bool guard = false;
            bool locked = false;
            int lastOwner = 0, lastSegment = -1;
            for (int frame = 0; frame < 2700 && (run.Status == RunStatus.Fighting || run.Status == RunStatus.Proceed); frame++)
            {
                int actor = run.AttackOwner;
                var position = battle.Space.Position(0);
                if (actor == 0)
                {
                    float closest = float.PositiveInfinity;
                    for (int index = 1; index < run.Count; index++)
                    {
                        if (!run.View(index).Vitality.Alive) continue;
                        float distance = System.Numerics.Vector3.DistanceSquared(battle.Space.Position(index), position);
                        if (distance >= closest) continue;
                        closest = distance;
                        actor = index;
                    }
                }
                var state = new GamepadState();
                Vector3 direction = Vector3.forward;
                if (run.Status == RunStatus.Fighting && actor > 0)
                {
                    var target = battle.Space.Position(actor) - position;
                    direction = new Vector3(target.X, 0, target.Z);
                    var opponent = run.View(actor);
                    if (run.AttackOwner == 0 || lastOwner != run.AttackOwner ||
                        lastSegment != opponent.Segment || opponent.Phase == AttackPhase.Ready) guard = false;
                    lastOwner = run.AttackOwner;
                    lastSegment = opponent.Segment;
                    if (run.AttackOwner == actor && opponent.Phase == AttackPhase.Windup && opponent.PhaseProgress >= .86f)
                        guard = true;
                    if (!locked && direction.sqrMagnitude < 36)
                        state = state.WithButton(GamepadButton.RightStick);
                    locked |= run.Locked > 0;
                    if (guard) state = state.WithButton(GamepadButton.LeftShoulder);
                    if (run.CanFinish || run.IsHoldingFinish) state = state.WithButton(GamepadButton.RightShoulder);
                    if (direction.magnitude < 1.83f || run.IsHoldingFinish) direction = Vector3.zero;
                }
                Vector3 relative = Quaternion.Euler(0, -battle.CameraYaw, 0) * direction.normalized;
                state.leftStick = new Vector2(relative.x, relative.z);
                yield return PadFrame(pad, state);
            }
            Assert.That(run.Status, Is.EqualTo(RunStatus.Victory), $"tick {run.Tick}, encounter {run.Encounter}, player health {run.View(0).Vitality.Health}");
            Assert.That(locked, Is.True, "Right stick clicked to lock an opponent.");
            yield return PadFrame(pad, new GamepadState());
        }

        private static IEnumerator PadFrame(Gamepad pad, GamepadState state)
        {
            InputSystem.QueueStateEvent(pad, state);
            yield return null;
        }

        private sealed class KeyboardInput : IInputBackend
        {
            internal float Forward;
            public bool IsFocused => true;
            public float ReadControl(string control) => control == "keyboard/w" ? Forward : 0;
        }
    }
}
