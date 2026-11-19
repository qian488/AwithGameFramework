using System;
using System.Collections.Generic;
using Test.Domain.Mathematics;
using UnityEngine;
using UnityApplication = UnityEngine.Application;

namespace Test.Client
{
    [DisallowMultipleComponent]
    public sealed class ProceduralParticleEffect : MonoBehaviour
    {
        private enum EmitterShape
        {
            Point, Ring, Sphere, Cone,
            [InspectorName("Spiral Formula (Legacy)")] SpiralFormula,
            [InspectorName("Projectile Formula (Legacy)")] ProjectileFormula,
            Formula
        }

        [Serializable]
        private sealed class Layer
        {
            public string label = "Layer";
            public bool enabled = true;
            public EmitterShape shape;
            [Tooltip("Assign a reusable formula asset when Shape is Formula.")]
            public ParticleLaunchProfile launchProfile;
            public Vector3 offset;
            public Vector3 rotation;
            [Min(0.01f)] public float radius = 0.15f;
            [Range(0, 85)] public float coneAngle = 25f;
            [Tooltip("Spiral Formula: number of turns from the center to Radius.")]
            [Range(0.25f, 8f)] public float spiralTurns = 2f;
            [Tooltip("Projectile Formula: center angle above local +X, in the local XY plane.")]
            [Range(-180f, 180f)] public float launchAngle = 55f;
            [Tooltip("Projectile Formula: angle range centered around Launch Angle.")]
            [Range(0f, 180f)] public float launchSpread = 32f;
            [Min(0)] public float delay;
            [Range(1, 512)] public int count = 32;
            [Min(0.05f)] public float lifetime = 1.2f;
            [Min(0)] public float speed = 2f;
            [Min(0.01f)] public float size = 0.22f;
            [Tooltip("Multiplier of the Unity project's gravity vector.")]
            [Range(-2, 2)] public float gravity;
            public Color color = Color.white;
            public Color fadeColor = new Color(0.15f, 0.7f, 1f);
        }

        private sealed class FormulaBurst
        {
            public ParticleSystem Emitter { get; }
            public ILaunchFormula2D Formula { get; }
            public float Speed { get; }
            public float Delay { get; }
            public float Remaining { get; set; }
            public bool Pending { get; set; }

            public FormulaBurst(ParticleSystem emitter, ILaunchFormula2D formula, float speed, float delay)
            {
                Emitter = emitter;
                Formula = formula;
                Speed = speed;
                Delay = delay;
                Reset();
            }

            public void Reset()
            {
                Remaining = Delay;
                Pending = true;
            }
        }

        [SerializeField] private Material material;
        [SerializeField, Tooltip("Play once when enabled in Play Mode. Leave off if a host triggers playback.")]
        private bool playOnEnable;
        [SerializeField] private int seed = 817;
        [SerializeField] private List<Layer> layers = new List<Layer>();

        private readonly List<ParticleSystem> emitters = new List<ParticleSystem>();
        private readonly List<FormulaBurst> formulaBursts = new List<FormulaBurst>();
        private GameObject generated;

        public void Replay()
        {
            if (!isActiveAndEnabled || !UnityApplication.isPlaying) return;
            if (generated == null) BuildEmitters();

            foreach (var emitter in emitters)
            {
                emitter.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                emitter.Play(false);
            }

            foreach (var burst in formulaBursts)
            {
                burst.Emitter.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                burst.Reset();
                if (burst.Delay <= 0f) EmitFormula(burst, true);
            }
        }

        public float BeginPreview()
        {
            if (!isActiveAndEnabled || UnityApplication.isPlaying) return 0f;
            ReleaseEmitters();
            return BuildEmitters();
        }

        public void StepPreview(float seconds)
        {
            if (UnityApplication.isPlaying) return;
            foreach (var emitter in emitters)
                emitter.Simulate(seconds, false, false, false);

            foreach (var burst in formulaBursts)
            {
                if (burst.Pending)
                {
                    var wait = Mathf.Min(seconds, burst.Remaining);
                    burst.Remaining -= wait;
                    if (burst.Remaining > 0f) continue;
                    EmitFormula(burst, false);
                    burst.Emitter.Simulate(seconds - wait, false, false, false);
                }
                else burst.Emitter.Simulate(seconds, false, false, false);
            }
        }

        public void EndPreview()
        {
            if (!UnityApplication.isPlaying) ReleaseEmitters();
        }

