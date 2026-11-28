using UnityEngine;
using UnityEngine.UI;

namespace BBads.Combat
{
    public sealed class BaseHealthBarView : MonoBehaviour
    {
        [SerializeField] Image fillImage;

        public void SetValue(float current, float maximum)
        {
            if (fillImage == null)
                return;
            fillImage.fillAmount = maximum <= 0f ? 0f : Mathf.Clamp01(current / maximum);
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        void Awake()
        {
            ConfigureFill();
        }

        void OnValidate()
        {
            if (fillImage == null)
            {
                Transform fill = transform.Find("Fill");
                if (fill != null)
                    fillImage = fill.GetComponent<Image>();
            }
            ConfigureFill();
        }

        void ConfigureFill()
        {
            if (fillImage == null)
                return;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillClockwise = true;
        }
    }
}
