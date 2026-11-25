using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal sealed class ContractPanel
    {
        private readonly RectTransform root;
        private readonly Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        private readonly Text heading;
        private readonly Text body;
        private readonly Button primary;
        private readonly Button secondary;

        public ContractPanel(RectTransform root, Action onPrimary, Action onSecondary)
        {
            this.root = root;
            root.gameObject.AddComponent<Image>().color = new Color(.025f, .045f, .05f, .88f);
            var card = Box(root, "Contract card", new Vector2(.12f, .5f), new Vector2(.88f, .5f),
                new Vector2(0, -132), new Vector2(0, 132));
            card.gameObject.AddComponent<Image>().color = new Color(.075f, .115f, .13f);
            heading = Label(card, "Heading", 34, new Vector2(28, -85), new Vector2(-28, -22));
            heading.fontStyle = FontStyle.Bold;
            body = Label(card, "Status", 22, new Vector2(28, -173), new Vector2(-28, -91));
            body.color = new Color(.78f, .86f, .85f);
            primary = ActionButton(card, "Primary", 0f, .5f, onPrimary);
            secondary = ActionButton(card, "Secondary", .5f, 1f, onSecondary);
            primary.gameObject.SetActive(false);
            secondary.gameObject.SetActive(false);
        }

        public void Show(string title, string message, string primaryAction = null, string secondaryAction = null)
        {
            bool refocus = !root.gameObject.activeSelf || primary.gameObject.activeSelf != (primaryAction != null)
                || secondary.gameObject.activeSelf != (secondaryAction != null);
            root.gameObject.SetActive(true);
            heading.text = title;
            body.text = message;
            SetAction(primary, primaryAction);
            SetAction(secondary, secondaryAction);
            if (refocus && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(primaryAction != null ? primary.gameObject :
                    secondaryAction != null ? secondary.gameObject : null);
        }

        public void Hide()
        {
            if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        private Button ActionButton(RectTransform card, string name, float left, float right, Action click)
        {
            var rect = Box(card, name, new Vector2(left, 0), new Vector2(right, 0),
                new Vector2(left == 0 ? 28 : 6, 25), new Vector2(right == 1 ? -28 : -6, 70));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = name == "Primary" ? new Color(.17f, .41f, .37f) : new Color(.19f, .25f, .28f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => click());
            var label = Label(rect, "Label", 19, Vector2.zero, Vector2.zero);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private Text Label(RectTransform parent, string name, int size, Vector2 min, Vector2 max)
        {
            var label = Box(parent, name, new Vector2(0, 1), Vector2.one, min, max).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = new Color(.93f, .96f, .92f);
            label.raycastTarget = false;
            return label;
        }

        private static void SetAction(Button button, string label)
        {
            button.gameObject.SetActive(label != null);
            if (label != null) button.GetComponentInChildren<Text>().text = label;
        }

        private static RectTransform Box(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }
    }
}
