using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentUnity.UI.PlayerProfile;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The daily login panel ("Ваша ежедневная награда"), which the game shows on its own when a new day's reward
    /// is ready, and which the quests window's button opens at any time. Its title is the screen's name.
    ///
    /// Two stops, as on screen: the week's seven days, then the way out. A day reads what its slot draws: the
    /// day, the reward in the game's own words ("Тайная карта" until a card is revealed; a boost's reward
    /// after it), then whether it was claimed and when, or the countdown on the day that comes next. The
    /// cursor starts on the day claimed last or coming next, whichever the game is at.
    ///
    /// There is nothing to press to claim: the panel claims a ready day by itself once its slots are shown (a
    /// server write that simply opening the panel makes). The day's reading follows, live, while the cursor
    /// stands on it. A plain click on a day closes the panel, as the continue line below does, so Enter does
    /// nothing on a day unless it holds a revealed card: then it opens the game's card preview, as the right
    /// mouse button does, and the card keys and Space read the card here.
    ///
    /// After the week's last day the game replaces the continue line with its Continue button (next week); it
    /// lands in the last stop like any bar button. Escape stays the game's Back.
    /// </summary>
    internal sealed class DailyLoginScreen : Screen
    {
        public override string Key => "daily-login";
        public override int Layer => 2;

        public override string ScreenName
        {
            get
            {
                var title = Panel()?.m_Title;
                var texts = title != null ? CrimsonNodes.Texts(title) : null;
                return texts != null && texts.Count > 0 ? string.Join(", ", texts) : GameText.Get("daily_login_title", "Daily reward");
            }
        }

        public override bool IsActive() => Panel() != null;

        // The panel attached to the input layer that has the input.
        private static UIDailyLoginRewardPanel Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var p = attached[i]?.TryCast<UIDailyLoginRewardPanel>();
                if (p != null && p.gameObject.activeInHierarchy) return p;
            }
            return null;
        }

        // The game parks no selection here, and moves it around a claim; the cursor stays with the player.
        public override AControl GameSelection() => null;

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();
            _days.Clear();

            var slots = p.m_RewardsContainer?.m_DailyLoginRewardSlots;
            if (slots != null && slots.Count > 0)
            {
                b.BeginStop("days");
                ControlId start = null;
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (!Shown(slot)) continue;
                    used.Add(slot.Pointer);
                    var id = CrimsonNodes.Id(slot);
                    _days[(string)id.StructuralKey] = slot;
                    // The days run in order, so the last claimed or counting down is where the game is at.
                    if (start == null || Shown(slot.m_ClaimBox) || Shown(slot.m_TimerCountdownRenderer)) start = id;
                    b.AddItem(id, Day(p, slot));
                }
                if (start != null) b.SetStart(start);
            }

            var buttons = CrimsonNodes.Leaves(p, used);
            AddBar(p.m_InputButtonContainer, buttons, used);
            if (buttons.Count > 0)
            {
                b.BeginStop("buttons");
                foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        // The bar's glyph-key buttons are never selectable, so the walk above misses them.
        private static void AddBar(InputButtonContainer bar, List<AControl> buttons, HashSet<IntPtr> used)
        {
            if (!Shown(bar)) return;
            var children = bar.GetChildren();
            for (int i = 0; children != null && i < children.Count; i++)
            {
                var c = children[i];
                if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                if (CrimsonNodes.Texts(c).Count == 0) continue;  // an empty spare slot in the bar
                if (used.Add(c.Pointer)) buttons.Add(c);
            }
        }

        // ---- a day ----

        private NodeVtable Day(UIDailyLoginRewardPanel p, UIDailyLoginRewardSlot slot)
        {
            return new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(() => UiText.Clean(slot.m_DayLabel?.Text?.text), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Reward(p, slot), live: true, kind: AnnouncementKinds.Value),
                    new NodeAnnouncement(() => Claimed(slot), live: true, kind: AnnouncementKinds.Value),
                    new NodeAnnouncement(() => Countdown(slot), kind: AnnouncementKinds.Value),
                },
                OnActivate = () => Preview(slot),
                OnTooltip = () => Speech.Say(Details(p, slot), true),
            };
        }

        // The reward in the game's words, then a boost's. Null before the slot's view is made.
        private static string Reward(UIDailyLoginRewardPanel p, UIDailyLoginRewardSlot slot)
        {
            var parts = new List<string>();
            try
            {
                var reward = slot.RewardView?.Reward;
                if (reward != null) parts.Add(UiText.Clean(RewardTextHelper.GetText(reward)));
                if (Shown(slot.m_BoostRewardIcon))
                {
                    var boost = p.m_RewardsContainer.m_RewardController?.GimmeStuff(slot.SlotId).BoostReward;
                    if (boost != null) parts.Add(UiText.Clean(RewardTextHelper.GetText(boost)));
                }
            }
            catch (Exception e) { Plugin.LogInfo("[daily-login] reward read failed: " + e.Message); }
            parts.RemoveAll(string.IsNullOrEmpty);
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        // The slot's claimed box, with its date ("Выполнено, 30.10.2026", as a finished contract reads).
        private static string Claimed(UIDailyLoginRewardSlot slot)
        {
            if (!Shown(slot.m_ClaimBox)) return null;
            string word = GameText.Get("daily_quest_completed", "Completed");
            string date = UiText.Clean(slot.m_ClaimedDataLabel?.Text?.text);
            return date.Length > 0 ? word + ", " + date : word;
        }

        // Read on landing, not live: it changes every second.
        private static string Countdown(UIDailyLoginRewardSlot slot)
        {
            var timer = slot.m_TimerCountdownRenderer;
            return Shown(timer) ? string.Join(", ", CrimsonNodes.Texts(timer)) : null;
        }

        private static string Details(UIDailyLoginRewardPanel p, UIDailyLoginRewardSlot slot)
        {
            var parts = new List<string> { UiText.Clean(slot.m_DayLabel?.Text?.text), Reward(p, slot), Claimed(slot), Countdown(slot) };
            parts.RemoveAll(string.IsNullOrEmpty);
            string text = string.Join(", ", parts);
            var card = CardOf(slot);
            return card != null ? text + ". " + CardReader.Whole(card) : text;
        }

        /// <summary>The card a slot has revealed; null while it holds none (a day to come keeps its card secret).</summary>
        private static GwentGameplay.Card CardOf(UIDailyLoginRewardSlot slot)
        {
            try { return slot.Card ?? slot.RewardView?.TryCast<UIDailyLoginCardReward>()?.Card; }
            catch (Exception e) { Plugin.LogInfo("[daily-login] card read failed: " + e.Message); return null; }
        }

        private static void Preview(UIDailyLoginRewardSlot slot)
        {
            var card = CardOf(slot);
            if (card == null) return;
            try { slot.OnSlotRightClick?.Invoke(card); }
            catch (Exception e) { Plugin.LogInfo("[daily-login] preview failed: " + e.Message); }
        }

        // ---- card keys: a revealed card reads line by line, like a card on the board ----

        private readonly Dictionary<string, UIDailyLoginRewardSlot> _days = new Dictionary<string, UIDailyLoginRewardSlot>();
        private CardKeys _cardKeys;
        private List<ScreenKey> _keys;

        public override IReadOnlyList<ScreenKey> ExtraKeys
        {
            get
            {
                if (_keys != null) return _keys;
                _cardKeys = new CardKeys(FocusedCard, () => null);
                _keys = new List<ScreenKey>(_cardKeys.Keys);
                return _keys;
            }
        }

        public override void OnCursorMoved(GraphNode node)
        {
            _cardKeys?.Reset();
            base.OnCursorMoved(node);
        }

        private GwentGameplay.Card FocusedCard()
        {
            var node = GraphNavigator.CurrentNode;
            return node?.Id.StructuralKey is string key && _days.TryGetValue(key, out var slot) ? CardOf(slot) : null;
        }

        private static bool Shown(Component c)
        {
            try { return c != null && c.gameObject.activeInHierarchy; } catch { return false; }
        }

        private static bool Shown(GameObject g)
        {
            try { return g != null && g.activeInHierarchy; } catch { return false; }
        }
    }
}
