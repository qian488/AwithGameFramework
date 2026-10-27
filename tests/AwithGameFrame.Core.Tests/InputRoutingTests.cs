using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Events;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Gameplay.Input;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class InputRoutingTests
    {
        private sealed class Loader : IResourceLoader
        {
            public Task<LoadedResource> LoadAsync(string key, Type type, CancellationToken token)
                => throw new InvalidOperationException("This view owns no resource.");
        }
        private sealed class View : IView
        {
            public Func<ViewInput, bool> Input;
            public Task ShowAsync(CancellationToken token) => Task.CompletedTask;
            public Task HideAsync(CancellationToken token) => Task.CompletedTask;
            public void ApplyState(ViewState state) { }
            public bool HandleInput(ViewInput input) => Input?.Invoke(input) ?? false;
            public ValueTask DisposeAsync() => default;
        }
        private sealed class Factory : IViewFactory
        {
            public readonly Dictionary<string, View> Views = new Dictionary<string, View>();
            public Task<IView> CreateAsync(ViewRequest request, ResourceScope scope, CancellationToken token)
            {
                var view = new View(); Views[request.Key] = view;
                return Task.FromResult<IView>(view);
            }
        }
        private sealed class Router : IInputRouter
        {
            public Func<InputActionEvent, bool> RouteAction;
            public bool Route(InputActionEvent input) => RouteAction(input);
        }
        private static Dictionary<string, float> Frame(float value) => new Dictionary<string, float> { ["space"] = value };

        [Test]
        public void ModalConsumesBeforeGameplayAndHeldKeyStaysReleasedAfterModalCloses()
        {
            var host = new GameHost(); var resources = new ResourceStore(new Loader());
            var factory = new Factory();
            var views = new ViewService(host.Context, factory, resources);
            using var input = new InputService(host.Events, new ViewInputRouter(views));
            using var game = input.CreateContext("game"); game.Bind("jump", "space");
            var gameplay = new List<InputActionPhase>();
            using var subscription = host.Events.Subscribe<InputActionEvent>(message => gameplay.Add(message.Phase));
            input.ProcessFrame(Frame(1));
            var modal = views.OpenAsync(new ViewRequest("modal", modal: true)).GetAwaiter().GetResult();
            InputActionEvent routed = null;
            factory.Views["modal"].Input = message => { routed = (InputActionEvent)message.Payload; return false; };
            var consumed = input.ProcessFrame(Frame(1));
            Assert.That(routed.Phase, Is.EqualTo(InputActionPhase.Held));
            Assert.That(routed.ContextId, Is.EqualTo("game"));
            Assert.That(consumed.Events.Count, Is.EqualTo(1));
            Assert.That(consumed.Events[0].Phase, Is.EqualTo(InputActionPhase.Released));
            Assert.That(input.ProcessFrame(Frame(1)).Events, Is.Empty);
            modal.CloseAsync().GetAwaiter().GetResult();
            Assert.That(input.ProcessFrame(Frame(1)).Events, Is.Empty);
            Assert.That(input.ProcessFrame(Frame(0)).Events, Is.Empty);
            input.ProcessFrame(Frame(1)); input.ProcessFrame(Frame(0));
            Assert.That(gameplay, Is.EqualTo(new[] { InputActionPhase.Pressed, InputActionPhase.Released, InputActionPhase.Pressed, InputActionPhase.Released }));
            modal = views.OpenAsync(new ViewRequest("modal", modal: true)).GetAwaiter().GetResult();
            Assert.That(input.ProcessFrame(Frame(1)).Events, Is.Empty);
            modal.CloseAsync().GetAwaiter().GetResult();
            Assert.That(input.ProcessFrame(Frame(1)).Events, Is.Empty);
            input.ProcessFrame(Frame(0));
            Assert.That(input.ProcessFrame(Frame(1)).Events[0].Phase, Is.EqualTo(InputActionPhase.Pressed));
            input.ProcessFrame(Frame(0));
            views.StopAsync().GetAwaiter().GetResult(); resources.DisposeAsync().AsTask().GetAwaiter().GetResult();
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void RouterFailureConsumesActionAndRejectsInputMutationReentry()
        {
            using var events = new EventBus(); var router = new Router();
            using var input = new InputService(events, router);
            using var game = input.CreateContext("game"); game.Bind("jump", "space");
            router.RouteAction = _ => { input.SetRouter(null); return false; };
            var failed = input.ProcessFrame(Frame(1));
            Assert.That(failed.Events, Is.Empty);
            Assert.That(failed.Errors.Count, Is.EqualTo(1));
            Assert.That(failed.Errors[0], Is.TypeOf<InvalidOperationException>());
            input.SetRouter(null);
            Assert.That(input.ProcessFrame(Frame(1)).Events, Is.Empty);
            input.ProcessFrame(Frame(0));
            Assert.That(input.ProcessFrame(Frame(1)).Events[0].Phase, Is.EqualTo(InputActionPhase.Pressed));
        }

        [Test]
        public void ViewRoutingRejectsLifetimeMutationAndWorkerThread()
        {
            var host = new GameHost(); var resources = new ResourceStore(new Loader());
            var factory = new Factory();
            var views = new ViewService(host.Context, factory, resources);
            var router = new ViewInputRouter(views);
            using var input = new InputService(host.Events, router);
            using var game = input.CreateContext("game"); game.Bind("jump", "space");
            var message = input.ProcessFrame(Frame(1)).Events[0];
            var workerFailure = Task.Run(() =>
            {
                try { router.Route(message); return null; } catch (Exception error) { return error; }
            }).GetAwaiter().GetResult();
            Assert.That(workerFailure, Is.TypeOf<InvalidOperationException>());
            var modal = views.OpenAsync(new ViewRequest("modal", modal: true)).GetAwaiter().GetResult();
            factory.Views["modal"].Input = _ => { modal.CloseAsync().GetAwaiter().GetResult(); return false; };
            var result = input.ProcessFrame(Frame(1));
            Assert.That(result.Errors[0], Is.TypeOf<InvalidOperationException>());
            Assert.That(views.OpenViews.Count, Is.EqualTo(1));
            factory.Views["modal"].Input = null;
            input.ProcessFrame(Frame(0));
            views.StopAsync().GetAwaiter().GetResult(); resources.DisposeAsync().AsTask().GetAwaiter().GetResult();
            host.StopAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void CompleteBindingSnapshotSwapsControlsAndReleasesPreviouslyDeliveredActions()
        {
            using var events = new EventBus(); using var input = new InputService(events);
            using var game = input.CreateContext("game", priority: 7);
            game.Bind("jump", "space"); game.Bind("confirm", "enter");
            var original = input.CaptureBindings();
            input.ProcessFrame(Frame(1));
            var releases = new List<string>();
            using var subscription = events.Subscribe<InputActionEvent>(message =>
            { if (message.Phase == InputActionPhase.Released) releases.Add(message.Action); });
            input.ApplyBindings(new[] { new InputBindingSnapshot("game", "jump", "enter"), new InputBindingSnapshot("game", "confirm", "space", -1, 0.2f) });
            Assert.That(releases, Is.EqualTo(new[] { "jump" }));
            Assert.That(original[0].Control, Is.EqualTo("space"));
            var next = input.ProcessFrame(Frame(1)).Events[0];
            Assert.That(next.Action, Is.EqualTo("confirm"));
            Assert.That(next.Value, Is.EqualTo(-1));
            Assert.That(game.Priority, Is.EqualTo(7));
        }

        [Test]
        public void InvalidBindingSnapshotsLeaveAllBindingsAndPressedStateUntouched()
        {
            using var events = new EventBus(); using var input = new InputService(events);
            using var game = input.CreateContext("game"); game.Bind("jump", "space"); game.Bind("confirm", "enter");
            input.ProcessFrame(Frame(1));
            var invalid = new IReadOnlyList<InputBindingSnapshot>[]
            {
                new[] { new InputBindingSnapshot("game", "jump", "other") },
                new[] { new InputBindingSnapshot("game", "jump", "other"), new InputBindingSnapshot("unknown", "confirm", "enter") },
                new[] { new InputBindingSnapshot("game", "jump", "other"), new InputBindingSnapshot("game", "jump", "enter") },
                new[] { new InputBindingSnapshot("game", "jump", "same"), new InputBindingSnapshot("game", "confirm", "same") },
                new[] { new InputBindingSnapshot("game", "jump", "other"), new InputBindingSnapshot("game", "confirm", "enter", deadZone: float.NaN) },
                new InputBindingSnapshot[] { new InputBindingSnapshot("game", "jump", "other"), null }
            };
            foreach (var snapshot in invalid)
            {
                Assert.Catch<ArgumentException>(() => input.ApplyBindings(snapshot));
                Assert.That(input.CaptureBindings()[0].Control, Is.EqualTo("space"));
                Assert.That(input.ProcessFrame(Frame(1)).Events[0].Phase, Is.EqualTo(InputActionPhase.Held));
            }
        }

        [Test]
        public void BindingReleaseListenerFailureReportsAfterNewBindingsHaveCommitted()
        {
            using var events = new EventBus(); using var input = new InputService(events);
            using var game = input.CreateContext("game"); game.Bind("jump", "space");
            input.ProcessFrame(Frame(1));
            using var subscription = events.Subscribe<InputActionEvent>(_ => throw new FormatException("listener"));
            Assert.Throws<AggregateException>(() => input.ApplyBindings(new[] { new InputBindingSnapshot("game", "jump", "enter") }));
            Assert.That(input.CaptureBindings()[0].Control, Is.EqualTo("enter"));
            subscription.Dispose();
            Assert.That(input.ProcessFrame(new Dictionary<string, float> { ["enter"] = 1 }).Events[0].Phase, Is.EqualTo(InputActionPhase.Pressed));
        }
    }
}
