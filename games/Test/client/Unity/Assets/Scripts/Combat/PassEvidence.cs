using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Test.Application.Combat;
using Test.Domain.Combat;
using Unity.Profiling;
using UnityEngine;
using Numerics = System.Numerics;

namespace Test.Client.Combat
{
    public sealed class PassEvidence : MonoBehaviour
    {
        private static readonly string Output = Argument("-rain-pass-evidence");
        private static readonly bool PracticeOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-rain-pass-practice") >= 0;
        internal static bool Requested => Output != null;
        private CombatShowcase showcase;
        private int deflects, finishes, lastOwner, lastSegment = -1;
        private bool fell, reset, guarded;
        private string failure;

        internal void Initialize(CombatShowcase value)
        {
            showcase = value;
            Directory.CreateDirectory(Output);
            UnityEngine.Application.runInBackground = true;
            showcase.Run.Cue += Record;
            StartCoroutine(Verify());
        }

        private void Record(CombatCue cue)
        {
            if (cue.Result == ContactResult.Deflect) deflects++;
            if (cue.Result == ContactResult.Finish) finishes++;
            if (cue.Result == ContactResult.Fall && cue.Target == 0) fell = true;
        }

        private IEnumerator Verify()
        {
            bool captureScreenshots = !UnityEngine.Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            var report = new EvidenceReport
            {
                unity = UnityEngine.Application.unityVersion, platform = UnityEngine.Application.platform.ToString(),
                gpu = SystemInfo.graphicsDeviceName, cpu = SystemInfo.processorType, digest = showcase.LayoutDigest,
                graphicsEvidence = captureScreenshots, configurationHash = showcase.BossConfigurationHash
            };
            QualitySettings.vSyncCount = 0;
            UnityEngine.Application.targetFrameRate = -1;
            var run = showcase.Run;
            yield return new WaitForFixedUpdate();
            failure = VerifyBow(run);
            report.bowVerified = failure == null;
            run.Restart();
            var areas = new[] { new Samples(), new Samples(), new Samples() };
            int lastArea = -1;
            int lastDeflect = 0;
            var capturedDeflect = new bool[3];
            float started = Time.realtimeSinceStartup;
            double accumulator = 0;
            using (var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1))
            {
                while (failure == null && run.Status != RunStatus.Victory && Time.realtimeSinceStartup - started < 115)
                {
                    int area = (int)run.Encounter;
                    if (captureScreenshots && lastArea != area)
                    {
                        lastArea = area;
                        showcase.SetEvidenceCamera(true);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        report.width = Screen.width;
                        report.height = Screen.height;
                        ScreenCapture.CaptureScreenshot(Path.Combine(Output, $"{area + 1}-layout.png"));
                        yield return null;
                        showcase.SetEvidenceCamera(false);
                    }
                    if (captureScreenshots && deflects != lastDeflect && !capturedDeflect[area])
                    {
                        capturedDeflect[area] = true;
                        yield return new WaitForSecondsRealtime(.055f);
                        yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(Path.Combine(Output, $"{area + 1}-deflect.png"));
                        yield return null;
                    }
                    lastDeflect = deflects;
                    if (run.Status == RunStatus.Defeated)
                    {
                        if (reset) { failure = "Player fell after the controlled retry."; break; }
                        if (captureScreenshots)
                        {
                            yield return new WaitForEndOfFrame();
                            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "retry.png"));
                        }
                        run.Retry();
                        reset = fell && run.Tick == 0 && run.AttackOwner == 0 && run.Locked == 0 && run.View(0).Vitality.Health == 100;
                        if (!reset) failure = "Checkpoint state was not reset.";
                        lastOwner = 0;
                        lastSegment = -1;
                        guarded = false;
                    }
                    if (PracticeOnly && run.Encounter == Encounter.Practice && run.Status == RunStatus.Proceed) break;
                    if (run.Tick > 45 && (reset || area != 0)) areas[area].Add(Time.unscaledDeltaTime * 1000f, gc.Valid ? gc.LastValue : -1);
                    accumulator += Mathf.Min(Time.unscaledDeltaTime, .1f);
                    for (int steps = 0; accumulator >= 1.0 / CombatRun.StepsPerSecond && steps < 6; steps++)
                    {
                        accumulator -= 1.0 / CombatRun.StepsPerSecond;
                        if (run.Status != RunStatus.Defeated && run.Status != RunStatus.Victory) Drive(run);
                    }
                    yield return null;
                }
                report.gcCounterAvailable = gc.Valid;
            }
            report.deflects = deflects;
            report.finishes = finishes;
            report.defeatAndRetry = reset;
            report.result = run.Status.ToString();
            report.passed = report.bowVerified && reset && deflects > 0 && (PracticeOnly ? run.Status == RunStatus.Proceed && finishes == 1
                : run.Status == RunStatus.Victory && finishes == 5);
            report.error = failure ?? (report.passed ? "" : "Evidence run did not reach its expected outcome.");
            report.areas = new[] { areas[0].Report("Practice"), areas[1].Report("Courtyard"), areas[2].Report("Gate") };
            if (captureScreenshots)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(Output, "result.png"));
                yield return null;
                yield return new WaitForSecondsRealtime(.25f);
            }
            if (report.passed && !PracticeOnly)
            {
                yield return VerifyRoutes(report);
                report.passed = failure == null;
                if (failure != null) report.error = failure;
            }
