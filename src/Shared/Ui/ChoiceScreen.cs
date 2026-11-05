using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentGameplay;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// A card choice (create, pick from the deck, discard…): the offered cards as one row, read like on the
    /// board (Up/Down, I, K, Space). Enter picks the focused card — or toggles it when several may be
    /// picked — E finishes a multi-pick, Backspace cancels when the game allows it. Every press goes through
    /// the game's own choice panel handler, as its buttons and card clicks do. The board stays readable
    /// behind the choice, as a sighted player looks past the panel before picking: 1 to 4 (and Shift+1 to 4)
    /// read a board row, the battle screen's own row read (<see cref="BattleScreen.RowKeys"/>).
    /// </summary>
    internal sealed class ChoiceScreen : Screen
    {
        public override string Key => "choice";
        public override int Layer => 1;

        private GameController _gc;
        private RequestCardChoicesAction _req;

        public override bool IsActive()
        {
            _gc = Battle.Controller();
            if (_gc == null) return false;
            _req = Plays.Read(_gc).Choice;
            return _req != null && !IsHidden(_req);
        }

        // ---- hiding the panel to look at the board ----

        /// <summary>Whether <paramref name="req"/>'s panel is hidden to show the board: the panel's own "Hide
        /// cards" button tucked it away (its Visibility Hidden) and the board's "Show cards" button is up. The
        /// battle screen has the cursor meanwhile. Both are checked: a panel is also Hidden for a moment as a
        /// choice opens, with no "Show cards" button.</summary>
        internal static bool IsHidden(RequestCardChoicesAction req)
        {
            if (req == null) return false;
            try
            {
                var panel = Handler()?.m_BattleChoicePanel;
                if (panel == null || panel.Visibility != CrimsonUIBehaviour.EVisibility.Hidden) return false;
                var show = ShowButton();
                return show != null && show.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        private static UIBattlePanel _battlePanel;

        // The board's "Show cards" button (UIBattlePanel.m_ShowChoicePanelButton), which brings a hidden choice back.
        private static ButtonControl ShowButton()
        {
            if (_battlePanel == null) _battlePanel = UnityEngine.Object.FindObjectOfType<UIBattlePanel>();
            return _battlePanel?.m_ShowChoicePanelButton;
        }

        /// <summary>Tab: hides the choice panel to look at the board, or brings it back, by clicking the button a
        /// sighted player clicks ("Hide cards" on the panel, "Show cards" on the board), and says which, as
        /// Hearthstone Access does ("Choices hidden." / "Choices shown."). The panel's handler toggle
        /// (HandleHideTogglePressed) did not hide it in a practice match; the button does.</summary>
        internal static void ToggleHidden(RequestCardChoicesAction req)
        {
            if (req == null) return;
            bool hide = !IsHidden(req);
            ButtonControl button = null;
            try { button = hide ? Handler()?.m_BattleChoicePanel?.m_IconHideButtonControl : ShowButton(); } catch { }
            if (button == null || !Navigator.MouseClick(button))
            {
                Plugin.LogInfo("[choice] " + (hide ? "hide" : "show") + " button not found");
                return;
            }
            Plugin.LogInfo("[choice] " + (hide ? "hidden" : "shown"));
            _quietReturn = !hide;
            Speech.Say(hide ? Loc.T("Choices hidden.") : Loc.T("Choices shown."), true);
        }

        public override string ScreenName
        {
            get
            {
                if (_req == null) return Loc.T("Choose a card");
                string what = Title(_req.ChoiceType);
                Card requester = null;
                try { requester = _gc.CardManager.GetCard(_req.RequesterId); } catch { }
                // Back from Tab's hide the player hears "Choices shown." and the card, not the title again.
                if (_quietReturn) return "";
                if (requester != null) what = Loc.T("{0} for {1}", what, CardReader.Name(requester, _gc.PlayerManager));
                int max = 1;
                try { max = _req.MaxChoices; } catch { }
                return max > 1 ? Loc.T("{0}, pick up to {1}, E when done", what, max) : what;
            }
        }

        private static string Title(ERequestChoiceType t) => t switch
        {
            ERequestChoiceType.CardToPlay => Loc.T("Choose a card to play"),
            ERequestChoiceType.CardToMove => Loc.T("Choose a card to move"),
            ERequestChoiceType.CardToDiscard => Loc.T("Choose a card to discard"),
            ERequestChoiceType.CardToPutBackInDeck => Loc.T("Choose a card to put back in your deck"),
            ERequestChoiceType.CardToDraw => Loc.T("Choose a card to draw"),
            ERequestChoiceType.CardToSpawn => Loc.T("Choose a card to spawn"),
            ERequestChoiceType.CardToCopy => Loc.T("Choose a card to copy"),
            ERequestChoiceType.CardToReveal => Loc.T("Choose a card to reveal"),
            _ => Loc.T("Choose a card"),
        };

        private static SelectChoicesHandlerComponent Handler()
        {
            try { return UnityEngine.Object.FindObjectOfType<SelectChoicesHandlerComponent>(); } catch { return null; }
        }

        private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();

        public override void Build(GraphBuilder b)
        {
            var req = _req;
            if (req == null || _gc == null) return;
            // A choice is the local player's own (Plays reads only their requests), and the panel shows its
            // chooser every offered card face up: cards made for the choice sit in the spawning pool, where every
            // visibility rule the game has (IsVisibleToLocal, IsVisibleToLocalPreview) reads them hidden (seen
            // in a practice match, Imperial Diplomacy's three), so no match check is made here.
            PlayerManager pm = null;
            _cards.Clear();
            var offered = new List<(Card Card, bool Enabled)>();
            try
            {
                for (int i = 0; i < req.ValidChoices.Count; i++) offered.Add((req.ValidChoices[i], true));
                for (int i = 0; i < req.DisabledChoices.Count; i++) offered.Add((req.DisabledChoices[i], false));
            }
            catch (Exception e) { Plugin.LogInfo("[choice] read failed: " + e.Message); }
            if (offered.Count == 0) return;

            b.BeginStop("choices");
            b.PushContext(Loc.T("Choices"));
            b.StartRow();
            foreach (var (card, enabled) in offered)
            {
                var id = ControlId.Structural("choice:card:" + card.Id);
                _cards[(string)id.StructuralKey] = card;
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => CardReader.Summary(card, pm), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => enabled ? (Chosen(card) ? Loc.T("chosen") : null) : Loc.T("unavailable"), live: true, kind: AnnouncementKinds.Selected),
                    },
                    OnActivate = () => Pick(card, enabled),
                    OnTooltip = () => Speech.Say(CardReader.Whole(card, pm), true),
                });
            }
            b.EndRow();
            // Shown again after Tab hid it: the cursor goes back to the card it was on.
            // Only within the same choice: the cards made for each choice reuse the same ids (1000, 1001, …), so
            // a card remembered from an earlier choice would start the next one on its third card (seen in a
            // ranked match).
            if (_lastCard != null && _lastReq == ReqId(req) && _cards.ContainsKey(_lastCard)) b.SetStart(ControlId.Structural(_lastCard));
            b.PopContext();
        }

        private bool Chosen(Card card)
        {
            try { return _req.SelectedChoices.Contains(card.Id); } catch { return false; }
        }

        /// <summary>When Enter or E last answered a choice (realtime seconds). The play it asks for comes as a
        /// request of its own a few frames after the panel closes; the battle screen waits for it
        /// (<see cref="BattleScreen"/>'s Build) instead of announcing a landing that is gone at once.</summary>
        public static float AnsweredAt { get; private set; } = -100f;

        /// <summary>The card a single pick chose, and where it was then: the game's own sign that the answer
        /// went through is the card leaving that spot (to be placed, or played on its own). Null after a
        /// multi-pick's Finish, which waits on <see cref="AnsweredAt"/> alone.</summary>
        public static Card AnsweredCard { get; private set; }
        private static ELocation _answeredFrom;

        /// <summary>Whether the picked card still sits where the choice offered it: the game has not acted on
        /// the answer yet.</summary>
        public static bool AnswerPending()
        {
            var card = AnsweredCard;
            if (card == null) return false;
            try { return card.Position.Location == _answeredFrom; } catch { return false; }
        }

        private void Pick(Card card, bool enabled)
        {
            if (!enabled) { Speech.Say(Loc.T("Unavailable."), true); return; }
            var h = Handler();
            if (h == null) { Speech.Say(Loc.T("Couldn't pick."), true); return; }
            try
            {
                int max = _req.MaxChoices;
                if (max > 1) h.ToggleChoice(card);
                else h.SelectChoice(card);
                AnsweredAt = Time.realtimeSinceStartup;
                AnsweredCard = max > 1 ? null : card;
                try { _answeredFrom = card.Position.Location; } catch { AnsweredCard = null; }
                Plugin.LogInfo("[choice] pick " + card.Id);
            }
            catch (Exception e) { Plugin.LogInfo("[choice] pick failed: " + e); Speech.Say(Loc.T("Couldn't pick."), true); }
        }

        private void Finish()
        {
            var h = Handler();
            if (h == null) return;
            try { h.HandleFinishBtnClicked(); AnsweredAt = Time.realtimeSinceStartup; AnsweredCard = null; Plugin.LogInfo("[choice] finish"); }
            catch (Exception e) { Plugin.LogInfo("[choice] finish failed: " + e); Speech.Say(Loc.T("Couldn't finish."), true); }
        }

        private void Cancel()
        {
            bool can = false;
            try { can = _req.CanCancel; } catch { }
            if (!can) { Speech.Say(Loc.T("This can't be cancelled."), true); return; }
            var h = Handler();
            try { h?.HandleCancelBtnClicked(); Speech.Say(Loc.T("Cancelled."), true); }
            catch (Exception e) { Plugin.LogInfo("[choice] cancel failed: " + e); }
        }

        // ---- keys ----

        public override AControl GameSelection() => null;
        public override void OnCursorMoved(GraphNode node)
        {
            _cardKeys?.Reset();
            if (node?.Id.StructuralKey is string key && _cards.ContainsKey(key)) { _lastCard = key; _lastReq = ReqId(_req); }
        }

        private static int _lastReq = -1;

        private static int ReqId(RequestCardChoicesAction req)
        {
            try { return req?.Id ?? -1; } catch { return -1; }
        }

        // The card the cursor was last on, so a choice shown again after Tab comes back to it.
        private static string _lastCard;

        // Set when Tab shows the choices again: the title stays quiet once, so the player hears "Choices
        // shown." and then the card they were on (Hearthstone Access's way), not the title again.
        private static bool _quietReturn;

        public override void OnPop() => _quietReturn = false; // the choice went; the next one is news

        public override bool QuietLanding(GraphNode node)
        {
            _quietReturn = false; // the title was skipped; the card is said, as a reminder of where you are
            return false;
        }

        private CardKeys _cardKeys;
        private List<ScreenKey> _keys;

        public override IReadOnlyList<ScreenKey> ExtraKeys
        {
            get
            {
                if (_keys != null) return _keys;
                _cardKeys = new CardKeys(FocusedCard, () => null, always: true); // as in Build: the chooser sees every offered card
                _keys = new List<ScreenKey>(_cardKeys.Keys)
                {
                    new ScreenKey { Key = KeyCode.E, Run = Finish, Hint = Loc.T("E, done choosing") },
                    new ScreenKey { Key = KeyCode.Tab, Run = () => ToggleHidden(_req), Hint = Loc.T("Tab, hide the choices to look at the board, Tab again to show them") },
                    new ScreenKey { Key = KeyCode.Backspace, Run = Cancel, Hint = Loc.T("Backspace, cancel") },
                    new ScreenKey { Key = KeyCode.A, Run = () => { if (_gc != null) Speech.Say(Battle.StatusText(_gc), true); }, Hint = Loc.T("A, score") },
                };
                _keys.AddRange(BattleScreen.RowKeys(() => _gc, jump: null));
                return _keys;
            }
        }

        private Card FocusedCard()
        {
            var node = GraphNavigator.CurrentNode;
            return node?.Id.StructuralKey is string key && _cards.TryGetValue(key, out var card) ? card : null;
        }
    }
}
