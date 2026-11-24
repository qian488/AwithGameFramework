using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AwithGameFrame.Unity.Editor
{
    [Serializable]
    internal sealed class AutomationSceneHealth
    {
        public int objectCount;
        public int enabledCameraCount;
        public int issueCount;
        public AutomationSceneIssue[] issues;
    }

    [Serializable]
    internal sealed class AutomationSceneIssue
    {
        public string kind;
        public string path;
        public string component;
        public string property;
    }

    internal static class EditorAutomationSceneHealth
    {
        internal static AutomationResponse Inspect(Scene scene, int limit)
        {
            var response = EditorAutomationActions.Result(scene);
            var health = new AutomationSceneHealth();
            var issues = new List<AutomationSceneIssue>(limit);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var gameObject = transform.gameObject;
                    if ((gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) continue;
                    health.objectCount++;
                    string path = null;
                    foreach (var component in gameObject.GetComponents<Component>())
                    {
                        if (component == null)
                        {
                            AddIssue("missing-script", null, null);
                            continue;
                        }

                        if (component is Camera camera && camera.isActiveAndEnabled) health.enabledCameraCount++;
                        using var serialized = new SerializedObject(component);
                        using var property = serialized.GetIterator();
                        var visitedReferences = new HashSet<long>();
                        bool enterChildren = true;
                        while (property.NextVisible(enterChildren))
                        {
                            enterChildren = property.propertyType != SerializedPropertyType.ManagedReference ||
                                (property.managedReferenceValue != null && visitedReferences.Add(property.managedReferenceId));
                            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                property.objectReferenceInstanceIDValue != 0 && property.objectReferenceValue == null)
                                AddIssue("missing-reference", component.GetType().FullName, property.propertyPath);
                        }
                    }

                    void AddIssue(string kind, string component, string property)
                    {
                        health.issueCount++;
                        if (issues.Count == limit) return;
                        path ??= EditorAutomationActions.GetHierarchyPath(transform);
                        issues.Add(new AutomationSceneIssue { kind = kind, path = path, component = component, property = property });
                    }
                }
            }
            health.issues = issues.ToArray();
            response.health = health;
            response.truncated = health.issueCount > issues.Count;
            return response;
        }

    }
}
