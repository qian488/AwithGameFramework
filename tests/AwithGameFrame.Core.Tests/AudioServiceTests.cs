using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Gameplay.Audio;
using NUnit.Framework;

namespace AwithGameFrame.Core.Tests
{
    [TestFixture]
    public class AudioServiceTests
    {
        private sealed class Asset { }
        private sealed class Loader : IResourceLoader
        {
            public int Releases, Loads;
            public Func<Task> Release;
            public Task<LoadedResource> LoadAsync(string key, Type resourceType, CancellationToken cancellationToken)
            {
                Loads++;
                return Task.FromResult(new LoadedResource(new Asset(), () => { Releases++; return Release == null ? Task.CompletedTask : Release(); }));
            }
        }
        private class Voice : IAudioVoice
        {
            public float Volume;
            public bool Playing, Paused;
            public int Pauses, Resumes, Stops, Disposals;
            public Action StopAction, DisposeAction;
            public Func<bool> ReadPlaying;
            public bool IsPlaying => ReadPlaying == null ? Playing && !Paused : ReadPlaying();
            public void SetVolume(float volume) { Volume = volume; }
            public void Play() { Playing = true; }
            public void Pause() { Paused = true; Pauses++; }
            public void Resume() { Paused = false; Resumes++; }
            public void Stop() { Playing = false; Stops++; StopAction?.Invoke(); }
            public void Dispose() { Disposals++; DisposeAction?.Invoke(); }
        }
        private sealed class AsyncVoice : Voice, IAsyncDisposable
        {
            public readonly TaskCompletionSource<bool> NativeReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public int AsyncDisposals;
            public async ValueTask DisposeAsync()
            {
                AsyncDisposals++;
                await NativeReleased.Task;
                Dispose();
            }
        }
        private sealed class Backend : IAudioBackend
        {
            public readonly List<Voice> Voices = new List<Voice>();
            public Func<string, ResourceScope, CancellationToken, Task<IAudioVoice>> Create;
            public Task<IAudioVoice> CreateAsync(string key, ResourceScope scope, bool loop, CancellationToken cancellationToken)
                => Create == null ? DefaultCreateAsync(key, scope, cancellationToken) : Create(key, scope, cancellationToken);
            private async Task<IAudioVoice> DefaultCreateAsync(string key, ResourceScope scope, CancellationToken token)
            {
                await scope.LoadAsync<Asset>(key, token);
                var voice = new Voice(); Voices.Add(voice); return voice;
            }
        }

        [Test]
        public void MasterGroupTrackVolumesMultiplyAndMuteRestoresOriginalLevels() => Run(async () =>
        {
            var loader = new Loader();
            await using var resources = new ResourceStore(loader);
            var backend = new Backend();
            await using var audio = new AudioService(resources, backend);
            audio.SetMasterVolume(0.5f); audio.SetBusVolume("sfx", 0.4f);
            await using var handle = await audio.PlayAsync(new AudioRequest("clip", volume: 0.5f));
            var voice = backend.Voices[0];
            Assert.That(voice.Volume, Is.EqualTo(0.1f).Within(0.0001));
            handle.SetVolume(0.25f);
            Assert.That(voice.Volume, Is.EqualTo(0.05f).Within(0.0001));
            audio.SetBusMuted("sfx", true); Assert.That(voice.Volume, Is.Zero);
            audio.SetBusMuted("sfx", false); Assert.That(voice.Volume, Is.EqualTo(0.05f).Within(0.0001));
            audio.SetMasterMuted(true); Assert.That(voice.Volume, Is.Zero);
            audio.SetMasterMuted(false); Assert.That(voice.Volume, Is.EqualTo(0.05f).Within(0.0001));
            Assert.Throws<ArgumentOutOfRangeException>(() => handle.SetVolume(float.NaN));
        });

