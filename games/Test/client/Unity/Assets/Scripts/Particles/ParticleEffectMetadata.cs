using System;
using UnityEngine;

namespace Test.Client
{
    public enum ParticleEffectCategory
    {
        Burst,
        Environment,
        Nature,
        Magic,
        Fire
    }

    [DisallowMultipleComponent]
    public sealed class ParticleEffectMetadata : MonoBehaviour
    {
        public string displayName;
        public ParticleEffectCategory category;
        [TextArea(2, 5)] public string description;
        public string[] tags = Array.Empty<string>();
        public Texture2D thumbnail;
    }
}
