using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UnityMigrationAuditFixture
{
    public static void Create()
    {
        EditorSettings.serializationMode = SerializationMode.ForceText;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Root", typeof(MeshRenderer));
        var material = new Material(Shader.Find("Standard"));
        AssetDatabase.CreateAsset(material, "Assets/Fixture.mat");
        root.GetComponent<MeshRenderer>().sharedMaterial = material;
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Fixture.prefab");
        EditorSceneManager.SaveScene(scene, "Assets/Fixture.unity");
    }
}
