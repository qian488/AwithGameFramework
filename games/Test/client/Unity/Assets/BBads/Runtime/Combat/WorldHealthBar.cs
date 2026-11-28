using BBads.Data;
using UnityEngine;

namespace BBads.Combat
{
    public sealed class WorldHealthBar : MonoBehaviour
    {
        [SerializeField, HideInInspector] Vector2 size = new Vector2(0.3f, 0.05f);
        [SerializeField, HideInInspector] Vector3 offset = new Vector3(0f, 0.45f, 0f);
        [SerializeField, HideInInspector] Color backgroundColor = Color.white;

        Transform barRoot;
        Transform fill;
        SpriteRenderer backgroundRenderer;
        SpriteRenderer fillRenderer;
        Vector3 fillBaseScale = Vector3.one;
        float lastRatio = 1f;
        UnitCamp camp = UnitCamp.Player;
        static Sprite pixel;

        public void SetCamp(UnitCamp value)
        {
            camp = value;
            Ensure();
            ApplyCampSprite();
        }

        public void SetValue(float current, float maximum)
        {
            Ensure();
            lastRatio = maximum <= 0f ? 0f : Mathf.Clamp01(current / maximum);
            Apply();
        }

        public void SetOffsetY(float value)
        {
            offset.y = value;
            Ensure();
            barRoot.localPosition = offset;
        }

        public void ConfigureLayout(Vector2 valueSize, Vector2 valueOffset)
        {
            size = new Vector2(
                Mathf.Max(0.01f, valueSize.x),
                Mathf.Max(0.01f, valueSize.y));
            offset = new Vector3(valueOffset.x, valueOffset.y, 0f);
            Ensure();
            ApplyLayout();
        }

        public void SetVisible(bool visible)
        {
            Ensure();
            barRoot.gameObject.SetActive(visible);
        }

        void Awake() => Ensure();

        void Ensure()
        {
            if (barRoot != null)
                return;

            barRoot = new GameObject("HealthBar").transform;
            barRoot.SetParent(transform, false);
            barRoot.localPosition = offset;

            backgroundRenderer = CreatePart(
                "Background",
                backgroundColor,
                Resources.Load<Sprite>("BBads/HealthBar/Unit_HealthBar"),
                100);
            fillRenderer = CreatePart("Fill", Color.white, LoadFillSprite(), 101);
            fill = fillRenderer.transform;
            ApplyLayout();
            barRoot.gameObject.SetActive(false);
            Apply();
        }

        void ApplyLayout()
        {
            if (barRoot != null)
                barRoot.localPosition = offset;
            if (backgroundRenderer != null)
                backgroundRenderer.transform.localScale = ScaleFor(backgroundRenderer.sprite);
            if (fillRenderer != null)
                fillBaseScale = ScaleFor(fillRenderer.sprite);
            Apply();
        }

        void ApplyCampSprite()
        {
            if (fillRenderer == null)
                return;
            fillRenderer.sprite = LoadFillSprite() ?? Pixel;
            fillRenderer.color = Color.white;
            fillBaseScale = ScaleFor(fillRenderer.sprite);
            Apply();
        }

        Sprite LoadFillSprite()
        {
            string resourcePath = camp == UnitCamp.Enemy
                ? "BBads/HealthBar/Unit_HealthBar1"
                : "BBads/HealthBar/Unit_HealthBar2";
            return Resources.Load<Sprite>(BBadsResource.Key(resourcePath));
        }

        Vector3 ScaleFor(Sprite sprite)
        {
            if (sprite == null || sprite.bounds.size.x <= 0f || sprite.bounds.size.y <= 0f)
                return new Vector3(size.x, size.y, 1f);
            return new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
        }

        SpriteRenderer CreatePart(string name, Color color, Sprite sprite, int sortingOrder)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(barRoot, false);
            SpriteRenderer renderer = part.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite != null ? sprite : Pixel;
            renderer.color = color;
            renderer.sortingLayerName = "ui";
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        void Apply()
        {
            if (fill == null)
                return;
            fill.localPosition = new Vector3((lastRatio - 1f) * size.x * 0.5f, 0f, 0f);
            fill.localScale = new Vector3(fillBaseScale.x * lastRatio, fillBaseScale.y, 1f);
        }

        static Sprite Pixel
        {
            get
            {
                if (pixel == null)
                    pixel = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                return pixel;
            }
        }
    }
}