        private float BuildEmitters()
        {
            if (material == null)
            {
                Debug.LogWarning("Assign a particle material before playing this effect.", this);
                return 0f;
            }

            generated = CreateTransientObject("Generated particles", transform);
            var length = 0f;
            for (var index = 0; index < layers.Count; index++)
            {
                var layer = layers[index];
                if (layer == null || !layer.enabled) continue;
                var delay = Mathf.Max(0f, layer.delay);
                var lifetime = Mathf.Max(0.05f, layer.lifetime);
                var formula = ResolveFormula(layer);
                if (layer.shape == EmitterShape.Formula && formula == null)
                {
                    Debug.LogWarning($"Assign a launch profile to formula layer '{layer.label}'.", this);
                    continue;
                }

                var emitter = CreateEmitter(layer, index, delay, lifetime, formula != null);
                if (formula != null) formulaBursts.Add(new FormulaBurst(emitter, formula, Mathf.Max(0f, layer.speed), delay));
                else emitters.Add(emitter);
                length = Mathf.Max(length, delay + lifetime);
            }

            if (emitters.Count == 0 && formulaBursts.Count == 0) ReleaseEmitters();
            return length;
        }

        private static ILaunchFormula2D ResolveFormula(Layer layer) => layer.shape switch
        {
            EmitterShape.Formula => layer.launchProfile != null ? layer.launchProfile.CreateFormula() : null,
            EmitterShape.SpiralFormula => new SpiralFormula2D(Mathf.Max(0.01f, layer.radius), layer.spiralTurns),
            EmitterShape.ProjectileFormula => new ProjectileFormula2D(layer.launchAngle, layer.launchSpread),
            _ => null
        };

        private ParticleSystem CreateEmitter(Layer layer, int index, float delay, float lifetime, bool formula)
        {
            var node = CreateTransientObject(layer.label, generated.transform);
            node.SetActive(false);
            node.transform.localPosition = layer.offset;
            node.transform.localRotation = Quaternion.Euler(layer.rotation);
            var emitter = node.AddComponent<ParticleSystem>();
            emitter.useAutoRandomSeed = false;
            emitter.randomSeed = unchecked((uint)(seed + index * 7919)) | 1u;

            var count = Mathf.Clamp(layer.count, 1, 512);
            var speed = Mathf.Max(0f, layer.speed);
            var size = Mathf.Max(0.01f, layer.size);
            var main = emitter.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = lifetime;
            main.startDelay = formula ? 0f : delay;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.65f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.68f, speed * 1.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.65f, size * 1.2f);
            main.gravityModifier = layer.gravity;
            main.maxParticles = count;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = emitter.emission;
            emission.rateOverTime = 0;
            emission.enabled = !formula;
            if (!formula)
            {
                emission.burstCount = 1;
                emission.SetBurst(0, new ParticleSystem.Burst(0, (short)count));
            }

            var shape = emitter.shape;
            shape.enabled = !formula && layer.shape != EmitterShape.Point;
            shape.radius = Mathf.Max(0.01f, layer.radius);
            switch (layer.shape)
            {
                case EmitterShape.Ring:
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radiusThickness = 0;
                    shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
                    break;
                case EmitterShape.Sphere:
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    break;
                case EmitterShape.Cone:
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = layer.coneAngle;
                    break;
            }

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(layer.color, 0), new GradientColorKey(layer.fadeColor, 1) },
                new[] { new GradientAlphaKey(layer.color.a, 0), new GradientAlphaKey(layer.color.a * 0.65f, 0.45f), new GradientAlphaKey(layer.fadeColor.a, 1) });
            var color = emitter.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var scale = emitter.sizeOverLifetime;
            scale.enabled = true;
            scale.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.15f));

            var renderer = node.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            node.SetActive(true);
            return emitter;
        }

        private static void EmitFormula(FormulaBurst burst, bool play)
        {
            burst.Pending = false;
            var count = burst.Emitter.main.maxParticles;
            if (play) burst.Emitter.Play(false);
            else burst.Emitter.Simulate(0f, false, true, false);

            for (var index = 0; index < count; index++)
            {
                var fraction = count == 1 ? 0.5f : index / (float)(count - 1);
                var launch = burst.Formula.Sample(fraction, burst.Speed);
                burst.Emitter.Emit(new ParticleSystem.EmitParams
                {
                    position = new Vector3(launch.Position.X, launch.Position.Y, 0f),
                    velocity = new Vector3(launch.Velocity.X, launch.Velocity.Y, 0f)
                }, 1);
            }
        }

        private void OnEnable()
        {
            if (playOnEnable) Replay();
        }

        private void Update()
        {
            foreach (var burst in formulaBursts)
            {
                if (!burst.Pending) continue;
                burst.Remaining -= Time.deltaTime;
                if (burst.Remaining <= 0f) EmitFormula(burst, true);
            }
        }

        private static GameObject CreateTransientObject(string label, Transform parent)
        {
            var node = new GameObject(label) { hideFlags = HideFlags.DontSave };
            node.transform.SetParent(parent, false);
            return node;
        }

        private void OnDisable() => ReleaseEmitters();

        private void ReleaseEmitters()
        {
            emitters.Clear();
            formulaBursts.Clear();
            if (generated == null) return;
            generated.SetActive(false);
            if (UnityApplication.isPlaying) Destroy(generated);
            else DestroyImmediate(generated);
            generated = null;
        }
    }
}
