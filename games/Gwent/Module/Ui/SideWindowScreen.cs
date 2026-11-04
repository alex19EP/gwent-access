using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The Journey and season cycle ("Autumn Cycle") side windows: the social panel's Journey and Ouroboros
    /// sections, opened from the first two buttons of the menu bar's social group. Before, pressing those only
    /// said "pressed": the main menu skips the social window (it has screens of its own), and only Friends and
    /// Quests had one. Read top to bottom as drawn, each part a Tab stop:
    ///
    /// <list type="bullet">
    /// <item><b>Journey</b>: the journey's name, premium and bonus status; the refill countdown; the journey
    /// level, its progress and the next reward, and the well-rested bonus left; the journey quests, each a row
    /// (the quest, then its Change Quest button as in <see cref="QuestsScreen"/>); the buttons (buy premium,
    /// open the journey, story, information).</item>
    /// <item><b>Cycle</b>: its name, level and dates, and the information button; the current and next level
    /// with their rewards and the progress toward the next; the Easy, Medium and Hard quests under their
    /// foldout headers (read as in <see cref="CycleScreen"/>); the cycle's contract.</item>
    /// </list>
    ///
    /// Whatever else the window shows that can be pressed is listed last, so nothing is out of reach. The
    /// window shares the main menu's input layer, so this screen sits above <see cref="MainMenuScreen"/> while
    /// it is open; Backspace closes it as pressing its bar button again does.
    /// </summary>
    internal sealed class SideWindowScreen : Screen
    {
        public override string Key => "side-window";
        public override int Layer => 1;

        public override string ScreenName
        {
            get
            {
                if (Journey() != null) return GameText.Get("ep_5_2_title", "Journey");
                if (Cycle() is OuroborosSocialFeatureWindow c)
                    return CrimsonNodes.Text(c.m_NameLabel) is string n && n.Length > 0 ? n : GameText.Get("ouroboros_main_menu_card_title_autumn", "Season cycle");
                return null;
            }
        }

        public override bool IsActive() => Journey() != null || Cycle() != null;

        private static JourneyWindow Journey() => ContactsScreen.OpenWindow<JourneyWindow>(SocialPanel.SocialPanelSection.Journey);
        private static OuroborosSocialFeatureWindow Cycle() => ContactsScreen.OpenWindow<OuroborosSocialFeatureWindow>(SocialPanel.SocialPanelSection.Ouroboros);

        // The game parks its selection on the window's first control; the cursor starts at the top.
        public override AControl GameSelection() => null;

        /// <summary>Backspace closes the window as pressing its social bar button again does (the window knows
        /// its button).</summary>
        public override bool Back()
        {
            try
            {
                SocialFeatureWindow w = (SocialFeatureWindow)Journey() ?? Cycle();
                var button = w?.SocialButtonRef;
                if (!CrimsonNodes.Shown(button)) return false;
                Navigator.ClickControl(button);
                return true;
            }
            catch { return false; }
        }

        public override IReadOnlyList<ScreenKey> ExtraKeys => _keys ??= new List<ScreenKey>
        {
            // Escape is not the game's Back here: it opens the main menu's "quit?" question.
            new ScreenKey { Key = KeyCode.Escape, Run = () => { if (!Back()) Speech.Say("Nothing to go back to here.", true); },
                Hint = "Escape or Backspace, close the window" },
        };
        private List<ScreenKey> _keys;

        public override void Build(GraphBuilder b)
        {
            var used = new HashSet<IntPtr>();
            if (Journey() is JourneyWindow j) BuildJourney(b, j, used);
            else if (Cycle() is OuroborosSocialFeatureWindow c) BuildCycle(b, c, used);
            else return;
            SocialFeatureWindow w = (SocialFeatureWindow)Journey() ?? Cycle();
            var rest = CrimsonNodes.Leaves(w, used);
            if (rest.Count > 0)
            {
                b.BeginStop("side:other");
                foreach (var c in rest) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        // ---- Journey ----

        private static void BuildJourney(GraphBuilder b, JourneyWindow w, HashSet<IntPtr> used)
        {
            b.BeginStop("side:journey");
            var info = w.m_JourneyInfoPanel;
            if (CrimsonNodes.Shown(info))
                b.AddLabel(ControlId.Structural("side:journey:info"), () =>
                    UiText.Join(CrimsonNodes.Text(info.m_JourneyName), CrimsonNodes.Text(info.m_PremiumStatus), CrimsonNodes.Text(info.m_BonusStatus)));
            var refill = w.m_RefillCountdown;
            if (CrimsonNodes.Shown(refill))
                b.AddLabel(ControlId.Structural("side:journey:refill"), () => string.Join(" ", CrimsonNodes.Texts(refill)));
            var progress = w.m_JourneyProgress;
            if (CrimsonNodes.Shown(progress))
                b.AddLabel(ControlId.Structural("side:journey:progress"), () => JourneyProgress(progress));

            var quests = w.m_InfiniteJourneyQuests;
            if (CrimsonNodes.Shown(quests))
            {
                b.BeginStop("side:journey:quests");
                b.PushContext(GameText.Get("ui_social_panel_journey_quests", "Journey Quests"));
                var items = quests.GetComponentsInChildren<UIQuestJourneyListItem>(false);
                for (int i = 0; i < items.Length; i++) AddJourneyQuest(b, items[i], i, items.Length, used);
                b.PopContext();
            }

            b.BeginStop("side:journey:buttons");
            AddButton(b, w.m_BuyButton, () => CrimsonNodes.Text(w.m_BuyButton.m_TitleLabel), used);
            AddButton(b, w.m_ProgressionButton, null, used);
            AddButton(b, w.m_StoryButton, () => "Story", used);
            AddButton(b, w.m_InfoButton, () => GameText.Capitalized(GameText.Get("ouroboros_info_title", "Information")), used);
        }

        // "Journey level 12, 3/10, Reward: 50 Ore, 2 bonus left": the level badge, the bar toward the next level,
        // the reward at its end, the well-rested counter.
        private static string JourneyProgress(UIJourneyProgressInSocial p)
        {
            var parts = new List<string>();
            try
            {
                var level = p.m_JourneyLevel;
                if (CrimsonNodes.Shown(level) && CrimsonNodes.Text(level.m_Level) is string l && l.Length > 0)
                    parts.Add(GameText.Get("player_profile_level", "Level") + " " + l);
                var bar = p.m_JourneyProgressBar;
                if (CrimsonNodes.Shown(bar)) parts.AddRange(CrimsonNodes.Texts(bar));
                if (Reward(p.m_RewardIcon) is string r) parts.Add(r);
                var rested = p.m_WellRestedReward;
                if (CrimsonNodes.Shown(rested)) parts.Add(CrimsonNodes.Text(rested.m_Label));
            }
            catch (Exception e) { Plugin.LogInfo("[side] journey progress: " + e.Message); }
            return UiText.Join(parts.ToArray());
        }

        // A quest row: the quest (title, text, progress, reward), then its Change Quest button. Keyed by slot:
        // the game renames a quest's object after the quest.
        private static void AddJourneyQuest(GraphBuilder b, UIQuestJourneyListItem q, int slot, int count, HashSet<IntPtr> used)
        {
            foreach (var c in q.GetComponentsInChildren<ButtonControl>(true)) used.Add(c.Pointer);
            b.StartRow("side:journey:quest:" + slot);
            b.AddItem(ControlId.Structural("side:journey:quest:" + slot), new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(() => JourneyQuestText(q), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => (slot + 1) + " of " + count, kind: AnnouncementKinds.Position),
                },
                SpeaksOwnPosition = true,
                OnTooltip = () => Speech.Say(JourneyQuestText(q), true),
            });
            var reroll = q.m_MouseButtonControl;
            if (CrimsonNodes.Shown(reroll) && CrimsonNodes.Shown(q.m_RerollButtonContainer))
            {
                var v = CrimsonNodes.Button(reroll, () => CrimsonNodes.TooltipText(reroll) ?? GameText.Get("ouroboros_reroll_title", "Change Quest"));
                v.SpeaksOwnPosition = true;
                b.AddItem(ControlId.Referenced(reroll, "side:journey:reroll:" + slot), v);
            }
            b.EndRow();
        }

        private static string JourneyQuestText(UIQuestJourneyListItem q)
        {
            var parts = new List<string>();
            try
            {
                if (q.m_QuestItem == null && CrimsonNodes.Shown(q.m_InactiveTitle)) return CrimsonNodes.Text(q.m_InactiveTitle);
                parts.Add(CrimsonNodes.Text(q.m_TitleLabel));
                parts.Add(CrimsonNodes.Text(q.m_DescriptionLabel));
                var bar = q.m_ProgressBarWithReward;
                if (CrimsonNodes.Shown(bar))
                {
                    if (CrimsonNodes.Shown(bar.m_ProgressBar)) foreach (var t in CrimsonNodes.Texts(bar.m_ProgressBar)) parts.Add("progress " + t);
                    parts.Add(Reward(bar.m_RewardIcon));
                }
            }
            catch (Exception e) { Plugin.LogInfo("[side] journey quest: " + e.Message); }
            parts.RemoveAll(string.IsNullOrEmpty);
            return parts.Count > 0 ? string.Join(", ", parts) : "Quest";
        }

        // A reward icon as a mouse player reads it: its hover tooltip (what it is), then the number drawn on it.
        private static string Reward(UIRewardIcon icon)
        {
            if (!CrimsonNodes.Shown(icon)) return null;
            string n = CrimsonNodes.Text(icon.m_Number);
            string what = CrimsonNodes.TooltipText(icon);
            string text = UiText.Join(n.Length > 0 ? n : null, what);
            return string.IsNullOrEmpty(text) ? null : GameText.Get("achievements_reward", "Reward:") + " " + text;
        }

        // ---- the season cycle ----

        private static void BuildCycle(GraphBuilder b, OuroborosSocialFeatureWindow w, HashSet<IntPtr> used)
        {
            b.BeginStop("side:cycle");
            b.AddLabel(ControlId.Structural("side:cycle:level"), () =>
            {
                string n = CrimsonNodes.Text(w.m_LevelLabel);
                return UiText.Join(CrimsonNodes.Text(w.m_NameLabel), n.Length > 0 ? "level " + n : null);
            });
            b.AddLabel(ControlId.Structural("side:cycle:dates"), () => CrimsonNodes.Text(w.m_DateLabel));
            AddButton(b, w.m_TopBarInfoButton, () => GameText.Capitalized(GameText.Get("ouroboros_info_title", "Information")), used);

            b.BeginStop("side:cycle:levels");
            AddLevel(b, "current", w.m_LevelItemViewCurrent, used);
            AddLevel(b, "next", w.m_LevelItemViewNext, used);

            b.BeginStop("side:cycle:quests");
            foreach (var s in new[] { w.m_QuestsEasy, w.m_QuestsNormal, w.m_QuestsHard })
            {
                if (!CrimsonNodes.Shown(s)) continue;
                var header = s.SectionButton;
                if (QuestsScreen.BeginSection(b, s, header, used))
                {
                    CycleScreen.AddTier(b, string.Join(", ", CrimsonNodes.Texts(header)), s.m_QuestList,
                        s.m_LockedRoot != null ? s.m_LockedRoot.gameObject : null, used, context: false);
                    b.EndGroup();
                }
            }

            var contract = w.m_AchievementSectionRenderer?.m_TrackedAchievementRenderer;
            if (CrimsonNodes.Shown(contract))
            {
                b.BeginStop("side:cycle:contract");
                b.PushContext(GameText.Get("ouroboros_social_contract", "Contract"));
                var button = contract.GetComponentInChildren<ButtonControl>(false);
                Func<string> text = () => string.Join(", ", CrimsonNodes.Texts(contract));
                if (button != null && used.Add(button.Pointer)) b.AddItem(CrimsonNodes.Id(button), CrimsonNodes.Button(button, text));
                else b.AddLabel(ControlId.Structural("side:cycle:contract"), text);
                b.PopContext();
            }
        }

        // The current or next level of the track, with its reward ("Level 12, 40 Ore, next"); the next one also
        // reads the bar toward it.
        private static void AddLevel(GraphBuilder b, string which, UIOuroborosLevelListItemViewSocialPanel view, HashSet<IntPtr> used)
        {
            if (!CrimsonNodes.Shown(view)) return;
            foreach (var c in view.GetComponentsInChildren<ButtonControl>(true)) used.Add(c.Pointer);
            b.AddLabel(ControlId.Structural("side:cycle:" + which), () =>
            {
                string level = null;
                try
                {
                    var data = view.GetContent()?.TryCast<UIOuroborosLevelListItemViewData>();
                    level = data != null ? CycleScreen.LevelText(data) : null;
                }
                catch { }
                level ??= string.Join(", ", CrimsonNodes.Texts(view));
                return UiText.Join(level, which, Progress(view.m_NextLevelProgressBar));
            });
        }

        // A bar's own text ("3/10"), else how full it is drawn.
        private static string Progress(GwentUnity.UI.PlayerProfile.AUIProgressBar bar)
        {
            if (!CrimsonNodes.Shown(bar)) return null;
            string t = CrimsonNodes.Text(bar.m_ProgressText);
            if (t.Length > 0) return "progress " + t;
            try
            {
                float v = bar.m_ProgressValue;
                return v >= 0f && v <= 1f ? "progress " + Mathf.RoundToInt(v * 100f) + "%" : null;
            }
            catch { return null; }
        }

        // ---- helpers ----

        private static void AddButton(GraphBuilder b, AControl c, Func<string> label, HashSet<IntPtr> used)
        {
            if (!CrimsonNodes.Shown(c) || !used.Add(c.Pointer)) return;
            Func<string> name = label == null ? null : () =>
                CrimsonNodes.Texts(c) is var t && t.Count > 0 ? t[0] : CrimsonNodes.TooltipText(c) ?? label();
            b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, name));
        }
    }
}
