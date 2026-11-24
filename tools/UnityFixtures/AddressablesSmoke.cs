using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Threading.Tasks;
using AwithGameFrame.Core.Hosting;
using AwithGameFrame.Core.Resources;
using AwithGameFrame.Extensions.Scenes;
using AwithGameFrame.Unity.Addressables;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class AddressablesSmoke
{
    private const string Key = "smoke/text";
    private const string SceneKey = "smoke/scene";
    private const string Payload = "Awith Addressables fixture";
    private const string OriginalPayload = "Awith Addressables v1";

    static AddressablesSmoke()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-awith-addressables-smoke") >= 0)
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Prepare()
    {
        var settings = AddressableAssetSettings.Create("Assets/AddressableAssetsData", "AddressableAssetSettings", true, true);
        UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings = settings;
        const string assetPath = "Assets/Fixture.txt";
        File.WriteAllText(assetPath, Payload);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(assetPath), settings.DefaultGroup).address = Key;
        const string scenePath = "Assets/FixtureScene.unity";
        var fixtureScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (!EditorSceneManager.SaveScene(fixtureScene, scenePath))
            throw new InvalidOperationException("Could not save the addressable scene fixture.");
        var bootstrap = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (!EditorSceneManager.SaveScene(bootstrap, "Assets/Bootstrap.unity"))
            throw new InvalidOperationException("Could not save the bootstrap scene fixture.");
        settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(scenePath), settings.DefaultGroup).address = SceneKey;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        settings.BuildRemoteCatalog = true;
        settings.DisableCatalogUpdateOnStartup = true;
        settings.UniqueBundleIds = true;
        var schema = settings.DefaultGroup.GetSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        BuildVersion(settings, "v1", assetPath, OriginalPayload);
        BuildVersion(settings, "v2", assetPath, Payload);
    }

    private static void BuildVersion(AddressableAssetSettings settings, string version, string assetPath, string payload)
    {
        settings.OverridePlayerVersion = version;
        settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteBuildPath, "SmokeContent/" + version);
        settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, BaseUrl() + version);
        File.WriteAllText(assetPath, payload);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        AddressableAssetSettings.BuildPlayerContent(out var result);
        if (!string.IsNullOrEmpty(result.Error)) throw new InvalidOperationException(result.Error);
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Bootstrap.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static async void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        int exitCode = 1;
        try
        {
            using var server = new HttpListener();
            server.Prefixes.Add(BaseUrl());
            server.Start();
            var serving = ServeFixtureAsync(server);
            await CheckAsync();
            await CheckCatalogsAsync();
            server.Stop();
            await serving;
            Debug.Log("AWITH_ADDRESSABLES_SMOKE_PASSED");
            exitCode = 0;
        }
        catch (Exception error) { Debug.LogException(error); }
        finally { EditorApplication.Exit(exitCode); }
    }

    private static string BaseUrl()
    {
        var arguments = Environment.GetCommandLineArgs();
        return arguments[Array.IndexOf(arguments, "-awith-content-url") + 1];
    }

    private static async Task ServeFixtureAsync(HttpListener server)
    {
        var root = Path.GetFullPath("SmokeContent") + Path.DirectorySeparatorChar;
        while (server.IsListening)
        {
            HttpListenerContext request;
            try { request = await server.GetContextAsync(); }
            catch (HttpListenerException) when (!server.IsListening) { return; }
            catch (ObjectDisposedException) { return; }
            var path = Path.GetFullPath(Path.Combine(root, request.Request.Url.AbsolutePath.TrimStart('/')));
            using var response = request.Response;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                response.StatusCode = 404;
                continue;
            }
            using var file = File.OpenRead(path);
            response.ContentLength64 = file.Length;
            if (request.Request.HttpMethod != "HEAD") await file.CopyToAsync(response.OutputStream);
        }
    }

    private static async Task CheckAsync()
    {
        await using var store = new ResourceStore(new AddressablesResourceLoader());
        await using (var first = await store.LoadAsync<TextAsset>(Key))
        {
            await using var second = await store.LoadAsync<TextAsset>(Key);
            if (first.Value.text != Payload || first.Value != second.Value)
                throw new InvalidOperationException("Concurrent leases did not share the expected text asset.");
        }

        try
        {
            await using var wrongType = await store.LoadAsync<GameObject>(Key);
            throw new InvalidDataException("The key unexpectedly resolved to a GameObject.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("has no asset of type")) { }

        using var cancellation = new System.Threading.CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await using var cancelled = await store.LoadAsync<TextAsset>(Key, cancellation.Token);
            throw new InvalidDataException("A cancelled resource load returned a lease.");
        }
        catch (OperationCanceledException) { }

        await using var again = await store.LoadAsync<TextAsset>(Key);
        if (again.Value.text != Payload)
            throw new InvalidOperationException("The asset could not be loaded after its previous leases were released.");

        var bootstrap = SceneManager.GetActiveScene();
        var host = new GameHost();
        var scenes = new SceneService(host.Context, new AddressablesSceneBackend(), store);
        var additivelyLoaded = await scenes.LoadAsync(SceneKey);
        if (!bootstrap.isLoaded || SceneManager.GetActiveScene().name != "FixtureScene")
            throw new InvalidOperationException("The addressable scene did not activate alongside the bootstrap scene.");
        await additivelyLoaded.UnloadAsync();
        if (SceneManager.GetSceneByName("FixtureScene").isLoaded || !bootstrap.isLoaded)
            throw new InvalidOperationException("The addressable scene did not unload independently.");
        await scenes.StopAsync();
    }

    private static async Task CheckCatalogsAsync()
    {
        var v1 = await LoadCatalogAsync("v1");
        var v2 = await LoadCatalogAsync("v2");
        await using var oldStore = new ResourceStore(v1.CreateResourceLoader());
        await using var newStore = new ResourceStore(v2.CreateResourceLoader());
        await using var oldLease = await oldStore.LoadAsync<TextAsset>(Key);
        await using (var newLease = await newStore.LoadAsync<TextAsset>(Key))
        {
            if (newLease.Value.text != Payload || oldLease.Value.text != OriginalPayload)
                throw new InvalidOperationException("Catalog versions did not isolate the shared address.");
        }
        await using var rolledBack = await oldStore.LoadAsync<TextAsset>(Key);
        if (rolledBack.Value.text != OriginalPayload)
            throw new InvalidOperationException("The retained catalog could not serve the previous version.");
    }

    private static Task<AddressablesCatalog> LoadCatalogAsync(string version)
    {
        var catalog = Directory.EnumerateFiles("SmokeContent/" + version, "catalog_" + version + ".*")
            .Single(path => Path.GetExtension(path) is ".json" or ".bin");
        return AddressablesCatalog.LoadAsync(new Uri(BaseUrl() + version + "/" + Path.GetFileName(catalog)));
    }
}
