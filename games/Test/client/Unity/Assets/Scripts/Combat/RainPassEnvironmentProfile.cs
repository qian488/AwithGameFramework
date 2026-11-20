using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Test.Client.Combat
{
    public enum RainPassEnvironmentMode
    {
        RuntimeGenerated,
        BakedAsset
    }

    [Serializable]
    public sealed class RainPassEnvironmentParameters
    {
        public int seed = 27041;
        [Range(0, 120)] public int detailDensity = 65;
        public Color moonlight = new Color(.66f, .79f, .94f);
        public Color fogColor = new Color(.115f, .15f, .187f);

        public RainPassEnvironmentParameters Clone()
        {
            return new RainPassEnvironmentParameters
            {
                seed = seed,
                detailDensity = detailDensity,
                moonlight = moonlight,
                fogColor = fogColor
            };
        }

        public string ComputeInputHash(string generatorVersion)
        {
            var payload = generatorVersion + "|" + JsonUtility.ToJson(this);
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(payload)))
                    .Replace("-", "").ToLowerInvariant();
        }
    }

    [CreateAssetMenu(menuName = "Test/Rain Pass/Environment Profile", fileName = "RainPassEnvironment")]
    public sealed class RainPassEnvironmentProfile : ScriptableObject
    {
        public const string GeneratorId = "test.rain-pass.environment";
        public const string GeneratorVersion = "1";

        [SerializeField] private RainPassEnvironmentParameters parameters = new RainPassEnvironmentParameters();
        [SerializeField] private GameObject bakedPrefab;
        [SerializeField] private string bakedInputHash;
        [SerializeField] private string bakedGeneratorVersion;
        [SerializeField] private string manifestPath;
        [SerializeField] private string[] generatedAssetPaths = new string[0];

        public RainPassEnvironmentParameters Parameters
        {
            get
            {
                if (parameters == null) parameters = new RainPassEnvironmentParameters();
                return parameters;
            }
        }

        public GameObject BakedPrefab => bakedPrefab;
        public string BakedInputHash => bakedInputHash;
        public string ManifestPath => manifestPath;
        public string[] GeneratedAssetPaths => generatedAssetPaths ?? new string[0];
        public string CurrentInputHash => Parameters.ComputeInputHash(GeneratorVersion);
        public bool HasCurrentBakedAsset => bakedPrefab != null && bakedInputHash == CurrentInputHash &&
            bakedGeneratorVersion == GeneratorVersion;

        public void CopyParametersFrom(RainPassEnvironmentParameters source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            parameters = source.Clone();
        }

        public void SetBakeResult(GameObject prefab, string inputHash, string manifest, string[] outputs)
        {
            bakedPrefab = prefab;
            bakedInputHash = inputHash;
            bakedGeneratorVersion = GeneratorVersion;
            manifestPath = manifest;
            generatedAssetPaths = outputs ?? new string[0];
        }

        public void ClearBakeResult()
        {
            bakedPrefab = null;
            bakedInputHash = null;
            bakedGeneratorVersion = null;
            manifestPath = null;
            generatedAssetPaths = new string[0];
        }
    }

}
