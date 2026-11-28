using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Unity.Hosting;
using BBads.Data;
using BBads.Infrastructure;
using UnityEngine;

namespace BBads.Combat
{
    public sealed class CombatSpawner : MonoBehaviour
    {
        [SerializeField] UnitCatalog unitCatalog;
        [SerializeField] CombatWorld world;
        [SerializeField] CombatArena arena;
        [SerializeField] Transform actorRoot;
        [SerializeField] GameObject fallbackActorPrefab;
        [SerializeField] CombatPresentationPool presentationPool;

        readonly Dictionary<GameObject, PrefabPool> pools = new Dictionary<GameObject, PrefabPool>();
        readonly Dictionary<CombatActor, PrefabPool> actorPools = new Dictionary<CombatActor, PrefabPool>();
        readonly Queue<(UnitDefinition Definition, Vector2 Position)> pendingSpawns = new Queue<(UnitDefinition, Vector2)>();
        IResourceLoader resourceLoader;
        ResourceStore resources;
        ResourceScope resourceScope;
        CancellationTokenSource spawnLifetime;
        GameObject unitPrefab;
        Transform poolRoot;
        bool loadingPrefab;
        int peakUnits;
        (int Created, int Rents, int Returns, int Active, int Idle, int Peak)? finalPoolCounts;

        public (int Created, int Rents, int Returns, int Active, int Idle, int Peak) UnitPoolCounts
        {
            get
            {
                if (finalPoolCounts.HasValue)
                    return finalPoolCounts.Value;

                var total = (Created: 0, Rents: 0, Returns: 0, Active: 0, Idle: 0, Peak: peakUnits);
                foreach (var pool in pools.Values)
                {
                    var counts = pool.Counts;
                    total.Created += counts.Created;
                    total.Rents += counts.Rents;
                    total.Returns += counts.Returns;
                    total.Active += counts.Active;
                    total.Idle += counts.Idle;
                }
                return total;
            }
        }

        GameObject SpawnPrefab => fallbackActorPrefab != null ? fallbackActorPrefab : unitPrefab;
        bool CanSpawn => isActiveAndEnabled && (world == null || world.Clock == null || !world.Clock.IsPaused);

        public void SetUnitCatalog(UnitCatalog value) => unitCatalog = value;

        public void Configure(
            CombatWorld valueWorld,
            CombatArena valueArena,
            Transform valueActorRoot,
            GameObject valueFallbackPrefab,
            CombatPresentationPool valuePresentationPool = null,
            IResourceLoader valueResourceLoader = null)
        {
            world = valueWorld;
            arena = valueArena;
            actorRoot = valueActorRoot;
            fallbackActorPrefab = valueFallbackPrefab;
            presentationPool = valuePresentationPool;
            resourceLoader = valueResourceLoader;
        }

        void OnEnable()
        {
            if (fallbackActorPrefab == null)
                StartLoadingUnitPrefab();
        }

        void Update()
        {
            if (pendingSpawns.Count > 0 && CanSpawn && SpawnPrefab != null)
                FlushPendingSpawns(SpawnPrefab);
        }

        // The battle's spawn events can arrive while the default prefab is being prepared.
        // In that case the request is queued and the instance is created once the load completes.
        public CombatActor Spawn(string unitId, Vector2 position)
        {
            if (unitCatalog == null || !unitCatalog.TryGet(unitId, out UnitDefinition definition))
            {
                Debug.LogError($"Unknown unit id '{unitId}'.", this);
                return null;
            }

            GameObject prefab = SpawnPrefab;
            if (prefab != null && CanSpawn)
            {
                FlushPendingSpawns(prefab);
                return SpawnActor(prefab, definition, position);
            }

            pendingSpawns.Enqueue((definition, position));
            if (prefab == null)
                StartLoadingUnitPrefab();
            return null;
        }

        void FlushPendingSpawns(GameObject prefab)
        {
            while (pendingSpawns.Count > 0)
            {
                var request = pendingSpawns.Dequeue();
                SpawnActor(prefab, request.Definition, request.Position);
            }
        }

