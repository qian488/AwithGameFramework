using UnityEngine;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal enum ExpeditionFeedbackCue
    {
        CheckpointSaved,
        Resupply,
        Reward
    }

    [DisallowMultipleComponent]
    internal sealed class ExpeditionFeedback : MonoBehaviour
    {
        private const float PulseDuration = .36f;
        private const float NoteDuration = .105f;
        private static readonly (Color Color, float[] Notes)[] Cues =
        {
            (new Color(.43f, .82f, .92f), new[] { 523.25f, 783.99f }),
            (new Color(.56f, .90f, .64f), new[] { 392f, 523.25f }),
            (new Color(1f, .80f, .42f), new[] { 523.25f, 659.25f, 880f })
        };

        private readonly AudioClip[] clips = new AudioClip[Cues.Length];
        private Image pulse;
        private AudioSource source;
        private AudioListener fallbackListener;
        private float pulseEnd;

        internal static ExpeditionFeedback Create(Canvas canvas, float uiSfxVolume)
        {
            var node = new GameObject("Expedition Feedback", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)node.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -18f);
            rect.sizeDelta = new Vector2(128f, 4f);

            var feedback = node.AddComponent<ExpeditionFeedback>();
            feedback.pulse = node.GetComponent<Image>();
            feedback.pulse.raycastTarget = false;
            feedback.pulse.enabled = false;
            feedback.source = node.AddComponent<AudioSource>();
            feedback.source.playOnAwake = false;
            feedback.source.spatialBlend = 0f;
            feedback.SetVolume(uiSfxVolume);
            return feedback;
        }

        internal void SetVolume(float uiSfxVolume)
        {
            source.volume = Mathf.Clamp01(uiSfxVolume);
            if (source.volume == 0f) StopAudio();
        }

        internal void Play(ExpeditionFeedbackCue cue)
        {
            if (!isActiveAndEnabled) return;

            int index = (int)cue;
            var (color, notes) = Cues[index];
            StopAudio();
            transform.SetAsLastSibling();
            pulse.color = color;
            pulse.enabled = true;
            pulseEnd = Time.unscaledTime + PulseDuration;

            if (source.volume == 0f) return;
            var clip = clips[index];
            if (clip == null) clips[index] = clip = Synthesize(cue, notes);
            if (clip == null) return;

            // The camp has no listener. Add one only while playing outside the battle scene.
            if (!HasActiveListener())
            {
                if (fallbackListener == null) fallbackListener = gameObject.AddComponent<AudioListener>();
                fallbackListener.enabled = true;
            }
            source.clip = clip;
            source.Play();
        }

        internal void Stop()
        {
            if (pulse != null) pulse.enabled = false;
            StopAudio();
        }

        private void Update()
        {
            if (pulse.enabled)
            {
                var color = pulse.color;
                color.a = Mathf.Clamp01((pulseEnd - Time.unscaledTime) / PulseDuration);
                pulse.color = color;
                if (color.a == 0f) pulse.enabled = false;
            }

            if (fallbackListener != null && fallbackListener.enabled && !source.isPlaying)
                fallbackListener.enabled = false;
        }

        private void OnDisable() => Stop();

        private void OnDestroy()
        {
            foreach (var clip in clips) PassWorld.Release(clip);
        }

        private void StopAudio()
        {
            if (source != null)
            {
                source.Stop();
                source.clip = null;
            }
            if (fallbackListener != null) fallbackListener.enabled = false;
        }

        private static bool HasActiveListener()
        {
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener.isActiveAndEnabled) return true;
            return false;
        }

        private static AudioClip Synthesize(ExpeditionFeedbackCue cue, float[] notes)
        {
            int framesPerNote = Mathf.RoundToInt(NoteDuration * PassSound.SampleRate);
            var samples = new float[framesPerNote * notes.Length];
            for (int note = 0; note < notes.Length; note++)
            {
                float phaseStep = 2f * Mathf.PI * notes[note] / PassSound.SampleRate;
                int offset = note * framesPerNote;
                for (int frame = 0; frame < framesPerNote; frame++)
                {
                    float time = (float)frame / PassSound.SampleRate;
                    float tail = (float)(framesPerNote - 1 - frame) / PassSound.SampleRate;
                    float envelope = Mathf.Clamp01(time / .008f) * Mathf.Clamp01(tail / .055f);
                    float phase = frame * phaseStep;
                    samples[offset + frame] = .23f * envelope * (Mathf.Sin(phase) + .18f * Mathf.Sin(2f * phase));
                }
            }

            var clip = AudioClip.Create("Expedition " + cue, samples.Length, 1, PassSound.SampleRate, false);
            if (clip != null && clip.SetData(samples, 0)) return clip;
            PassWorld.Release(clip);
            Debug.LogWarning($"Could not upload expedition audio '{cue}'.");
            return null;
        }
    }
}
