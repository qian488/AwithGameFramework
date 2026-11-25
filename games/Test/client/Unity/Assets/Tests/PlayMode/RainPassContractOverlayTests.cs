#if UNITY_WEBGL && !UNITY_EDITOR
using System.Collections;
using Test.Application.Combat;
using Test.Client.Combat;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Test.Client.Tests
{
    public sealed class RainPassContractOverlayTests
    {
        [UnityTest]
        public IEnumerator DrawsTheAuthoredPanelInEveryContractState()
        {
            var camera = new GameObject("Capture camera", typeof(Camera));
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(0.07f, 0.10f, 0.13f);
            var root = new GameObject("Capture Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(960, 540);
                var prefab = Resources.Load<GameObject>("RainPassContract/Panel");
                var panel = Object.Instantiate(prefab, root.transform, false).GetComponent<ContractPanel>();
                panel.Bind(() => {}, () => {});
                yield return null;

                panel.Render(ContractPhase.Camp, false, false, null, false, null);
                yield return RainPassContractFrames.Check(canvas, "01-camp", "Accept contract");
                panel.Render(ContractPhase.Loading, false, false, null, false, null);
                yield return RainPassContractFrames.Check(canvas, "02-loading", "Cancel loading");
                panel.Render(ContractPhase.Loading, false, true, null, false, null);
                yield return RainPassContractFrames.Check(canvas, "03-cancelling");
                panel.Render(ContractPhase.Battle, false, false, RunStatus.Paused, false, null);
                yield return RainPassContractFrames.Check(canvas, "04-paused", "Retreat to camp");
                panel.Render(ContractPhase.Battle, false, false, RunStatus.Defeated, false, null);
                yield return RainPassContractFrames.Check(canvas, "05-defeated", "Retry checkpoint", "Retreat to camp");
                panel.Render(ContractPhase.Result, false, false, RunStatus.Victory, false, "Completion could not be saved.");
                yield return RainPassContractFrames.Check(canvas, "06-save-failed", "Retry save");
                panel.Render(ContractPhase.Result, true, false, RunStatus.Victory, true, null);
                yield return RainPassContractFrames.Check(canvas, "07-victory", "Return to camp");
                panel.Render(ContractPhase.Camp, true, false, null, false, null);
                yield return RainPassContractFrames.Check(canvas, "08-reopened", "Accept contract");
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(camera);
            }
        }
    }
}
#endif
