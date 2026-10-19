using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Gameplay.Audio;
using AwithGameFrame.Gameplay.Input;
using AwithGameFrame.Godot;
using Godot;

public static class InputAudioSmoke
{
    public static async Task RunAsync(GameHost host, Node parent)
    {
        var owner = new Node { Name = "InputAudioSmoke" };
        parent.AddChild(owner);
        using var input = new InputService(host.Events);
        using var context = input.CreateContext("smoke"); context.Bind("jump", "smoke/key");
        var nativeInput = new GodotInputBackend(parent.GetWindow());
        // Logical key state can be injected even under DisplayServerHeadless, where physical-key
        // layout translation is unavailable. Physical mappings remain the production default.
        nativeInput.MapKey("smoke/key", Key.F12, physical: false);
        var loader = new MemoryAudioLoader();
        var store = new ResourceStore(loader);
        var audio = new AudioService(store, new GodotAudioBackend(owner), 2);
        try
        {
            using (var press = new InputEventKey { Keycode = Key.F12, PhysicalKeycode = Key.F12, Pressed = true })
                global::Godot.Input.ParseInputEvent(press);
            global::Godot.Input.FlushBufferedEvents();
            Require(nativeInput.ReadControl("smoke/key") == 1, "Native injected logical key mapping");
            Require(input.ProcessFrame(new Dictionary<string, float> { ["smoke/key"] = nativeInput.ReadControl("smoke/key") }).Events[0].Phase == InputActionPhase.Pressed, "Action press from native key");
            Require(input.SetFocused(false).Events[0].Phase == InputActionPhase.Released, "Focus loss releases held action");
            using (var release = new InputEventKey { Keycode = Key.F12, PhysicalKeycode = Key.F12, Pressed = false })
                global::Godot.Input.ParseInputEvent(release);
            global::Godot.Input.FlushBufferedEvents();
            Require(nativeInput.ReadControl("smoke/key") == 0, "Native key release");
            audio.SetMasterVolume(0.5f); audio.SetBusVolume("sfx", 0.4f);
            var first = await audio.PlayAsync(new AudioRequest("memory/tone", volume: 0.5f, loop: true));
            var second = await audio.PlayAsync(new AudioRequest("memory/tone", volume: 0.5f, loop: true));
            var a = (AudioStreamPlayer)owner.GetChild(0);
            var b = (AudioStreamPlayer)owner.GetChild(1);
            var firstStream = a.Stream;
            var secondStream = b.Stream;
            Require(a.Stream != null && b.Stream != null && loader.Loads == 1, "Real voices share leased source stream");
            Require(Math.Abs(a.VolumeLinear - 0.1f) < 0.0001f && ((AudioStreamWav)a.Stream).LoopMode == AudioStreamWav.LoopModeEnum.Forward, "Native master/bus/track volume and loop");
            Require(loader.Stream.LoopMode == AudioStreamWav.LoopModeEnum.Disabled, "Per-voice looping does not mutate shared stream");
            audio.SetBusMuted("sfx", true); Require(a.VolumeLinear == 0 && b.VolumeLinear == 0, "Native mute");
            audio.SetBusMuted("sfx", false);
            bool limited = false;
            try { await audio.PlayAsync(new AudioRequest("third")); }
            catch (InvalidOperationException) { limited = true; }
            Require(limited && owner.GetChildCount() == 2, "Concurrency rejects extra native player");
            first.Pause(); audio.Pause(); Require(a.StreamPaused && b.StreamPaused, "Native pause");
            audio.Resume(); Require(a.StreamPaused && !b.StreamPaused, "Host resume preserves manual pause");
            first.Resume(); Require(!a.StreamPaused, "Manual resume");
            await first.StopAsync();
            Require(!GodotObject.IsInstanceValid(a) && !GodotObject.IsInstanceValid(firstStream) && loader.Releases == 0,
                "Awaited stop frees the native player and mixer-owned private stream while source remains leased");
            await second.StopAsync();
            Require(!GodotObject.IsInstanceValid(b) && !GodotObject.IsInstanceValid(secondStream) && owner.GetChildCount() == 0 &&
                loader.Releases == 1 && !GodotObject.IsInstanceValid(loader.Stream), "Awaited stop releases all native players, mixer streams and source scope");
            GD.Print("GODOT_INPUT_AUDIO_PASSED");
        }
        finally
        {
            using (var release = new InputEventKey { Keycode = Key.F12, PhysicalKeycode = Key.F12, Pressed = false })
                global::Godot.Input.ParseInputEvent(release);
            global::Godot.Input.FlushBufferedEvents();
            try { await audio.DisposeAsync(); }
            finally { await store.DisposeAsync(); owner.QueueFree(); }
        }
    }

    private sealed class MemoryAudioLoader : IResourceLoader
    {
        internal int Loads, Releases;
        internal AudioStreamWav Stream;
        public Task<LoadedResource> LoadAsync(string key, Type type, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type != typeof(AudioStream)) throw new InvalidOperationException("Expected AudioStream.");
            Loads++;
            Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 22050, Stereo = false, Data = new byte[22050 * 2 * 2] };
            var stream = Stream;
            return Task.FromResult(new LoadedResource(stream, () => { Releases++; stream.Dispose(); return Task.CompletedTask; }));
        }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Input/audio smoke: " + message); }
}
