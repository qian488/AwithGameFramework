using Test.Domain.Mathematics;
using UnityEngine;
using NumericsVector = System.Numerics.Vector3;

namespace Test.Client
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class LightningChainEffect : TestEffectBehaviour
    {
        private static readonly int Intensity = Shader.PropertyToID("_Intensity");

        [SerializeField] private Transform[] anchors;
        [SerializeField, Range(2, 64)] private int segmentsPerLink = 7;
        [SerializeField, Min(0)] private float jitter = 0.23f;
        [SerializeField, Min(0.1f)] private float duration = 0.65f;
        [SerializeField, Min(1)] private float flashesPerSecond = 14f;
        [SerializeField] private bool playOnEnable = true;
        private float elapsed;
        private int lastFlash = -1;
        private bool playing;
        private MaterialPropertyBlock flashProperties;

        public int AnchorCount => anchors?.Length ?? 0;
        public float Duration => Mathf.Max(0.1f, duration);

        public bool HasValidAnchors()
        {
            if (AnchorCount < 2) return false;
            foreach (var anchor in anchors)
                if (anchor == null) return false;
            return true;
        }

        public override void Replay()
        {
            if (!isActiveAndEnabled || !UnityEngine.Application.isPlaying || !HasValidAnchors()) return;
            elapsed = 0f;
            lastFlash = -1;
            playing = true;
            Sample(0f);
        }

        public void Sample(float time)
        {
            var line = GetComponent<LineRenderer>();
            line.enabled = HasValidAnchors() && time < Duration;
            if (!line.enabled) return;

            var phase = time * Mathf.Max(1f, flashesPerSecond);
            var flash = Mathf.FloorToInt(phase);
            flashProperties ??= new MaterialPropertyBlock();
            flashProperties.SetFloat(Intensity, Mathf.Lerp(1f, 0.28f, phase - flash) * Mathf.Lerp(1f, 0.4f, time / Duration));
            line.SetPropertyBlock(flashProperties);
            if (flash == lastFlash) return;
            lastFlash = flash;
            var segments = Mathf.Clamp(segmentsPerLink, 2, 64);
            line.positionCount = (anchors.Length - 1) * segments + 1;
            for (var link = 0; link < anchors.Length - 1; link++)
            {
                var from = transform.InverseTransformPoint(anchors[link].position);
                var to = transform.InverseTransformPoint(anchors[link + 1].position);
                var points = LightningPath.Build(new NumericsVector(from.x, from.y, from.z),
                    new NumericsVector(to.x, to.y, to.z), segments, Mathf.Max(0f, jitter), unchecked(917 + 103 * flash + link));
                for (var index = 0; index < points.Length; index++)
                {
                    var point = points[index];
                    line.SetPosition(link * segments + index, new Vector3(point.X, point.Y, point.Z));
                }
            }
        }

        private void OnEnable()
        {
            if (playOnEnable) Replay();
        }

        private void Update()
        {
            if (!playing) return;
            elapsed += Time.deltaTime;
            Sample(elapsed);
            playing = elapsed < Duration;
        }

        private void OnDisable()
        {
            playing = false;
            if (UnityEngine.Application.isPlaying) GetComponent<LineRenderer>().enabled = false;
        }
    }
}
