using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("Test.Client.EditMode.Tests")]

namespace Test.Client.Editor
{
    internal sealed class EffectPreview : System.IDisposable
    {
        private readonly PreviewRenderUtility renderer = new PreviewRenderUtility();
        private GameObject instance;
        private ParticleSystem[] particles;
        private ProceduralParticleEffect procedural;
        private LightningChainEffect lightning;

        internal float Duration { get; private set; }
        internal float Time { get; private set; }

        internal EffectPreview()
        {
            var camera = renderer.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.065f, 0.085f, 0.12f);
            camera.transform.position = new Vector3(0, 0, -12);
            camera.orthographic = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 32f;
        }

        internal void Reset(GameObject asset)
        {
            if (instance != null) Object.DestroyImmediate(instance);
            instance = renderer.InstantiatePrefabInScene(asset);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            procedural = instance.GetComponent<ProceduralParticleEffect>();
            lightning = instance.GetComponent<LightningChainEffect>();
            particles = instance.GetComponentsInChildren<ParticleSystem>();
            Time = 0f;
            Duration = procedural != null ? procedural.BeginPreview() : lightning != null ? lightning.Duration : 7f;
            for (var index = 0; index < particles.Length; index++)
            {
                var emitter = particles[index];
                if (emitter.useAutoRandomSeed) emitter.randomSeed = (uint)(817 + index);
                emitter.Simulate(0f, false, true, false);
            }
            if (lightning != null) lightning.Sample(0f);
        }

        internal void Advance(float target)
        {
            while (Time < target)
            {
                var next = Mathf.Min(Time + 1f / 30f, target);
                var step = next - Time;
                foreach (var emitter in particles) emitter.Simulate(step, false, false, false);
                if (procedural != null) procedural.StepPreview(step);
                Time = next;
            }
            if (lightning != null) lightning.Sample(target);
        }

        internal Texture Render(Rect bounds)
        {
            renderer.BeginPreview(bounds, GUIStyle.none);
            renderer.camera.orthographicSize = Mathf.Max(2.8f, 3.65f * bounds.height / bounds.width);
            renderer.camera.Render();
            return renderer.EndPreview();
        }

        public void Dispose() => renderer.Cleanup();
    }
}
