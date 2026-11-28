using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Test.Application.BBads.Session
{
    public sealed class EnemySpawn
    {
        public EnemySpawn(float time, string unitId, Vector2 position)
        {
            if (!float.IsFinite(time) || time < 0f)
                throw new ArgumentOutOfRangeException(nameof(time));

            Time = time;
            UnitId = unitId;
            Position = position;
        }

        public float Time { get; }
        public string UnitId { get; }
        public Vector2 Position { get; }
    }

    public sealed class ItemSpawn
    {
        public ItemSpawn(float appearTime, string itemId, float placeTime = -1f, int x = -1, int y = -1)
        {
            if (!float.IsFinite(appearTime) || appearTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(appearTime));

            AppearTime = appearTime;
            ItemId = itemId;
            PlaceTime = placeTime;
            X = x;
            Y = y;
        }

        public float AppearTime { get; }
        public string ItemId { get; }
        public float PlaceTime { get; }
        public int X { get; }
        public int Y { get; }
        public bool HasScheduledPlacement => PlaceTime >= AppearTime && X >= 0 && Y >= 0;
    }

    // Use from the owning game loop. Spawn and completion callbacks run synchronously.
    public sealed class BBadsSpawnSchedule : IDisposable
    {
        readonly EnemySpawn[] enemies;
        readonly ItemSpawn[] items;
        int enemyIndex;
        int itemIndex;
        int runVersion;
        bool isDispatching;
        bool disposed;

        public BBadsSpawnSchedule(float duration, IEnumerable<EnemySpawn> enemies, IEnumerable<ItemSpawn> items)
        {
            if (!float.IsFinite(duration) || duration < 0f)
                throw new ArgumentOutOfRangeException(nameof(duration));

            Duration = duration;
            this.enemies = enemies?.Where(spawn => spawn != null).OrderBy(spawn => spawn.Time).ToArray() ??
                           Array.Empty<EnemySpawn>();
            this.items = items?.Where(spawn => spawn != null).OrderBy(spawn => spawn.AppearTime).ToArray() ??
                         Array.Empty<ItemSpawn>();
        }

        public event Action<EnemySpawn> EnemySpawnRequested;
        public event Action<ItemSpawn> ItemSpawnRequested;
        public event Action Completed;

        public float Duration { get; }
        public float Elapsed { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsCompleted { get; private set; }

        public void Play()
        {
            if (!disposed && !IsCompleted)
                IsPlaying = true;
        }

        public void Pause() => IsPlaying = false;

        public void ResetRun()
        {
            if (disposed)
                return;

            runVersion++;
            IsPlaying = false;
            IsCompleted = false;
            Elapsed = 0f;
            enemyIndex = 0;
            itemIndex = 0;
        }

        public void Tick(float deltaTime)
        {
            if (!IsPlaying || isDispatching || deltaTime < 0f || !float.IsFinite(deltaTime))
                return;

            isDispatching = true;
            int version = runVersion;
            try
            {
                Elapsed = Math.Min(float.MaxValue, Elapsed + deltaTime);

                // The original runner drains due enemies before due items each frame.
                while (CanAdvance(version) && enemyIndex < enemies.Length && enemies[enemyIndex].Time <= Elapsed)
                {
                    EnemySpawn spawn = enemies[enemyIndex++];
                    Dispatch(EnemySpawnRequested, spawn, version);
                }

                while (CanAdvance(version) && itemIndex < items.Length && items[itemIndex].AppearTime <= Elapsed)
                {
                    ItemSpawn spawn = items[itemIndex++];
                    Dispatch(ItemSpawnRequested, spawn, version);
                }

                if (CanAdvance(version) && enemyIndex == enemies.Length && itemIndex == items.Length &&
                    Elapsed >= Duration)
                {
                    IsPlaying = false;
                    IsCompleted = true;
                    Action completed = Completed;
                    if (completed != null)
                    {
                        foreach (Action subscriber in completed.GetInvocationList())
                        {
                            if (runVersion != version)
                                break;
                            subscriber();
                        }
                    }
                }
            }
            finally
            {
                isDispatching = false;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            Pause();
            disposed = true;
            runVersion++;
            EnemySpawnRequested = null;
            ItemSpawnRequested = null;
            Completed = null;
        }

        bool CanAdvance(int version) => IsPlaying && runVersion == version;

        void Dispatch<T>(Action<T> callback, T spawn, int version)
        {
            if (callback == null)
                return;

            foreach (Action<T> subscriber in callback.GetInvocationList())
            {
                if (runVersion != version)
                    break;
                subscriber(spawn);
            }
        }
    }
}