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
    /// The season cycle ("Autumn Cycle"): <see cref="UIOuroborosProgressionPanel"/>. The fallback panel screen
    /// read it as six "Ouroboros Level Reward button"s and loose numbers ("x40", "2"): the level track is pictures
    /// with a count. Tab stops, as the panel is drawn:
    ///
    /// - The cycle: its name and level, its dates, and the Deck builder and Information buttons beside them.
    /// - The level track: every level from the first, read from the game's own list (the track scrolls; only six
    ///   are drawn at a time): "Level 2, 40 Ore, next". The cursor starts on the next reward, where the game
    ///   scrolls the track when the panel opens, and the track scrolls with the cursor. Enter clicks the level.
    /// - The quests, under their difficulty (Easy, Medium, Hard): each quest a row, the quest then its Change
    ///   Quest button, as the daily quests in <see cref="QuestsScreen"/>. A locked difficulty reads the level that
    ///   unlocks it.
    /// - The cycle's contract (reach level 250 for a cardback).
    ///
    /// Backspace is the panel's Back.
    /// </summary>
    internal sealed class CycleScreen : Screen
    {
        public override string Key => "cycle";
        public override int Layer => 1;
        public override bool Overlays => true;

        public override string ScreenName => UiText.Clean(Panel()?.m_TitleLabel?.Text?.text) is string s && s.Length > 0
            ? s : GameText.Get("ouroboros_main_menu_card_title_autumn", "Season cycle");

        // Not while it opens: its first frames show the prefab's placeholder labels.
        public override bool IsActive() => Panel() is UIOuroborosProgressionPanel p && p.Visibility == CrimsonUIBehaviour.EVisibility.Shown;

        private static UIOuroborosProgressionPanel Panel() => CrimsonNodes.ActivePanel<UIOuroborosProgressionPanel>();

        // The game parks its selection on the Deck builder button; the cursor starts on the next reward.
        public override AControl GameSelection() => null;

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();

            // ---- the cycle ----
            b.BeginStop("cycle:summary");
            b.AddLabel(ControlId.Structural("cycle:level"), () => UiText.Join(CrimsonNodes.Text(p.m_TitleLabel), Level(p)));
            b.AddLabel(ControlId.Structural("cycle:dates"), () => CrimsonNodes.Text(p.m_DateLabel));
            AddButton(b, p.m_DeckbuilderButton, () => GameText.Get("mainmenu_deckbuilder_title", "Deck builder"), used);
            AddButton(b, p.m_InfoButton, () => GameText.Capitalized(GameText.Get("ouroboros_info_title", "Information")), used);

            AddLevels(b, p, used);
            AddQuests(b, p, used);

            // ---- the contract ----
            var contract = p.m_Achievement;
            if (CrimsonNodes.Shown(contract))
            {
                b.BeginStop("cycle:contract");
                b.PushContext(GameText.Get("ouroboros_social_contract", "Contract"));
                var button = contract.GetComponentInChildren<ButtonControl>(false);
                Func<string> text = () => string.Join(", ", CrimsonNodes.Texts(contract));
                if (button != null && used.Add(button.Pointer)) b.AddItem(CrimsonNodes.Id(button), CrimsonNodes.Button(button, text));
                else b.AddLabel(ControlId.Structural("cycle:contract"), text);
                b.PopContext();
            }
        }

        // ---- the level track ----

        private UIOuroborosLevelListViewData _levels;

        private void AddLevels(GraphBuilder b, UIOuroborosProgressionPanel p, HashSet<IntPtr> used)
        {
            UIOuroborosLevelListViewData data;
            try { data = p.m_ViewData?.LevelListData?.Value; } catch { data = null; }
            _levels = data;
            if (data?.Levels == null || data.Levels.Count == 0) return;
            foreach (var r in p.GetComponentsInChildren<UIOuroborosLevelRewardView>(true))
                if (r.m_ButtonControl != null) used.Add(r.m_ButtonControl.Pointer);
            // The track repeats its last levels without end; listed far enough past the player's level to show
            // what comes next.
            int count = Math.Max(data.Levels.Count, data.PlayerLevel + (data.RepeatingLevels?.Count ?? 0));
            b.BeginStop("cycle:levels");
            b.PushContext(GameText.Get("journey_levels_title", "Levels"));
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var id = ControlId.Structural("cycle:level:" + i);
                if (i == data.LevelIndexToScroll) b.SetStart(id);
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => LevelText(data, index), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => LevelState(data, index), live: true, kind: AnnouncementKinds.Value),
                    },
                    OnActivate = () => ClickLevel(p, index),
                    OnTooltip = () => Speech.Say(UiText.Join(LevelText(data, index), LevelState(data, index)), true),
                });
            }
            b.PopContext();
        }

        private static UIOuroborosLevelListItemViewData LevelAt(UIOuroborosLevelListViewData data, int index)
        {
            try { return data.GetViewDataForIndex(index, true); } catch { return null; }
        }

        // "Level 2, 40 Ore". A chest has no words, only its picture.
        private static string LevelText(UIOuroborosLevelListViewData data, int index)
        {
            var item = LevelAt(data, index);
            return item == null ? "Level " + (index + 1) : LevelText(item);
        }

        /// <summary>A level of the track and its reward: "Level 2, 40 Ore".</summary>
        internal static string LevelText(UIOuroborosLevelListItemViewData item)
        {
            string text = null;
            try
            {
                var reward = item.StandardRewardData;
                text = RewardText.Of(reward?.Rewards);
                if (text == null && reward?.GetReward()?.Type == Modules.Rewards.Entities.ERewardType.ItemTemplateReward) text = "Chest";
            }
            catch { }
            return UiText.Join("Level " + item.Level, text);
        }

        private static string LevelState(UIOuroborosLevelListViewData data, int index)
        {
            var item = LevelAt(data, index);
            if (item == null) return null;
            try
            {
                if (item.IsStandardRewardClaimed) return "claimed";
                if (item.IsNextStep?.Value == true) return "next";
                return item.Level > data.PlayerLevel ? "locked" : null;
            }
            catch { return null; }
        }

        // The track scrolls to the level under the cursor, as a gamepad player's selection scrolls it.
        public override void OnCursorMoved(GraphNode node)
        {
            if (node?.Id.Reference is AControl) { base.OnCursorMoved(node); return; }
            if (!LevelIndex(node, out int index)) return;
            CrimsonNodes.ScrollTo(Panel()?.m_LevelList?.m_InfiniteScrollList, index);
        }

        private static bool LevelIndex(GraphNode node, out int index)
        {
            index = -1;
            string key = node?.Id.StructuralKey as string;
            return key != null && key.StartsWith("cycle:level:") && int.TryParse(key.Substring(12), out index);
        }

        // The level's own button, once the track has scrolled it into view: the mouse's click.
        private static void ClickLevel(UIOuroborosProgressionPanel p, int index)
        {
            foreach (var view in p.GetComponentsInChildren<UIOuroborosLevelListItemView>(false))
            {
                var d = view.GetContent()?.TryCast<UIOuroborosLevelListItemViewData>();
                if (d == null || d.Level != index + 1) continue;
                var button = view.m_StandardReward?.m_ButtonControl;
                if (button != null && Navigator.MouseClick(button))
                {
                    // Clicking a locked or claimed level changes nothing on screen: say what it is.
                    if (LevelState(p.m_ViewData?.LevelListData?.Value, index) is string state)
                        Speech.Say(char.ToUpperInvariant(state[0]) + state.Substring(1) + ".", true);
                    return;
                }
            }
            Speech.Say("Not available right now.", true);
        }

        // ---- the quests ----

        private static void AddQuests(GraphBuilder b, UIOuroborosProgressionPanel p, HashSet<IntPtr> used)
        {
            b.BeginStop("cycle:quests");
            AddTier(b, CrimsonNodes.Text(p.m_EasyQuestLabel), p.m_EasyQuestsContainer, null, used);
            AddTier(b, CrimsonNodes.Text(p.m_MediumQuestLabel), p.m_NormalQuestsContainer, p.m_NormalQuestUnlockRoot, used);
            AddTier(b, CrimsonNodes.Text(p.m_HardQuestLabel), p.m_HardQuestsContainer, p.m_HardQuestUnlockRoot, used);
            // When the next quests come ("New Quest(s) added in: 06h: 59m"), read when landed on, not ticking.
            var timer = p.m_TimerRenderer;
            if (CrimsonNodes.Shown(timer))
                b.AddLabel(ControlId.Structural("cycle:quests:timer"), () => string.Join(" ", Caption(timer.transform, p.transform)));
        }

        // A countdown with the caption drawn beside it: the texts of its nearest ancestor that has both.
        private static List<string> Caption(Transform t, Transform panel)
        {
            for (var a = t; a != null && a.Pointer != panel.Pointer; a = a.parent)
            {
                var texts = CrimsonNodes.Texts(a);
                if (texts.Count >= 2) return texts.Count <= 3 ? texts : CrimsonNodes.Texts(t);
            }
            return CrimsonNodes.Texts(t);
        }

        /// <summary>A difficulty's quests under its name ("Easy"): each quest a row with its Change Quest button,
        /// or while the difficulty is locked, the line saying at which level it unlocks. Shared with the cycle's
        /// side window (<see cref="SideWindowScreen"/>), which draws the same quest list; there the name is
        /// already the foldout header's, so <paramref name="context"/> is off.</summary>
        internal static void AddTier(GraphBuilder b, string name, UIQuestOuroborosList list, GameObject lockRoot, HashSet<IntPtr> used,
            bool context = true)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (context) b.PushContext(name);
            if (lockRoot != null && lockRoot.activeInHierarchy)
            {
                // "Unlock Medium Quests at level", "5": one line.
                b.AddLabel(ControlId.Structural("cycle:lock:" + name), () => string.Join(" ", CrimsonNodes.Texts(lockRoot.transform)));
            }
            else if (CrimsonNodes.Shown(list))
            {
                var quests = list.GetComponentsInChildren<UIQuestOuroborosListItem>(false);
                int n = 0;
                foreach (var q in quests)
                {
                    var quest = q;
                    var buttons = new List<ButtonControl>();
                    foreach (var c in q.GetComponentsInChildren<ButtonControl>(false)) { used.Add(c.Pointer); buttons.Add(c); }
                    int slot = n++;
                    b.StartRow("cycle:quest:" + name + ":" + slot);
                    b.AddItem(ControlId.Structural("cycle:quest:" + name + ":" + slot), new NodeVtable
                    {
                        // Its place among the tier's quests, not in its row (every row's "1 of 2").
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => QuestText(quest), kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => (slot + 1) + " of " + quests.Length, kind: AnnouncementKinds.Position),
                        },
                        SpeaksOwnPosition = true,
                        OnTooltip = () => Speech.Say(QuestText(quest), true),
                    });
                    // Change Quest: the button the game shows now (an unavailable one is drawn in its place).
                    var reroll = buttons.Find(c => c.IsEnabled && CrimsonNodes.Texts(c).Count == 0 && c.Pointer != quest.m_SkipTimeButtonControl?.Pointer)
                                 ?? quest.m_MouseButtonControl;
                    if (CrimsonNodes.Shown(reroll) && CrimsonNodes.Shown(quest.m_RerollButtonContainer))
                    {
                        var v = CrimsonNodes.Button(reroll, () => CrimsonNodes.TooltipText(reroll) ?? GameText.Get("ouroboros_reroll_title", "Change Quest"));
                        v.SpeaksOwnPosition = true;
                        b.AddItem(ControlId.Referenced(reroll, "cycle:reroll:" + name + ":" + slot), v);
                    }
                    var skip = quest.m_SkipTimeButtonControl;
                    if (CrimsonNodes.Shown(skip)) b.AddItem(CrimsonNodes.Id(skip), CrimsonNodes.Button(skip));
                    b.EndRow();
                }
            }
            if (context) b.PopContext();
        }

        // The quest as drawn: its text, its progress ("1 of 5"), the levels it gives; an empty slot's notice.
        private static string QuestText(UIQuestOuroborosListItem q)
        {
            var parts = new List<string>();
            try
            {
                if (q.m_QuestItem == null && CrimsonNodes.Shown(q.m_InactiveLabel)) return CrimsonNodes.Text(q.m_InactiveLabel);
                parts.Add(CrimsonNodes.Text(q.m_DescriptionLabel));
                var bar = q.m_ProgressBar;
                if (CrimsonNodes.Shown(bar)) foreach (var t in CrimsonNodes.Texts(bar)) parts.Add("progress " + t); // drawn "0/5"
                var icon = q.m_RewardIcon;
                if (CrimsonNodes.Shown(icon) && CrimsonNodes.Text(icon.m_Number) is string n && n.Length > 0) parts.Add(GameText.Get("achievements_reward", "Reward:") + " " + n);
            }
            catch (Exception e) { Plugin.LogInfo("[cycle] quest read failed: " + e.Message); }
            parts.RemoveAll(string.IsNullOrEmpty);
            return parts.Count > 0 ? string.Join(", ", parts) : "Quest";
        }

        // ---- helpers ----

        private static string Level(UIOuroborosProgressionPanel p)
        {
            string n = CrimsonNodes.Text(p.m_LevelLabel);
            return n.Length > 0 ? "level " + n : null;
        }

        private static void AddButton(GraphBuilder b, ButtonControl c, Func<string> label, HashSet<IntPtr> used)
        {
            if (!CrimsonNodes.Shown(c) || !used.Add(c.Pointer)) return;
            b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, () => CrimsonNodes.TooltipText(c) ?? label()));
        }
    }
}
