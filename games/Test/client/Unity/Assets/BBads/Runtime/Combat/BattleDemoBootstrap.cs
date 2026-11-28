using BBads.Backpack;
using BBads.Combat.Behavior;
using BBads.Data;
using BBads.Timeline;
using BBads.UI;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using BBads.Combat.Diagnostics;
#endif

namespace BBads.Combat
{
    public sealed class BattleDemoBootstrap : MonoBehaviour
    {
        [SerializeField] bool autoPlaceItems = true;
        [SerializeField] bool autoMergeItems = true;
        [SerializeField] bool useImprovedEngagementAvoidance = true;
        [SerializeField] Rect battleBounds = new Rect(-2.5f, -1.25f, 5f, 2.5f);
        [SerializeField] PlayerBase playerBase;

        [Header("战斗行为模式（运行时可用 F1 面板切换）")]
        [SerializeField] CombatBehaviorMode globalBehaviorMode = CombatBehaviorLibrary.DefaultMode;
        [SerializeField] bool overrideBehaviorPerCamp;
        [SerializeField] CombatBehaviorMode playerBehaviorMode = CombatBehaviorLibrary.DefaultMode;
        [SerializeField] CombatBehaviorMode enemyBehaviorMode = CombatBehaviorLibrary.DefaultMode;
        [SerializeField] bool spawnBehaviorDebugTools = true;

        CombatWorld runtimeWorld;
        bool lastPushedAvoidance;

        void Awake()
        {
            Transform existing = transform.Find("BattleRuntime");
            if (existing != null)
                return;
            playerBase ??= GetComponentInChildren<PlayerBase>(true);
            if (playerBase == null)
            {
                Debug.LogError("SampleScene requires a prebuilt PlayerBase child.", this);
                return;
            }

            GameObject runtime = new GameObject("BattleRuntime");
            runtime.transform.SetParent(transform, false);
            runtime.SetActive(false);
            var clock = runtime.AddComponent<BBadsBattleClock>();

            Transform actorRoot = new GameObject("Actors").transform;
            actorRoot.SetParent(runtime.transform, false);

            CombatWorld world = runtime.AddComponent<CombatWorld>();
            runtimeWorld = world;
            CombatArena arena = runtime.AddComponent<CombatArena>();
            CombatSpawner spawner = runtime.AddComponent<CombatSpawner>();
            ScenarioRunner runner = runtime.AddComponent<ScenarioRunner>();
            BackpackController backpack = runtime.AddComponent<BackpackController>();
            BackpackPresenter backpackPresenter = FindInBattleScene<BackpackPresenter>();
            CombatPresentationPool presentationPool = GetComponent<CombatPresentationPool>();
            TimeWavePresenter wavePresenter = FindInBattleScene<TimeWavePresenter>();
            ScenarioCombatBridge combatBridge = runtime.AddComponent<ScenarioCombatBridge>();
            BackpackSummonBridge summonBridge = runtime.AddComponent<BackpackSummonBridge>();
            CsvRuntimeData csv = runtime.AddComponent<CsvRuntimeData>();

            world.SetPlayerBase(playerBase);
            world.SetClock(clock);
            world.SetImprovedEngagementAvoidance(useImprovedEngagementAvoidance);
            lastPushedAvoidance = useImprovedEngagementAvoidance;
            if (overrideBehaviorPerCamp)
            {
                world.SetCampMode(UnitCamp.Player, playerBehaviorMode);
                world.SetCampMode(UnitCamp.Enemy, enemyBehaviorMode);
            }
            else
            {
                world.SetGlobalMode(globalBehaviorMode);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (spawnBehaviorDebugTools)
            {
                runtime.AddComponent<CombatBehaviorPanel>().Configure(world, clock);
                runtime.AddComponent<CombatBehaviorGizmos>().Configure(world);
            }
#endif
            spawner.Configure(
                world,
                arena,
                actorRoot,
                null,
                presentationPool);
            if (presentationPool == null)
                Debug.LogError("SampleScene requires a prebuilt CombatPresentationPool.", this);
            else
                presentationPool.SetClock(clock);
            runner.SetClock(clock);
            backpack.SetClock(clock);
            if (wavePresenter != null)
                wavePresenter.SetScenarioRunner(runner);
            backpack.SetAutoPlaceItems(autoPlaceItems);
            backpack.SetAutoMergeItems(autoMergeItems);
            backpack.ConfigureScenarioRunner(runner);
            if (backpackPresenter != null)
                backpackPresenter.Configure(backpack);
            else
                Debug.LogError("SampleScene requires a prebuilt BackpackPresenter.", this);
            combatBridge.Configure(runner, spawner, arena, playerBase);
            combatBridge.ConfigureLayout(battleBounds);
            summonBridge.Configure(backpack, spawner);
            csv.Configure(spawner, backpack, runner);

            runtime.SetActive(true);
            if (backpackPresenter != null)
                backpackPresenter.SetUnitCatalog(csv.Units);
        }

        void Update()
        {
            if (runtimeWorld == null)
                return;
            // 双向同步：Inspector 改了就推给世界；调试面板改了世界就同步回 Inspector，
            // 否则每帧推送会把面板的修改覆盖掉。
            if (useImprovedEngagementAvoidance != lastPushedAvoidance)
            {
                lastPushedAvoidance = useImprovedEngagementAvoidance;
                runtimeWorld.SetImprovedEngagementAvoidance(useImprovedEngagementAvoidance);
            }
            else if (runtimeWorld.UseImprovedEngagementAvoidance != useImprovedEngagementAvoidance)
            {
                useImprovedEngagementAvoidance = runtimeWorld.UseImprovedEngagementAvoidance;
                lastPushedAvoidance = useImprovedEngagementAvoidance;
            }
        }

        T FindInBattleScene<T>() where T : Component
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(battleBounds.center, battleBounds.size);
        }
    }
}
