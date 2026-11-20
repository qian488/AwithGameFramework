using System;
using Test.Application.Combat;
using Test.Domain.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Test.Client.Combat
{
    internal sealed class PassEffects
    {
        private readonly Material ink;
        private readonly Mesh lines;
        private readonly Vector3[] vertices;
        private readonly Color32[] colors;
        private readonly Vector3[] rain;
        private readonly ParticleSystem sparks;
        private readonly Light pop;
        private readonly AudioSource ambient, foley;
        private readonly AudioClip deflect, impact, swing, step, finish;
        private readonly LineRenderer[] ribbons = new LineRenderer[4];
        private readonly float[] trailLife = new float[4];
        private readonly Vector3[] oldTip = new Vector3[4];
        private readonly AttackPhase[] lastPhase = new AttackPhase[4];
        private float pulse, nextFootstep;
        private readonly float volume;

        internal PassEffects(PassWorld world, Shader shader, int raindrops, float volume)
        {
            this.volume = volume;
            var effects = PassWorld.Group("Generated rain and combat feedback", world.Root.transform);
            ink = new Material(shader) { name = "Transient effects" };
            world.Own(ink);
            lines = new Mesh { name = "Rain streak overlay" };
            lines.MarkDynamic();
            raindrops = Mathf.Clamp(raindrops, 0, 1200);
            rain = new Vector3[raindrops];
            vertices = new Vector3[raindrops * 2];
            colors = new Color32[vertices.Length];
            var indices = new int[vertices.Length];
            for (int index = 0; index < rain.Length; index++)
            {
                rain[index] = new Vector3(world.Range(-12, 12), world.Range(0, 12), world.Range(-9, 18));
                colors[index * 2] = new Color32(142, 184, 209, 22);
                colors[index * 2 + 1] = new Color32(149, 190, 215, 103);
            }
            for (int index = 0; index < indices.Length; index++) indices[index] = index;
            lines.vertices = vertices;
            lines.colors32 = colors;
            lines.SetIndices(indices, MeshTopology.Lines, 0, false);
            lines.bounds = new Bounds(Vector3.zero, Vector3.one * 420);
            var rainObject = PassWorld.Group("Rain field", effects);
            rainObject.gameObject.AddComponent<MeshFilter>().sharedMesh = lines;
            var rainRenderer = rainObject.gameObject.AddComponent<MeshRenderer>();
            rainRenderer.sharedMaterial = ink;
            rainRenderer.shadowCastingMode = ShadowCastingMode.Off;
            rainRenderer.receiveShadows = false;
            world.Own(lines);

            var burst = PassWorld.Group("Contact sparks", effects);
            sparks = burst.gameObject.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.13f, .38f);
            main.startSize = new ParticleSystem.MinMaxCurve(.025f, .095f);
            main.gravityModifier = 1.8f;
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = sparks.emission;
            emission.enabled = false;
            var shape = sparks.shape;
            shape.enabled = false;
            var particleRenderer = burst.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = ink;
            particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            particleRenderer.lengthScale = 2.2f;
            var lighting = PassWorld.Group("Contact light", effects);
            pop = lighting.gameObject.AddComponent<Light>();
            pop.color = new Color(1f, .59f, .19f);
            pop.range = 5;
            pop.intensity = 0;
            for (int index = 0; index < ribbons.Length; index++)
            {
                var ribbon = PassWorld.Group("Sword trace", effects).gameObject.AddComponent<LineRenderer>();
                ribbon.sharedMaterial = ink;
                ribbon.enabled = false;
                ribbon.positionCount = 3;
                ribbon.widthMultiplier = .12f;
                ribbon.numCapVertices = 2;
                ribbon.shadowCastingMode = ShadowCastingMode.Off;
                ribbon.receiveShadows = false;
                ribbons[index] = ribbon;
            }
            ambient = effects.gameObject.AddComponent<AudioSource>();
            foley = effects.gameObject.AddComponent<AudioSource>();
            ambient.playOnAwake = foley.playOnAwake = false;
            ambient.loop = true;
            ambient.volume = volume * .32f;
            foley.volume = volume;
            ambient.clip = Synthesize(world, "Rain and distant wind", 4f, 0, 0, .0f);
            deflect = Synthesize(world, "Metal deflect", .44f, 783, .32f, 9);
            impact = Synthesize(world, "Impact", .28f, 118, .52f, 14);
            swing = Synthesize(world, "Sword pass", .22f, 266, .69f, 20);
            step = Synthesize(world, "Wet foothold", .11f, 76, .86f, 30);
            finish = Synthesize(world, "Finish", .75f, 168, .22f, 6);
            ambient.Play();
        }

        internal void Pause(bool paused)
        {
            if (paused) { sparks.Pause(); ambient.Pause(); foley.Pause(); }
            else { sparks.Play(); ambient.UnPause(); foley.UnPause(); }
        }

        internal void Reset()
        {
            sparks.Clear();
            foley.Stop();
            pulse = 0;
            pop.intensity = 0;
            nextFootstep = 0;
            Array.Clear(lastPhase, 0, lastPhase.Length);
            Array.Clear(trailLife, 0, trailLife.Length);
            foreach (var ribbon in ribbons) ribbon.enabled = false;
        }

        internal void Cue(CombatCue cue, PassSpace space)
        {
            if (cue.Result == ContactResult.Dodge || cue.Result == ContactResult.Miss) return;
            bool bright = cue.Result == ContactResult.Deflect || cue.Result == ContactResult.Finish;
            Vector3 center = Vector3.Lerp(space.Fighter(cue.Attacker).Focus, space.Fighter(cue.Target).Focus, .65f);
            if (!sparks.isPlaying) sparks.Play();
            for (int index = 0; index < (bright ? 26 : 11); index++)
            {
                var direction = UnityEngine.Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) + .23f;
                var emit = new ParticleSystem.EmitParams
                {
                    position = center,
                    velocity = direction * UnityEngine.Random.Range(2f, bright ? 9f : 5.5f),
                    startColor = bright ? new Color32(255, 210, 113, 235) : new Color32(255, 122, 69, 195)
                };
                sparks.Emit(emit, 1);
            }
            pop.transform.position = center;
            pulse = bright ? 1 : .4f;
            if (volume > 0) foley.PlayOneShot(cue.Result == ContactResult.Finish ? finish : bright || cue.Result == ContactResult.Guard ? deflect : impact);
        }

        internal void Advance(CombatRun run, PassSpace space, float delta)
        {
            var center = space.Fighter(0).Root.localPosition;
            for (int index = 0; index < rain.Length; index++)
            {
                rain[index].y -= (9f + index % 6) * delta;
                if (rain[index].y < .02f) rain[index].y += 12;
                var drop = rain[index] + new Vector3(center.x * .2f, 0, center.z);
                drop.x -= (12 - drop.y) * .135f;
                vertices[index * 2] = drop + new Vector3(.095f, .64f, -.07f);
                vertices[index * 2 + 1] = drop;
            }
            lines.vertices = vertices;
            lines.bounds = new Bounds(Vector3.zero, Vector3.one * 420);
            pulse = Mathf.MoveTowards(pulse, 0, delta * 5.1f);
            pop.intensity = pulse * 5f;
            float now = (float)run.Tick / CombatRun.StepsPerSecond;
            for (int index = 0; index < run.Count; index++)
            {
                var phase = run.View(index).Phase;
                var tip = space.Fighter(index).SwordTip;
                var trail = ribbons[index];
                if (phase == AttackPhase.Active)
                {
                    if (lastPhase[index] != phase && volume > 0) foley.PlayOneShot(swing, .43f);
                    trail.SetPosition(0, space.Fighter(index).SwordHeel);
                    trail.SetPosition(1, tip);
                    trail.SetPosition(2, oldTip[index]);
                    trailLife[index] = .12f;
                }
                else trailLife[index] -= delta;
                var tint = index == 0 ? new Color(.5f, .92f, 1f) : new Color(1f, .63f, .39f);
                tint.a = Mathf.Clamp01(trailLife[index] * 6f);
                trail.startColor = tint;
                tint.a = 0;
                trail.endColor = tint;
                trail.enabled = trailLife[index] > 0;
                oldTip[index] = tip;
                lastPhase[index] = phase;
            }
            if (run.View(0).Moving && now >= nextFootstep)
            {
                nextFootstep = now + .32f;
                if (volume > 0) foley.PlayOneShot(step, .46f);
            }
        }

        private static AudioClip Synthesize(PassWorld world, string name, float seconds, float tone, float grit, float decay)
        {
            const int sampleRate = 22050;
            var samples = new float[Mathf.RoundToInt(sampleRate * seconds)];
            var noise = new System.Random(name.Length * 13);
            float filtered = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                float time = (float)index / sampleRate;
                float random = (float)noise.NextDouble() * 2 - 1;
                filtered = Mathf.Lerp(filtered, random, tone == 0 ? .06f : .21f);
                float fade = Mathf.Min(1, time * 280, (seconds - time) * 280);
                if (tone == 0)
                    samples[index] = fade * (.39f * filtered + .075f * random) * (.85f + .15f * Mathf.Sin(time * Mathf.PI));
                else
                {
                    float wave = Mathf.Sin(time * tone * 6.28318f) + .48f * Mathf.Sin(time * tone * 14.1f) + .21f * Mathf.Sin(time * tone * 23.78f);
                    samples[index] = .31f * fade * Mathf.Exp(-time * decay) * (wave * (1 - grit) + filtered * grit * 2.25f);
                }
            }
            var clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            world.Own(clip);
            return clip;
        }
    }
}
