using Test.Application.Combat;
using UnityEngine;

namespace Test.Client.Combat
{
    internal sealed class PassScore
    {
        private enum Voice { Strings, Pluck, Flute, Bell, Drum, Rim }

        private readonly string name;
        private readonly int beatsPerMinute;
        private readonly int[] roots;
        private readonly int[] melody;
        private readonly Voice lead;

        private PassScore(string name, int beatsPerMinute, int[] roots, int[] melody, Voice lead)
        {
            this.name = name;
            this.beatsPerMinute = beatsPerMinute;
            this.roots = roots;
            this.melody = melody;
            this.lead = lead;
        }

        private static readonly PassScore Rain = new PassScore("Rain Pass - Lanterns in Rain", 76,
            new[] { 50, 50, 46, 48, 50, 46, 43, 45 },
            new[] { 62, -1, 65, 69, 72, 69, -1, 65, 62, -1, 65, 70, 67, -1, 65, -1,
                62, 65, 69, -1, 70, 69, 65, -1, 67, 65, 62, -1, 64, -1, 57, -1 }, Voice.Pluck);

        private static readonly PassScore Grove = new PassScore("Cedar Grove - Through the Boughs", 88,
            new[] { 55, 57, 55, 52, 55, 57, 52, 50 },
            new[] { 67, 69, 71, -1, 74, -1, 76, 74, 71, -1, 69, 67, 64, -1, 67, -1,
                71, 74, 76, -1, 81, 76, -1, 74, 76, 71, 69, -1, 69, 66, -1, -1 }, Voice.Flute);

        private static readonly PassScore Salt = new PassScore("Salt Causeway - The Long Tide", 64,
            new[] { 45, 45, 41, 43, 45, 48, 41, 40 },
            new[] { 57, -1, 64, -1, 67, 64, -1, -1, 60, -1, 65, 64, 62, -1, 59, -1,
                57, -1, 69, 67, 64, -1, 60, -1, 65, 64, -1, 60, 59, -1, 56, -1 }, Voice.Bell);

        private static readonly PassScore Foundry = new PassScore("Ember Foundry - Under the Anvil", 96,
            new[] { 45, 45, 41, 43, 45, 46, 41, 40 },
            new[] { 57, 60, -1, 57, 63, -1, 62, -1, 57, 53, -1, 60, 58, -1, 62, -1,
                57, 60, 63, 64, 65, -1, 63, -1, 60, 57, 53, -1, 56, 52, -1, -1 }, Voice.Bell);

        internal static (AudioClip Theme, AudioClip Percussion) Render(PassWorld world)
        {
            var score = world.Map?.Id switch
            {
                CombatMapId.CedarGrove => Grove,
                CombatMapId.SaltCauseway => Salt,
                CombatMapId.EmberFoundry => Foundry,
                _ => Rain
            };
            return score.Render(world, world.Map?.Id == CombatMapId.EmberFoundry);
        }

        private (AudioClip, AudioClip) Render(PassWorld world, bool industrial)
        {
            float beat = 60f / beatsPerMinute;
            int frames = Mathf.RoundToInt(roots.Length * 4 * beat * PassSound.SampleRate);
            var theme = new float[frames * 2];
            var percussion = new float[frames * 2];
            for (int bar = 0; bar < roots.Length; bar++)
            {
                float start = bar * 4 * beat;
                int root = roots[bar];
                Add(theme, start, beat * 4.35f, root - 12, Voice.Strings, .105f, 0);
                Add(theme, start + .04f, beat * 3.8f, root + 7, Voice.Strings, .043f, -.52f);
                Add(theme, start + beat * 1.5f, beat * 1.6f, root + 12, Voice.Pluck, .064f, .50f);
                Add(theme, start + beat * 3.25f, beat * 1.2f, root + 7, Voice.Pluck, .052f, -.39f);
                for (int count = 0; count < 4; count++)
                {
                    int note = melody[bar * 4 + count];
                    if (note < 0) continue;
                    float onset = start + count * beat + .035f;
                    Add(theme, onset, beat * (lead == Voice.Flute ? .93f : 1.24f), note, lead, .16f, -.19f);
                    Add(theme, onset + beat * .67f, beat * 1.1f, note, lead, .033f, .63f);
                }
                Add(percussion, start, .48f, industrial ? 40 : 37, Voice.Drum, .36f, 0);
                Add(percussion, start + beat * (bar % 2 == 0 ? 2.5f : 2), .42f, 41, Voice.Drum, .26f, .16f);
                Add(percussion, start + beat * 1.5f, .19f, industrial ? 82 : 74, Voice.Rim, .23f, -.47f);
                Add(percussion, start + beat * 3.5f, .21f, industrial ? 79 : 78, Voice.Rim, .21f, .47f);
            }
            return (PassSound.CreateClip(world, name, theme, 2), PassSound.CreateClip(world, name + " - Drums", percussion, 2));
        }

