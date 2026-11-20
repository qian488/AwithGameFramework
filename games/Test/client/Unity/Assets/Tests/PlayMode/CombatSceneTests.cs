using System.Collections;
using NUnit.Framework;
using Test.Client.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Test.Client.Tests
{
    public sealed class CombatSceneTests
    {
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
    }
}
