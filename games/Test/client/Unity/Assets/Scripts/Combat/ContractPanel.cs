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
        private Button[] buttons;
        private ContractAction primary;
        private ContractAction secondary;

        internal void Bind(Action onPrimary, Action onSecondary)
        {
            buttons = new[] { accept, cancel, retryCheckpoint, retreat, retrySave, returnToCamp, retryRead, retryReturn };
            if (heading == null || status == null || error == null || Array.Exists(buttons, button => button == null))
                throw new InvalidOperationException("The contract panel prefab has missing control references.");

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
            bool saved, string error)
        {
            switch (phase)
            {
                case ContractPhase.Opening:
                    Show("Rain Pass Contract", "Reading the completion record…",
                        error != null ? ContractAction.RetryRead : ContractAction.None, errorMessage: error);
                    break;
                case ContractPhase.Camp:
                    var state = completed ? "Completed. You can challenge Cedar Sentinel again." :
                        "Defeat Cedar Sentinel in Cedar Grove. Press Esc in battle to pause or retreat.";
                    Show("Rain Pass Contract", state, ContractAction.Accept, errorMessage: error);
                    break;
                case ContractPhase.Loading:
                    Show("Cedar Grove", cancelling ? "Cancelling…" : "Loading the route…",
                        secondaryAction: cancelling ? ContractAction.None : ContractAction.Cancel);
                    break;
                case ContractPhase.Battle:
                    if (battleStatus == RunStatus.Paused)
                        Show("Paused", "Press Esc to resume.", secondaryAction: ContractAction.Retreat);
                    else if (battleStatus == RunStatus.Defeated)
                        Show("Defeated", "Retry from the current checkpoint or return to camp.", ContractAction.RetryCheckpoint, ContractAction.Retreat);
                    else Hide();
                    break;
                case ContractPhase.Result:
                    Show("Cedar Sentinel defeated", saved ? "Completion saved." : error == null ? "Saving completion…" : "",
                        saved ? ContractAction.Return : error != null ? ContractAction.RetrySave : ContractAction.None,
                        errorMessage: error);
                    break;
                case ContractPhase.Returning:
                    Show("Returning to camp", "Closing the battle…",
                        error != null ? ContractAction.RetryReturn : ContractAction.None, errorMessage: error);
                    break;
            }
        }

        private void Show(string title, string message, ContractAction primaryAction = ContractAction.None,
            ContractAction secondaryAction = ContractAction.None, string errorMessage = null)
        {
            bool refocus = !gameObject.activeSelf || primary != primaryAction || secondary != secondaryAction;
            gameObject.SetActive(true);
            heading.text = title;
            status.text = message;
            error.text = errorMessage ?? "";
            error.gameObject.SetActive(errorMessage != null);
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
