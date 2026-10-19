using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwithGameFrame.Core.Events;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Gameplay.Input;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class InputServiceTests
    {
        private sealed class Backend : IInputBackend
        {
            public bool IsFocused { get; set; } = true;
            public float Value;
            public int Reads;
            public float ReadControl(string control) { Reads++; return Value; }
        }

        private static Dictionary<string, float> Frame(string control, float value)
            => new Dictionary<string, float> { [control] = value };

        [Test]
        public void FramesGeneratePressHoldReleaseAndScaledAxes()
        {
            using var events = new EventBus();
            using var input = new InputService(events);
            using var context = input.CreateContext("game");
            context.Bind("move", "axis", scale: -1, deadZone: 0.2f);
            Assert.That(input.ProcessFrame(Frame("axis", 0.1f)).Events, Is.Empty);
            var press = input.ProcessFrame(Frame("axis", 0.5f)).Events[0];
            Assert.That(press.Action, Is.EqualTo("move"));
            Assert.That(press.ContextId, Is.EqualTo("game"));
            Assert.That(press.Phase, Is.EqualTo(InputActionPhase.Pressed));
            Assert.That(press.Value, Is.EqualTo(-0.5f));
            Assert.That(input.ProcessFrame(Frame("axis", 0.7f)).Events[0].Phase, Is.EqualTo(InputActionPhase.Held));
            var release = input.ProcessFrame(Frame("axis", 0)).Events[0];
            Assert.That(release.Phase, Is.EqualTo(InputActionPhase.Released));
            Assert.That(release.Value, Is.Zero);
            Assert.That(input.ProcessFrame(Frame("axis", 0)).Events, Is.Empty);
        }

        [Test]
        public void HigherPriorityConsumesSharedControlsAndModalBlocksAllLowerActions()
        {
            using var events = new EventBus();
            using var input = new InputService(events);
            using var game = input.CreateContext("game");
            game.Bind("jump", "space"); game.Bind("fire", "mouse");
            var both = new Dictionary<string, float> { ["space"] = 1, ["mouse"] = 1 };
            Assert.That(input.ProcessFrame(both).Events.Count, Is.EqualTo(2));
            using var ui = input.CreateContext("ui", 10);
            ui.Bind("confirm", "space");
            var routed = input.ProcessFrame(both);
            Assert.That(routed.Events.Count, Is.EqualTo(3));
            Assert.That(routed.Events[0].Action, Is.EqualTo("confirm"));
            Assert.That(routed.Events[1].Phase, Is.EqualTo(InputActionPhase.Released));
            using var modal = input.CreateContext("modal", 20, true);
            var blocked = input.ProcessFrame(both);
            Assert.That(blocked.Events.Count, Is.EqualTo(2));
            foreach (var release in blocked.Events) Assert.That(release.Phase, Is.EqualTo(InputActionPhase.Released));
            modal.Dispose();
            Assert.That(input.ProcessFrame(both).Events[0].Phase, Is.EqualTo(InputActionPhase.Pressed));
        }

        [Test]
        public void EqualPriorityUsesMostRecentlyCreatedContextFirst()
        {
            using var events = new EventBus();
            using var input = new InputService(events);
            using var first = input.CreateContext("first"); first.Bind("accept", "enter");
            using var second = input.CreateContext("second"); second.Bind("accept", "enter");
            var result = input.ProcessFrame(Frame("enter", 1));
            Assert.That(result.Events.Count, Is.EqualTo(1));
            Assert.That(result.Events[0].ContextId, Is.EqualTo("second"));
        }

        [Test]
        public void RebindingRejectsConflictsOrExplicitlyReplacesAndReleasesOldActions()
        {
            using var events = new EventBus();
            using var input = new InputService(events);
            using var context = input.CreateContext("game");
            context.Bind("jump", "space"); context.Bind("confirm", "enter");
            var releases = new List<string>();
            events.Subscribe<InputActionEvent>(message =>
            { if (message.Phase == InputActionPhase.Released) releases.Add(message.Action); });
            input.ProcessFrame(new Dictionary<string, float> { ["space"] = 1, ["enter"] = 1 });
            Assert.Throws<InvalidOperationException>(() => context.Rebind("jump", "enter"));
            Assert.That(releases, Is.Empty);
            context.Rebind("jump", "enter", BindingConflictPolicy.ReplaceExisting);
            Assert.That(releases, Is.EqualTo(new[] { "confirm", "jump" }));
            var next = input.ProcessFrame(Frame("enter", 1));
            Assert.That(next.Events.Count, Is.EqualTo(1));
            Assert.That(next.Events[0].Action, Is.EqualTo("jump"));
            Assert.That(next.Events[0].Phase, Is.EqualTo(InputActionPhase.Pressed));
        }

        [Test]
        public void DisableFocusLossAndScopeDisposalReleaseWithoutStickyInput()
        {
            using var events = new EventBus();
            using var input = new InputService(events);
            using var context = input.CreateContext("game"); context.Bind("jump", "space");
            var backend = new Backend { Value = 1 };
            input.Poll(backend);
            backend.IsFocused = false;
            Assert.That(input.Poll(backend).Events[0].Phase, Is.EqualTo(InputActionPhase.Released));
            Assert.That(backend.Reads, Is.EqualTo(1));
            backend.IsFocused = true;
            Assert.That(input.Poll(backend).Events[0].Phase, Is.EqualTo(InputActionPhase.Pressed));
            Assert.That(input.SetEnabled(false).Events[0].Phase, Is.EqualTo(InputActionPhase.Released));
            Assert.That(input.Poll(backend).Events, Is.Empty);
            input.SetEnabled(true);
            input.Poll(backend);
            int releases = 0;
            events.Subscribe<InputActionEvent>(message => { if (message.Phase == InputActionPhase.Released) releases++; });
            context.Dispose(); context.Dispose();
            Assert.That(releases, Is.EqualTo(1));
            Assert.That(input.Poll(backend).Events, Is.Empty);
        }

        [Test]
        public void ListenerFailuresAndMutationReentryAreObservableWithoutLosingOtherEvents()
        {
            using var events = new EventBus();
            using var input = new InputService(events);
            using var context = input.CreateContext("game"); context.Bind("jump", "space");
            int delivered = 0;
            events.Subscribe<InputActionEvent>(_ => context.SetEnabled(false));
            events.Subscribe<InputActionEvent>(_ => delivered++);
            var result = input.ProcessFrame(Frame("space", 1));
            Assert.That(result.Errors.Count, Is.EqualTo(1));
            Assert.That(result.Errors[0], Is.TypeOf<InvalidOperationException>());
            Assert.That(delivered, Is.EqualTo(1));
            Assert.Throws<AggregateException>(result.ThrowIfFailed);
            events.Dispose(); // Avoid triggering the deliberately failing listener during input cleanup.
            Assert.Throws<AggregateException>(input.Dispose);
        }

        [Test]
        public void InvalidFrameDoesNotChangePreviouslyHeldStateAndCrossThreadUseIsRejected()
        {
            using var events = new EventBus();
            using var input = new InputService(events);
            using var context = input.CreateContext("game"); context.Bind("jump", "space");
            input.ProcessFrame(Frame("space", 1));
            Assert.Throws<ArgumentException>(() => input.ProcessFrame(Frame("space", float.NaN)));
            Assert.That(input.ProcessFrame(Frame("space", 1)).Events[0].Phase, Is.EqualTo(InputActionPhase.Held));
            Exception failure = Task.Run(() =>
            {
                try { input.ProcessFrame(Frame("space", 0)); return null; }
                catch (Exception error) { return error; }
            }).GetAwaiter().GetResult();
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void ModulePollsRegistersAndRestoresEnabledStateAcrossHostPause()
        {
            var host = new GameHost();
            var input = new InputService(host.Events);
            var backend = new Backend { Value = 1 };
            using var context = input.CreateContext("game"); context.Bind("jump", "space");
            host.AddModule(new InputModule(input, backend));
            host.StartAsync().GetAwaiter().GetResult();
            Assert.That(host.Services.Get<InputService>(), Is.SameAs(input));
            host.Tick(0.016);
            Assert.That(backend.Reads, Is.EqualTo(1));
            host.PauseAsync().GetAwaiter().GetResult();
            Assert.That(input.IsEnabled, Is.False);
            host.ResumeAsync().GetAwaiter().GetResult();
            Assert.That(input.IsEnabled, Is.True);
            input.SetEnabled(false);
            host.PauseAsync().GetAwaiter().GetResult();
            host.ResumeAsync().GetAwaiter().GetResult();
            Assert.That(input.IsEnabled, Is.False);
            host.StopAsync().GetAwaiter().GetResult();
            Assert.Throws<ObjectDisposedException>(() => input.CreateContext("closed"));
        }
    }
}
