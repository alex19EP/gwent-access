using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentVisuals.ArenaV2;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Draft (Arena v2) outside the picking step, which is <see cref="DraftScreen"/>'s: the arena panel shows
    /// one of three widgets, and this screen reads it.
    ///
    /// - The ticket (<see cref="ArenaTicketPurchaseContainer"/>): the title and subtitle, then the ways in, each
    ///   with its price and currency named ("Pay now, 150 Ore"), or Use your ticket; the user agreement link.
    /// - Between games (<see cref="ArenaReadyToPlayWidget"/>): the wins so far, the reward wheel read in words
    ///   (a section per game: won, lost, drawn, next, then how many are still to play), Play, and while a match
    ///   is being searched for, the search's text and its cancel.
    /// - The run's end (<see cref="ArenaSummaryWidget"/>): the wins, "Your rewards:" and each reward, then
    ///   Draft again.
    ///
    /// Then the panel's own buttons (show deck, redraft, the bar). Not while picking; the deck picker Show deck
    /// opens is <see cref="DraftDeckScreen"/>'s, over this one.
    /// </summary>
    internal sealed class DraftLobbyScreen : Screen
    {
        public override string Key => "draft-lobby";
        public override int Layer => 1;
        public override bool Overlays => true;
        public override string ScreenName => GameText.Get("arenav2_mainmenu_title", "Draft");

        public override bool IsActive() => Panel() != null;

        private static UIArenaPanel Panel()
        {
            var p = CrimsonNodes.ActivePanel<UIArenaPanel>();
            if (p == null) return null;
            try
            {
                // The deck picker (Show deck) is DraftDeckScreen's, over this one: the lobby stays under it, so
                // closing the deck comes back to Show deck.
                if (CrimsonNodes.Shown(p.ArenaDraftingContainerInstance)) return null;
                return Ticket(p) != null || Ready(p) != null || Summary(p) != null ? p : null;
            }
            catch { return null; }
        }

        private static ArenaTicketPurchaseContainer Ticket(UIArenaPanel p) =>
            CrimsonNodes.Shown(p.ArenaTicketPurchaseContainerInstance) ? p.ArenaTicketPurchaseContainerInstance : null;
        private static ArenaReadyToPlayWidget Ready(UIArenaPanel p) =>
            CrimsonNodes.Shown(p.m_ArenaReadyToPlayWidgetInstance) ? p.m_ArenaReadyToPlayWidgetInstance : null;
        private static ArenaSummaryWidget Summary(UIArenaPanel p) =>
            CrimsonNodes.Shown(p.ArenaSummaryWidgetInstance) ? p.ArenaSummaryWidgetInstance : null;

        public override AControl GameSelection() => null;

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();
            if (Summary(p) is ArenaSummaryWidget s) BuildSummary(b, s, used);
            else if (Ready(p) is ArenaReadyToPlayWidget r) BuildReady(b, r, used);
            else if (Ticket(p) is ArenaTicketPurchaseContainer t) BuildTicket(b, t, used);

            // The panel's own controls: show deck, redraft, the cosmetics, the bar.
            var rest = new List<AControl>();
            foreach (var c in new AControl[] { p.m_DeckToggle, p.m_RedraftToggle })
                if (CrimsonNodes.Shown(c) && used.Add(c.Pointer)) rest.Add(c);
            rest.AddRange(CrimsonNodes.Leaves(p, used));
            rest.AddRange(CrimsonNodes.BarButtons(p, used));
            if (rest.Count > 0)
            {
                b.BeginStop("draft:buttons");
                foreach (var c in rest)
                {
                    // Show deck opens the deck picker over the lobby and Abandon the redraft confirmation: toggles
                    // only in the game's make (each alone in its group), so buttons. The panel's own copy of the
                    // social bar (journey, season cycle, quests, friends: one radio group) opens the side window's
                    // sections, as the main menu's does: tabs. Their toggled flags don't follow what is open (the
                    // group keeps its first one on), so no state is said.
                    var v = CrimsonNodes.Button(c);
                    if (c.TryCast<ToggleButtonControl>() != null && CrimsonNodes.Role(c) == Loc.T("radio button"))
                        v = CrimsonNodes.WithRole(v, Loc.T("tab"));
                    b.AddItem(CrimsonNodes.Id(c), v);
                }
            }
        }

        // ---- the ticket ----

        private static void BuildTicket(GraphBuilder b, ArenaTicketPurchaseContainer t, HashSet<IntPtr> used)
        {
            b.BeginStop("draft:ticket");
            AddText(b, "draft:ticket:title", t.m_TitleLabel);
            AddText(b, "draft:ticket:subtitle", t.m_SubtitleLabel);
            AddText(b, "draft:ticket:buy", t.m_BuyLabel);
            AddText(b, "draft:ticket:enter", t.m_EnterLabel);
            if (CrimsonNodes.Shown(t.m_NoProducts)) AddText(b, "draft:ticket:none", t.m_NoProductLabel);
            ControlId first = null;
            foreach (var buy in new[] { t.m_BuyRealButton, t.m_BuyVirtualButton1, t.m_BuyVirtualButton2 })
            {
                var c = buy?.ButtonControl;
                if (!CrimsonNodes.Shown(c) || !used.Add(c.Pointer)) continue;
                var id = CrimsonNodes.Id(c);
                first ??= id;
                b.AddItem(id, CrimsonNodes.Button(c, () => ShopScreen.PricedLabel(c)));
            }
            var use = t.m_UseTicketButton;
            if (CrimsonNodes.Shown(use) && used.Add(use.Pointer))
            {
                var id = CrimsonNodes.Id(use);
                first = id; // a ticket in hand: entering is what the window is for
                b.AddItem(id, CrimsonNodes.Button(use));
            }
            if (first != null) b.SetStart(first);
            var eula = t.m_EulaLink;
            if (CrimsonNodes.Shown(eula) && used.Add(eula.Pointer))
                b.AddItem(CrimsonNodes.Id(eula), CrimsonNodes.Button(eula, () =>
                    UiText.Join(CrimsonNodes.Text(eula.m_TitleLabel), CrimsonNodes.Text(eula.m_TextLabel)) is string s && s.Length > 0 ? s : CrimsonNodes.Label(eula)));
            foreach (var c in CrimsonNodes.Leaves(t, used)) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        // ---- between games ----

        private static void BuildReady(GraphBuilder b, ArenaReadyToPlayWidget r, HashSet<IntPtr> used)
        {
            b.BeginStop("draft:ready");
            if (CrimsonNodes.Shown(r.m_MatchMakingRoot)) AddText(b, "draft:searching", r.m_MatchmakingText);
            string wins = CrimsonNodes.Text(r.m_WinCount);
            if (wins.Length > 0)
                b.AddLabel(ControlId.Structural("draft:wins"), () =>
                    GameText.Get("arenav2_battlepicker_wins_counter", "Wins in current draft") + ", " + CrimsonNodes.Text(r.m_WinCount));
            b.AddLabel(ControlId.Structural("draft:wheel"), () => Wheel(r));
            ControlId start = null;
            foreach (var c in new AControl[] { r.m_PlayButton, r.m_CancelButton })
            {
                if (!CrimsonNodes.Shown(c) || !used.Add(c.Pointer)) continue;
                var id = CrimsonNodes.Id(c);
                start ??= id;
                b.AddItem(id, CrimsonNodes.Button(c));
            }
            if (start != null) b.SetStart(start);
            foreach (var c in CrimsonNodes.Leaves(r, used)) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        /// <summary>The reward wheel in words: a section per game of the run, coloured by its result, and the
        /// next one marked: "Won, won, lost, next; 5 still to play".</summary>
        private static string Wheel(ArenaReadyToPlayWidget r)
        {
            try
            {
                var states = r.GetWheelPartsStates();
                if (states == null || states.Count == 0) return null;
                var said = new List<string>();
                int open = 0;
                for (int i = 0; i < states.Count; i++)
                {
                    switch (states[i])
                    {
                        case EWheelPartState.Win: said.Add(Loc.T("won")); break;
                        case EWheelPartState.Lost: said.Add(Loc.T("lost")); break;
                        case EWheelPartState.Draw: said.Add(Loc.T("drawn")); break;
                        case EWheelPartState.Next: said.Add(Loc.T("next")); break;
                        default: open++; break;
                    }
                }
                string games = said.Count > 0 ? GameText.Capitalized(string.Join(", ", said)) : null;
                return UiText.Join(Loc.T("Wheel"), games, open > 0 ? Loc.T("{0} still to play", open) : null);
            }
            catch (Exception e) { Plugin.LogInfo("[draft] wheel read failed: " + e.Message); return null; }
        }

        // ---- the run's end ----

        private static void BuildSummary(GraphBuilder b, ArenaSummaryWidget s, HashSet<IntPtr> used)
        {
            b.BeginStop("draft:summary");
            string wins = CrimsonNodes.Text(s.m_WinsCountLabel);
            if (wins.Length > 0)
                b.AddLabel(ControlId.Structural("draft:summary:wins"), () =>
                    GameText.Get("arenav2_summary_wins", "Wins") + ", " + CrimsonNodes.Text(s.m_WinsCountLabel));
            AddText(b, "draft:summary:rewards", s.m_YourRewardsText);
            var slots = s.m_RewardSlots;
            for (int i = 0; slots != null && i < slots.Count; i++)
            {
                var slot = slots[i];
                if (!CrimsonNodes.Shown(slot) || !used.Add(slot.Pointer)) continue;
                b.AddItem(CrimsonNodes.Id(slot), CrimsonNodes.Button(slot, () => RewardSlot(slot)));
            }
            foreach (var c in CrimsonNodes.Leaves(s, used)) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        // A reward as a mouse player reads it: its tooltip, else what its picture shows, then the amount.
        private static string RewardSlot(ArenaRewardSlot slot)
        {
            string amount = CrimsonNodes.Text(slot.m_RewardAmount);
            string what = CrimsonNodes.TooltipText(slot);
            if (what == null)
            {
                try
                {
                    var card = slot.m_SingleCardRenderer?.m_CardView?.Card;
                    if (card != null && CrimsonNodes.Shown(slot.m_CardImage)) what = CardReader.Summary(card);
                }
                catch { }
            }
            if (what == null)
            {
                foreach (var img in slot.GetComponentsInChildren<UnityEngine.UI.Image>(false))
                {
                    string n = (img.sprite?.name ?? "").ToLowerInvariant();
                    what = n.Contains("keg") ? Currency.Kegs() ?? GameText.Get("panel_currency_keg", "Kegs")
                        : n.Contains("scrap") ? Currency.Scraps()
                        : n.Contains("powder") ? Currency.Powder()
                        : n.Contains("ore") || n.Contains("gold") ? Currency.Ore()
                        : n.Contains("reward") ? Currency.RewardPoints()
                        : null;
                    if (what != null) break;
                }
            }
            string text = UiText.Join(amount.Length > 0 ? amount : null, what);
            return text.Length > 0 ? text : Loc.T("Reward");
        }

        // ---- helpers ----

        private static void AddText(GraphBuilder b, string key, Label label)
        {
            if (CrimsonNodes.Text(label).Length == 0) return;
            b.AddLabel(ControlId.Structural(key), () => CrimsonNodes.Text(label));
        }
    }
}
