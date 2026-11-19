using UnityEngine;

namespace Test.Client
{
    public sealed class EffectShowcase : MonoBehaviour
    {
        [SerializeField] private TestEffectBehaviour[] presets;
        private TestEffectBehaviour instance;
        private int selected;

        public int Count => presets?.Length ?? 0;
        public TestEffectBehaviour Current => instance;

        private void Start()
        {
            if (instance == null && Count > 0) ShowEffect(0);
        }

        public void ShowEffect(int index)
        {
            if (index < 0 || index >= Count || presets[index] == null) return;
            if (instance != null)
            {
                instance.gameObject.SetActive(false);
                Destroy(instance.gameObject);
            }
            selected = index;
            instance = Instantiate(presets[index], transform);
            instance.Replay();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 164f, Mathf.Max(160f, Screen.height - 32f)), GUI.skin.box);
            GUILayout.Label("Effect Showcase");
            for (var index = 0; index < Count; index++)
            {
                if (presets[index] == null) continue;
                var active = selected == index;
                if (GUILayout.Button(presets[index].name, active ? GUI.skin.box : GUI.skin.button) && !active) ShowEffect(index);
            }
            if (instance != null && GUILayout.Button("Replay")) instance.Replay();
            GUILayout.EndArea();
        }
    }
}