#if !UNITY_EDITOR
            var shutdown = showcase.ShutdownAsync();
            while (!shutdown.IsCompleted) yield return null;
            if (shutdown.IsFaulted || shutdown.IsCanceled)
            {
                report.passed = false;
                report.error = shutdown.Exception?.GetBaseException().Message ?? "Host shutdown was canceled.";
            }
#endif
            File.WriteAllText(Path.Combine(Output, "result.json"), JsonUtility.ToJson(report, true));
            UnityEngine.Debug.Log($"Rain Pass evidence complete ({report.result}): {report.passed}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit(report.passed ? 0 : 1);
#endif
        }

        private IEnumerator VerifyRoutes(EvidenceReport report)
        {
            const int pilotBossId = 101;
            var selections = new List<(CombatMapId? Map, int BossId)>();
            foreach (var map in CombatMap.Routes) selections.Add((map.Id, 0));
            selections.Add((null, pilotBossId));
            var results = new List<RouteReport>();
            foreach (var selection in selections)
            {
                showcase.ShowTrialSelection();
                bool started = selection.Map.HasValue
                    ? showcase.StartMap(selection.Map.Value) : showcase.StartMapBoss(selection.BossId);
                if (!started)
                {
                    failure = $"Could not start route {selection.Map} or BossChallenge ID {selection.BossId}.";
                    break;
                }
                var run = showcase.Run;
                var map = run.Map;
                int initialFinishes = finishes;
                int initialDeflects = deflects;
                guarded = false;
                lastOwner = 0;
                lastSegment = -1;
                run.Cue += Record;
                const int maximumSteps = 9000;
                int steps = 0;
                int bossEntries = 0;
                while (steps < maximumSteps && (run.Status == RunStatus.Fighting || run.Status == RunStatus.Proceed) && failure == null)
                {
                    var area = run.Encounter;
                    showcase.Space.Gates(run.Status == RunStatus.Proceed, 1);
                    Drive(run);
                    steps++;
                    if (area == Encounter.Courtyard && run.Encounter == Encounter.Gate)
                    {
                        bossEntries++;
                        run.Retry();
                        if (run.Encounter != Encounter.Gate || run.Tick != 0 || run.View(0).Vitality.Health != 100)
                            failure = $"{map.Name} did not restore the boss checkpoint.";
                    }
                    if (steps % 60 == 0) yield return null;
                }
                run.Cue -= Record;
                int routeFinishes = finishes - initialFinishes;
                results.Add(new RouteReport { name = map.Name, boss = map.Boss.Name, bossChallengeId = selection.BossId,
                    configurationHash = showcase.BossConfigurationHash,
                    objectCount = showcase.GetComponentsInChildren<Transform>(true).Length,
                    audioSourceCount = showcase.GetComponentsInChildren<AudioSource>(true).Length,
                    architectureCount = showcase.ArchitectureCount,
                    digest = showcase.LayoutDigest, result = run.Status.ToString(),
                    steps = steps, deflects = deflects - initialDeflects, finishes = routeFinishes });
                if (failure == null && (run.Status != RunStatus.Victory || routeFinishes != map.Guards + 1 || bossEntries != 1))
                    failure = $"{map.Name} stopped at {run.Encounter}/{run.Status} after {steps} steps with {routeFinishes} finishes. " +
                        $"Player at {showcase.Space.Position(0)}, health {run.View(0).Vitality.Health}, owner {run.AttackOwner}.";
                if (failure != null) break;
            }
            report.routes = results.ToArray();
        }

        private string VerifyBow(CombatRun run)
        {
            var space = showcase.Space;
            var shooter = space.Position(0) + Numerics.Vector3.UnitY * 1.34f;
            var opponent = space.Position(1) + Numerics.Vector3.UnitY * 1.34f;
            if (!space.TraceArrow(shooter, opponent, out int target) || target != 1)
            {
                var fighter = space.Fighter(1);
                var origin = space.Fighter(0).Focus;
                var offset = fighter.Focus - origin;
                var hits = Physics.RaycastAll(origin, offset.normalized, offset.magnitude, ~0, QueryTriggerInteraction.Ignore);
                var names = Array.ConvertAll(hits, hit => $"{hit.collider.name} at {hit.distance:0.00} m (layer {hit.collider.gameObject.layer})");
                return $"Bow check failed from {shooter} to {opponent}. Opponent bounds {fighter.Controller.bounds}, enabled {fighter.Controller.enabled}. Ray hits [{string.Join(", ", names)}].";
            }

            var blocker = new GameObject("Bow verification cover");
            blocker.transform.position = Vector3.Lerp(space.Fighter(0).Focus, space.Fighter(1).Focus, .5f);
            blocker.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            bool stoppedAtCover = space.TraceArrow(shooter, opponent, out target) && target == 0;
            blocker.SetActive(false);
            Destroy(blocker);
            Physics.SyncTransforms();
            if (!stoppedAtCover) return "Bow check did not report the nearest obstruction as cover.";

            int health = run.View(1).Vitality.Health;
            run.Queue(CombatButtons.EquipBow | CombatButtons.Lock | CombatButtons.AttackPressed);
            for (int step = 0; step < BowRules.Shot.Duration; step++)
                run.Step(Numerics.Vector3.Zero, Numerics.Vector3.UnitZ, false);
            if (run.Weapon != PlayerWeapon.Bow || run.ArrowsRemaining != BowRules.QuiverCapacity - 1)
                return "Bow check did not launch exactly one arrow.";
            if (run.View(1).Vitality.Health != health - BowRules.Shot.Damage || run.AirborneArrows.Count != 0)
                return "Bow check did not damage the locked opponent exactly once.";
            return null;
        }

        private void Drive(CombatRun run)
        {
            var space = showcase.Space;
            var position = space.Position(0);
            if (run.Status == RunStatus.Proceed)
            {
                run.Step(Numerics.Vector3.UnitZ, Numerics.Vector3.UnitZ, false);
                return;
            }
            int actor = run.AttackOwner;
            if (actor == 0)
            {
                float closest = float.PositiveInfinity;
                for (int index = 1; index < run.Count; index++)
                {
                    if (!run.View(index).Vitality.Alive) continue;
                    float distance = Numerics.Vector3.DistanceSquared(space.Position(index), position);
                    if (distance < closest) { actor = index; closest = distance; }
                }
            }
            if (actor == 0) { failure = "No live opponent, but encounter did not advance."; return; }
            var toward = space.Position(actor) - position;
            toward.Y = 0;
            float range = toward.Length();
            var direction = range > .001f ? toward / range : Numerics.Vector3.UnitZ;
            if (reset)
            {
                if (run.CanFinish && !run.IsHoldingFinish) run.Queue(CombatButtons.AttackPressed);
                var view = run.View(actor);
                if (run.AttackOwner == 0 || lastOwner != run.AttackOwner || lastSegment != view.Segment || view.Phase == AttackPhase.Ready) guarded = false;
                if (!guarded && run.AttackOwner == actor && view.Phase == AttackPhase.Windup && view.PhaseProgress >= (run.Map == null ? .88f : .92f))
                {
                    run.Queue(CombatButtons.Guard);
                    guarded = true;
                }
                lastOwner = run.AttackOwner;
                lastSegment = view.Segment;
            }
            run.Step(range > (reset ? 1.83f : 1.57f) ? direction : Numerics.Vector3.Zero, direction, reset && guarded);
        }

        private sealed class Samples
        {
            private readonly List<float> frame = new List<float>();
            private long maximumGc, sumGc;
            private int gcSamples;
            public void Add(float duration, long gc)
            {
                frame.Add(duration);
                if (gc < 0) return;
                sumGc += gc;
                maximumGc = Math.Max(maximumGc, gc);
                gcSamples++;
            }
            public AreaReport Report(string name)
            {
                frame.Sort();
                return new AreaReport
                {
                    name = name, frames = frame.Count,
                    medianMs = Percentile(.5f), p95Ms = Percentile(.95f),
                    averageGcBytes = gcSamples == 0 ? -1 : sumGc / gcSamples, maximumGcBytes = gcSamples == 0 ? -1 : maximumGc
                };
            }
            private float Percentile(float fraction) => frame.Count == 0 ? 0 : frame[Mathf.Min(frame.Count - 1, Mathf.FloorToInt(frame.Count * fraction))];
        }

        [Serializable]
        private sealed class EvidenceReport
        {
            public string unity, platform, gpu, cpu, digest, result, error, configurationHash;
            public int width, height, deflects, finishes;
            public bool passed, defeatAndRetry, gcCounterAvailable, graphicsEvidence, bowVerified;
            public AreaReport[] areas;
            public RouteReport[] routes;
        }

        [Serializable]
        private sealed class RouteReport
        {
            public string name, boss, digest, result, configurationHash;
            public int steps, deflects, finishes, bossChallengeId, objectCount, audioSourceCount, architectureCount;
        }

        [Serializable]
        private sealed class AreaReport
        {
            public string name;
            public int frames;
            public float medianMs, p95Ms;
            public long averageGcBytes, maximumGcBytes;
        }

        private static string Argument(string name)
        {
            string[] values = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(values, name);
            return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
        }
    }
}
