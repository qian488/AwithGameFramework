using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Test.Client.Tests
{
    public sealed class ParticleSceneTests
    {
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator PlacedFormulaPrefabPlaysOnEnableWithoutHost()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Particles/Presets/Formula Burst.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab);
            try
            {
                var spiral = instance.transform.Find("Generated particles/Archimedean spiral").GetComponent<ParticleSystem>();
                var projectile = instance.transform.Find("Generated particles/Projectile fan").GetComponent<ParticleSystem>();
                Assert.That(spiral.particleCount, Is.EqualTo(120));
                Assert.That(projectile.particleCount, Is.Zero);
                yield return WaitFor(() => projectile.particleCount > 0);
                Assert.That(projectile.particleCount, Is.EqualTo(54));
            }
            finally
            {
                Object.Destroy(instance);
            }
        }
#endif

        [UnityTest]
        public IEnumerator ScenePlaysAndReplaysThroughExternalHostModule()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene");
            var bootstrap = Object.FindObjectOfType<TestBootstrap>();
            var effect = Object.FindObjectOfType<ProceduralParticleEffect>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(effect, Is.Not.Null);
            yield return WaitFor(() => bootstrap.IsReady && FindRing(effect) != null && FindRing(effect).particleCount > 0);

            var ring = FindRing(effect);
            Assert.That(effect.GetComponentsInChildren<ParticleSystem>().Length, Is.EqualTo(4));
            yield return WaitFor(() => ring.particleCount == 0);
            yield return WaitFor(() => ring.particleCount > 0);
            Assert.That(FindRing(effect), Is.SameAs(ring));

            var shutdown = bootstrap.ShutdownAsync();
            yield return WaitFor(() => shutdown.IsCompleted);
            shutdown.GetAwaiter().GetResult();
            yield return WaitFor(() => ring.particleCount == 0);
            yield return new WaitForSeconds(3.2f);
            Assert.That(ring.particleCount, Is.Zero);
            effect.enabled = false;
            yield return null;
            Assert.That(effect.transform.childCount, Is.Zero);
        }

        private static ParticleSystem FindRing(ProceduralParticleEffect effect) =>
            System.Array.Find(effect.GetComponentsInChildren<ParticleSystem>(), emitter => emitter.name == "Cyan shock ring");

        private static IEnumerator WaitFor(System.Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 8f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for the particle scene state.");
        }
    }
}
