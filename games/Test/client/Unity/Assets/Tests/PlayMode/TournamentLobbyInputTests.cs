using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using Test.Application.Protocol;
using Test.Client.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Test.Client.Tests
{
    public sealed class TournamentLobbyInputTests
    {
        [UnityTest]
        public IEnumerator KeyboardAndGamepadUseNativeLobbyControls()
        {
            var prefab = Resources.Load<GameObject>("Tournament/Lobby");
            Assert.That(prefab, Is.Not.Null);
            var lobby = Object.Instantiate(prefab).GetComponent<TournamentLobbyView>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var gamepad = InputSystem.AddDevice<Gamepad>();
            var background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            try
            {
                int ready = 0;
                lobby.Initialize("http://127.0.0.1:17840", "room", "Wayfarer", null, () =>
                {
                    ready++;
                    return Task.CompletedTask;
                }, null, null);
                lobby.Present("join", "Join a tournament", null, 0, true, false, false);
                yield return null;
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(lobby.Player.gameObject));
                keyboard.MakeCurrent();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Tab));
                InputSystem.Update();
                yield return null;
                yield return null;
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(lobby.Join.gameObject));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                var state = new PvpTournamentState { ConnectedMask = 3, RoundNumber = 1, Mode = "rain-pass" };
                lobby.Present("lobby", "Both players connected", state, 0, false, true, false);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(lobby.Ready.gameObject));
                gamepad.MakeCurrent();
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                yield return null;
                yield return null;
                Assert.That(ready, Is.EqualTo(1));
            }
            finally
            {
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(gamepad);
                Object.Destroy(lobby.gameObject);
            }
        }
    }
}
