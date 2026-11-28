using BBads.Timeline;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BBads.UI
{
    [DisallowMultipleComponent]
    public sealed class TimeWavePresenter : MonoBehaviour
    {
        [SerializeField, Min(1)] int waveCount = 16;
        [SerializeField] TMP_Text waveText;
        [SerializeField] Image waveFill;

        ScenarioRunner scenarioRunner;
        int displayedWave = -1;
        float displayedProgress = -1f;

        internal void SetScenarioRunner(ScenarioRunner value) => scenarioRunner = value;

        void Awake()
        {
            waveText ??= GetComponent<TMP_Text>();
            if (waveFill == null)
            {
                Transform root = transform.parent;
                if (root != null)
                {
                    Transform fill = root.Find("WaveFill");
                    if (fill != null)
                        waveFill = fill.GetComponent<Image>();
                }
            }
            ApplyWave(1, 0f);
        }

        void Update()
        {
            if (scenarioRunner == null || scenarioRunner.Scenario == null)
                return;

            float duration = Mathf.Max(0.001f, scenarioRunner.Scenario.Duration);
            float overallProgress = Mathf.Clamp01(scenarioRunner.Elapsed / duration);
            float scaledProgress = overallProgress * waveCount;
            int wave = overallProgress >= 1f
                ? waveCount
                : Mathf.FloorToInt(scaledProgress) + 1;
            float waveProgress = overallProgress >= 1f
                ? 1f
                : scaledProgress - Mathf.Floor(scaledProgress);
            ApplyWave(wave, waveProgress);
        }

        void ApplyWave(int wave, float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (displayedWave != wave && waveText != null)
            {
                waveText.text = $"WAVE {wave}/{waveCount}";
                displayedWave = wave;
            }
            if (waveFill != null && !Mathf.Approximately(displayedProgress, progress))
            {
                waveFill.fillAmount = progress;
                displayedProgress = progress;
            }
        }
    }
}
