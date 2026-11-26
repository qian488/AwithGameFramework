using System;
using Test.Application.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal enum ContractAction { None, Accept, Cancel, RetryCheckpoint, Retreat, RetrySave, Return, RetryRead, RetryReturn }

    internal sealed class ContractPanel : MonoBehaviour
    {
        [SerializeField] private Text heading;
        [SerializeField] private Text status;
        [SerializeField] private Text error;
        [SerializeField] private Button accept;
        [SerializeField] private Button cancel;
        [SerializeField] private Button retryCheckpoint;
        [SerializeField] private Button retreat;
        [SerializeField] private Button retrySave;
        [SerializeField] private Button returnToCamp;
        [SerializeField] private Button retryRead;
        [SerializeField] private Button retryReturn;
        [SerializeField] private Button settings;
        private Button[] buttons;
        private ContractAction primary;
        private ContractAction secondary;
        private bool showingOffer;
        private Text acceptLabel, retreatLabel;
        private string offerText;
        private string[] buttonText;
        private Text[] buttonLabels;
        private Func<string, string> translate;
        private Font localizedFont;

        internal void Bind(Action onPrimary, Action onSecondary, Action onSettings = null,
            Func<string, string> lookup = null, Font font = null)
        {
            translate = lookup ?? (value => value);
            localizedFont = font;
            buttons = new[] { accept, cancel, retryCheckpoint, retreat, retrySave, returnToCamp, retryRead, retryReturn };
            if (heading == null || status == null || error == null || Array.Exists(buttons, button => button == null))
                throw new InvalidOperationException("The contract panel prefab has missing control references.");
            acceptLabel = accept.GetComponentInChildren<Text>(true);
            retreatLabel = retreat.GetComponentInChildren<Text>(true);
            buttonLabels = Array.ConvertAll(buttons, button => button.GetComponentInChildren<Text>(true));
            buttonText = Array.ConvertAll(buttonLabels, label => label.text);
            if (settings != null)
            {
                settings.gameObject.SetActive(false);
                if (onSettings != null) settings.onClick.AddListener(() => onSettings());
            }
            if (localizedFont != null)
                foreach (var label in GetComponentsInChildren<Text>(true)) label.font = localizedFont;

            for (int index = 0; index < buttons.Length; index++)
            {
                var action = (ContractAction)(index + 1);
                buttons[index].gameObject.SetActive(false);
                buttons[index].onClick.AddListener(() =>
                {
                    if (primary == action) onPrimary();
                    else if (secondary == action) onSecondary();
                });
            }
        }

        internal void Render(ContractPhase phase, bool completed, bool cancelling, RunStatus? battleStatus,
            bool saved, string error, string contractTitle = null, string pauseMessage = null, string pauseKey = "Esc",
            string scoreStatus = null)
        {
            showingOffer = false;
            switch (phase)
            {
                case ContractPhase.Opening:
                    Show("Rain Pass Contract", "Reading the completion record…",
                        error != null ? ContractAction.RetryRead : ContractAction.None, errorMessage: error);
                    break;
                case ContractPhase.Camp:
                    var state = completed ? "Completed. You can challenge Cedar Sentinel again." :
                        string.Format(translate("Defeat Cedar Sentinel in Cedar Grove. Press {0} in battle to pause or retreat."), pauseKey);
                    Show("Rain Pass Contract", state, ContractAction.Accept, errorMessage: error);
                    break;
                case ContractPhase.Loading:
                    Show(contractTitle ?? "Cedar Grove", cancelling ? "Cancelling…" : "Loading the route…",
                        secondaryAction: cancelling ? ContractAction.None : ContractAction.Cancel);
                    break;
                case ContractPhase.Battle:
                    if (battleStatus == RunStatus.Paused)
                        Show("Paused", string.Format(translate("Press {0} to resume."), pauseKey) +
                            (pauseMessage == null ? "" : " " + translate(pauseMessage)), secondaryAction: ContractAction.Retreat, errorMessage: error);
                    else if (battleStatus == RunStatus.Defeated)
                        Show("Defeated", "Retry from the current checkpoint or return to camp.", ContractAction.RetryCheckpoint, ContractAction.Retreat);
                    else Hide();
                    break;
                case ContractPhase.Result:
                    Show(contractTitle == null ? "Cedar Sentinel defeated" : string.Format(translate("{0} complete"), translate(contractTitle)),
                        saved ? scoreStatus ?? "Completion saved." : error == null ? "Saving completion…" : "",
                        saved ? ContractAction.Return : error != null ? ContractAction.RetrySave : ContractAction.None,
                        errorMessage: error);
                    break;
                case ContractPhase.Returning:
                    Show("Returning to camp", "Closing the battle…",
                        error != null ? ContractAction.RetryReturn : ContractAction.None, errorMessage: error);
                    break;
            }
        }

        internal void ShowOffer(string title, string message, string primaryLabel, bool busy, bool canCycle, string issue)
        {
            showingOffer = true;
            offerText = primaryLabel;
            Show(title, message, busy ? ContractAction.None : ContractAction.Accept,
                busy || !canCycle ? ContractAction.None : ContractAction.Retreat, issue);
        }

        internal void SetSettingsAvailable(bool available)
        {
            if (settings == null) return;
            settings.gameObject.SetActive(available);
            if (available) settings.GetComponentInChildren<Text>().text = translate("Settings");
        }

        private void Show(string title, string message, ContractAction primaryAction = ContractAction.None,
            ContractAction secondaryAction = ContractAction.None, string errorMessage = null)
        {
            bool refocus = !gameObject.activeSelf || primary != primaryAction || secondary != secondaryAction;
            gameObject.SetActive(true);
            heading.text = translate(title);
            status.text = translate(message);
            error.text = translate(errorMessage ?? "");
            error.gameObject.SetActive(errorMessage != null);
            for (int index = 0; index < buttons.Length; index++)
                buttonLabels[index].text = translate(buttonText[index]);
            if (showingOffer)
            {
                acceptLabel.text = translate(offerText);
                retreatLabel.text = translate("Next contract");
            }
            if (!refocus) return;

            primary = primaryAction;
            secondary = secondaryAction;
            for (int index = 0; index < buttons.Length; index++)
            {
                var action = (ContractAction)(index + 1);
                buttons[index].gameObject.SetActive(action == primary || action == secondary);
            }
            var events = EventSystem.current;
            if (events == null) return;
            var focused = primary != ContractAction.None ? primary : secondary;
            events.SetSelectedGameObject(focused == ContractAction.None ? null : buttons[(int)focused - 1].gameObject);
        }

        private void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