        void StartLoadingUnitPrefab()
        {
            if (loadingPrefab || unitPrefab != null)
                return;

            // Unity lifecycle callbacks and the captured Unity synchronization context own
            // both sides of the Resources request and the eventual native release.
            resources ??= new ResourceStore(resourceLoader ?? new UnityResourceLoader());
            resourceScope ??= new ResourceScope(resources);
            spawnLifetime ??= new CancellationTokenSource();
            loadingPrefab = true;
            _ = LoadUnitPrefabAsync(spawnLifetime.Token);
        }

        async Task LoadUnitPrefabAsync(CancellationToken cancellationToken)
        {
            try
            {
                var lease = await resourceScope.LoadAsync<GameObject>(BBadsResource.UnitPrefab, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                unitPrefab = lease.Value;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                pendingSpawns.Clear();
                Debug.LogException(exception, this);
            }
            finally { loadingPrefab = false; }
        }

        CombatActor SpawnActor(GameObject prefab, UnitDefinition definition, Vector2 position)
        {
            if (!pools.TryGetValue(prefab, out PrefabPool pool))
            {
                if (poolRoot == null)
                {
                    poolRoot = new GameObject("Unit Pool").transform;
                    poolRoot.SetParent(actorRoot != null ? actorRoot : transform, false);
                }
                pool = new PrefabPool(prefab, poolRoot);
                pools.Add(prefab, pool);
            }

            GameObject instance = pool.Get();
            float spawnRadius = world != null && world.UseImprovedEngagementAvoidance
                ? definition.bodyRadius
                : definition.attackSize;
            Vector2 spawnPosition = world != null
                ? world.ResolveSpawnPosition(definition.camp, spawnRadius, position, arena)
                : position;
            spawnPosition = arena != null ? arena.Clamp(spawnPosition) : spawnPosition;
            instance.transform.position = new Vector3(spawnPosition.x, spawnPosition.y, instance.transform.position.z);
            CombatActor actor = instance.GetComponent<CombatActor>();
            if (actor == null)
                actor = instance.AddComponent<CombatActor>();
            actor.Died -= HandleActorDied;
            actor.Died += HandleActorDied;
            actorPools[actor] = pool;
            peakUnits = Mathf.Max(peakUnits, actorPools.Count);
            actor.Initialize(definition, world, arena, presentationPool);
            return actor;
        }

        void HandleActorDied(CombatActor actor)
        {
            actor.Died -= HandleActorDied;
            if (actorPools.TryGetValue(actor, out PrefabPool pool))
            {
                actorPools.Remove(actor);
                pool.Release(actor.gameObject);
            }
        }

        void OnDestroy()
        {
            spawnLifetime?.Cancel();
            pendingSpawns.Clear();
            foreach (var actor in actorPools.Keys)
                if (actor != null)
                    actor.Died -= HandleActorDied;
            var counts = UnitPoolCounts;
            finalPoolCounts = (counts.Created, counts.Rents, counts.Returns, 0, 0, counts.Peak);
            actorPools.Clear();
            pools.Clear();
            unitPrefab = null;

            if (poolRoot != null)
            {
                if (poolRoot.gameObject.activeInHierarchy)
                    poolRoot.gameObject.SetActive(false);
                Destroy(poolRoot.gameObject);
            }
            if (resourceScope != null)
                _ = ReleaseResourcesAsync(poolRoot, resourceScope, resources, spawnLifetime);
        }

        static async Task ReleaseResourcesAsync(
            Transform root, ResourceScope scope, ResourceStore store, CancellationTokenSource lifetime)
        {
            try
            {
                // Destroy is deferred by Unity. Keep the lease until the pool hierarchy is gone.
                while (root != null)
                    await Task.Yield();
                await scope.DisposeAsync();
            }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { lifetime.Dispose(); }

            try { await store.DisposeAsync(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
