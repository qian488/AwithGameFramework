using UnityEngine;

namespace Test.Client.Combat
{
    [DisallowMultipleComponent]
    public sealed class RainPassGeneratedAssetMarker : MonoBehaviour
    {
        [SerializeField] private string generatorId;
        [SerializeField] private string generatorVersion;
        [SerializeField] private string inputHash;

        public string GeneratorId => generatorId;
        public string GeneratorVersion => generatorVersion;
        public string InputHash => inputHash;

        public void Set(string id, string version, string hash)
        {
            generatorId = id;
            generatorVersion = version;
            inputHash = hash;
        }
    }
}