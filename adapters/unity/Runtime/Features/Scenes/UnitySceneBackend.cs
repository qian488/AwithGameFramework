using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Scenes;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace AwithGameFrame.Unity.Scenes
{
    /// <summary>Always loads additively, keeping bootstrap/other-host scenes outside SceneService ownership.</summary>
    public sealed class UnitySceneBackend : ISceneBackend
    {
        private static readonly SemaphoreSlim NativeLoads = new SemaphoreSlim(1, 1);
        private readonly int thread = Environment.CurrentManagedThreadId;

        public async Task<ISceneInstance> LoadAsync(string key, ResourceScope resources, CancellationToken cancellationToken)
        {
            Verify();
            await NativeLoads.WaitAsync(cancellationToken);
            Scene loaded = default;
            UnityAction<Scene, LoadSceneMode> listener = (scene, mode) =>
            {
                if (mode == LoadSceneMode.Additive && (scene.path == key || scene.name == Path.GetFileNameWithoutExtension(key))) loaded = scene;
            };
            try
            {
                Verify(); cancellationToken.ThrowIfCancellationRequested();
                SceneManager.sceneLoaded += listener;
                var operation = SceneManager.LoadSceneAsync(key, LoadSceneMode.Additive);
                if (operation == null) throw new InvalidOperationException("Unity refused scene load: " + key);
                // Native AsyncOperation cannot be aborted. Return its eventual ownership even after cancellation.
                await CompleteAsync(operation);
                Verify();
                if (!loaded.IsValid() || !loaded.isLoaded) throw new InvalidOperationException("Unity scene finished without a loaded scene handle: " + key);
                return new Instance(loaded, thread);
            }
            finally { SceneManager.sceneLoaded -= listener; NativeLoads.Release(); }
        }

        private void Verify() { if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Unity scenes require the creating thread."); }
        private static Task CompleteAsync(AsyncOperation operation)
        {
            if (operation.isDone) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.completed += _ => completion.TrySetResult(true);
            return completion.Task;
        }

        private sealed class Instance : ISceneInstance
        {
            private readonly Scene scene;
            private readonly int thread;
            private Task unloading;
            public Instance(Scene scene, int thread) { this.scene = scene; this.thread = thread; }
            private void Verify() { if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Unity scene lifecycle requires the creating thread."); }
            public Task ActivateAsync(CancellationToken token)
            {
                Verify(); token.ThrowIfCancellationRequested();
                if (!scene.IsValid() || !scene.isLoaded || !SceneManager.SetActiveScene(scene)) throw new InvalidOperationException("Cannot activate Unity scene: " + scene.path);
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync()
            {
                Verify();
                if (!scene.IsValid() || !scene.isLoaded) return default;
                if (unloading == null || unloading.IsFaulted || unloading.IsCanceled) unloading = UnloadAsync();
                return new ValueTask(unloading);
            }
            private async Task UnloadAsync()
            {
                var operation = SceneManager.UnloadSceneAsync(scene);
                if (operation == null) throw new InvalidOperationException("Unity refused scene unload: " + scene.path);
                await CompleteAsync(operation);
            }
        }
    }
}
