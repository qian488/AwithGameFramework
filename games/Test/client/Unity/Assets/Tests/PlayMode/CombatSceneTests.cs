using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Test.Application.Combat;
using Test.Client.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Test.Client.Tests
{
    public sealed class CombatSceneTests
    {
        [UnityTest]
        public IEnumerator NewRoutesLoadDistinctNativeLayoutsAndRestoreTheOriginalSelection()
        {
            yield return SceneManager.LoadSceneAsync("CombatShowcase");
            var showcase = Object.FindObjectOfType<CombatShowcase>();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!showcase.TrialsReady && showcase.Failure == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(showcase.Failure, Is.Null);
            Assert.That(showcase.TrialsReady, Is.True);
            string original = showcase.LayoutDigest;
            var layouts = new HashSet<string> { original };
            var originalMusic = showcase.GetComponentsInChildren<AudioSource>().Single(source => source.name == "Music theme");
            var themes = new HashSet<string> { originalMusic.clip.name };
            AssertAudible(originalMusic.clip);

            foreach (var map in CombatMap.Routes)
            {
                Assert.That(showcase.StartMap(map.Id), Is.True);
                Assert.That(showcase.ActiveMapId, Is.EqualTo(map.Id));
                Assert.That(showcase.CurrentArea, Is.EqualTo(nameof(Encounter.Courtyard)));
                string digest = showcase.LayoutDigest;
                Assert.That(layouts.Add(digest), Is.True, map.Name);
                var mapRoot = showcase.transform.Find(map.Name);
                Assert.That(mapRoot.Find("Route checkpoint").GetComponent<BoxCollider>().enabled, Is.True, map.Name);
                Assert.That(mapRoot.Find("Encounter actors/Courtyard/" + map.Patrol.Name), Is.Not.Null);
                var music = mapRoot.Find("Procedural audio/Music theme").GetComponent<AudioSource>();
                var drums = mapRoot.Find("Procedural audio/Combat percussion").GetComponent<AudioSource>();
                var ambience = mapRoot.Find("Procedural audio/Environment loop").GetComponent<AudioSource>();
                Assert.That(themes.Add(music.clip.name), Is.True, map.Name);
                Assert.That(originalMusic.gameObject.activeInHierarchy, Is.False);
                Assert.That(music.loop && drums.loop && ambience.loop, Is.True);
                Assert.That(drums.clip.samples, Is.EqualTo(music.clip.samples));
                Assert.That(drums.clip.frequency, Is.EqualTo(music.clip.frequency));
                Assert.That(music.clip.length, Is.InRange(19f, 31f));
                Assert.That(ambience.clip.length, Is.EqualTo(8));
                AssertAudible(music.clip);
                AssertAudible(drums.clip);
                AssertAudible(ambience.clip);
                Assert.That(Object.FindObjectsOfType<Camera>(), Has.Length.EqualTo(1));
                showcase.RetryEncounter();
                showcase.RestartRun();
                Assert.That(showcase.LayoutDigest, Is.EqualTo(digest));
                showcase.ShowTrialSelection();
                yield return null;
                Assert.That(showcase.ActiveMapId, Is.Null);
                Assert.That(showcase.LayoutDigest, Is.EqualTo(original));
                Assert.That(mapRoot == null, Is.True, map.Name);
                Assert.That(music == null, Is.True);
                Assert.That(originalMusic.gameObject.activeInHierarchy, Is.True);
            }
            Assert.That(showcase.StartTrial(1), Is.True);
            yield return SceneManager.LoadSceneAsync("SampleScene");
        }

        private static void AssertAudible(AudioClip clip)
        {
            Assert.That(clip.channels, Is.EqualTo(2));
            var samples = new float[4096];
            Assert.That(clip.GetData(samples, 0), Is.True, clip.name);
            float peak = 0;
            foreach (float sample in samples)
            {
                Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False, clip.name);
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            Assert.That(peak, Is.InRange(.003f, .95f), clip.name);
        }

        [UnityTest]
        public IEnumerator SceneGeneratesContentAndReusesTheEnvironmentAcrossRetries()
        {
            yield return SceneManager.LoadSceneAsync("CombatShowcase");
            var showcase = Object.FindObjectOfType<CombatShowcase>();
            Assert.That(showcase, Is.Not.Null);
            for (int frame = 0; frame < 90 && showcase.LayoutDigest == null && showcase.Failure == null; frame++) yield return null;
            Assert.That(showcase.Failure, Is.Null);
            Assert.That(showcase.LayoutDigest, Has.Length.EqualTo(64));
            Assert.That(showcase.ArchitectureCount, Is.GreaterThan(300));
            int count = showcase.GetComponentsInChildren<Transform>(true).Length;
            string digest = showcase.LayoutDigest;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                showcase.RetryEncounter();
                Assert.That(showcase.SimulationTick, Is.Zero);
                Assert.That(showcase.CurrentArea, Is.EqualTo("Practice"));
                yield return null;
                Assert.That(showcase.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(count));
                Assert.That(showcase.LayoutDigest, Is.EqualTo(digest));
            }
            yield return SceneManager.LoadSceneAsync("SampleScene");
        }

        [UnityTest]
        public IEnumerator TrialSelectionDrivesRealSceneAndKeepsTheOriginalRunAccessible()
        {
            yield return SceneManager.LoadSceneAsync("CombatShowcase");
            var showcase = Object.FindObjectOfType<CombatShowcase>();
            Assert.That(showcase, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!showcase.TrialsReady && showcase.Failure == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(showcase.Failure, Is.Null);
            Assert.That(showcase.TrialsReady, Is.True);
            Assert.That(showcase.IsTrialSelection, Is.True);
            Assert.That(showcase.SimulationTick, Is.Zero);
            string digest = showcase.LayoutDigest;

            Assert.That(showcase.StartTrial(1), Is.True);
            Assert.That(showcase.ActiveTrialId, Is.EqualTo(1));
            Assert.That(showcase.IsTrialSelection, Is.False);
            showcase.RetryEncounter();
            Assert.That(showcase.ActiveTrialId, Is.EqualTo(1));
            Assert.That(showcase.SimulationTick, Is.Zero);
            showcase.ShowTrialSelection();
            Assert.That(showcase.StartTrial(2), Is.True);
            Assert.That(showcase.ActiveTrialId, Is.EqualTo(2));
            Assert.That(showcase.LayoutDigest, Is.EqualTo(digest));

            showcase.ShowTrialSelection();
            showcase.RestartRun();
            Assert.That(showcase.ActiveTrialId, Is.Null);
            Assert.That(showcase.IsTrialSelection, Is.False);
            Assert.That(showcase.CurrentArea, Is.EqualTo("Practice"));
            yield return SceneManager.LoadSceneAsync("SampleScene");
        }
    }
}