        [Test]
        public void InFlightCreatesReserveGlobalAndBusCapacity() => Run(async () =>
        {
            await using var resources = new ResourceStore(new Loader());
            var pending = new TaskCompletionSource<IAudioVoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            var otherPending = new TaskCompletionSource<IAudioVoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            int creates = 0;
            var backend = new Backend { Create = (_, _, _) => ++creates == 1 ? pending.Task : otherPending.Task };
            await using var audio = new AudioService(resources, backend, 2);
            var music = audio.PlayAsync(new AudioRequest("music", "music"));
            Assert.Throws<InvalidOperationException>(() => audio.PlayAsync(new AudioRequest("other", "music")));
            var sound = audio.PlayAsync(new AudioRequest("sound"));
            Assert.That(audio.ActiveCount, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => audio.PlayAsync(new AudioRequest("third")));
            pending.SetResult(new Voice());
            otherPending.SetResult(new Voice());
            await music; await sound;
        });

        [Test]
        public void CancellationReturnsPromptlyAndLateVoiceIsReleasedBeforeSlotReuse() => Run(async () =>
        {
            var loader = new Loader();
            await using var resources = new ResourceStore(loader);
            var pending = new TaskCompletionSource<IAudioVoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new Backend { Create = async (key, scope, token) =>
            {
                await scope.LoadAsync<Asset>(key); // Deliberately ignores caller cancellation.
                return await pending.Task;
            } };
            await using var audio = new AudioService(resources, backend, 1);
            using var cancellation = new CancellationTokenSource();
            var play = audio.PlayAsync(new AudioRequest("clip"), cancellation.Token);
            cancellation.Cancel();
            Assert.That(await Capture(play), Is.InstanceOf<OperationCanceledException>());
            Assert.That(audio.ActiveCount, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => audio.PlayAsync(new AudioRequest("next")));
            var voice = new Voice();
            pending.SetResult(voice);
            await audio.DisposeAsync();
            Assert.That(voice.Stops, Is.EqualTo(1));
            Assert.That(voice.Disposals, Is.EqualTo(1));
            Assert.That(loader.Releases, Is.EqualTo(1));
            Assert.That(audio.ActiveCount, Is.Zero);
        });

        [Test]
        public void ShutdownCancelsPendingPlaybackAndWaitsForLateOwnedVoice() => Run(async () =>
        {
            await using var resources = new ResourceStore(new Loader());
            var pending = new TaskCompletionSource<IAudioVoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new Backend { Create = (_, _, _) => pending.Task };
            var audio = new AudioService(resources, backend);
            var play = audio.PlayAsync(new AudioRequest("clip"));
            var shutdown = audio.DisposeAsync().AsTask();
            Assert.That(await Capture(play), Is.InstanceOf<OperationCanceledException>());
            Assert.That(shutdown.IsCompleted, Is.False);
            var voice = new Voice(); pending.SetResult(voice);
            await shutdown;
            Assert.That(voice.Disposals, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => audio.PlayAsync(new AudioRequest("late")));
        });

        [Test]
        public void ShutdownObservesLateCancelledCleanupFailureExactlyOnce() => Run(async () =>
        {
            await using var resources = new ResourceStore(new Loader());
            var pending = new TaskCompletionSource<IAudioVoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new Backend { Create = (_, _, _) => pending.Task };
            var audio = new AudioService(resources, backend);
            using var cancellation = new CancellationTokenSource();
            var play = audio.PlayAsync(new AudioRequest("late"), cancellation.Token);
            cancellation.Cancel();
            Assert.That(await Capture(play), Is.InstanceOf<OperationCanceledException>());
            var shutdown = audio.DisposeAsync().AsTask();
            pending.SetResult(new Voice { DisposeAction = () => throw new FormatException("late cleanup") });
            var failure = (AggregateException)await Capture(shutdown);
            Assert.That(failure.Flatten().InnerExceptions.Count, Is.EqualTo(1));
            Assert.That(failure.Flatten().InnerExceptions[0].Message, Is.EqualTo("late cleanup"));
            Assert.That(audio.ActiveCount, Is.Zero);
        });

