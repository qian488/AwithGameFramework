using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Test.Application.Combat;
using Test.Client.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Test.Client.Tests
{
    public sealed partial class RainPassContractTests
    {
        private string saveRoot;
        private RainPassCamp camp;

        [SetUp]
        public void SetUp()
        {
            saveRoot = Path.Combine(Path.GetTempPath(), "RainPassContract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (camp != null)
            {
                var shutdown = camp.ShutdownAsync();
                yield return Await(() => shutdown.IsCompleted, "camp host shutdown");
                Assert.That(shutdown.IsFaulted, Is.False, shutdown.Exception?.ToString());
                Object.Destroy(camp.gameObject);
                yield return null;
            }
            if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
        }

        [UnityTest]
        public IEnumerator OpensTheAuthoredCampScene()
        {
            yield return SceneManager.LoadSceneAsync(RainPassCamp.ScenePath);
            camp = Object.FindObjectOfType<RainPassCamp>();
            Assert.That(camp, Is.Not.Null);
            yield return Await(() => HasButton("Accept contract"), "commission card");
            AssertCampReleased();
            Assert.That(camp.GetComponentInChildren<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(camp.GetComponentsInChildren<Button>(true), Has.Length.EqualTo(12));
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(FindButton("Accept contract").gameObject));
            yield return RainPassContractFrames.Check(camp.GetComponentInChildren<Canvas>(), "01-camp", "Accept contract", "Settings", "Expedition");
        }

        [UnityTest]
        public IEnumerator CancelsLoadingAndRetreatsRepeatedlyWithoutLeavingScenesOrAudio()
        {
            yield return OpenCamp();
            var accept = FindButton("Accept contract");
            accept.onClick.Invoke();
            accept.onClick.Invoke();
            yield return Await(() => HasButton("Cancel loading"), "cancel button");
            yield return RainPassContractFrames.Check(camp.GetComponentInChildren<Canvas>(), "02-loading", "Cancel loading");
            Click("Cancel loading");
            yield return RainPassContractFrames.Check(camp.GetComponentInChildren<Canvas>(), "03-cancelling");
            yield return Await(() => HasButton("Accept contract"), "camp after cancellation");
            AssertCampReleased();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Click("Accept contract");
                yield return Await(() => camp.Phase == ContractPhase.Battle, "start Cedar Sentinel");
                Assert.That(camp.LoadedBattleCount, Is.EqualTo(1));
                var battle = Object.FindObjectOfType<CombatShowcase>();
                Assert.That(battle.ActiveMapId, Is.EqualTo(CombatMapId.CedarGrove));
                Assert.That(camp.GetComponentInChildren<Camera>().isActiveAndEnabled, Is.False);
                Assert.That(battle.GetComponentsInChildren<Camera>().Count(camera => camera.isActiveAndEnabled), Is.EqualTo(1));
                battle.Run.TogglePause();
                yield return Await(() => HasButton("Retreat to camp"), "pause menu");
                if (attempt == 0) yield return RainPassContractFrames.Check(camp.GetComponentInChildren<Canvas>(), "04-paused", "Retreat to camp", "Settings");
                Click("Retreat to camp");
                yield return Await(() => HasButton("Accept contract"), "camp after retreat");
                AssertCampReleased();
                Assert.That(battle == null, Is.True);
            }
            Assert.That(camp.Completed, Is.False);
            Assert.That(Directory.GetFiles(saveRoot, "*.save", SearchOption.AllDirectories), Is.Empty);
        }

        [UnityTest]
        public IEnumerator RecoversFromAValidSceneWithoutACombatHost()
        {
            yield return OpenCamp("Assets/Scenes/SampleScene.unity");
            LogAssert.Expect(LogType.Error, new Regex("Could not enter Cedar Grove"));
            Click("Accept contract");
            yield return Await(() => camp.Phase == ContractPhase.Camp, "recover from bad route scene");
            Assert.That(HasButton("Accept contract"), Is.True);
            Assert.That(SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity").isLoaded, Is.False);
            AssertCampReleased();
        }

        [UnityTest]
        public IEnumerator DefeatCanRetryAndRealVictoryIsSavedAfterRetryAndReopened()
        {
            yield return OpenCamp();
            Click("Accept contract");
            yield return Await(() => camp.Phase == ContractPhase.Battle, "start Cedar Sentinel");
            var battle = Object.FindObjectOfType<CombatShowcase>();
            var driver = battle.gameObject.AddComponent<PassEvidence>();
            yield return DriveUntil(driver, battle, RunStatus.Defeated, defend: false);
            yield return Await(() => HasButton("Retry checkpoint"), "defeat menu");
            yield return RainPassContractFrames.Check(camp.GetComponentInChildren<Canvas>(), "05-defeated", "Retry checkpoint", "Retreat to camp");
            Click("Retry checkpoint");
            Assert.That(battle.Run.Status, Is.EqualTo(RunStatus.Fighting));
            Assert.That(battle.SimulationTick, Is.Zero);

            File.WriteAllText(camp.SaveDirectory, "temporarily block save directory creation");
            LogAssert.Expect(LogType.Error, new Regex("Completion could not be saved"));
            yield return DriveUntil(driver, battle, RunStatus.Victory, defend: true);
            yield return Await(() => HasButton("Retry save"), "retry save after victory");
            yield return RainPassContractFrames.Check(camp.GetComponentInChildren<Canvas>(), "06-save-failed", "Retry save");
            Assert.That(camp.Completed, Is.False);
            Assert.That(HasButton("Return to camp"), Is.False);
            File.Delete(camp.SaveDirectory);
            Click("Retry save");
            yield return Await(() => HasButton("Return to camp"), "saved completion");
            yield return RainPassContractFrames.Check(camp.GetComponentInChildren<Canvas>(), "07-victory", "Return to camp");
            Click("Return to camp");
            yield return Await(() => HasButton("Accept contract"), "return after settlement");
            AssertCampReleased();
            Assert.That(camp.Completed, Is.True);
            var shutdown = camp.ShutdownAsync();
            yield return Await(() => shutdown.IsCompleted, "close camp before reopening");
            Assert.That(shutdown.IsFaulted, Is.False);
            Object.Destroy(camp.gameObject);
            yield return null;
            yield return OpenCamp();
            Assert.That(camp.Completed, Is.True);
            yield return RainPassContractFrames.Check(camp.GetComponentInChildren<Canvas>(), "08-reopened", "Accept contract", "Settings", "Expedition");
        }

        private IEnumerator OpenCamp(string battleScenePath = CombatShowcase.ScenePath)
        {
            var root = new GameObject("Rain Pass contract test");
            root.SetActive(false);
            var camera = new GameObject("Camp Camera", typeof(Camera));
            camera.transform.SetParent(root.transform, false);
            var canvas = new GameObject("Contract Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(root.transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);
            var events = new GameObject("Camp EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(root.transform, false);
            camp = root.AddComponent<RainPassCamp>();
            camp.UsePresentation(camera.GetComponent<Camera>(), canvas.GetComponent<Canvas>());
            camp.SaveDirectory = Path.Combine(saveRoot, "contract");
            camp.BattleScenePath = battleScenePath;
            root.SetActive(true);
            yield return Await(() => HasButton("Accept contract"), "open camp");
        }

        private static IEnumerator DriveUntil(PassEvidence driver, CombatShowcase battle, RunStatus expected, bool defend)
        {
            for (int step = 0; step < 9000 && (battle.Run.Status == RunStatus.Fighting ||
                battle.Run.Status == RunStatus.Proceed); step++)
            {
                driver.DriveContract(battle, defend);
                if (step % 60 == 0) yield return null;
            }
            Assert.That(battle.Run.Status, Is.EqualTo(expected), $"{battle.CurrentArea} at tick {battle.SimulationTick}");
        }

        private void AssertCampReleased()
        {
            Assert.That(camp.GetComponentsInChildren<Camera>().Any(camera =>
                camera.isActiveAndEnabled && camera.targetDisplay == 0 && camera.targetTexture == null), Is.True,
                "An active camera must render the camp on Display 1.");
            Assert.That(camp.LoadedBattleCount, Is.Zero);
            Assert.That(SceneManager.GetSceneByPath(CombatShowcase.ScenePath).isLoaded, Is.False);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(camp.gameObject.scene));
            Assert.That(Object.FindObjectsOfType<CombatShowcase>(), Is.Empty);
            Assert.That(Object.FindObjectsOfType<AudioSource>().Any(source => source.name == "Music theme"), Is.False);
            Assert.That(Object.FindObjectsOfType<Camera>().Any(camera => camera.name == "Shoulder camera"), Is.False);
        }

        private void Click(string label)
        {
            var button = FindButton(label);
            Assert.That(button, Is.Not.Null, label);
            button.onClick.Invoke();
        }

        private bool HasButton(string label) => FindButton(label) != null;

        private Button FindButton(string label) => camp == null ? null : camp.GetComponentsInChildren<Button>()
            .FirstOrDefault(button => button.GetComponentInChildren<Text>().text == label);

        private static IEnumerator Await(Func<bool> condition, string explanation)
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, explanation);
        }
    }
}
