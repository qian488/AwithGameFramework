using System;
using System.Globalization;
using System.Text;
using BBads.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BBads.Backpack
{
    [DisallowMultipleComponent]
    public sealed class CardDragInfoView : MonoBehaviour
    {
        [Serializable]
        sealed class StatSlot
        {
            public GameObject root;
            public Image icon;
            public TMP_Text valueText;
        }

        [Header("Scene References")]
        [SerializeField] TMP_Text titleText;
        [SerializeField] StatSlot[] statSlots = new StatSlot[0];
        [SerializeField] Sprite attackIcon;
        [SerializeField] Sprite fishIcon;
        [SerializeField] Sprite healthIcon;
        [SerializeField] Sprite cooldownIcon;

        [Header("Designer Layout")]
        [SerializeField, Min(1f)] float panelHeight = 130f;
        [SerializeField, Min(1f)] float titleWidth = 290f;
        [SerializeField, Min(1f)] float statWidth = 150f;
        [SerializeField, Min(0f)] float horizontalPadding = 20f;

        UnitCatalog unitCatalog;

        public void Configure(UnitCatalog value)
        {
            unitCatalog = value;
        }

        public void Show(ItemDefinition item)
        {
            if (item == null || item.type == ItemType.None || item.type == ItemType.Slot)
            {
                Hide();
                return;
            }

            ResetSlots();
            UnitDefinition unit = ResolveUnit(item);
            int statCount = 0;
            switch (item.type)
            {
                case ItemType.Cat:
                    SetStat(statCount++, fishIcon, $"x{FormatNumber(item.fishCost)}");
                    SetStat(statCount++, attackIcon, FormatUnitValue(unit, true));
                    SetStat(statCount++, healthIcon, FormatUnitValue(unit, false));
                    break;
                case ItemType.Fish:
                    SetStat(statCount++, cooldownIcon, $"{item.cooldown.ToString("0.0", CultureInfo.InvariantCulture)}s");
                    break;
                case ItemType.Weapon:
                    SetStat(statCount++, attackIcon, FormatUnitValue(unit, true));
                    SetStat(statCount++, cooldownIcon, $"{item.cooldown.ToString("0.0", CultureInfo.InvariantCulture)}s");
                    SetStat(statCount++, healthIcon, FormatUnitValue(unit, false));
                    break;
                case ItemType.Character:
                    SetStat(statCount++, healthIcon, FormatUnitValue(unit, false));
                    break;
            }

            if (statCount == 0)
            {
                Hide();
                return;
            }

            if (titleText != null)
            {
                titleText.richText = true;
                titleText.text = BuildTitle(item);
            }
            ApplyLayout(statCount);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        UnitDefinition ResolveUnit(ItemDefinition item)
        {
            if (item == null || unitCatalog == null || string.IsNullOrWhiteSpace(item.summonUnitId))
                return null;
            return unitCatalog.TryGet(item.summonUnitId, out UnitDefinition unit) ? unit : null;
        }

        void ResetSlots()
        {
            if (statSlots == null)
                return;
            foreach (StatSlot slot in statSlots)
                if (slot?.root != null)
                    slot.root.SetActive(false);
        }

        void SetStat(int index, Sprite icon, string value)
        {
            if (statSlots == null || index < 0 || index >= statSlots.Length || statSlots[index] == null)
                return;
            StatSlot slot = statSlots[index];
            if (slot.root != null)
                slot.root.SetActive(true);
            if (slot.icon != null)
            {
                slot.icon.sprite = icon;
                slot.icon.enabled = icon != null;
                slot.icon.preserveAspect = true;
            }
            if (slot.valueText != null)
                slot.valueText.text = value;
        }

        void ApplyLayout(int statCount)
        {
            RectTransform panel = transform as RectTransform;
            if (panel == null)
                return;
            float width = horizontalPadding * 2f + titleWidth + statWidth * statCount;
            panel.sizeDelta = new Vector2(width, panelHeight);

            if (titleText != null)
            {
                RectTransform titleRect = titleText.rectTransform;
                titleRect.sizeDelta = new Vector2(titleWidth, panelHeight - 20f);
                titleRect.anchoredPosition = new Vector2(
                    -width * 0.5f + horizontalPadding + titleWidth * 0.5f,
                    0f);
            }

            for (int i = 0; i < statCount && i < statSlots.Length; i++)
            {
                if (statSlots[i]?.root == null)
                    continue;
                RectTransform slotRect = statSlots[i].root.transform as RectTransform;
                if (slotRect == null)
                    continue;
                slotRect.sizeDelta = new Vector2(statWidth, panelHeight - 20f);
                slotRect.anchoredPosition = new Vector2(
                    -width * 0.5f + horizontalPadding + titleWidth + statWidth * (i + 0.5f),
                    0f);
            }
        }

        static string BuildTitle(ItemDefinition item)
        {
            string source = !string.IsNullOrWhiteSpace(item.mergeGroup)
                ? SplitPascalCase(item.mergeGroup)
                : StripLevelSuffix(item.displayName);
            if (string.IsNullOrWhiteSpace(source))
                source = item.id;
            string suffix = item.type == ItemType.Fish
                ? $"x{Mathf.Max(1, item.mergeLevel)}"
                : $"LV.{Mathf.Max(1, item.mergeLevel)}";
            return $"{source.ToUpperInvariant()} <color=#25FF20>{suffix}</color>";
        }

        static string SplitPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            StringBuilder result = new StringBuilder(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (i > 0 && char.IsUpper(current) && !char.IsWhiteSpace(value[i - 1]))
                    result.Append(' ');
                result.Append(current);
            }
            return result.ToString();
        }

        static string StripLevelSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            int levelIndex = value.LastIndexOf(" Lv.", StringComparison.OrdinalIgnoreCase);
            return levelIndex >= 0 ? value.Substring(0, levelIndex) : value;
        }

        static string FormatUnitValue(UnitDefinition unit, bool attack)
        {
            if (unit == null)
                return "--";
            return FormatNumber(attack ? unit.attack : unit.maxHealth);
        }

        static string FormatNumber(float value)
        {
            if (Mathf.Approximately(value, Mathf.Round(value)))
                return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
