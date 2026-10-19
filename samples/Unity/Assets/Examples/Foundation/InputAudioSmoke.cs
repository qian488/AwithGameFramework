using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Gameplay.Audio;
using AwithGameFrame.Gameplay.Input;
using AwithGameFrame.Unity.Audio;
using AwithGameFrame.Unity.Input;
using UnityEngine;

namespace AwithGameFrame.Samples
{
    /// <summary>Uses actual Input APIs, AudioClips and AudioSources; does not require audible output.</summary>
    public sealed class InputAudioSmoke : MonoBehaviour
    {
        public static async Task RunAsync(GameHost host)
        {
            var owner = new GameObject("Input audio smoke");
            var clock = owner.AddComponent<InputAudioSmoke>();
            if (FindObjectOfType<AudioListener>() == null) owner.AddComponent<AudioListener>();
            using var input = new InputService(host.Events);
            using var context = input.CreateContext("smoke");
            context.Bind("jump", "keyboard/space");
            var nativeInput = new UnityInputBackend();
            nativeInput.MapKey("smoke/none", KeyCode.None);
            Require(nativeInput.ReadControl("smoke/none") == 0, "Native Unity key mapping");
            _ = nativeInput.ReadControl("mouse/left");
            // Unity's built-in Input exposes no public key injection. Exercise its real mappings
            // above, then feed a deterministic held frame into the same portable focus policy.
            Require(input.ProcessFrame(new Dictionary<string, float> { ["keyboard/space"] = 1 }).Events[0].Phase == InputActionPhase.Pressed, "Action press");
            Require(input.SetFocused(false).Events[0].Phase == InputActionPhase.Released, "Focus loss releases held action");
            var loader = new MemoryAudioLoader();
            var store = new ResourceStore(loader);
            var audio = new AudioService(store, new UnityAudioBackend(owner.transform), 2);
            try
            {
                audio.SetMasterVolume(0.5f); audio.SetBusVolume("sfx", 0.4f);
                var first = await audio.PlayAsync(new AudioRequest("memory/tone", volume: 0.5f, loop: true));
                var second = await audio.PlayAsync(new AudioRequest("memory/tone", volume: 0.5f, loop: true));
                var sources = owner.GetComponentsInChildren<AudioSource>();
                Require(sources.Length == 2 && sources[0].clip != null && sources[0].clip == sources[1].clip && loader.Loads == 1, "Real voices share leased AudioClip");
                Require(Math.Abs(sources[0].volume - 0.1f) < 0.0001f && sources[0].loop, "Native master/bus/track volume and loop");
                audio.SetBusMuted("sfx", true);
                Require(sources[0].volume == 0 && sources[1].volume == 0, "Native mute");
                audio.SetBusMuted("sfx", false);
                bool limited = false;
                try { await audio.PlayAsync(new AudioRequest("third")); }
                catch (InvalidOperationException) { limited = true; }
                Require(limited && owner.GetComponentsInChildren<AudioSource>().Length == 2, "Concurrency rejects extra native voice");
                first.Pause(); audio.Pause();
                Require(first.State == AudioPlaybackState.Paused && second.State == AudioPlaybackState.Paused && !sources[0].isPlaying && !sources[1].isPlaying, "Native pause");
                audio.Resume();
                Require(first.State == AudioPlaybackState.Paused && !sources[0].isPlaying && second.State == AudioPlaybackState.Playing, "Host resume preserves manual pause");
                first.Resume();
                await first.StopAsync();
                await clock.NextFrame();
                Require(sources[0] == null && loader.Releases == 0, "First native source destroyed while clip remains leased");
                await second.StopAsync();
                await clock.NextFrame();
                Require(sources[1] == null && owner.GetComponentsInChildren<AudioSource>().Length == 0 && loader.Releases == 1 && loader.Clip == null, "All native voices and clip released");
                Debug.Log("UNITY_INPUT_AUDIO_PASSED");
            }
            finally
            {
                try { await audio.DisposeAsync(); }
                finally { await store.DisposeAsync(); Destroy(owner); }
            }
        }

        private sealed class MemoryAudioLoader : IResourceLoader
        {
            internal int Loads, Releases;
            internal AudioClip Clip;
            public Task<LoadedResource> LoadAsync(string key, Type type, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (type != typeof(AudioClip)) throw new InvalidOperationException("Expected AudioClip.");
                Loads++;
                Clip = AudioClip.Create("Awith silent smoke", 22050 * 2, 1, 22050, false);
                var clip = Clip;
                return Task.FromResult(new LoadedResource(clip, () =>
                { Releases++; UnityEngine.Object.Destroy(clip); return Task.CompletedTask; }));
            }
        }
        private Task NextFrame()
        {
            var completion = new TaskCompletionSource<bool>();
            StartCoroutine(CompleteFrame(completion)); return completion.Task;
        }
        private static IEnumerator CompleteFrame(TaskCompletionSource<bool> completion)
        { yield return null; completion.SetResult(true); }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Input/audio smoke: " + message); }
    }
}
