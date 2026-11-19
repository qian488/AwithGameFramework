using UnityEditor;
using UnityEngine;

namespace Test.Client.Editor
{
    [CustomEditor(typeof(ProceduralParticleEffect))]
    internal sealed class ProceduralParticleEffectEditor : UnityEditor.Editor
    {
        private double lastStep;
        private float remaining;

        public override void OnInspectorGUI()
        {
            var changed = DrawDefaultInspector();
            var effect = (ProceduralParticleEffect)target;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Open Particle Effect Library"))
                    EditorApplication.ExecuteMenuItem("Tools/Test/Particle Effect Library");
            }

            if (EditorUtility.IsPersistent(effect))
            {
                EditorGUILayout.HelpBox("Open the prefab in Prefab Mode or place it in a scene to preview.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || !effect.isActiveAndEnabled))
            {
                if (GUILayout.Button("Preview in Scene View") || (changed && remaining > 0f)) StartPreview(effect);
                using (new EditorGUI.DisabledScope(remaining <= 0f))
                {
                    if (GUILayout.Button("Stop Preview")) StopPreview();
                }
            }
        }

        private void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            StopPreview();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) StopPreview();
        }

        private void StartPreview(ProceduralParticleEffect effect)
        {
            StopPreview();
            remaining = effect.BeginPreview();
            if (remaining <= 0f) return;
            lastStep = EditorApplication.timeSinceStartup;
            EditorApplication.update += UpdatePreview;
        }

        private void UpdatePreview()
        {
            if (!(target is ProceduralParticleEffect effect) || effect == null || !effect.isActiveAndEnabled || remaining <= 0f)
            {
                StopPreview();
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var step = Mathf.Min((float)(now - lastStep), 0.05f);
            lastStep = now;
            remaining -= step;
            effect.StepPreview(step);
            SceneView.RepaintAll();
        }

        private void StopPreview()
        {
            EditorApplication.update -= UpdatePreview;
            remaining = 0f;
            if (target is ProceduralParticleEffect effect && effect != null) effect.EndPreview();
            SceneView.RepaintAll();
        }
    }
}
