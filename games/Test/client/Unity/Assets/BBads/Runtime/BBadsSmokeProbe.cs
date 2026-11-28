using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BBads.Backpack;
using BBads.Combat;
using BBads.Data;
using BBads.Quality;
using BBads.Timeline;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Test.Client.BBads
{
    public sealed class BBadsSmokeProbe : MonoBehaviour
    {
        const float SampleUntil = 16f;
        const double WallTimeout = 45d;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartOptIn()
        {
#if !UNITY_EDITOR
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-bbads-smoke") < 0)
                return;

            var host = new GameObject(nameof(BBadsSmokeProbe));
            DontDestroyOnLoad(host);
            host.AddComponent<BBadsSmokeProbe>();
#endif
        }

        IEnumerator Start()
        {
            UnityEngine.Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Application.targetFrameRate = -1;
            string[] args = Environment.GetCommandLineArgs();
            string capture = GetArgument(args, "-bbads-smoke-capture");
            Vector2Int captureSize = new Vector2Int(540, 960);
            string sizeArgument = GetArgument(args, "-bbads-smoke-capture-size");
            if (sizeArgument != null && !TryParseSize(sizeArgument, out captureSize))
            {
                FinishPlayer("Expected -bbads-smoke-capture-size WIDTHxHEIGHT with dimensions from 1 to 4096.");
                yield break;
            }

            BBadsArtTier? expectedTier = null;
            string tierArgument = GetArgument(args, "-bbads-smoke-expect-tier");
            if (tierArgument != null)
            {
                if (!Enum.TryParse(tierArgument, true, out BBadsArtTier tier) ||
                    !string.Equals(tier.ToString(), tierArgument, StringComparison.OrdinalIgnoreCase))
                {
                    FinishPlayer("Expected -bbads-smoke-expect-tier high or low.");
                    yield break;
                }
                expectedTier = tier;
            }
            Debug.Log($"BBADS_SMOKE_STARTED unity={UnityEngine.Application.unityVersion} development={Debug.isDebugBuild}");

            bool finished = false;
            yield return Run(failure =>
            {
                finished = true;
                FinishPlayer(failure);
            }, capture, captureSize, expectedTier);
            if (!finished)
                FinishPlayer("Probe ended before reporting a result.");
        }

        static string GetArgument(string[] args, string flag)
        {
            int index = Array.IndexOf(args, flag);
            return index < 0 ? null : index + 1 < args.Length ? args[index + 1] : string.Empty;
        }

        static bool TryParseSize(string value, out Vector2Int size)
        {
            string[] dimensions = value.Split('x', 'X');
            size = default;
            if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out int width) ||
                !int.TryParse(dimensions[1], out int height) || width < 1 || height < 1 || width > 4096 || height > 4096)
                return false;
            size = new Vector2Int(width, height);
            return true;
        }

        static void FinishPlayer(string failure)
        {
            if (failure == null)
                Debug.Log("BBADS_SMOKE_PASSED");
            else
                Debug.LogError($"BBADS_SMOKE_FAILED {failure}");
            UnityEngine.Application.Quit(failure == null ? 0 : 1);
        }

        // A null result denotes success. Both Release Players and Play Mode can run this coroutine.
        public static IEnumerator Run(Action<string> complete, string captureDirectory = null,
            Vector2Int captureSize = default, BBadsArtTier? expectedTier = null)
        {
            int frameRate = Time.captureFramerate;
            Time.captureFramerate = 60;
            BattleObservation loadedBattle = null;
            void ObserveLoadedBattle(Scene loaded, LoadSceneMode mode)
            {
                if (loaded.path != BBadsSceneActions.BattleScene)
                    return;

                // Observe the initial state before Start and Update, including while the old scene unloads.
                loadedBattle?.Dispose();
                loadedBattle = new BattleObservation(loaded);
            }

            SceneManager.sceneLoaded += ObserveLoadedBattle;
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                if (scene.path != BBadsSceneActions.BattleScene)
                {
                    if (!BBadsSceneActions.TryEnterBattle())
                    {
                        complete("Could not enter the BBads battle scene.");
                        yield break;
                    }
                    yield return WaitForScene(BBadsSceneActions.BattleScene, scene.handle);
                }

                foreach (string run in new[] { "initial", "restart", "reenter" })
                {
                    scene = SceneManager.GetActiveScene();
                    if (scene.path != BBadsSceneActions.BattleScene)
                    {
                        complete($"{run}: BBads scene did not load.");
                        yield break;
                    }

                    using var observed = loadedBattle ?? new BattleObservation(scene);
                    loadedBattle = null;
                    double deadline = Time.realtimeSinceStartupAsDouble + WallTimeout;
                    while (observed.Failure == null && observed.Runner.Elapsed < SampleUntil &&
                           Time.realtimeSinceStartupAsDouble < deadline)
                        yield return null;

                    string failure = observed.Finish() ?? ReportBattleArt(scene, run, expectedTier);
                    if (failure != null)
                    {
                        complete($"{run}: {failure}");
                        yield break;
                    }

                    Debug.Log($"BBADS_SMOKE_RUN {run} enemies={observed.EnemyCount} items={observed.ItemCount} " +
                              $"placements={observed.PlacementCount} summons={observed.SummonCount} " +
                              $"damageEvents={observed.DamageCount} invalidPlacementRejected={observed.InvalidPlacementRejected}");
                    CaptureBattle(captureDirectory, run, captureSize == default ? new Vector2Int(540, 960) : captureSize);

                    failure = observed.VerifyDefeat();
                    if (failure != null)
                    {
                        complete($"{run}: {failure}");
                        yield break;
                    }
                    Debug.Log($"BBADS_SMOKE_DEFEAT run={run} health=0 destroyed=1 runnerStopped=True postDefeatSpawns=0");

                    if (run == "reenter")
                        break;

                    bool restart = run == "initial";
                    string button = restart ? BBadsSceneActions.RestartButton : BBadsSceneActions.ExitButton;
                    string destination = restart ? BBadsSceneActions.BattleScene : BBadsSceneActions.ExitScene;
                    if (!Click(scene, button))
                    {
                        complete($"{run}: could not invoke {button}.");
                        yield break;
                    }
                    yield return WaitForScene(destination, scene.handle);

                    if (SceneManager.GetActiveScene().path != destination || scene.isLoaded ||
                        !observed.CheckReleased())
                    {
                        complete($"{run}: old battle did not unload and release its schedule.");
                        yield break;
                    }

                    if (!restart)
                    {
                        Scene exit = SceneManager.GetActiveScene();
                        if (BBadsSceneActions.FindInScene<ScenarioRunner>(exit) != null)
                        {
                            complete("Exit retained a BBads runner in SampleScene.");
                            yield break;
                        }

                        Debug.Log($"BBADS_SMOKE_EXIT {exit.path}");
                        if (!Click(exit, BBadsSceneActions.EnterButton))
                        {
                            complete("SampleScene did not expose the Open BBads button.");
                            yield break;
                        }
                        yield return WaitForScene(BBadsSceneActions.BattleScene, exit.handle);
                    }
                }

                complete(null);
            }
            finally
            {
                SceneManager.sceneLoaded -= ObserveLoadedBattle;
                loadedBattle?.Dispose();
                Time.captureFramerate = frameRate;
            }
        }

        static IEnumerator WaitForScene(string path, int priorHandle)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + WallTimeout;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                Scene active = SceneManager.GetActiveScene();
                if (!BBadsSceneActions.IsTransitioning && active.path == path && active.handle != priorHandle)
                    yield break;
                yield return null;
            }
        }

        static bool Click(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Button button in root.GetComponentsInChildren<Button>())
                {
                    if (button.name != name || !button.IsInteractable())
                        continue;
                    button.onClick.Invoke();
                    return true;
                }
            }
            return false;
        }

        static string ReportBattleArt(Scene scene, string run, BBadsArtTier? expectedTier)
        {
            var background = BBadsSceneActions.FindInScene<global::BBads.BBadsBattleArt>(scene);
            Image image = background != null ? background.GetComponent<Image>() : null;
            Sprite sprite = image != null ? image.sprite : null;
            Texture2D texture = sprite != null ? sprite.texture : null;
            BBadsArtTier tier = BBadsArtQuality.SelectedTier;
            string bound = texture != null
                ? $"object={image.name} sprite={sprite.name} texture={texture.name} width={texture.width} " +
                  $"height={texture.height} format={texture.format} mipmaps={texture.mipmapCount}"
                : "texture=missing";
            Debug.Log($"BBADS_SMOKE_ART run={run} selected={tier} key={BBadsArtQuality.BackgroundKey} {bound}");
            ReportPresentation(scene, run);

            if (!expectedTier.HasValue)
                return null;
            if (tier != expectedTier.Value)
                return $"Selected art tier is {tier}, expected {expectedTier.Value}.";

            string key = "BBads/Quality/" + tier + "/" + BBadsArtQuality.BackgroundKey.Substring("BBads/".Length);
            Sprite expectedSprite = Resources.Load<Sprite>(key);
            if (texture == null || expectedSprite == null || texture != expectedSprite.texture)
                return $"Battle background is not bound to the expected {tier} texture from {key}.";
            return null;
        }

        static void ReportPresentation(Scene scene, string run)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            Image[] images = roots.SelectMany(root => root.GetComponentsInChildren<Image>())
                .Where(image => image.isActiveAndEnabled && image.color.a > 0f && image.sprite != null)
                .ToArray();
            foreach (Image image in images.Where(image =>
                image.name == "WaveTrack" || image.name == "WaveFill" || image.name == "WaveLabelFrame"))
            {
                Sprite sprite = image.sprite;
                Debug.Log($"BBADS_SMOKE_HUD run={run} object={image.name} packed={sprite.packed} {DescribeTexture(sprite)}");
            }

            foreach (string key in new[] { BBadsArtQuality.BagSlotKey, BBadsArtQuality.GangsterIconKey })
            {
                Sprite resolved = BBadsArtQuality.LoadSprite(key, out string resolvedKey);
                Image bound = resolved != null ? images.FirstOrDefault(image => image.sprite == resolved) : null;
                Debug.Log($"BBADS_SMOKE_ICON run={run} key={key} resolved={resolvedKey ?? "missing"} " +
                          $"boundObject={(bound != null ? bound.name : "none")} {DescribeTexture(bound != null ? bound.sprite : resolved)}");
            }

            foreach (UnitCamp camp in new[] { UnitCamp.Player, UnitCamp.Enemy })
            {
                CombatActor actor = roots.SelectMany(root => root.GetComponentsInChildren<CombatActor>())
                    .FirstOrDefault(candidate => candidate.IsAlive && candidate.Camp == camp &&
                        candidate.GetComponentInChildren<SkeletonAnimation>() != null);
                var spine = actor != null ? actor.GetComponentInChildren<SkeletonAnimation>() : null;
                var track = spine != null && spine.valid ? spine.AnimationState.GetCurrent(0) : null;
                Debug.Log($"BBADS_SMOKE_SPINE run={run} camp={camp} unit={actor?.Definition.id ?? "none"} " +
                          $"animation={track?.Animation.Name ?? "none"} trackTime={track?.TrackTime ?? 0f:F3}");
            }
        }

        static string DescribeTexture(Sprite sprite)
        {
            Texture2D texture = sprite != null ? sprite.texture : null;
            return texture != null
                ? $"sprite={sprite.name} texture={texture.name} width={texture.width} height={texture.height} format={texture.format}"
                : "texture=missing";
        }

        static void CaptureBattle(string directory, string name, Vector2Int size)
        {
            if (string.IsNullOrEmpty(directory))
                return;

            Camera camera = Camera.main;
            if (camera == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning($"BBads smoke skipped capture for {name}: no main camera or graphics device.");
                return;
            }

            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            Rect previousRect = camera.rect;
            float previousAspect = camera.aspect;
            RenderTexture target = null;
            Texture2D frame = null;
            try
            {
                target = RenderTexture.GetTemporary(size.x, size.y, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.aspect = (float)size.x / size.y;
                RenderTexture.active = target;
                GL.Clear(true, true, Color.clear);
                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture.active = target;
                frame = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                frame.ReadPixels(new Rect(0f, 0f, size.x, size.y), 0, 0, false);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, name + ".png");
                File.WriteAllBytes(path, frame.EncodeToPNG());
                Debug.Log($"BBADS_SMOKE_CAPTURE {path}");
            }
            catch (Exception error)
            {
                Debug.LogWarning($"BBads smoke capture failed for {name}: {error.Message}");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.rect = previousRect;
                camera.ResetAspect();
                if (!Mathf.Approximately(camera.aspect, previousAspect))
                    camera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                if (target != null)
                    RenderTexture.ReleaseTemporary(target);
                if (frame != null)
                    Destroy(frame);
            }
        }

        sealed class BattleObservation : IDisposable
        {
            readonly BackpackController backpack;
            readonly CombatWorld world;
            readonly PlayerBase playerBase;
            readonly EnemySpawnEvent[] enemies;
            readonly ItemSpawnEvent[] items;
            readonly HashSet<CombatActor> seenActors = new HashSet<CombatActor>();
            readonly HashSet<int> placedTrayIds = new HashSet<int>();
            readonly HashSet<int> spawnedTrayIds = new HashSet<int>();
            int catCharges;
            bool fishPlaced;
            bool catPlaced;

            public BattleObservation(Scene scene)
            {
                Runner = BBadsSceneActions.FindInScene<ScenarioRunner>(scene);
                backpack = BBadsSceneActions.FindInScene<BackpackController>(scene);
                world = BBadsSceneActions.FindInScene<CombatWorld>(scene);
                playerBase = world != null ? world.PlayerBase : null;
                if (Runner == null || Runner.Scenario == null || backpack == null || playerBase == null)
                {
                    Failure = "Missing battle runtime objects or scenario.";
                    return;
                }

                enemies = Runner.Scenario.EnemySpawns.OrderBy(spawn => spawn.time).ToArray();
                items = Runner.Scenario.ItemSpawns.OrderBy(spawn => spawn.appearTime).ToArray();
                Check(Runner.Elapsed < 0.1f && world.Actors.Count == 0 && playerBase.IsAlive &&
                      backpack.TrayItems.Count == 0 && backpack.Model.Items.Count == 0,
                    "Battle did not start with an empty tray, empty grid, and a fresh timeline.");
                Runner.EnemySpawnRequested += OnEnemy;
                Runner.ItemSpawnRequested += OnItem;
                backpack.PlacementCompleted += OnPlacement;
                backpack.SummonRequested += OnSummon;
                backpack.CatChargeChanged += OnCatCharge;
                playerBase.HealthChanged += OnHealthChanged;
            }

            public ScenarioRunner Runner { get; }
            public string Failure { get; private set; }
            public int EnemyCount { get; private set; }
            public int ItemCount { get; private set; }
            public int SummonCount { get; private set; }
            public int DamageCount { get; private set; }
            public int PlacementCount => placedTrayIds.Count;
            public bool InvalidPlacementRejected { get; private set; }

            public string Finish()
            {
                if (Failure != null)
                    return Failure;
                if (Runner.Elapsed < SampleUntil)
                    return $"Timeline stalled at {Runner.Elapsed:F2} seconds.";
                if (EnemyCount == 0 || ItemCount == 0 ||
                    EnemyCount != enemies.Count(spawn => spawn.time <= Runner.Elapsed) ||
                    ItemCount != items.Count(spawn => spawn.appearTime <= Runner.Elapsed))
                    return "The number of due spawns differs from the scenario.";
                if (!InvalidPlacementRejected || !fishPlaced || !catPlaced || SummonCount == 0 ||
                    catCharges != SummonCount || DamageCount == 0)
                    return $"Expected rejection, fish/cat placements, summons per completed charge, and damage. " +
                           $"Observed rejected={InvalidPlacementRejected} fish={fishPlaced} cat={catPlaced} " +
                           $"summons={SummonCount} completedCharges={catCharges} damage={DamageCount}.";
                return null;
            }

            public string VerifyDefeat()
            {
                float elapsed = Runner.Elapsed;
                float nextEnemy = EnemyCount < enemies.Length ? enemies[EnemyCount].time : float.PositiveInfinity;
                float nextItem = ItemCount < items.Length ? items[ItemCount].appearTime : float.PositiveInfinity;
                float nextSpawn = Mathf.Min(nextEnemy, nextItem);
                if (!Runner.IsPlaying || !playerBase.IsAlive || float.IsInfinity(nextSpawn))
                    return "Defeat requires a running timeline, a living base, and at least one pending spawn.";

                int destroyed = 0;
                void CountDestruction() => destroyed++;
                playerBase.Destroyed += CountDestruction;
                try
                {
                    var spawnCounts = (EnemyCount, ItemCount);
                    playerBase.ReceiveDamage(playerBase.CurrentHealth + 1f);
                    playerBase.ReceiveDamage(1f);
                    Runner.Tick(nextSpawn - elapsed + 1f);

                    if (playerBase.CurrentHealth != 0f || playerBase.IsAlive || destroyed != 1 ||
                        Runner.IsPlaying || Runner.Elapsed != elapsed || spawnCounts != (EnemyCount, ItemCount))
                        return $"Defeat check failed. Health={playerBase.CurrentHealth} destroyed={destroyed} " +
                               $"playing={Runner.IsPlaying} elapsed={Runner.Elapsed:F2} enemies={EnemyCount} items={ItemCount}.";
                    return null;
                }
                finally
                {
                    playerBase.Destroyed -= CountDestruction;
                }
            }

            public bool CheckReleased()
            {
                var before = (EnemyCount, ItemCount, SummonCount, PlacementCount, DamageCount);
                // Use the managed reference to verify that a stale tick cannot publish after Unity unloads the scene.
                Runner.Tick(1000f);
                return Runner == null && !Runner.IsPlaying &&
                       before == (EnemyCount, ItemCount, SummonCount, PlacementCount, DamageCount);
            }

            void OnEnemy(EnemySpawnEvent spawn)
            {
                int index = EnemyCount++;
                Check(index < enemies.Length && enemies[index].unitId == spawn.unitId &&
                      enemies[index].time == spawn.time && enemies[index].position == spawn.position &&
                      spawn.time <= Runner.Elapsed, $"Unexpected enemy row {index} for {spawn.unitId}.");
                ObserveNewActor(spawn.unitId, UnitCamp.Enemy);
            }

            void OnItem(ItemSpawnEvent spawn)
            {
                int index = ItemCount++;
                Check(index < items.Length && items[index].itemId == spawn.itemId &&
                      items[index].appearTime == spawn.appearTime && items[index].placeTime == spawn.placeTime &&
                      items[index].x == spawn.x && items[index].y == spawn.y && spawn.appearTime <= Runner.Elapsed,
                    $"Unexpected item row {index} for {spawn.itemId}.");

                BackpackTrayItem[] newItems = backpack.TrayItems
                    .Where(item => spawnedTrayIds.Add(item.TrayId)).ToArray();
                if (!Check(newItems.Length == 1 && newItems[0].Definition.id == spawn.itemId,
                      $"Expected one new tray item for {spawn.itemId}, got {newItems.Length}."))
                    return;

                if (index == 0)
                {
                    int trayId = newItems[0].TrayId;
                    int count = backpack.Model.Items.Count;
                    InvalidPlacementRejected = !backpack.TryPlaceFromTray(trayId, new Vector2Int(-1, -1)) &&
                        backpack.Model.Items.Count == count && backpack.TrayItems.Any(item => item.TrayId == trayId);
                    Check(InvalidPlacementRejected, "Invalid placement changed the grid or consumed its tray item.");
                }
            }

            void OnPlacement(BackpackPlacementResult result)
            {
                Check(result.TrayId > 0 && spawnedTrayIds.Contains(result.TrayId) && placedTrayIds.Add(result.TrayId),
                    $"Duplicate or unknown placement for tray item {result.TrayId}.");
                var item = result.Item;
                if (item == null)
                    return;
                Check(backpack.Model.TryGetItemAt(item.Anchor, out BackpackItemInstance placed) && placed == item,
                    $"Placed item {item.Definition.id} is missing from its grid cell.");
                fishPlaced |= item.Definition.type == ItemType.Fish;
                catPlaced |= item.Definition.type == ItemType.Cat;
            }

            void OnSummon(string unitId, Vector2 position)
            {
                SummonCount++;
                ObserveNewActor(unitId, UnitCamp.Player);
            }

            void OnCatCharge(BackpackItemInstance cat, bool summoned)
            {
                if (summoned)
                    catCharges++;
            }

            void ObserveNewActor(string unitId, UnitCamp camp)
            {
                seenActors.RemoveWhere(actor =>
                {
                    if (actor != null && actor.IsAlive)
                        return false;
                    actor.HealthChanged -= OnHealthChanged;
                    return true;
                });

                CombatActor[] spawned = world.Actors.Where(actor => seenActors.Add(actor)).ToArray();
                Check(spawned.Length == 1 && spawned[0].Camp == camp && spawned[0].Definition.id == unitId,
                    $"Expected exactly one new {camp} actor for {unitId}, got {spawned.Length}.");
                foreach (CombatActor actor in spawned)
                    actor.HealthChanged += OnHealthChanged;
            }

            void OnHealthChanged(float health, float maxHealth)
            {
                if (health < maxHealth)
                    DamageCount++;
            }

            bool Check(bool condition, string message)
            {
                if (!condition)
                    Failure ??= message;
                return condition;
            }

            public void Dispose()
            {
                if (!ReferenceEquals(Runner, null))
                {
                    Runner.EnemySpawnRequested -= OnEnemy;
                    Runner.ItemSpawnRequested -= OnItem;
                }
                if (!ReferenceEquals(backpack, null))
                {
                    backpack.PlacementCompleted -= OnPlacement;
                    backpack.SummonRequested -= OnSummon;
                    backpack.CatChargeChanged -= OnCatCharge;
                }
                if (!ReferenceEquals(playerBase, null))
                    playerBase.HealthChanged -= OnHealthChanged;
                foreach (CombatActor actor in seenActors)
                    actor.HealthChanged -= OnHealthChanged;
            }
        }
    }
}
