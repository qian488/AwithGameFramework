using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using BBads.Data;
using BBads.Timeline;
using BBads.UI;
using NUnit.Framework;
using Test.Client.BBads;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class BBadsContentValidation
{
    [UnityTest]
    public IEnumerator ReadsImportedBattleCsv()
    {
        yield return SceneManager.LoadSceneAsync(BBadsSceneActions.BattleScene);
        var data = Object.FindFirstObjectByType<CsvRuntimeData>();
        Assert.That(data, Is.Not.Null);
        Assert.That(data.Units.TryGet("PlayerWarrior", out var warrior), Is.True);
        Assert.That(warrior.maxHealth, Is.EqualTo(100f));
        Assert.That(data.Scenario.EnemySpawns.Count, Is.EqualTo(96));
        Debug.Log("BBADS_CONTENT_CSV PlayerWarrior hp=100 enemyRows=96");
    }

    [UnityTest]
    public IEnumerator CapturesWaveHud()
    {
        yield return SceneManager.LoadSceneAsync(BBadsSceneActions.BattleScene);
        var runner = Object.FindFirstObjectByType<ScenarioRunner>();
        var presenter = Object.FindFirstObjectByType<TimeWavePresenter>();
        Assert.That(runner, Is.Not.Null);
        Assert.That(presenter, Is.Not.Null);
        var text = presenter.GetComponent<TMP_Text>();
        var fill = presenter.transform.parent.Find("WaveFill").GetComponent<Image>();
        Assert.That(text, Is.Not.Null);
        Assert.That(fill.sprite, Is.Not.Null);
        Assert.That(text.text, Is.EqualTo("WAVE 1/16"));
        runner.enabled = false;
        runner.ResetRun();
        runner.Play();
        runner.Tick(15.5f);
        yield return null;
        yield return null;
        Assert.That(text.text, Is.EqualTo("WAVE 2/16"));
        Assert.That(runner.Scenario.Duration, Is.EqualTo(160f));
        Assert.That(fill.fillAmount, Is.EqualTo(0.55f).Within(0.0001f));
        Debug.Log(string.Format(CultureInfo.InvariantCulture,
            "BBADS_CONTENT_HUD elapsed={0:F2} duration={1:F2} label={2} fill={3:F6} sprite={4}",
            runner.Elapsed, runner.Scenario.Duration, text.text, fill.fillAmount, fill.sprite.name));
        string output = Environment.GetEnvironmentVariable("BBADS_CONTENT_CAPTURES");
        Assert.That(output, Is.Not.Null.And.Not.Empty);
        MethodInfo capture = typeof(BBadsSmokeProbe).GetMethod("CaptureBattle", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(capture, Is.Not.Null);
        void Capture(string stage) => capture.Invoke(null, new object[] { output, "hud-" + stage, new Vector2Int(540, 960) });
        Capture("clean");
        text.enabled = false;
        try { Capture("hidden"); }
        finally { text.enabled = true; }
        Capture("restored");
    }
}