        internal static AudioClip Stinger(PassWorld world, bool won)
        {
            var mix = new float[PassSound.SampleRate * 2 * 2];
            if (won)
            {
                Add(mix, 0, 1.15f, 57, Voice.Drum, .37f, 0);
                Add(mix, .10f, 1.6f, 64, Voice.Bell, .32f, -.3f);
                Add(mix, .29f, 1.6f, 69, Voice.Bell, .30f, .3f);
                Add(mix, .45f, 1.48f, 76, Voice.Pluck, .22f, 0);
            }
            else
            {
                Add(mix, 0, 1.25f, 36, Voice.Drum, .50f, 0);
                Add(mix, .12f, 1.6f, 53, Voice.Bell, .30f, -.2f);
                Add(mix, .37f, 1.55f, 48, Voice.Strings, .32f, .2f);
            }
            return PassSound.CreateClip(world, won ? "Victory cadence" : "Defeat cadence", mix, 2);
        }

        private static void Add(float[] mix, float start, float length, int note, Voice voice, float gain, float pan)
        {
            float speed = 2 * Mathf.PI * 440 * Mathf.Pow(2, (note - 69) / 12f) / PassSound.SampleRate;
            int first = Mathf.RoundToInt(start * PassSound.SampleRate);
            int samples = Mathf.RoundToInt(length * PassSound.SampleRate);
            int frames = mix.Length / 2;
            float left = gain * Mathf.Sqrt((1 - pan) * .5f);
            float right = gain * Mathf.Sqrt((1 + pan) * .5f);
            float attack = voice == Voice.Strings ? .41f : voice == Voice.Flute ? .09f : .008f;
            for (int sample = 0; sample < samples; sample++)
            {
                float time = (float)sample / PassSound.SampleRate;
                float phase = speed * sample;
                float envelope = Mathf.Clamp01(time / attack) * Mathf.Clamp01((length - time) / .15f);
                float signal;
                switch (voice)
                {
                    case Voice.Strings:
                        signal = .58f * Mathf.Sin(phase) + .25f * Mathf.Sin(phase * 1.003f) + .17f * Mathf.Sin(phase * 2.005f);
                        break;
                    case Voice.Flute:
                        float vibrato = .18f * Mathf.Sin(time * 32f) * Mathf.Clamp01(time * 5);
                        signal = .82f * Mathf.Sin(phase + vibrato) + .18f * Mathf.Sin(phase * 2 + vibrato);
                        break;
                    case Voice.Pluck:
                        signal = Mathf.Exp(-time * 2.9f) * (.67f * Mathf.Sin(phase) + .25f * Mathf.Exp(-time * 6) * Mathf.Sin(phase * 2.01f) + .08f * Mathf.Sin(phase * 3));
                        break;
                    case Voice.Bell:
                        signal = Mathf.Exp(-time * 2.3f) * (.69f * Mathf.Sin(phase) + .23f * Mathf.Exp(-time * 4) * Mathf.Sin(phase * 2.78f) + .08f * Mathf.Sin(phase * 4.15f));
                        break;
                    case Voice.Drum:
                        signal = Mathf.Exp(-time * 8) * (.88f * Mathf.Sin(phase + 5 * (1 - Mathf.Exp(-time * 26))) + .12f * Mathf.Sin(phase * 3.4f));
                        break;
                    default:
                        signal = Mathf.Exp(-time * 23) * (.61f * Mathf.Sin(phase * 1.73f) + .39f * Mathf.Sin(phase * 3.07f));
                        break;
                }
                int offset = (first + sample) % frames * 2;
                mix[offset] += left * signal * envelope;
                mix[offset + 1] += right * signal * envelope;
            }
        }
    }
}