        [Test]
        public void HostResumePreservesManualPauseAndContinuePolicy() => Run(async () =>
        {
            await using var resources = new ResourceStore(new Loader());
            var backend = new Backend();
            await using var audio = new AudioService(resources, backend);
            await using var manual = await audio.PlayAsync(new AudioRequest("manual"));
            await using var normal = await audio.PlayAsync(new AudioRequest("normal"));
            await using var continuing = await audio.PlayAsync(new AudioRequest("continue", pauseWithHost: false));
            manual.Pause();
            audio.Pause();
            Assert.That(backend.Voices[0].Pauses, Is.EqualTo(1));
            Assert.That(backend.Voices[1].Pauses, Is.EqualTo(1));
            Assert.That(backend.Voices[2].Pauses, Is.Zero);
            audio.Resume();
            Assert.That(manual.State, Is.EqualTo(AudioPlaybackState.Paused));
            Assert.That(normal.State, Is.EqualTo(AudioPlaybackState.Playing));
            Assert.That(backend.Voices[0].Resumes, Is.Zero);
            Assert.That(backend.Voices[1].Resumes, Is.EqualTo(1));
            manual.Resume();
            Assert.That(backend.Voices[0].Resumes, Is.EqualTo(1));
        });

        [Test]
        public void PlaybackStartedWhilePausedWaitsForHostUnlessPolicyContinues() => Run(async () =>
        {
            await using var resources = new ResourceStore(new Loader());
            var backend = new Backend();
            await using var audio = new AudioService(resources, backend);
            audio.Pause();
            await using var handle = await audio.PlayAsync(new AudioRequest("clip"));
            Assert.That(handle.State, Is.EqualTo(AudioPlaybackState.Paused));
            handle.Pause(); audio.Resume();
            Assert.That(handle.State, Is.EqualTo(AudioPlaybackState.Paused));
            handle.Resume();
            Assert.That(backend.Voices[0].Resumes, Is.EqualTo(1));
        });

        [Test]
        public void NaturalCompletionAndExplicitStopReleaseVoiceAndResourceScopeOnce() => Run(async () =>
        {
            var loader = new Loader();
            await using var resources = new ResourceStore(loader);
            var backend = new Backend();
            await using var audio = new AudioService(resources, backend);
            var handle = await audio.PlayAsync(new AudioRequest("clip"));
            backend.Voices[0].Playing = false;
            audio.Tick();
            await handle.StopAsync(); await handle.StopAsync();
            Assert.That(handle.State, Is.EqualTo(AudioPlaybackState.Stopped));
            Assert.That(backend.Voices[0].Disposals, Is.EqualTo(1));
            Assert.That(loader.Releases, Is.EqualTo(1));
            Assert.That(audio.ActiveCount, Is.Zero);
        });

