using System;
using Test.Application.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal sealed class ContractReplayPanel : MonoBehaviour
    {
        internal const string Key = "RainPassContract/Replay";
        [SerializeField] private Text title;
        [SerializeField] private Text status;
        [SerializeField] private Button previous, next, watch, close, pause, speed, deflect, finish;
        internal Button CloseButton => close;

        internal void Bind(Action onPrevious, Action onNext, Action onWatch, Action onClose,
            Action onPause, Action onSpeed, Action onDeflect, Action onFinish)
        {
            previous.onClick.AddListener(() => onPrevious());
            next.onClick.AddListener(() => onNext());
            watch.onClick.AddListener(() => onWatch());
            close.onClick.AddListener(() => onClose());
            pause.onClick.AddListener(() => onPause());
            speed.onClick.AddListener(() => onSpeed());
            deflect.onClick.AddListener(() => onDeflect());
            finish.onClick.AddListener(() => onFinish());
        }

        internal void List(string contractTitle, string message, bool canWatch, bool multiple)
        {
            title.text = contractTitle;
            status.text = message;
            previous.gameObject.SetActive(multiple);
            next.gameObject.SetActive(multiple);
            watch.gameObject.SetActive(true);
            watch.interactable = canWatch;
            SetPlaybackButtons(false);
        }

        internal void Playing(string version, int tick, int lastTick, bool paused, bool slow, bool hasDeflect, bool hasFinish)
        {
            title.text = "Replay " + version;
            status.text = $"{tick / (float)CombatRun.StepsPerSecond:0.0}s / {lastTick / (float)CombatRun.StepsPerSecond:0.0}s";
            previous.gameObject.SetActive(false);
            next.gameObject.SetActive(false);
            watch.gameObject.SetActive(false);
            SetPlaybackButtons(true);
            pause.GetComponentInChildren<Text>().text = paused ? "Play" : "Pause";
            speed.GetComponentInChildren<Text>().text = slow ? "0.5x" : "1x";
            deflect.interactable = hasDeflect;
            finish.interactable = hasFinish;
        }

        private void SetPlaybackButtons(bool active)
        {
            pause.gameObject.SetActive(active);
            speed.gameObject.SetActive(active);
            deflect.gameObject.SetActive(active);
            finish.gameObject.SetActive(active);
        }
    }
}
