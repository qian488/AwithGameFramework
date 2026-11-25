using System;
using NUnit.Framework.Interfaces;
using UnityEngine.TestRunner;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools;

[assembly: TestPlayerBuildModifier(typeof(Test.Client.Tests.RainPassContractTestPlayer))]
[assembly: PostBuildCleanup(typeof(Test.Client.Tests.RainPassContractTestPlayer))]
#endif
[assembly: TestRunCallback(typeof(Test.Client.Tests.RainPassContractTestResult))]

namespace Test.Client.Tests
{
#if UNITY_EDITOR
    public sealed class RainPassContractTestPlayer : ITestPlayerBuildModifier, IPostBuildCleanup
    {
        private static bool buildOnly;

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions options)
        {
            buildOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-build-rain-pass-ui-test-player") >= 0;
            if (buildOnly)
                options.options &= ~(BuildOptions.AutoRunPlayer | BuildOptions.ConnectToHost |
                    BuildOptions.WaitForPlayerConnection | BuildOptions.ConnectWithProfiler);
            return options;
        }

        public void Cleanup()
        {
            if (buildOnly) EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
#endif

    public sealed class RainPassContractTestResult : ITestRunCallback
    {
        public void RunStarted(ITest testsToRun) { }
        public void TestStarted(ITest test) { }
        public void TestFinished(ITestResult result) { }

        public void RunFinished(ITestResult result)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            UnityEngine.Debug.Log("RAIN_PASS_UI_RESULT " + result.ResultState.Status + " " + result.PassCount + " " + result.FailCount);
            if (result.FailCount != 0) LogFailures(result);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static void LogFailures(ITestResult result)
        {
            if (!result.Test.IsSuite && result.FailCount != 0)
                UnityEngine.Debug.Log("RAIN_PASS_UI_FAILURE " + result.Name + " " + result.Message);
            foreach (var child in result.Children) LogFailures(child);
        }
#endif
    }
}
