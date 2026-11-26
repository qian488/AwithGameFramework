using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Test.Client.Expedition
{
    public sealed class ExpeditionPanel : MonoBehaviour
    {
        public const string Key = "Expedition/Panel";

        [SerializeField] private Text heading;
        [SerializeField] private Text statusText;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private Button actionTemplate;

        private readonly List<ActionButton> buttons = new List<ActionButton>();
        private GameObject revealedSelection;

        public void Render(string title, string status, IReadOnlyList<(string Label, Action Execute)> actions)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));

            bool layoutChanged = heading.text != title || statusText.text != status;
            heading.text = title;
            statusText.text = status;
            statusText.gameObject.SetActive(!string.IsNullOrEmpty(status));

            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            Button first = null;
            Button previous = null;
            Button retained = null;

            for (int index = 0; index < actions.Count; index++)
            {
                if (index == buttons.Count)
                    buttons.Add(new ActionButton(Instantiate(actionTemplate, scroll.content, false)));

                var entry = buttons[index];
                var (label, execute) = actions[index];
                bool sameLabel = entry.Label.text == label;
                layoutChanged |= !sameLabel || !entry.Button.gameObject.activeSelf;
                entry.Label.text = label;
                entry.Execute = execute;
                entry.Button.interactable = execute != null;
                entry.Button.navigation = new Navigation { mode = Navigation.Mode.Explicit };
                entry.Button.gameObject.SetActive(true);
                if (execute == null) continue;

                if (first == null) first = entry.Button;
                if (previous != null) Link(previous, entry.Button);
                previous = entry.Button;
                if (sameLabel && selected == entry.Button.gameObject) retained = entry.Button;
            }

            for (int index = actions.Count; index < buttons.Count; index++)
            {
                var entry = buttons[index];
                layoutChanged |= entry.Button.gameObject.activeSelf;
                entry.Execute = null;
                entry.Button.gameObject.SetActive(false);
            }

            if (layoutChanged) revealedSelection = null;

            if (events == null || !isActiveAndEnabled) return;
            if (first != null && first.IsInteractable())
                events.SetSelectedGameObject((retained ?? first).gameObject);
            else if (selected != null && selected.transform.parent == scroll.content)
                events.SetSelectedGameObject(null);
        }

        private static void Link(Button above, Button below)
        {
            var aboveNavigation = above.navigation;
            aboveNavigation.selectOnDown = below;
            above.navigation = aboveNavigation;

            var belowNavigation = below.navigation;
            belowNavigation.selectOnUp = above;
            below.navigation = belowNavigation;
        }

        private void LateUpdate()
        {
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == revealedSelection) return;
            revealedSelection = selected;
            if (selected == null || selected.transform.parent != scroll.content) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            var viewport = scroll.viewport;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, selected.transform);
            float offset = bounds.max.y > viewport.rect.yMax
                ? viewport.rect.yMax - bounds.max.y
                : Mathf.Max(0, viewport.rect.yMin - bounds.min.y);
            if (offset == 0) return;

            float scrollableHeight = scroll.content.rect.height - viewport.rect.height;
            if (scrollableHeight <= 0) return;
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition - offset / scrollableHeight);
        }

        private sealed class ActionButton
        {
            public readonly Button Button;
            public readonly Text Label;
            public Action Execute;

            public ActionButton(Button button)
            {
                Button = button;
                Label = button.GetComponentInChildren<Text>(true);
                button.onClick.AddListener(() => Execute?.Invoke());
            }
        }
    }
}