        [Test]
        public void StopAndDisposeFailuresDoNotPreventOtherVoicesOrResourceRelease() => Run(async () =>
        {
            var loader = new Loader();
            await using var resources = new ResourceStore(loader);
            var backend = new Backend();
            var audio = new AudioService(resources, backend);
            var first = await audio.PlayAsync(new AudioRequest("one"));
            var second = await audio.PlayAsync(new AudioRequest("two"));
            backend.Voices[0].StopAction = () => throw new FormatException("stop");
            backend.Voices[0].DisposeAction = () => throw new InvalidOperationException("dispose");
            var failure = await Capture(audio.DisposeAsync().AsTask());
            Assert.That(failure, Is.TypeOf<AggregateException>());
            Assert.That(((AggregateException)failure).Flatten().InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(backend.Voices[0].Disposals, Is.EqualTo(1));
            Assert.That(backend.Voices[1].Disposals, Is.EqualTo(1));
            Assert.That(loader.Releases, Is.EqualTo(2));
            Assert.That(first.State, Is.EqualTo(AudioPlaybackState.Stopped));
            Assert.That(second.State, Is.EqualTo(AudioPlaybackState.Stopped));
            Assert.That(audio.ActiveCount, Is.Zero);
            Assert.That(await Capture(audio.DisposeAsync().AsTask()), Is.SameAs(failure));
        });

        [Test]
        public void BackendFailureAfterAssetLoadReturnsScopeAndAllowsRetry() => Run(async () =>
        {
            var loader = new Loader();
            await using var resources = new ResourceStore(loader);
            var backend = new Backend { Create = async (key, scope, token) =>
            {
                await scope.LoadAsync<Asset>(key, token);
                throw new FormatException("create");
            } };
            await using var audio = new AudioService(resources, backend, 1);
            Assert.That(await Capture(audio.PlayAsync(new AudioRequest("bad"))), Is.TypeOf<FormatException>());
            Assert.That(loader.Releases, Is.EqualTo(1));
            Assert.That(audio.ActiveCount, Is.Zero);
            backend.Create = null;
            await using var good = await audio.PlayAsync(new AudioRequest("good"));
            Assert.That(good.State, Is.EqualTo(AudioPlaybackState.Playing));
        });

        [Test]
        public void NaturalCleanupErrorsRemainObservableThroughTakeErrors() => Run(async () =>
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loader = new Loader { Release = () => release.Task };
            var resources = new ResourceStore(loader);
            var backend = new Backend();
            var audio = new AudioService(resources, backend);
            var handle = await audio.PlayAsync(new AudioRequest("clip"));
            backend.Voices[0].Playing = false;
            audio.Tick();
            release.SetException(new FormatException("background release"));
            await Capture(handle.StopAsync());
            await Task.Yield();
            var errors = audio.TakeErrors();
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(((AggregateException)errors[0]).Flatten().InnerExceptions[0].Message, Is.EqualTo("background release"));
            Assert.That(audio.ActiveCount, Is.Zero);
            await audio.DisposeAsync();
            Assert.That(await Capture(resources.DisposeAsync().AsTask()), Is.TypeOf<AggregateException>());
        });

        [Test]
        public void WorkerCallsAndBackendReentryAreRejectedWithoutChangingPlayback() => Run(async () =>
        {
            await using var resources = new ResourceStore(new Loader());
            AudioService audio = null;
            Exception reentry = null;
            var backend = new Backend { Create = (_, _, _) =>
            {
                try { audio.SetMasterVolume(0.5f); } catch (Exception error) { reentry = error; }
                return Task.FromResult<IAudioVoice>(new Voice());
            } };
            await using (audio = new AudioService(resources, backend))
            {
                await using var handle = await audio.PlayAsync(new AudioRequest("clip"));
                Assert.That(reentry, Is.TypeOf<InvalidOperationException>());
                var error = await Task.Run(() =>
                {
                    try { handle.Pause(); return null; }
                    catch (Exception failure) { return failure; }
                });
                Assert.That(error, Is.TypeOf<InvalidOperationException>());
                Assert.That(handle.State, Is.EqualTo(AudioPlaybackState.Playing));
            }
        });

        [Test]
        public void BrokenNativePlaybackQueryIsReportedAndOwnedVoiceStillReleased() => Run(async () =>
        {
            var loader = new Loader();
            await using var resources = new ResourceStore(loader);
            var backend = new Backend();
            await using var audio = new AudioService(resources, backend);
            var handle = await audio.PlayAsync(new AudioRequest("clip"));
            backend.Voices[0].ReadPlaying = () => throw new FormatException("native query");
            var failure = Assert.Throws<AggregateException>(audio.Tick);
            Assert.That(failure.Flatten().InnerExceptions[0].Message, Is.EqualTo("native query"));
            await handle.StopAsync();
            Assert.That(backend.Voices[0].Disposals, Is.EqualTo(1));
            Assert.That(loader.Releases, Is.EqualTo(1));
            Assert.That(audio.ActiveCount, Is.Zero);
        });

