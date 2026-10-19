using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Extensions.Presentation;
using AwithGameFrame.Samples.Shared;
using AwithGameFrame.Unity.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace AwithGameFrame.Samples
{
    /// <summary>Sample-only native controls. Production layouts can use authored prefabs with the same hooks.</summary>
    public sealed class DemoUnityView : UnityViewBehaviour
    {
        private DemoExperience experience;
        private string kind;
        private ViewBindingScope bindings;
        private readonly List<IDisposable> textBindings = new List<IDisposable>();
        private Font font;
        private RectTransform panel;
        private Text status, error;
        public void Configure(DemoExperience experience, string kind) { this.experience = experience; this.kind = kind; }

        public override Task ShowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (experience == null) throw new InvalidOperationException("Demo native view requires its shared application.");
            var viewRect = (RectTransform)transform;
            viewRect.anchorMin = Vector2.zero; viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero; viewRect.offsetMax = Vector2.zero;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 18);
            var root = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel = (RectTransform)root.transform; panel.SetParent(transform, false);
            panel.anchorMin = new Vector2(0, 1); panel.anchorMax = new Vector2(0, 1); panel.pivot = new Vector2(0, 1);
            panel.anchoredPosition = new Vector2(36, -36); panel.sizeDelta = new Vector2(740, kind == "settings" ? 580 : 430);
            root.GetComponent<Image>().color = new Color(0.10f, 0.14f, 0.21f, 0.98f);
            var layout = root.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(22, 22, 18, 18);
            layout.spacing = 10; layout.childControlHeight = true; layout.childForceExpandHeight = false;
            var title = AddText(42); Localize(kind, title);
            status = AddText(84); error = AddText(42); error.color = new Color(1, 0.55f, 0.45f);
            bindings = new ViewBindingScope();
            bindings.Bind(experience.Status, nameof(experience.Status.Value), () => experience.Status.Value, value => status.text = value);
            bindings.Bind(experience.Error, nameof(experience.Error.Value), () => experience.Error.Value, value => error.text = value);
            if (kind == "menu") AddButton("enter", experience.EnterLevelAsync);
            if (kind == "playing") AddButton("complete", experience.CompleteLevelAsync);
            if (kind == "settings")
            {
                AddButton("language", experience.ToggleLanguageAsync); AddButton("volume", experience.CycleVolumeAsync);
                AddButton("mute", experience.ToggleMuteAsync); AddButton("binding", experience.ToggleBindingAsync);
                AddButton("save.close", experience.CloseSettingsAsync);
            }
            else AddButton("open.settings", experience.OpenSettingsAsync);
            Localize("hint", AddText(64));
            return Task.CompletedTask;
        }

        private Text AddText(float height)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(panel, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var text = go.GetComponent<Text>(); text.font = font; text.fontSize = 18; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.raycastTarget = false;
            return text;
        }
        private void Localize(string key, Text text) => textBindings.Add(experience.Localization.BindText(key, value => text.text = value));
        private void AddButton(string key, Func<Task> action)
        {
            var go = new GameObject(key, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(panel, false); go.GetComponent<LayoutElement>().preferredHeight = 40;
            var image = go.GetComponent<Image>(); image.color = new Color(0.19f, 0.33f, 0.49f);
            var button = go.GetComponent<Button>(); button.targetGraphic = image;
            // Keyboard actions belong to InputService so rebound controls and modal consumption share one path.
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)); label.transform.SetParent(go.transform, false);
            var rect = (RectTransform)label.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = label.GetComponent<Text>(); text.font = font; text.fontSize = 18; text.color = Color.white; text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false; Localize(key, text);
            button.onClick.AddListener(() => experience.Request(action));
        }
        public override bool HandleInput(ViewInput input) => experience.HandleViewInput(kind, input);
        public override Task HideAsync(CancellationToken cancellationToken) { ReleaseBindings(); return Task.CompletedTask; }
        private void ReleaseBindings()
        {
            bindings?.Dispose(); bindings = null;
            foreach (var binding in textBindings) binding.Dispose(); textBindings.Clear();
        }
        private void OnDestroy() { ReleaseBindings(); if (font != null) Destroy(font); }
    }
}
