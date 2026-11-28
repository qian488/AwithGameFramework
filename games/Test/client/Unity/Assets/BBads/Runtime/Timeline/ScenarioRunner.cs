using System;
using System.Linq;
using BBads.Combat;
using BBads.Data;
using Test.Application.BBads.Session;
using UnityEngine;
using Point = System.Numerics.Vector2;

namespace BBads.Timeline
{
    public sealed class ScenarioRunner : MonoBehaviour
    {
        [SerializeField] ScenarioDefinition scenario;
        [SerializeField] bool playOnStart = true;
        [SerializeField] bool useUnscaledTime;

        BBadsSpawnSchedule schedule;
        BBadsBattleClock clock;

        public event Action<EnemySpawnEvent> EnemySpawnRequested;
        public event Action<ItemSpawnEvent> ItemSpawnRequested;
        public event Action Completed;

        public ScenarioDefinition Scenario => scenario;
        public float Elapsed => schedule?.Elapsed ?? 0f;
        public bool IsPlaying => schedule?.IsPlaying ?? false;

        void Start()
        {
            if (playOnStart && scenario != null)
                Play();
        }

        internal void SetClock(BBadsBattleClock value) => clock = value;

        void Update()
        {
            float delta = useUnscaledTime
                ? clock != null ? clock.UnscaledDelta : Time.unscaledDeltaTime
                : clock != null ? clock.Delta : Time.deltaTime;
            if (delta > 0f)
                Tick(delta);
        }

        public void SetScenario(ScenarioDefinition value)
        {
            schedule?.Dispose();
            schedule = null;
            scenario = value;
        }

        public void Play()
        {
            if (scenario == null)
            {
                Debug.LogError("ScenarioRunner requires a ScenarioDefinition.", this);
                return;
            }

            if (schedule == null)
                CreateSchedule();
            schedule.Play();
        }

        void CreateSchedule()
        {
            schedule = new BBadsSpawnSchedule(scenario.Duration,
                scenario.EnemySpawns.Where(spawn => spawn != null).Select(spawn =>
                    new EnemySpawn(spawn.time, spawn.unitId, new Point(spawn.position.x, spawn.position.y))),
                scenario.ItemSpawns.Where(spawn => spawn != null).Select(spawn =>
                    new ItemSpawn(spawn.appearTime, spawn.itemId, spawn.placeTime, spawn.x, spawn.y)));
            schedule.EnemySpawnRequested += spawn => EnemySpawnRequested?.Invoke(new EnemySpawnEvent
            {
                time = spawn.Time, unitId = spawn.UnitId, position = new Vector2(spawn.Position.X, spawn.Position.Y)
            });
            schedule.ItemSpawnRequested += spawn => ItemSpawnRequested?.Invoke(new ItemSpawnEvent
            {
                appearTime = spawn.AppearTime, itemId = spawn.ItemId, placeTime = spawn.PlaceTime,
                x = spawn.X, y = spawn.Y
            });
            schedule.Completed += () => Completed?.Invoke();
        }

        public void Pause() => schedule?.Pause();
        public void ResetRun() => schedule?.ResetRun();
        public void Tick(float deltaTime) => schedule?.Tick(deltaTime);

        void OnDestroy() => schedule?.Dispose();
    }
}
