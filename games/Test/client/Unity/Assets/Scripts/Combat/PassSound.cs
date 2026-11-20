using System;
using Test.Application.Combat;
using UnityEngine;
using Random = System.Random;

namespace Test.Client.Combat
{
    internal enum PassSoundId
    {
        Footstep, Draw, Arrow, Blade, Pole, HeavySwing, Hit, ArrowHit, Deflect, Guard, Break, Fall, Finish, Dodge, Gate
    }

    internal static class PassSound
    {
        internal const int SampleRate = 22050;

        internal static AudioClip CreateClip(PassWorld world, string name, float[] samples, int channels)
        {
            var clip = AudioClip.Create(name, samples.Length / channels, channels, SampleRate, false);
            if (!clip.SetData(samples, 0))
            {
                PassWorld.Release(clip);
                throw new InvalidOperationException($"Could not upload procedural audio '{name}'.");
            }
            world.Own(clip);
            return clip;
        }

        internal static AudioClip Ambient(PassWorld world)
        {
            const int seconds = 8;
            var sound = new float[SampleRate * seconds * 2];
            var random = new Random(8173 + (int)(world.Map?.Id ?? 0));
            var map = world.Map?.Id;
            string name = map == CombatMapId.CedarGrove ? "Cedar leaves" : map == CombatMapId.SaltCauseway ? "Salt surf"
                : map == CombatMapId.EmberFoundry ? "Furnace air" : "Rain on slate";
            float smooth = 0, broad = 0;
            for (int frame = 0; frame < sound.Length / 2; frame++)
            {
                float time = (float)frame / SampleRate;
                float cycle = time / seconds;
                float noise = (float)random.NextDouble() * 2 - 1;
                smooth += .048f * (noise - smooth);
                broad += .003f * (smooth - broad);
                float swell = .62f + .38f * Wave(cycle * 2);
                float detail = noise - smooth;
                float bass = map == CombatMapId.EmberFoundry ? .042f * Wave(time * 42) + .024f * Wave(time * 57) : 0;
                float mixed = map switch
                {
                    CombatMapId.CedarGrove => .22f * smooth * swell + .025f * detail * swell,
                    CombatMapId.SaltCauseway => (1.1f * broad + .33f * smooth) * swell + .015f * detail,
                    CombatMapId.EmberFoundry => .8f * broad + .20f * smooth + bass * (.65f + .35f * Wave(cycle * 3)),
                    _ => .19f * smooth + .045f * detail + .45f * broad
                };
                float edge = Mathf.Clamp01(Mathf.Min(time, seconds - time) * 170);
                float drift = Wave(cycle) * .12f;
                sound[frame * 2] = (mixed * (1 - drift) + .015f * detail) * edge;
                sound[frame * 2 + 1] = (mixed * (1 + drift) - .015f * detail) * edge;
            }
            return CreateClip(world, name, sound, 2);
        }

        internal static AudioClip Foley(PassWorld world, PassSoundId sound, int variant)
        {
            var voice = Describe(sound, world.Map?.Id);
            var samples = new float[Mathf.RoundToInt(voice.Length * SampleRate)];
            var random = new Random(7919 * ((int)sound + 1) + 157 * variant);
            float detune = .94f + .065f * variant;
            float smooth = 0;
            float phase = 0;
            for (int frame = 0; frame < samples.Length; frame++)
            {
                float time = (float)frame / SampleRate;
                float progress = time / voice.Length;
                float noise = (float)random.NextDouble() * 2 - 1;
                smooth += voice.Filter * (noise - smooth);
                phase += Mathf.Lerp(voice.Start, voice.End, Mathf.Sqrt(progress)) * detune / SampleRate;
                float grains = voice.Noise * (smooth + .18f * noise);
                float body = voice.Body * (Wave(phase) + voice.Ring * Wave(phase * 2.73f) * Mathf.Exp(-time * 8));
                float envelope = Mathf.Clamp01(time / voice.Attack) * Mathf.Exp(-time * voice.Decay) * Mathf.Clamp01((1 - progress) * 9);
                if (sound == PassSoundId.Break || sound == PassSoundId.Finish || sound == PassSoundId.Gate)
                    grains *= .75f + .25f * Wave(time * (sound == PassSoundId.Gate ? 9 : 28));
                samples[frame] = Mathf.Clamp((grains + body) * envelope, -.95f, .95f);
            }
            return CreateClip(world, $"{sound} {variant + 1}", samples, 1);
        }

        private static float Wave(float cycles) => Mathf.Sin(cycles * (2 * Mathf.PI));

        private static (float Length, float Start, float End, float Body, float Noise, float Filter, float Ring, float Attack, float Decay)
            Describe(PassSoundId sound, CombatMapId? map) => sound switch
            {
                PassSoundId.Footstep => map switch
                {
                    CombatMapId.CedarGrove => (.17f, 82, 52, .11f, .76f, .29f, .10f, .013f, 14),
                    CombatMapId.SaltCauseway => (.18f, 152, 81, .20f, .75f, .52f, .20f, .006f, 23),
                    CombatMapId.EmberFoundry => (.19f, 260, 129, .30f, .52f, .35f, .48f, .004f, 23),
                    _ => (.20f, 98, 52, .14f, .68f, .22f, .08f, .008f, 14)
                },
                PassSoundId.Draw => (.32f, 159, 379, .14f, .39f, .14f, .48f, .07f, 5),
                PassSoundId.Arrow => (.24f, 780, 183, .25f, .69f, .42f, .18f, .006f, 17),
                PassSoundId.Blade => (.21f, 560, 164, .08f, .92f, .39f, .36f, .036f, 8),
                PassSoundId.Pole => (.27f, 315, 222, .12f, .81f, .43f, .22f, .031f, 8),
                PassSoundId.HeavySwing => (.36f, 148, 52, .27f, .62f, .20f, .13f, .052f, 5),
                PassSoundId.Hit => (.21f, 201, 75, .47f, .79f, .31f, .16f, .006f, 20),
                PassSoundId.ArrowHit => (.17f, 310, 111, .36f, .81f, .37f, .22f, .003f, 25),
                PassSoundId.Deflect => (.58f, 1280, 1075, .45f, .47f, .35f, .63f, .002f, 9),
                PassSoundId.Guard => (.23f, 376, 195, .27f, .52f, .54f, .44f, .003f, 19),
                PassSoundId.Break => (.48f, 302, 93, .45f, .91f, .44f, .34f, .009f, 11),
                PassSoundId.Fall => (.38f, 124, 48, .44f, .75f, .16f, .07f, .029f, 11),
                PassSoundId.Finish => (.76f, 225, 47, .55f, .89f, .28f, .22f, .027f, 7),
                PassSoundId.Dodge => (.28f, 114, 77, .08f, .81f, .13f, .11f, .042f, 7),
                PassSoundId.Gate => (.94f, 116, 49, .28f, .70f, .15f, .41f, .064f, 3),
                _ => throw new ArgumentOutOfRangeException(nameof(sound), sound, null)
            };
    }
}
