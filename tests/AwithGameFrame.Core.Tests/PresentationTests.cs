using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Extensions.Scenes;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class PresentationTests
    {
        private sealed class Loader : IResourceLoader
        {
            public Action<string> Released;
            public Task<LoadedResource> LoadAsync(string key, Type type, CancellationToken token) =>
                Task.FromResult(new LoadedResource(key, () => { Released?.Invoke(key); return Task.CompletedTask; }));
        }
        private sealed class View : IView
        {
            public ViewState State;
            public int Disposals, Hides;
            public Func<CancellationToken, Task> Show;
            public Func<Task> Hide;
            public Action Dispose;
            public Func<ViewInput, bool> Input;
            public Task ShowAsync(CancellationToken token) => Show?.Invoke(token) ?? Task.CompletedTask;
            public Task HideAsync(CancellationToken token) { Hides++; return Hide?.Invoke() ?? Task.CompletedTask; }
            public void ApplyState(ViewState state) => State = state;
            public bool HandleInput(ViewInput input) => Input?.Invoke(input) ?? false;
            public ValueTask DisposeAsync() { Disposals++; Dispose?.Invoke(); return default; }
        }
        private sealed class Factory : IViewFactory
        {
            public int Creations;
            public readonly Dictionary<string, View> Views = new Dictionary<string, View>();
            public Func<ViewRequest, ResourceScope, CancellationToken, Task<IView>> Create;
            public Task<IView> CreateAsync(ViewRequest request, ResourceScope scope, CancellationToken token)
            {
                Creations++;
                if (Create != null) return Create(request, scope, token);
                var view = new View(); Views[request.Key] = view;
                return Task.FromResult<IView>(view);
            }
        }
        private sealed class SceneBackend : ISceneBackend
        {
            private readonly List<string> events;
            public SceneBackend(List<string> events) => this.events = events;
            public async Task<ISceneInstance> LoadAsync(string key, ResourceScope scope, CancellationToken token)
            { await scope.LoadAsync<string>("shared", token); return new Scene(events); }
        }
        private sealed class Scene : ISceneInstance
        {
            private readonly List<string> events;
            public Scene(List<string> events) => this.events = events;
            public Task ActivateAsync(CancellationToken token) => Task.CompletedTask;
            public ValueTask DisposeAsync() { events.Add("scene"); return default; }
        }

        [Test]
        public async Task HostModulesRegisterServicesAndDestroyViewsAndScenesBeforeTheirSharedResource()
        {
            var events = new List<string>(); var host = new GameHost();
            var store = new ResourceStore(new Loader { Released = _ => events.Add("asset") });
            var views = new ViewService(host.Context, new Factory { Create = async (_, scope, token) =>
            {
                await scope.LoadAsync<string>("shared", token);
                return new View { Dispose = () => events.Add("view") };
            } }, store);
            var scenes = new SceneService(host.Context, new SceneBackend(events), store);
            host.AddModule(new PresentationModule(views));
            host.AddModule(new SceneModule(scenes));
            host.AddModule(new ResourceModule(store));
            await host.StartAsync();
            Assert.That(host.Services.Get<ViewService>(), Is.SameAs(views));
            Assert.That(host.Services.Get<SceneService>(), Is.SameAs(scenes));
            await views.OpenAsync(new ViewRequest("menu")); await scenes.LoadAsync("level");
            await host.StopAsync();
            Assert.That(events, Has.Count.EqualTo(3));
            Assert.That(events[2], Is.EqualTo("asset"));
            Assert.That(events, Does.Contain("view").And.Contain("scene"));
            Assert.That(views.OpenViews, Is.Empty); Assert.That(scenes.Loaded, Is.Empty);
        }

        [Test]
        public async Task LayersModalAndFocusControlNativeStateAndLogicalInput()
        {
            var host = new GameHost(); var store = new ResourceStore(new Loader());
            var factory = new Factory(); var service = new ViewService(host.Context, factory, store);
            var calls = new List<string>();
            var modal = await service.OpenAsync(new ViewRequest("modal", 10, modal: true));
            var background = await service.OpenAsync(new ViewRequest("background", 0));
            var overlay = await service.OpenAsync(new ViewRequest("overlay", 20, takesFocus: false));
            factory.Views["background"].Input = _ => { calls.Add("background"); return true; };
            factory.Views["modal"].Input = _ => { calls.Add("modal"); return false; };
            factory.Views["overlay"].Input = _ => { calls.Add("overlay"); return false; };
            Assert.That(service.Focused, Is.SameAs(modal));
            Assert.That(background.IsOpen && modal.IsFocused && !overlay.IsFocused, Is.True);
            Assert.That(factory.Views["background"].State.IsCovered, Is.True);
            Assert.That(factory.Views["background"].State.IsInputEnabled, Is.False);
            Assert.That(factory.Views["overlay"].State.Order, Is.EqualTo(2));
            Assert.That(service.OpenViews, Is.EqualTo(new[] { background, modal, overlay }));
            Assert.That(service.RouteInput(new ViewInput("confirm")), Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "overlay", "modal" }));
            await modal.CloseAsync();
            Assert.That(service.Focused, Is.SameAs(background));
            Assert.That(factory.Views["background"].State.IsInputEnabled, Is.True);
            Assert.That(modal.IsOpen || modal.IsFocused, Is.False);
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task ConcurrentSameKeyOpensCreateOneViewAndReturnSameHandle()
        {
            var host = new GameHost(); var store = new ResourceStore(new Loader());
            var ready = new TaskCompletionSource<IView>(TaskCreationOptions.RunContinuationsAsynchronously);
            var factory = new Factory { Create = (_, _, _) => ready.Task };
            var service = new ViewService(host.Context, factory, store);
            var a = service.OpenAsync(new ViewRequest("same"));
            var b = service.OpenAsync(new ViewRequest("same"));
            ready.SetResult(new View());
            Assert.That(await a, Is.SameAs(await b));
            Assert.That(factory.Creations, Is.EqualTo(1));
            Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenAsync(new ViewRequest("same", modal: true)));
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task CancelledLateViewIsHiddenDisposedAndNeverCommittedBeforeStopCompletes()
        {
            var host = new GameHost(); var store = new ResourceStore(new Loader());
            var pending = new TaskCompletionSource<IView>(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new ViewService(host.Context, new Factory { Create = (_, _, _) => pending.Task }, store);
            using var cancel = new CancellationTokenSource();
            var opening = service.OpenAsync(new ViewRequest("late"), cancel.Token);
            cancel.Cancel(); var stopping = service.StopAsync();
            Assert.That(stopping.IsCompleted, Is.False);
            var view = new View(); pending.SetResult(view);
            Assert.CatchAsync<OperationCanceledException>(async () => await opening.WaitAsync(TimeSpan.FromSeconds(3)));
            await stopping.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(view.Hides, Is.EqualTo(1)); Assert.That(view.Disposals, Is.EqualTo(1));
            Assert.That(service.OpenViews, Is.Empty);
            Assert.ThrowsAsync<ObjectDisposedException>(() => service.OpenAsync(new ViewRequest("closed")));
            await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task FailedShowAndFailedHideStillDisposeNativeViewBeforeOwnedAssets()
        {
            var events = new List<string>();
            var host = new GameHost(); var store = new ResourceStore(new Loader { Released = _ => events.Add("asset") });
            var factory = new Factory { Create = async (_, scope, token) =>
            {
                await scope.LoadAsync<string>("asset", token);
                return new View { Show = _ => throw new FormatException("show"), Hide = () => throw new Exception("hide"), Dispose = () => events.Add("view") };
            } };
            var service = new ViewService(host.Context, factory, store);
            var error = Assert.ThrowsAsync<AggregateException>(() => service.OpenAsync(new ViewRequest("bad")));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<FormatException>());
            Assert.That(events, Is.EqualTo(new[] { "view", "asset" }));
            Assert.That(service.OpenViews, Is.Empty);
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task NavigationOwnsViewsRecreatesReturnedPageAndRestoresFailedDestination()
        {
            var host = new GameHost(); var store = new ResourceStore(new Loader());
            var factory = new Factory { Create = (request, _, _) => Task.FromResult<IView>(new View { Show = _ => request.Key == "bad" ? Task.FromException(new FormatException()) : Task.CompletedTask }) };
            var service = new ViewService(host.Context, factory, store);
            var navigation = new NavigationStack(host.Context);
            var menu = new ViewFlow(service, new ViewRequest("menu"));
            var play = new ViewFlow(service, new ViewRequest("play"));
            await navigation.NavigateAsync(menu); var originalMenu = menu.Handle;
            await navigation.NavigateAsync(play);
            Assert.That(originalMenu.IsOpen, Is.False);
            Assert.That(await navigation.BackAsync(), Is.True);
            Assert.That(menu.Handle, Is.Not.SameAs(originalMenu));
            Assert.ThrowsAsync<FormatException>(() => navigation.NavigateAsync(new ViewFlow(service, new ViewRequest("bad"))));
            Assert.That(navigation.Current, Is.SameAs(menu));
            Assert.That(service.OpenViews.Count, Is.EqualTo(1));
            await navigation.StopAsync(); await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task StopAttemptsEveryViewAfterOneCloseFails()
        {
            var host = new GameHost(); var store = new ResourceStore(new Loader());
            var views = new List<View>();
            var factory = new Factory { Create = (request, _, _) => { var view = new View { Hide = () => request.Key == "bad" ? Task.FromException(new FormatException()) : Task.CompletedTask }; views.Add(view); return Task.FromResult<IView>(view); } };
            var service = new ViewService(host.Context, factory, store);
            await service.OpenAsync(new ViewRequest("good")); await service.OpenAsync(new ViewRequest("bad"));
            var stop = service.StopAsync(); Assert.ThrowsAsync<AggregateException>(() => stop);
            Assert.That(views.TrueForAll(view => view.Disposals == 1), Is.True);
            Assert.That(service.OpenViews, Is.Empty); Assert.That(service.StopAsync(), Is.SameAs(stop));
            await store.DisposeAsync(); await host.StopAsync();
        }

        [Test]
        public async Task ViewCallbackCannotDeadlockByAwaitingServiceStop()
        {
            var host = new GameHost(); var store = new ResourceStore(new Loader());
            ViewService service = null;
            service = new ViewService(host.Context, new Factory { Create = (_, _, _) => Task.FromResult<IView>(new View { Show = _ => service.StopAsync() }) }, store);
            Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenAsync(new ViewRequest("recursive")));
            Assert.That(service.OpenViews, Is.Empty);
            await service.StopAsync(); await store.DisposeAsync(); await host.StopAsync();
        }
    }
}
