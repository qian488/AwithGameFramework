using BBads.Data;
using BBads.Timeline;
using UnityEngine;

namespace BBads.Combat
{
    public sealed class ScenarioCombatBridge : MonoBehaviour
    {
        [SerializeField] ScenarioRunner runner;
        [SerializeField] CombatSpawner spawner;
        [SerializeField] CombatArena arena;
        [SerializeField] PlayerBase playerBase;
        bool hasLayoutOverride;
        Rect layoutBounds;

        public void ConfigureLayout(Rect bounds)
        {
            hasLayoutOverride = true;
            layoutBounds = bounds;
            if (isActiveAndEnabled)
                ApplyScenario();
        }

        public void Configure(ScenarioRunner valueRunner, CombatSpawner valueSpawner, CombatArena valueArena, PlayerBase valueBase)
        {
            if (runner != null)
                runner.EnemySpawnRequested -= SpawnEnemy;
            if (playerBase != null)
                playerBase.Destroyed -= HandleBaseDestroyed;
            runner = valueRunner;
            spawner = valueSpawner;
            arena = valueArena;
            playerBase = valueBase;
            if (isActiveAndEnabled && runner != null)
            {
                runner.EnemySpawnRequested += SpawnEnemy;
                playerBase.Destroyed += HandleBaseDestroyed;
                ApplyScenario();
            }
        }

        void OnEnable()
        {
            if (runner != null)
            {
                runner.EnemySpawnRequested += SpawnEnemy;
            }
            if (playerBase != null)
                playerBase.Destroyed += HandleBaseDestroyed;
            ApplyScenario();
        }

        void Start() => ApplyScenario();

        void OnDisable()
        {
            if (runner != null)
            {
                runner.EnemySpawnRequested -= SpawnEnemy;
            }
            if (playerBase != null)
                playerBase.Destroyed -= HandleBaseDestroyed;
        }

        void ApplyScenario()
        {
            ScenarioDefinition scenario = runner != null ? runner.Scenario : null;
            if (scenario == null)
                return;
            arena?.SetBounds(hasLayoutOverride ? layoutBounds : scenario.BattleBounds);
            playerBase?.ResetHealth();
        }

        void SpawnEnemy(EnemySpawnEvent spawnEvent)
        {
            if (spawnEvent != null)
                spawner?.Spawn(spawnEvent.unitId, spawnEvent.position);
        }

        void HandleBaseDestroyed()
        {
            runner?.Pause();
        }
    }
}
