using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AwithGameFrame.Unity.Editor
{
    internal static class EditorAutomationActions
    {
        internal static AutomationResponse Execute(AutomationRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Run automation in Edit Mode and exit Prefab Mode first.");

            var scene = SceneManager.GetActiveScene();
            if (request.action == "project.info")
            {
                var response = Result(scene);
                response.unityVersion = Application.unityVersion;
                response.projectPath = Path.GetDirectoryName(Application.dataPath);
                return response;
            }

            if (request.action == "audit.list")
            {
                var response = Result(scene);
                response.audit = EditorAutomationAudit.Read(request.limit);
                return response;
            }

            if (request.action == "performance.inspect")
            {
                var response = Result(scene);
                response.performance = new AutomationPerformance
                {
                    capturedUtc = DateTimeOffset.UtcNow.ToString("O"),
                    unityVersion = Application.unityVersion,
                    batchMode = Application.isBatchMode,
                    scene = scene.path,
                    sceneDirty = scene.isDirty,
                    rootGameObjects = scene.IsValid() && scene.isLoaded ? scene.rootCount : 0,
                    gameObjects = Resources.FindObjectsOfTypeAll<GameObject>().Length,
                    components = Resources.FindObjectsOfTypeAll<Component>().Length,
                    totalAllocatedMemory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                    totalReservedMemory = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong(),
                    totalUnusedReservedMemory = UnityEngine.Profiling.Profiler.GetTotalUnusedReservedMemoryLong(),
                    monoUsedMemory = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(),
                    monoHeapSize = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong()
                };
                return response;
            }

            if (request.action == "asset.find") return FindAssets(request, scene);
            if (request.action == "prefab.inspect") return InspectPrefab(request, scene);
            if (request.action == "scene.transaction") return Transaction(request, scene);
            if (request.action.StartsWith("checkpoint.", StringComparison.Ordinal)) return EditorAutomationCheckpoints.Execute(request, scene);
            if (request.action == "scene.open") return Result(OpenScene(request.scene));
            if (request.action == "scene.create") return Result(CreateScene(request.scene));
            if (request.action == "scene.save")
            {
                if (request.batch) throw new InvalidOperationException("In batch mode, use -Save on the mutation that creates the change.");
                RequireScene(scene, request.scene, false);
                SaveScene(scene);
                return Result(scene);
            }

            var modifies = request.action == "object.create" || request.action == "object.transform" ||
                           request.action == "prefab.place" || request.action == "component.add" ||
                           (request.action == "component.set" && !request.preview);
            var reads = request.action == "scene.inspect" || request.action == "scene.health" || request.action == "component.inspect" ||
                        (request.action == "component.set" && request.preview);
            if (!modifies && !reads) throw new ArgumentException("Unknown editor action: " + request.action);
            if (modifies && request.batch && (!request.save || string.IsNullOrEmpty(request.scene)))
                throw new ArgumentException("Batch mutations require a saved -Scene and explicit -Save.");

            if (request.batch && !string.IsNullOrEmpty(request.scene)) scene = OpenScene(request.scene);
            RequireScene(scene, request.scene, true);
            if (request.action == "scene.inspect") return Inspect(request, scene);
            if (request.action == "scene.health")
                return EditorAutomationSceneHealth.Inspect(scene, ResultLimit(request.limit));
            if (!modifies) return ComponentProperties(request, scene);

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("AwithGameFrame " + request.action);
            try
            {
                var response = request.action == "component.set" ? ComponentProperties(request, scene) : ModifyObject(request, scene);
                if (request.batch) SaveScene(scene);
                Undo.CollapseUndoOperations(group);
                response.dirty = scene.isDirty;
                return response;
            }
            catch
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        internal static bool IsTransactionStep(string action) => action is "object.create" or "object.transform" or
            "prefab.place" or "component.add" or "component.set";

        private static AutomationResponse Transaction(AutomationRequest request, Scene scene)
        {
            if (request.steps == null || request.steps.Length < 1 || request.steps.Length > 32 ||
                request.steps.Any(step => step == null || !IsTransactionStep(step.action)))
                throw new ArgumentException("A transaction needs 1 to 32 supported scene steps without nested transactions.");
            if (string.IsNullOrEmpty(request.scene) || (request.save && (!request.batch || request.preview)) ||
                (request.batch && !request.preview && !request.save))
                throw new ArgumentException("A transaction requires -Scene. Batch writes require -Save and previews cannot save.");

            if (request.batch) scene = OpenScene(request.scene);
            RequireScene(scene, request.scene, false);
            var response = Result(scene);
            response.preview = request.preview;
            var records = new List<AutomationStep>();
            var wasDirty = scene.isDirty;
            var clearDirty = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(Scene) }, null);
            if (!wasDirty && clearDirty == null)
                throw new NotSupportedException("This Unity version cannot restore the clean flag in transactions.");

            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("AwithGameFrame scene transaction");
            try
            {
                foreach (var step in request.steps)
                {
                    var record = new AutomationStep { action = step.action, target = step.target, asset = step.asset, kind = step.kind, outcome = "failed" };
                    records.Add(record);
                    if (step.action == "object.transform" || step.action == "component.add")
                        record.before = Snapshot(FindObject(scene, step.target), step.target);
                    step.preview = false;
                    var result = step.action == "component.set" ? ComponentProperties(step, scene) : ModifyObject(step, scene);
                    Undo.FlushUndoRecordObjects();
                    record.objects = result.objects;
                    record.properties = result.properties;
                    record.outcome = "applied";
                }
                if (request.preview)
                {
                    RevertTransaction(scene, group, wasDirty, clearDirty);
                    foreach (var record in records) record.outcome = "previewed";
                    response.outcome = "previewed";
                }
                else
                {
                    Undo.CollapseUndoOperations(group);
                    if (request.save) SaveScene(scene);
                    response.outcome = "applied";
                }
            }
            catch (Exception exception)
            {
                response.error = "Transaction stopped after " + records.Count(record => record.outcome == "applied") + " completed step(s): " + exception.Message;
                if (records.LastOrDefault() is { outcome: "failed" } failed) failed.error = exception.Message;
                try
                {
                    RevertTransaction(scene, group, wasDirty, clearDirty);
                    foreach (var record in records.Where(record => record.outcome == "applied")) record.outcome = "reverted";
                    response.outcome = "reverted";
                }
                catch (Exception rollbackError)
                {
                    response.outcome = "rollback-failed";
                    response.error += " Rollback failed: " + rollbackError.Message;
                }
            }
            finally { Undo.IncrementCurrentGroup(); }
            response.steps = records.ToArray();
            response.dirty = scene.isDirty;
            return response;
        }

        private static void RevertTransaction(Scene scene, int group, bool wasDirty, MethodInfo clearDirty)
        {
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(group);
            if (wasDirty && !scene.isDirty) EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty && scene.isDirty) clearDirty.Invoke(null, new object[] { scene });
        }

        private static AutomationResponse ModifyObject(AutomationEdit request, Scene scene)
        {
            var item = Modify(request, scene);
            var response = Result(scene);
            response.objects = new[] { Snapshot(item, GetHierarchyPath(item.transform)) };
            return response;
        }

        private static AutomationResponse ComponentProperties(AutomationEdit request, Scene scene)
        {
            var target = FindObject(scene, request.target);
            var response = Result(scene);
            EditorAutomationProperties.Execute(request, target, response);
            return response;
        }

        private static GameObject Modify(AutomationEdit request, Scene scene)
        {
            if (request.action == "component.add") return AddComponent(request, scene);

            var position = Vector(request.position, "position");
            var rotation = Vector(request.rotation, "rotation");
            var scale = Vector(request.scale, "scale");
            if (request.action == "object.transform")
            {
                if (!position.HasValue && !rotation.HasValue && !scale.HasValue)
                    throw new ArgumentException("Supply at least one of -Position, -Rotation or -Scale.");
                var existing = FindObject(scene, request.target);
                Undo.RecordObject(existing.transform, "Set local transform");
                SetTransform(existing.transform, position, rotation, scale);
                PrefabUtility.RecordPrefabInstancePropertyModifications(existing.transform);
                EditorSceneManager.MarkSceneDirty(scene);
                return existing;
            }

            var parent = string.IsNullOrEmpty(request.parent) ? null : FindObject(scene, request.parent).transform;
            var prefab = request.action == "prefab.place"
                ? AssetDatabase.LoadAssetAtPath<GameObject>(ValidateAssetPath(request.asset, ".prefab")) : null;
            if (request.action == "prefab.place" && prefab == null) throw new ArgumentException("Prefab asset was not found.");
            var name = string.IsNullOrEmpty(request.name) && prefab != null ? prefab.name : request.name;
            ValidateName(name);
            if (Children(scene, parent).Any(child => child.name == name)) throw new ArgumentException("A sibling already has that name.");

            var item = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene) : CreateObject(request.kind);
            Undo.RegisterCreatedObjectUndo(item, "Create scene object");
            if (parent != null) Undo.SetTransformParent(item.transform, parent, "Set object parent");
            Undo.RecordObjects(new UnityEngine.Object[] { item, item.transform }, "Set object properties");
            item.name = name;
            SetTransform(item.transform, position, rotation, scale);
            PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            PrefabUtility.RecordPrefabInstancePropertyModifications(item.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            return item;
        }

        private static GameObject CreateObject(string kind) => kind switch
        {
            null or "" or "Empty" => new GameObject(),
            "Cube" => GameObject.CreatePrimitive(PrimitiveType.Cube),
            "Sphere" => GameObject.CreatePrimitive(PrimitiveType.Sphere),
            "Capsule" => GameObject.CreatePrimitive(PrimitiveType.Capsule),
            "Cylinder" => GameObject.CreatePrimitive(PrimitiveType.Cylinder),
            "Plane" => GameObject.CreatePrimitive(PrimitiveType.Plane),
            "Quad" => GameObject.CreatePrimitive(PrimitiveType.Quad),
            _ => throw new ArgumentException("Unknown primitive: " + kind)
        };

        private static GameObject AddComponent(AutomationEdit request, Scene scene)
        {
            var item = FindObject(scene, request.target);
            var component = request.kind switch
            {
                "Camera" => typeof(Camera),
                "Light" => typeof(Light),
                "AudioSource" => typeof(AudioSource),
                "BoxCollider" => typeof(BoxCollider),
                "SphereCollider" => typeof(SphereCollider),
                "Rigidbody" => typeof(Rigidbody),
                "ParticleSystem" => typeof(ParticleSystem),
                _ => throw new ArgumentException("This component is outside the supported set: " + request.kind)
            };
            if (item.GetComponent(component) != null) throw new InvalidOperationException("The component already exists.");
            if (Undo.AddComponent(item, component) == null) throw new InvalidOperationException("Unity rejected the component.");
            EditorSceneManager.MarkSceneDirty(scene);
            return item;
        }

        private static AutomationResponse Inspect(AutomationRequest request, Scene scene)
        {
            var queue = new Queue<(GameObject Item, string Path)>();
            if (string.IsNullOrEmpty(request.target))
                foreach (var root in scene.GetRootGameObjects()) queue.Enqueue((root, "/" + root.name));
            else queue.Enqueue((FindObject(scene, request.target), request.target));
            return InspectHierarchy(Result(scene), queue, ResultLimit(request.limit));
        }

        private static AutomationResponse InspectPrefab(AutomationRequest request, Scene scene)
        {
            var path = ValidateAssetPath(request.asset, ".prefab");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException("Prefab asset was not found: " + path);

            var response = Result(scene);
            response.asset = path;
            response.assetGuid = AssetDatabase.AssetPathToGUID(path);
            response.prefabKind = PrefabUtility.GetPrefabAssetType(prefab).ToString();
            var limit = ResultLimit(request.limit);
            var dependencies = AssetDatabase.GetDependencies(path, true)
                .Where(dependency => dependency != path)
                .OrderBy(dependency => dependency, StringComparer.Ordinal).Take(limit + 1).ToArray();
            response.assets = dependencies.Take(limit).ToArray();
            response.truncated = dependencies.Length > limit;
            var queue = new Queue<(GameObject Item, string Path)>();
            queue.Enqueue((prefab, "/" + prefab.name));
            return InspectHierarchy(response, queue, limit);
        }

        private static AutomationResponse InspectHierarchy(AutomationResponse response,
            Queue<(GameObject Item, string Path)> queue, int limit)
        {
            var items = new List<AutomationObject>();
            while (queue.Count > 0 && items.Count < limit)
            {
                var (item, path) = queue.Dequeue();
                items.Add(Snapshot(item, path));
                foreach (Transform child in item.transform)
                {
                    if ((child.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) continue;
                    if (items.Count + queue.Count >= limit)
                    {
                        response.truncated = true;
                        break;
                    }
                    queue.Enqueue((child.gameObject, path + "/" + child.name));
                }
            }
            response.truncated |= queue.Count > 0;
            response.objects = items.ToArray();
            return response;
        }

        private static AutomationResponse FindAssets(AutomationRequest request, Scene scene)
        {
            if (string.IsNullOrWhiteSpace(request.query)) throw new ArgumentException("Supply an AssetDatabase search query.");
            var response = Result(scene);
            var limit = ResultLimit(request.limit);
            var paths = AssetDatabase.FindAssets(request.query, new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .Distinct().OrderBy(path => path, StringComparer.Ordinal).Take(limit + 1).ToArray();
            response.truncated = paths.Length > limit;
            response.assets = paths.Take(limit).ToArray();
            return response;
        }

        private static int ResultLimit(int limit) => limit == 0 ? 100 : Mathf.Clamp(limit, 1, 500);

        private static AutomationObject Snapshot(GameObject item, string path) => new AutomationObject
        {
            path = path, active = item.activeSelf, localPosition = item.transform.localPosition,
            localRotation = item.transform.localEulerAngles, localScale = item.transform.localScale,
            components = item.GetComponents<Component>().Select(component => component == null ? "<Missing Script>" : component.GetType().FullName).ToArray()
        };

        internal static AutomationResponse Result(Scene scene) => new AutomationResponse { scene = scene.path, dirty = scene.isDirty };

        private static void RequireScene(Scene scene, string expected, bool allowUnnamed)
        {
            if (!scene.IsValid() || !scene.isLoaded || (!allowUnnamed && string.IsNullOrEmpty(scene.path)))
                throw new InvalidOperationException("Open a valid saved scene first.");
            if (!string.IsNullOrEmpty(expected) && !string.Equals(scene.path, ValidateAssetPath(expected, ".unity"), StringComparison.Ordinal))
                throw new InvalidOperationException("The active scene changed or does not match -Scene.");
        }

        private static Scene OpenScene(string path)
        {
            path = ValidateAssetPath(path, ".unity");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) throw new ArgumentException("Scene asset was not found: " + path);
            var active = SceneManager.GetActiveScene();
            if (active.path == path) return active;
            RequireCleanScenes();
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static Scene CreateScene(string path)
        {
            path = ValidateAssetPath(path, ".unity");
            var projectPath = Path.GetDirectoryName(Application.dataPath);
            var absolutePath = Path.Combine(projectPath, path);
            if (!AssetDatabase.IsValidFolder(path.Substring(0, path.LastIndexOf('/'))) || File.Exists(absolutePath) ||
                File.Exists(absolutePath + ".meta") || Directory.Exists(absolutePath))
                throw new ArgumentException("The destination folder must exist and the scene path must be unused.");
            RequireCleanScenes();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Unity could not save the new scene.");
            return scene;
        }

        private static void RequireCleanScenes()
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
                if (SceneManager.GetSceneAt(index).isDirty) throw new InvalidOperationException("Save or revert the open scenes before switching scenes.");
        }

        private static void SaveScene(Scene scene)
        {
            if (string.IsNullOrEmpty(scene.path) || !EditorSceneManager.SaveScene(scene)) throw new IOException("Unity could not save the scene.");
        }

        internal static string ValidateAssetPath(string path, string extension)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(extension, StringComparison.Ordinal) ||
                path.Any(character => char.IsControl(character) || "\\:<>\"|?*".Contains(character)) ||
                path.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal)))
                throw new ArgumentException("Use an Assets/ path with the expected extension: " + extension);

            var absolutePath = Path.GetDirectoryName(Application.dataPath);
            foreach (var part in path.Split('/'))
            {
                absolutePath = Path.Combine(absolutePath, part);
                if ((Directory.Exists(absolutePath) || File.Exists(absolutePath)) &&
                    (File.GetAttributes(absolutePath) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked asset paths are not supported: " + path);
            }
            return path;
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Any(char.IsControl))
                throw new ArgumentException("Supply a non-empty object name without slashes or control characters.");
        }

        private static IEnumerable<GameObject> Children(Scene scene, Transform parent) => parent == null
            ? scene.GetRootGameObjects() : parent.Cast<Transform>().Select(child => child.gameObject);

        private static GameObject FindObject(Scene scene, string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("Use an absolute hierarchy path such as /Objects/Marker.");

            Transform parent = null;
            foreach (var part in path.Substring(1).Split('/'))
            {
                ValidateName(part);
                var matches = Children(scene, parent).Where(item => item.name == part && (item.hideFlags & HideFlags.DontSaveInEditor) == 0).Take(2).ToArray();
                if (matches.Length != 1) throw new ArgumentException("The hierarchy path is missing or ambiguous: " + path);
                parent = matches[0].transform;
            }
            return parent.gameObject;
        }

        internal static string GetHierarchyPath(Transform item)
        {
            var segments = new Stack<string>();
            for (var current = item; current != null; current = current.parent) segments.Push(current.name);
            return "/" + string.Join("/", segments);
        }

        private static Vector3? Vector(float[] values, string field)
        {
            if (values == null || values.Length == 0) return null;
            if (values.Length != 3 || values.Any(value => float.IsNaN(value) || float.IsInfinity(value)))
                throw new ArgumentException(field + " requires three finite values.");
            return new Vector3(values[0], values[1], values[2]);
        }

        private static void SetTransform(Transform transform, Vector3? position, Vector3? rotation, Vector3? scale)
        {
            if (position.HasValue) transform.localPosition = position.Value;
            if (rotation.HasValue) transform.localEulerAngles = rotation.Value;
            if (scale.HasValue) transform.localScale = scale.Value;
        }
    }
}