        [Test]
        public void AudioModuleRegistersPausesAndStopsBeforeResourceModule() => Run(async () =>
        {
            var host = new GameHost();
            var loader = new Loader();
            var resources = new ResourceStore(loader);
            var backend = new Backend();
            var audio = new AudioService(resources, backend);
            host.AddModule(new ResourceModule(resources));
            host.AddModule(new AudioModule(audio, "resources"));
            await host.StartAsync();
            Assert.That(host.Services.Get<AudioService>(), Is.SameAs(audio));
            var handle = await audio.PlayAsync(new AudioRequest("clip"));
            await host.PauseAsync();
            Assert.That(handle.State, Is.EqualTo(AudioPlaybackState.Paused));
            await host.ResumeAsync();
            Assert.That(handle.State, Is.EqualTo(AudioPlaybackState.Playing));
            await host.StopAsync();
            Assert.That(loader.Releases, Is.EqualTo(1));
            Assert.That(backend.Voices[0].Disposals, Is.EqualTo(1));
        });

        [Test]
        public void DefaultModuleDependencyStopsNativeVoiceBeforeResourceEvenWhenAudioWasRegisteredFirst() => Run(async () =>
        {
            var host = new GameHost();
            var backend = new Backend();
            var loader = new Loader { Release = () =>
            {
                Assert.That(backend.Voices[0].Stops, Is.EqualTo(1));
                Assert.That(backend.Voices[0].Disposals, Is.EqualTo(1));
                return Task.CompletedTask;
            } };
            var resources = new ResourceStore(loader);
            var audio = new AudioService(resources, backend);
            host.AddModule(new AudioModule(audio));
            host.AddModule(new ResourceModule(resources));
            await host.StartAsync();
            await audio.PlayAsync(new AudioRequest("clip"));
            await host.StopAsync();
            Assert.That(loader.Releases, Is.EqualTo(1));
        });

        [Test]
        public void ExplicitEmptyDependenciesSupportAnExternallyOwnedStoreAndDependenciesAreCopied() => Run(async () =>
        {
            await using var resources = new ResourceStore(new Loader());
            var host = new GameHost();
            var audio = new AudioService(resources, new Backend());
            var dependencies = new[] { "resources" };
            var owned = new AudioModule(audio, dependencies);
            dependencies[0] = "changed";
            Assert.That(owned.Dependencies, Is.EqualTo(new[] { "resources" }));
            host.AddModule(new AudioModule(audio, Array.Empty<string>()));
            await host.StartAsync();
            await audio.PlayAsync(new AudioRequest("clip"));
            await host.StopAsync();
            await using var lease = await resources.LoadAsync<Asset>("still-open");
        });

        [Test]
        public void PausedHostReclaimsCompletedContinuingVoiceWithoutReclaimingPausedVoices() => Run(async () =>
        {
            var host = new GameHost();
            var loader = new Loader();
            var resources = new ResourceStore(loader);
            var backend = new Backend();
            var audio = new AudioService(resources, backend, maxConcurrent: 3);
            host.AddModule(new AudioModule(audio));
            host.AddModule(new ResourceModule(resources));
            await host.StartAsync();
            var normal = await audio.PlayAsync(new AudioRequest("normal"));
            var manual = await audio.PlayAsync(new AudioRequest("manual", pauseWithHost: false));
            manual.Pause();
            var continuing = await audio.PlayAsync(new AudioRequest("continue", pauseWithHost: false));
            await host.PauseAsync();
            backend.Voices[2].Playing = false;
            host.Tick(10);
            await continuing.StopAsync();
            Assert.That(continuing.State, Is.EqualTo(AudioPlaybackState.Stopped));
            Assert.That(audio.ActiveCount, Is.EqualTo(2));
            Assert.That(loader.Releases, Is.EqualTo(1));
            Assert.That(backend.Voices[2].Disposals, Is.EqualTo(1));
            Assert.That(normal.State, Is.EqualTo(AudioPlaybackState.Paused));
            Assert.That(manual.State, Is.EqualTo(AudioPlaybackState.Paused));
            Assert.That(backend.Voices[0].Disposals + backend.Voices[1].Disposals, Is.Zero);
            await audio.PlayAsync(new AudioRequest("slot-reused", pauseWithHost: false));
            Assert.That(host.Clock.ScaledTime, Is.Zero);
            Assert.That(host.Clock.UnscaledTime, Is.Zero);
            await host.StopAsync();
        });

