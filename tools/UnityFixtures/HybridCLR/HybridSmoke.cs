using System;
using System.IO;
using AwithGameFrame.Unity.HybridUpdate;
using UnityEngine;
public sealed class HybridSmoke : MonoBehaviour
{
    private void Start()
    {
        try
        {
            var update = Path.Combine(Application.streamingAssetsPath, "update");
            var manifest = JsonUtility.FromJson<HotUpdateManifest>(File.ReadAllText(Path.Combine(update, "hot-update.json")));
            try
            {
                HybridClrUpdate.Load(manifest, "wrong-player", "StandaloneWindows64", _ => throw new Exception("Unexpected read"));
                throw new Exception("Wrong player accepted");
            }
            catch (InvalidDataException) { }
            try
            {
                HybridClrUpdate.Load(manifest, "fixture-player", "StandaloneWindows64",
                    path => path.StartsWith("hot/", StringComparison.Ordinal) ? new byte[] { 1 } : File.ReadAllBytes(Path.Combine(update, path)));
                throw new Exception("Changed hot-update data was accepted");
            }
            catch (InvalidDataException error) when (error.Message.Contains("hash mismatch")) { }
            var assemblies = HybridClrUpdate.Load(manifest, "fixture-player", "StandaloneWindows64", path => File.ReadAllBytes(Path.Combine(update, path)));
            var answer = assemblies[0].GetType("AwithHybridFixture.HotLogic", true).GetMethod("Check").Invoke(null, null);
            if (!Equals(answer, 42)) throw new Exception("Wrong hot code result");
            Debug.Log("AWITH_HYBRIDCLR_PLAYER_PASSED");
            Application.Quit(0);
        }
        catch (Exception error) { Debug.LogException(error); Application.Quit(1); }
    }
}
