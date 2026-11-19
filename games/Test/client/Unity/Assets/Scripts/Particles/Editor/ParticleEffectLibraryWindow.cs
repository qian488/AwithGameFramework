using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Test.Client.Editor
{
    internal sealed class ParticleEffectLibraryWindow : EditorWindow
    {
        private GameObject[] assets = Array.Empty<GameObject>();
        private GameObject selected;
        private EffectPreview preview;
        private GUIStyle libraryRowStyle;
        private Vector2 libraryScroll;
        private Vector2 detailsScroll;
        private string query = string.Empty;
        private string status;
        private int category;
        private bool playing;
        private bool loop = true;
        private double lastTick;

        [MenuItem("Tools/Test/Effect Library")]
        private static void Open() => GetWindow<ParticleEffectLibraryWindow>("Effect Library");

        private void OnEnable()
        {
            minSize = new Vector2(655, 455);
            Refresh();
            EditorApplication.update += UpdatePreview;
            EditorApplication.playModeStateChanged += PlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            ReleasePreview();
        }

        private void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) ReleasePreview();
            Repaint();
        }

        private void OnProjectChange()
        {
            ReleasePreview();
            Refresh();
        }

        private void Refresh()
        {
            assets = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(asset => asset != null && asset.GetComponent<TestEffectBehaviour>() != null)
                .OrderBy(asset => asset.name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (selected == null || !assets.Contains(selected)) selected = assets.FirstOrDefault();
            Repaint();
        }

        private void OnGUI()
        {
            var editMode = !EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(!editMode))
                    if (GUILayout.Button("Create Effect…", EditorStyles.toolbarDropDown, GUILayout.Width(128))) ShowCreateMenu();
                query = GUILayout.TextField(query, EditorStyles.toolbarSearchField, GUILayout.MinWidth(110));
                category = EditorGUILayout.Popup(category, new[] { "All categories" }.Concat(Enum.GetNames(typeof(ParticleEffectCategory))).ToArray(),
                    EditorStyles.toolbarPopup, GUILayout.Width(142));
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(66))) Refresh();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLibrary();
                using (var details = new EditorGUILayout.ScrollViewScope(detailsScroll, GUILayout.ExpandWidth(true)))
                {
                    detailsScroll = details.scrollPosition;
                    if (selected != null) DrawDetails(editMode);
                    else EditorGUILayout.HelpBox("Create an effect from a template to start your library.", MessageType.Info);
                }
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        private void DrawLibrary()
        {
            const float rowHeight = 26f;
            const float iconSize = 16f;
            libraryRowStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fixedHeight = 0f,
                padding = new RectOffset(32, 8, 0, 0)
            };
            var selectionColor = EditorGUIUtility.isProSkin ? new Color32(48, 79, 118, 255) : new Color32(177, 211, 246, 255);
            using var library = new EditorGUILayout.ScrollViewScope(libraryScroll, GUILayout.Width(208));
            libraryScroll = library.scrollPosition;
            foreach (var asset in assets)
            {
                var metadata = asset.GetComponent<ParticleEffectMetadata>();
                if (category != 0 && (int)GetCategory(metadata) != category - 1) continue;
                var displayName = GetDisplayName(asset, metadata);
                if (asset.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (metadata?.tags == null || !metadata.tags.Any(tag => !string.IsNullOrEmpty(tag) && tag.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))) continue;
                var thumbnail = metadata != null && metadata.thumbnail != null ? metadata.thumbnail : AssetPreview.GetMiniThumbnail(asset);
                var row = GUILayoutUtility.GetRect(0f, rowHeight, GUILayout.ExpandWidth(true));
                if (selected == asset && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(row, selectionColor);
                var content = new GUIContent(displayName, AssetDatabase.GetAssetPath(asset));
                if (GUI.Button(row, content, libraryRowStyle) && selected != asset)
                    Select(asset);
                if (thumbnail != null && Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(new Rect(row.x + 8f, row.center.y - iconSize / 2f, iconSize, iconSize), thumbnail, ScaleMode.ScaleToFit);
            }
        }

        private void DrawDetails(bool editMode)
        {
            var metadata = selected.GetComponent<ParticleEffectMetadata>();
            EditorGUILayout.LabelField(GetDisplayName(selected, metadata), EditorStyles.boldLabel);
            var modules = Describe(selected, out var description, out var issue);
            EditorGUILayout.LabelField(GetCategory(metadata) + "  /  " + modules.Length + (modules.Length == 1 ? " module" : " modules"), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
            if (issue != null) EditorGUILayout.HelpBox(issue, MessageType.Warning);

            var bounds = GUILayoutUtility.GetRect(260, 232, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                if (preview != null && bounds.width > 0f) GUI.DrawTexture(bounds, preview.Render(bounds), ScaleMode.StretchToFill);
                else
                {
                    EditorGUI.DrawRect(bounds, new Color(0.065f, 0.085f, 0.12f));
                    var label = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                    GUI.Label(bounds, "Play Preview", label);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!editMode || issue != null))
            {
                if (GUILayout.Button(playing ? "Pause" : "Play Preview", GUILayout.Width(100)))
                {
                    if (playing) playing = false;
                    else StartPreview();
                }
                using (new EditorGUI.DisabledScope(preview == null))
                    if (GUILayout.Button("Restart", GUILayout.Width(57))) ResetPreview(true);
                loop = GUILayout.Toggle(loop, "Loop", GUILayout.Width(53));
                using (new EditorGUI.DisabledScope(preview == null))
                {
                    var time = preview?.Time ?? 0f;
                    var next = GUILayout.HorizontalSlider(time, 0f, Mathf.Max(0.001f, preview?.Duration ?? 0f));
                    if (preview != null && !Mathf.Approximately(time, next))
                    {
                        playing = false;
                        if (next < time) preview.Reset(selected);
                        preview.Advance(next);
                    }
                }
            }

            foreach (var module in modules) EditorGUILayout.LabelField("• " + module, EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!editMode))
            {
                if (GUILayout.Button("Edit Prefab"))
                {
                    PrefabStageUtility.OpenPrefab(AssetDatabase.GetAssetPath(selected));
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Duplicate…")) Duplicate();
                if (GUILayout.Button("Locate")) EditorGUIUtility.PingObject(selected);
                var scene = SceneManager.GetActiveScene();
                using (new EditorGUI.DisabledScope(!scene.IsValid() || !scene.isLoaded || PrefabStageUtility.GetCurrentPrefabStage() != null))
                    if (GUILayout.Button("Add to Scene")) Place(scene);
            }
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                EditorGUILayout.LabelField("Return to the scene to place an instance.", EditorStyles.wordWrappedMiniLabel);
        }

        internal static string[] Describe(GameObject asset, out string description, out string issue)
        {
            var metadata = asset.GetComponent<ParticleEffectMetadata>();
            issue = null;
            description = metadata != null ? metadata.description : asset.name == "Formula Burst"
                ? "Composite demo combining the Spiral and Projectile Fan into one prefab. Each is also available separately."
                : "A layered burst with a central flash, rings and sparks.";
            var procedural = asset.GetComponent<ProceduralParticleEffect>();
            if (procedural != null)
            {
                using var settings = new SerializedObject(procedural);
                var layers = settings.FindProperty("layers");
                var names = new string[layers.arraySize];
                if (settings.FindProperty("material").objectReferenceValue == null) issue = "Assign a material in the prefab Inspector.";
                for (var index = 0; index < names.Length; index++)
                {
                    var layer = layers.GetArrayElementAtIndex(index);
                    var profile = layer.FindPropertyRelative("launchProfile").objectReferenceValue;
                    var enabled = layer.FindPropertyRelative("enabled").boolValue;
                    names[index] = layer.FindPropertyRelative("label").stringValue + (profile == null ? "" : "  /  Formula") + (enabled ? "" : " (off)");
                    var shape = layer.FindPropertyRelative("shape");
                    if (enabled && shape.enumNames[shape.enumValueIndex] == "Formula" && profile == null)
                        issue = "Assign a formula profile to " + names[index] + ".";
                }
                return names;
            }
            var lightning = asset.GetComponent<LightningChainEffect>();
            if (lightning != null)
            {
                if (!lightning.HasValidAnchors() || lightning.GetComponent<LineRenderer>().sharedMaterial == null)
                    issue = "Assign at least two anchors and a line material.";
                return new[] { "Bolt renderer  /  " + lightning.AnchorCount + " anchors" };
            }
            var emitters = asset.GetComponentsInChildren<ParticleSystem>(true);
            if (emitters.Length == 0) issue = "Add a Particle System to the prefab.";
            else if (emitters.Any(emitter => emitter.GetComponent<ParticleSystemRenderer>().sharedMaterial == null))
                issue = "Assign a material to each particle renderer.";
            return emitters.Select(emitter => emitter.name).ToArray();
        }

        private static ParticleEffectCategory GetCategory(ParticleEffectMetadata metadata) => metadata == null ? ParticleEffectCategory.Burst : metadata.category;

        private static string GetDisplayName(GameObject asset, ParticleEffectMetadata metadata) =>
            metadata == null || string.IsNullOrWhiteSpace(metadata.displayName) ? asset.name : metadata.displayName;

        private void ShowCreateMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(EffectPresets.CustomTemplate), false,
                () => SaveNew(EffectPresets.CustomTemplate, path => EffectPresets.Create(EffectPresets.CustomTemplate, path)));
            menu.AddSeparator(string.Empty);
            foreach (var template in EffectPresets.Templates)
                menu.AddItem(new GUIContent(template), false, () => SaveNew(template, path => EffectPresets.Create(template, path)));
            menu.ShowAsContext();
        }

        private void Duplicate()
        {
            var source = AssetDatabase.GetAssetPath(selected);
            SaveNew(selected.name + " Variant", path =>
            {
                if (!AssetDatabase.CopyAsset(source, path)) throw new IOException("Could not duplicate the effect.");
                using (var prefab = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    var metadata = prefab.prefabContentsRoot.GetComponent<ParticleEffectMetadata>();
                    if (metadata != null) metadata.displayName = Path.GetFileNameWithoutExtension(path);
                }
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            });
        }

        private void SaveNew(string name, Func<string, GameObject> save)
        {
            var path = EditorUtility.SaveFilePanelInProject("Save Effect Prefab", name, "prefab", "Choose a location for the new effect.", EffectPresets.Folder);
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                var asset = save(path);
                Refresh();
                Select(asset);
                status = "Saved " + path;
            }
            catch (Exception error) { status = error.Message; }
        }

        private void Place(Scene scene)
        {
            var instance = PlaceEffect(selected, scene);
            Selection.activeGameObject = instance;
            status = "Placed " + instance.name + " in " + scene.name + ".";
        }

        internal static GameObject PlaceEffect(GameObject asset, Scene scene)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Place effect");
            EditorSceneManager.MarkSceneDirty(scene);
            return instance;
        }

        private void Select(GameObject asset)
        {
            ReleasePreview();
            status = null;
            selected = asset;
            Repaint();
        }

        private void StartPreview()
        {
            if (preview == null || preview.Time >= preview.Duration) ResetPreview(true);
            else
            {
                playing = true;
                lastTick = EditorApplication.timeSinceStartup;
            }
        }

        private void ResetPreview(bool play)
        {
            preview ??= new EffectPreview();
            preview.Reset(selected);
            playing = play;
            lastTick = EditorApplication.timeSinceStartup;
        }

        private void UpdatePreview()
        {
            if (!playing || selected == null || preview == null) return;
            var now = EditorApplication.timeSinceStartup;
            var seconds = Mathf.Min(0.15f, (float)(now - lastTick));
            lastTick = now;
            preview.Advance(Mathf.Min(preview.Duration, preview.Time + seconds));
            if (preview.Time >= preview.Duration)
            {
                if (loop && preview.Duration > 0f) ResetPreview(true);
                else playing = false;
            }
            Repaint();
        }

        private void ReleasePreview()
        {
            playing = false;
            preview?.Dispose();
            preview = null;
        }
    }
}
