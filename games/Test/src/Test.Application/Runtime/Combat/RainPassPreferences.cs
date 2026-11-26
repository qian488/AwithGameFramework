using System.Collections.Generic;
using AwithGameFrame.Extensions.Localization;
using AwithGameFrame.Gameplay.Input;
using AwithGameFrame.Gameplay.Settings;

namespace Test.Application.Combat
{
    public static class RainPassPreferences
    {
        private const string CombatContext = "rain-pass";

        public static UserSettingsData Defaults()
        {
            var settings = new UserSettingsData();
            foreach (var entry in new (string Action, string Control)[]
            {
                ("forward", "keyboard/w"), ("back", "keyboard/s"), ("left", "keyboard/a"), ("right", "keyboard/d"),
                ("attack", "mouse/left"), ("guard", "mouse/right"), ("sword", "keyboard/1"), ("bow", "keyboard/2"),
                ("dodge", "keyboard/space"), ("lock", "mouse/middle"), ("pause", "keyboard/escape"),
                ("retry", "keyboard/r"), ("restart", "keyboard/enter"), ("debug", "keyboard/f2"), ("fixed", "keyboard/f3")
            })
            {
                settings.InputBindings.Add(new UserInputBinding
                    { ContextId = CombatContext, Action = entry.Action, Control = entry.Control });
            }
            return settings;
        }

        public static void BindBattle(InputService input)
        {
            var context = input.CreateContext(CombatContext);
            foreach (var binding in Defaults().InputBindings) context.Bind(binding.Action, binding.Control);
        }

        public static void LoadLanguages(LocalizationService localization)
        {
            var english = new List<LocalizationEntry>();
            var chinese = new List<LocalizationEntry>();
            foreach (var line in translations)
            {
                english.Add(new LocalizationEntry { Key = line.English, Value = line.English });
                if (line.Chinese != null) chinese.Add(new LocalizationEntry { Key = line.English, Value = line.Chinese });
            }
            localization.ReplaceDatasets(new[]
            {
                new LocalizationDataset { LanguageCode = "en", Entries = english },
                new LocalizationDataset { LanguageCode = "zh-CN", Entries = chinese }
            });
        }

        private static readonly (string English, string Chinese)[] translations =
        {
            ("Settings", "设置"), ("Language", "语言"), ("Music", "音乐"), ("Sound effects", "音效"),
            ("Pause key", "暂停键"), ("Apply and save", "应用并保存"), ("Close", "关闭"),
            ("Saving…", "正在保存…"), ("Settings saved.", "设置已保存。"), ("Retry save", "重试保存"),
            ("Settings could not be saved. Your changes are active. Retry to keep them after restart.", "设置保存失败。更改已生效。请重试以便重开后恢复。"),
            ("This key is assigned to another action.", "该按键已被其他操作占用。"),
            ("Settings could not be applied.", "无法应用设置。"),
            ("Select a field to change it.", "选择要更改的选项。"),
            ("Press a letter, Esc or Space", "按字母键、Esc 或空格"),
            ("English", "英文"), ("简体中文", "简体中文"),
            ("Rain Pass Contract", "雨夜委托"), ("Cedar Sentinel", "杉林守卫"),
            ("Salt Causeway", "盐风堤"), ("Ember Foundry", "余烬铸造厂"), ("Cedar Grove", "杉林"),
            ("Accept contract", "接取委托"), ("Cancel loading", "取消加载"), ("Retreat to camp", "撤回营地"),
            ("Next contract", "下一个委托"), ("Download", "下载"), ("Retry checkpoint", "重试检查点"),
            ("Return to camp", "返回营地"), ("Retry read", "重试读取"), ("Retry return", "重试回营"),
            ("Reading the completion record…", "正在读取完成记录…"),
            ("Completed. You can challenge Cedar Sentinel again.", "已完成。可以再次挑战杉林守卫。"),
            ("Defeat Cedar Sentinel in Cedar Grove. Press {0} in battle to pause or retreat.", "在杉林击败守卫。战斗中按 {0} 暂停或撤退。"),
            ("Cancelling…", "正在取消…"), ("Loading the route…", "正在加载路线…"),
            ("Paused", "已暂停"), ("Press {0} to resume.", "按 {0} 继续。"),
            ("Defeated", "挑战失败"), ("Retry from the current checkpoint or return to camp.", "从当前检查点重试或返回营地。"),
            ("Cedar Sentinel defeated", "已击败杉林守卫"), ("{0} complete", "已完成{0}"),
            ("Completion saved.", "完成记录已保存。"), ("Saving completion…", "正在保存完成记录…"),
            ("Saved locally. Score pending.", "成绩已保存到本机，待同步。"),
            ("Score synced.", "成绩已同步。"), (" Pending scores: {0}.", " 待同步成绩：{0} 条。"),
            ("Cloud scores", "云端成绩"), ("Loading cloud scores…", "正在加载云端成绩…"),
            ("Cloud scores unavailable.", "云端成绩加载失败。"),
            ("Cached scores · may be out of date.", "缓存成绩，可能已过期。"),
            ("Your best {0:0.0}s · rank {1}", "你的最佳成绩 {0:0.0} 秒，排名 {1}"),
            ("No score for this contract version.", "当前委托版本暂无成绩。"),
            ("You", "你"), ("Refresh", "刷新"),
            ("Returning to camp", "正在返回营地"), ("Closing the battle…", "正在关闭战斗…"),
            ("Checking or downloading content…", "正在查询或下载内容…"),
            ("Local contract. Use F5 to refresh the published offers.", "本地委托。按 F5 刷新发布列表。"),
            ("Available {0}.", "可下载 {0}。"),
            ("Installed {0} · Available {1}. U updates. Backspace rolls back.", "已安装 {0}，可更新至 {1}。按 U 更新，按 Backspace 回退。"),
            (" Best {0:0.0}s.", " 最佳 {0:0.0} 秒。"), (" Completed.", " 已完成。"),
            ("F5 refreshes offers. U downloads the latest release.", "按 F5 刷新，按 U 下载最新版本。"),
            ("Downloading the latest release…", "正在下载最新版本…"),
            ("The feed is unavailable. Cached offers are shown.", null)
        };
    }
}