        [Test]
        public void AsyncNativeDisposalRetainsResourceAndCapacityUntilNativeOwnershipHasEnded() => Run(async () =>
        {
            var voice = new AsyncVoice();
            var loader = new Loader { Release = () =>
            {
                Assert.That(voice.Disposals, Is.EqualTo(1));
                return Task.CompletedTask;
            } };
            await using var resources = new ResourceStore(loader);
            var backend = new Backend { Create = async (key, scope, token) => { await scope.LoadAsync<Asset>(key, token); return voice; } };
            await using var audio = new AudioService(resources, backend, maxConcurrent: 1);
            var handle = await audio.PlayAsync(new AudioRequest("async-native"));
            var stopping = handle.StopAsync();
            Assert.That(stopping.IsCompleted, Is.False);
            Assert.That(voice.AsyncDisposals, Is.EqualTo(1));
            Assert.That(voice.Disposals, Is.Zero);
            Assert.That(loader.Releases, Is.Zero);
            Assert.That(audio.ActiveCount, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => audio.PlayAsync(new AudioRequest("too-early")));
            voice.NativeReleased.SetResult(true);
            await stopping;
            Assert.That(loader.Releases, Is.EqualTo(1));
            Assert.That(audio.ActiveCount, Is.Zero);
            backend.Create = null;
            await using var next = await audio.PlayAsync(new AudioRequest("reused-slot"));
            Assert.That(next.State, Is.EqualTo(AudioPlaybackState.Playing));
        });

        [Test]
        public void ShutdownAwaitsAsyncNativeDisposalAndStillReleasesResourcesAfterItsFailure() => Run(async () =>
        {
            var loader = new Loader();
            await using var resources = new ResourceStore(loader);
            var voice = new AsyncVoice { StopAction = () => throw new FormatException("native stop") };
            var backend = new Backend { Create = async (key, scope, token) => { await scope.LoadAsync<Asset>(key, token); return voice; } };
            var audio = new AudioService(resources, backend);
            await audio.PlayAsync(new AudioRequest("async-native"));
            var shutdown = audio.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(loader.Releases, Is.Zero);
            Assert.That(audio.ActiveCount, Is.EqualTo(1));
            voice.NativeReleased.SetException(new InvalidOperationException("native release"));
            var failure = (AggregateException)await Capture(shutdown);
            Assert.That(failure.Flatten().InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(voice.AsyncDisposals, Is.EqualTo(1));
            Assert.That(loader.Releases, Is.EqualTo(1));
            Assert.That(audio.ActiveCount, Is.Zero);
        });

        private static async Task<Exception> Capture(Task task)
        { try { await task; return null; } catch (Exception error) { return error; } }

        private static void Run(Func<Task> scenario)
        {
            var previous = SynchronizationContext.Current;
            var context = new PumpContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var task = scenario();
                var timeout = Stopwatch.StartNew();
                while (!task.IsCompleted)
                {
                    if (timeout.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Audio test did not finish on the host loop.");
                    context.Pump();
                }
                task.GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        private sealed class PumpContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _queue = new ConcurrentQueue<(SendOrPostCallback, object)>();
            public override void Post(SendOrPostCallback callback, object state) { _queue.Enqueue((callback, state)); }
            internal void Pump()
            {
                if (_queue.TryDequeue(out var work)) work.Callback(work.State);
                else Thread.Yield();
            }
        }
    }
}
