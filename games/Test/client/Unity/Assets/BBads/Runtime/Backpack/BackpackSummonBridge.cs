using BBads.Combat;
using UnityEngine;

namespace BBads.Backpack
{
    public sealed class BackpackSummonBridge : MonoBehaviour
    {
        [SerializeField] BackpackController backpack;
        [SerializeField] CombatSpawner combatSpawner;

        public void Configure(BackpackController valueBackpack, CombatSpawner valueSpawner)
        {
            if (backpack != null)
                backpack.SummonRequested -= Spawn;
            backpack = valueBackpack;
            combatSpawner = valueSpawner;
            if (isActiveAndEnabled && backpack != null)
                backpack.SummonRequested += Spawn;
        }

        void OnEnable()
        {
            if (backpack != null)
                backpack.SummonRequested += Spawn;
        }

        void OnDisable()
        {
            if (backpack != null)
                backpack.SummonRequested -= Spawn;
        }

        void Spawn(string unitId, Vector2 position) => combatSpawner?.Spawn(unitId, position);
    }
}
