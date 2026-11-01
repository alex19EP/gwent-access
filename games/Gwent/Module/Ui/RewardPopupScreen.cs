using System;
using System.Collections.Generic;
using System.Linq;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity.UI.PlayerProfile;
using GwentVisuals;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The reward popup ("Ваши награды"): what a finished match, a claimed achievement, a reward book node or a
    /// notification's claim button hands the player. It sits on the Popup input layer above whatever opened it
    /// and holds the input until closed; Escape is the game's own close.
    ///
    /// Three stops, as on screen: the rewards (each in the game's own reward words; Enter is the slot's own
    /// click, which opens a card's preview; Space reads a card reward in full), then the text below them (the
    /// achievement or event the rewards came from, its description, score and date), then the popup's
    /// glyph-key buttons (next, exit, open kegs, go to the reward book), clicked through the game's own controls,
    /// the main action first.
    /// </summary>
    internal sealed class RewardPopupScreen : Screen
    {
        public override string Key => "reward-popup";
        public override int Layer => 2;

        public override string ScreenName
        {
            get
            {
                var p = Panel();
                string title = p == null ? "" : UiText.Clean(p.m_TitleLabel?.Text?.text);
                return title.Length > 0 ? title : "Rewards";
            }
        }

        public override bool IsActive() => Panel() != null;

        // The reward popup attached to the input layer that has the input.
        private static UIRewardPopupPanel Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var p = attached[i]?.TryCast<UIRewardPopupPanel>();
                if (p != null && p.gameObject.activeInHierarchy) return p;
            }
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();
            var claimed = new HashSet<string>();  // texts read by a reward or a button, kept out of the details
            _cards.Clear();

            var slots = new List<UIPopupRewardSlot>();
            foreach (var s in p.GetComponentsInChildren<UIPopupRewardSlot>(false))
                if (s.ButtonControl != null && s.ButtonControl.gameObject.activeInHierarchy) slots.Add(s);
            if (slots.Count > 0)
            {
                b.BeginStop("rewards");
                b.PushContext("Rewards");
                b.StartRow("rewards"); // side by side as drawn: Left/Right move, Up/Down read a card reward
                foreach (var s in slots)
                {
                    var slot = s;
                    var btn = slot.ButtonControl;
                    used.Add(btn.Pointer);
                    foreach (var t in CrimsonNodes.Texts(slot)) claimed.Add(t);
                    var id = CrimsonNodes.Id(btn);
                    _cards[(string)id.StructuralKey] = slot;
                    b.AddItem(id, new NodeVtable
                    {
                        Announcements = new[] { new NodeAnnouncement(() => RewardName(slot), kind: AnnouncementKinds.Label) },
                        OnActivate = () => Navigator.ClickControl(btn),
                        OnTooltip = () => Speech.Say(RewardDetails(slot), true),
                    });
                }
                b.EndRow();
                b.PopContext();
            }

            var buttons = Buttons(p, used);
            foreach (var c in buttons) foreach (var t in CrimsonNodes.Texts(c)) claimed.Add(t);

            // Everything else the popup prints: the source of the rewards, below them. The title is the screen name.
            string title = UiText.Clean(p.m_TitleLabel?.Text?.text);
            var info = CrimsonNodes.Texts(p).Where(t => t != title && !claimed.Contains(t)).ToList();
            if (info.Count > 0)
            {
                string text = string.Join(". ", info);
                b.BeginStop("details");
                b.AddItem(ControlId.Structural("reward-popup:details:" + text), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => text, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(text, true),
                });
            }

            if (buttons.Count > 0)
            {
                b.BeginStop("buttons");
                foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        // The popup's own selectable controls, then its glyph-key buttons: those are never selectable — the game
        // reaches them by key — but clicking goes through the container's own callback.
        private static List<AControl> Buttons(UIRewardPopupPanel p, HashSet<IntPtr> used)
        {
            // A bare key-glyph label (Label_BasicGlyph) can be selectable with nothing to say: it read as its
            // object name, "Label Basic Glyph".
            var buttons = CrimsonNodes.Leaves(p, used).Where(c => c.TryCast<GlyphLabelControl>() == null || CrimsonNodes.Texts(c).Count > 0).ToList();
            foreach (var bar in p.GetComponentsInChildren<InputButtonContainer>(false))
            {
                var children = bar.GetChildren();
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                    if (CrimsonNodes.Texts(c).Count == 0) continue;  // an empty spare slot in the bar
                    if (used.Add(c.Pointer)) buttons.Add(c);
                }
            }
            // The popup's main action (Next, in ButtonsPanelMainParent) first, so Tab lands on it, not on the
            // side panel's Skip training (ButtonsPanelLeftParent) a stray Enter would take.
            return buttons.OrderBy(c => InMainPanel(c) ? 0 : 1).ToList();
        }

        private static bool InMainPanel(AControl c)
        {
            for (var t = c.transform; t != null; t = t.parent)
                if (t.name.IndexOf("ButtonsPanelMain", StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // The reward in the game's own words ("Гребаная пехтура (аватар)"), else what the slot prints.
        private static string RewardName(UIPopupRewardSlot slot)
        {
            try
            {
                string s = UiText.Clean(RewardTextHelper.GetText(slot.m_Reward));
                if (s.Length > 0) return s;
            }
            catch (Exception e) { Plugin.LogInfo("[reward-popup] reward text failed: " + e.Message); }
            var texts = CrimsonNodes.Texts(slot);
            return texts.Count > 0 ? string.Join(", ", texts) : "Reward";
        }

        // A card reward reads as the card in full; anything else as everything its slot prints.
        private static string RewardDetails(UIPopupRewardSlot slot)
        {
            var card = CardOf(slot);
            if (card != null) return RewardName(slot) + ". " + CardReader.Whole(card);
            var parts = new List<string> { RewardName(slot) };
            foreach (var t in CrimsonNodes.Texts(slot)) if (!parts.Contains(t)) parts.Add(t);
            return string.Join(". ", parts);
        }

        private static GwentGameplay.Card CardOf(UIPopupRewardSlot slot)
        {
            try { return slot?.m_View?.TryCast<UIPopupRewardCardView>()?.Card; }
            catch (Exception e) { Plugin.LogInfo("[reward-popup] card read failed: " + e.Message); return null; }
        }

        // ---- card keys: a card reward reads line by line, like a card on the board ----

        private readonly Dictionary<string, UIPopupRewardSlot> _cards = new Dictionary<string, UIPopupRewardSlot>();
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
            return node?.Id.StructuralKey is string key && _cards.TryGetValue(key, out var slot) ? CardOf(slot) : null;
        }
    }
}
