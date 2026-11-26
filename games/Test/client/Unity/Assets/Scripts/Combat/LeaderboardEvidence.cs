using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal sealed class LeaderboardEvidence : MonoBehaviour
    {
        private RainPassCamp camp;
        private string output;

        internal void Run(RainPassCamp owner)
        {
            camp = owner;
            output = RainPassCamp.Argument("-leaderboard-evidence");
            UnityEngine.Application.runInBackground = true;
            StartCoroutine(Report(Verify()));
        }

        private IEnumerator Report(IEnumerator flow)
        {
            var stack = new System.Collections.Generic.Stack<IEnumerator>();
            stack.Push(flow);
            while (stack.Count != 0)
            {
                object current;
                try
                {
                    var step = stack.Peek();
                    if (!step.MoveNext()) { stack.Pop(); continue; }
                    current = step.Current;
                }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                    UnityEngine.Application.Quit(1);
                    yield break;
                }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
            File.WriteAllText(Path.Combine(output, "passed.txt"), UnityEngine.Application.unityVersion);
            UnityEngine.Application.Quit();
        }

        private IEnumerator Verify()
        {
            yield return Until(() => Find("Next contract") != null, "published leaderboard offers");
            Click("Next contract");
            yield return null;
            yield return Until(() => Find("Cloud scores") != null, "cloud scores button");
            if (RainPassCamp.Argument("-leaderboard-reopen") != null)
            {
                Click("Cloud scores");
                yield return Until(() => Has("Cached scores"), "reopened leaderboard rows");
                Require(BoardShows("1. ") && BoardShows("2. "), "cached leaderboard rows after restart");
                Mark("reopened-cached-leaderboard");
                File.WriteAllText(Path.Combine(output, "cache-visible"), "");
                yield return Until(() => File.Exists(Path.Combine(output, "server-restored")), "leaderboard service restart");
                Click("Refresh");
                yield return Until(() => Find("Refresh") != null && !Has("Cached scores") && Has("Your best"), "fresh leaderboard replacing cached rows");
                Mark("refreshed-after-service-restart");
                yield break;
            }

            Click("Cloud scores");
            yield return Until(() => BoardShows("1. ") && BoardShows("2. ") && BoardShows("Your best"), "leaderboard rows and own rank");
            Mark("online-leaderboard");
            File.WriteAllText(Path.Combine(output, "query-loaded"), "");
            yield return Until(() => File.Exists(Path.Combine(output, "server-stopped")), "leaderboard service stop");
            Click("Refresh");
            yield return Until(() => Has("Cached scores"), "cached leaderboard after outage");
            Require(BoardShows("1. ") && BoardShows("2. "), "refresh retained cached rows");
            Mark("offline-cached-leaderboard");
        }

        private Button Find(string text) => camp.GetComponentsInChildren<Button>().FirstOrDefault(
            button => button.IsInteractable() && button.GetComponentInChildren<Text>().text == text);
        private bool Has(string text) => camp.GetComponentsInChildren<Text>().Any(label => label.text.Contains(text)) || Find(text) != null;
        private bool BoardShows(string text)
        {
            var board = camp.GetComponentInChildren<LeaderboardPanel>();
            return board != null && board.GetComponentsInChildren<Text>().Any(label => label.text.StartsWith(text, StringComparison.Ordinal));
        }
        private void Click(string text)
        {
            var button = Find(text);
            if (button == null) throw new InvalidOperationException("Missing button " + text);
            button.onClick.Invoke();
        }

        private static IEnumerator Until(Func<bool> condition, string description)
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            if (!condition()) throw new InvalidOperationException("Timed out waiting for " + description);
        }

        private void Mark(string step) => File.AppendAllText(Path.Combine(output, "steps.txt"), step + Environment.NewLine);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
