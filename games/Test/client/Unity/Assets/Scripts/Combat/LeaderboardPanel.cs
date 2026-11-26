using System;
using System.Linq;
using Test.Application.Combat;
using Test.Application.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace Test.Client.Combat
{
    internal sealed class LeaderboardPanel : MonoBehaviour
    {
        internal const string Key = "RainPassContract/Leaderboard";
        [SerializeField] private Text title, status, own, updated;
        [SerializeField] private Text[] rows;
        [SerializeField] private Button refresh, close;
        private Func<string, string> translate;

        internal void Bind(Func<string, string> lookup, Font font, Action onClose, Action onRefresh)
        {
            translate = lookup;
            foreach (var label in GetComponentsInChildren<Text>(true)) label.font = font;
            refresh.GetComponentInChildren<Text>().text = translate("Refresh");
            close.GetComponentInChildren<Text>().text = translate("Close");
            refresh.onClick.AddListener(() => onRefresh());
            close.onClick.AddListener(() => onClose());
        }

        internal void ShowLoading(string contractId, string version)
        {
            title.text = translate(contractId);
            status.text = translate("Loading cloud scores…");
            own.text = "";
            updated.text = version;
            foreach (var row in rows) row.text = "";
            refresh.interactable = false;
        }

        internal void Show(string contractId, string version, ContractLeaderboardService.LeaderboardView view)
        {
            title.text = translate(contractId);
            status.text = view.IsStale ? translate("Cached scores · may be out of date.") : translate("Cloud scores");
            own.text = view.Response.OwnRank > 0
                ? string.Format(translate("Your best {0:0.0}s · rank {1}"), view.Response.OwnBestTicks / 60f, view.Response.OwnRank)
                : translate("No score for this contract version.");
            updated.text = version + " · " + view.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            for (int index = 0; index < rows.Length; index++)
            {
                var entry = view.Response.Entries?.ElementAtOrDefault(index);
                rows[index].text = entry == null ? "" :
                    string.Format("{0}. {1}  {2:0.0}s", entry.Rank, entry.IsMine ? translate("You") : entry.PlayerId,
                        entry.ElapsedTicks / 60f);
            }
            refresh.interactable = true;
        }

        internal void ShowError()
        {
            status.text = translate("Cloud scores unavailable.");
            refresh.interactable = true;
        }
    }
}
