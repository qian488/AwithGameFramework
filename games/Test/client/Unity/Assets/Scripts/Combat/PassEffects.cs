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
        private readonly LineRenderer[] ribbons = new LineRenderer[4];
        private readonly float[] trailLife = new float[4];
        private readonly Vector3[] oldTip = new Vector3[4];
        private float pulse;

        internal PassEffects(PassWorld world, Shader shader, int raindrops)
        {
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
        }

        internal void Pause(bool paused)
        {
            if (paused) sparks.Pause();
            else sparks.Play();
        }

        internal void Reset()
        {
            sparks.Clear();
            pulse = 0;
            pop.intensity = 0;
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
            for (int index = 0; index < run.Count; index++)
            {
                var phase = run.View(index).Phase;
                var tip = space.Fighter(index).SwordTip;
                var trail = ribbons[index];
                bool bow = index == 0 && run.Weapon == PlayerWeapon.Bow;
                if (bow) trailLife[index] = 0;
                else if (phase == AttackPhase.Active)
                {
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
            }
        }
    }
}
