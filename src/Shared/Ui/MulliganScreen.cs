using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentGameplay;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The mulligan at the start of each round: your hand as one row of cards (read like on the board:
    /// Ctrl+Up/Down, I, K, Space), Enter swaps the focused card, E keeps the rest and ends your mulligan through
    /// the panel's own finish button (held while E is); Tab
    /// reaches the panel's own buttons (finish, hide cards), which the game itself only offers on glyph keys.
    ///
    /// Swaps are the panel's own click on the card (<c>UIMulliganPanel.HandleCardClicked</c>, as the mouse), so
    /// the cursor needs no game highlight. When the replacement arrives the swap is announced and the cursor
    /// lands on the new card.
    /// </summary>
    internal sealed class MulliganScreen : Screen
    {
        public override string Key => "mulligan";
        public override int Layer => 1;

        // A swapped card's slot comes back holding its replacement.
        public override bool ReturnsToOpener => false;

        public override string ScreenName
        {
            get
            {
                var gc = Battle.Controller();
                if (gc == null) return "Mulligan";
                // The first mulligan comes before the game starts round 1 (its index is still -1 then), so the
                // match opening is told by the match, not the round number.
                string round = "", leaders = "";
                try { round = ", round " + Math.Max(gc.RoundManager.CurrentRoundIndex + 1, 1); } catch { }
                if (gc.Pointer != _leadersSaidIn)
                {
                    _leadersSaidIn = gc.Pointer;
                    leaders = Leaders(gc);
                }
                return leaders + "Mulligan" + round + ", " + Swaps(Left(gc)) + " left. Enter swaps a card, E keeps the rest.";
            }
        }

        private static IntPtr _leadersSaidIn;

        /// <summary>The match opening: both leaders as the versus screen shows them, the character and then the
        /// leader ability card. "Your leader: Эмгыр вар Эмрейс, Двойная игра, Нильфгаард. Opponent's leader: …"</summary>
        private static string Leaders(GameController gc)
        {
            string Line(EPlayerId who, string label)
            {
                var cards = Battle.Cards(gc, who, ELocation.Leader);
                if (cards.Count == 0) return "";
                string character = Battle.LeaderCharacter(who);
                return label + ": " + (character != null ? character + ", " : "") + CardReader.Leader(cards[0]) + ". ";
            }
            try { return Line(Battle.Me(gc), "Your leader") + Line(Battle.Them(gc), "Opponent's leader"); }
            catch { return ""; }
        }

        public override bool IsActive() => Panel() != null && Battle.Controller() != null;

        private static AControl Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var p = attached[i]?.TryCast<GwentUnity.UI.UIMulliganPanel>();
                // Not while it is hiding: after the confirm popup's YES the panel plays its closing animation, and
                // taking the cursor back for those frames re-announced "Mulligan, 1 swap left".
                if (p != null && p.gameObject.activeInHierarchy
                    && p.Visibility != CrimsonUIBehaviour.EVisibility.Hiding && p.Visibility != CrimsonUIBehaviour.EVisibility.Hidden) return p;
            }
            return null;
        }

        private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();
        private GameController _gc;

        public override void Build(GraphBuilder b)
        {
            var panel = Panel();
            _gc = Battle.Controller();
            if (panel == null || _gc == null) return;
            var hand = Battle.Cards(_gc, Battle.Me(_gc), ELocation.Hand);
            var pm = _gc.PlayerManager;
            CheckSwap(hand);

            _cards.Clear();
            if (hand.Count > 0)
            {
                b.BeginStop("cards");
                b.PushContext("Your hand");
                b.StartRow();
                foreach (var card in hand)
                {
                    var id = CardId(card);
                    _cards[(string)id.StructuralKey] = card;
                    b.AddItem(id, new NodeVtable
                    {
                        Announcements = new[] { new NodeAnnouncement(() => CardReader.WithAbilities(card, pm), kind: AnnouncementKinds.Label) },
                        OnActivate = () => Swap(card),
                        OnTooltip = () => Speech.Say(CardReader.Whole(card, pm), true),
                    });
                }
                b.EndRow();
                b.PopContext();
            }

            // The panel's glyph buttons (finish swapping, hide cards): never selectable in the game, offered
            // here and clicked through their own callbacks, as in the settings window. The icon-only twins
            // (a checkmark, an eye) repeat the labelled ones and are left out.
            var buttons = new List<AControl>();
            foreach (var bar in panel.GetComponentsInChildren<InputButtonContainer>(false))
            {
                var children = bar.GetChildren();
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                    if (CrimsonNodes.Texts(c).Count == 0) continue;
                    if (!buttons.Exists(x => x.Pointer == c.Pointer)) buttons.Add(c);
                }
            }
            if (buttons.Count > 0)
            {
                b.BeginStop("buttons");
                foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        private static ControlId CardId(Card card) => ControlId.Structural("mulligan:card:" + card.Id);

        private static int Left(GameController gc)
        {
            try { return gc.PlayerManager.LocalUser.MulligansLeft; } catch { return 0; }
        }

        private static string Swaps(int n) => n == 1 ? "1 swap" : n + " swaps";

        // ---- swapping ----

        // The swap in flight: the card sent back and the hand it left, to spot the replacement.
        private ushort _swapped;
        private string _swappedName;
        private HashSet<ushort> _before;
        private float _swapAt;

        private void Swap(Card card)
        {
            var gc = _gc;
            if (gc == null) return;
            var me = gc.PlayerManager.LocalUser;
            bool can = false;
            try { can = me.CanMulligan; } catch { }
            if (!can) { Speech.Say("You've finished swapping. Waiting for the opponent.", true); return; }
            if (Left(gc) <= 0) { Speech.Say("No swaps left. E keeps your hand.", true); return; }
            if (_before != null) { Speech.Say("Still swapping.", true); return; }

            // The panel's own click on that card (selecting it and raising OnCardClicked, which the mulligan flow
            // swaps on). A cursor press on the card button doesn't reach it: the panel hears its card container,
            // not the button.
            var panel = Panel()?.TryCast<GwentUnity.UI.UIMulliganPanel>();
            var button = CardButton(card);
            try
            {
                if (panel == null || button == null) throw new InvalidOperationException("no card button for " + card.Id);
                panel.HandleCardClicked(button);
            }
            catch (Exception e)
            {
                Plugin.LogInfo("[mulligan] swap failed: " + e.Message);
                Speech.Say("Swap failed.", true);
                return;
            }
            _before = new HashSet<ushort>();
            foreach (var c in Battle.Cards(gc, Battle.Me(gc), ELocation.Hand)) _before.Add(c.Id);
            _swapped = card.Id;
            _swappedName = UiText.Clean(Localization.LocalizationHelper.GetCardName(card));
            _swapAt = Time.realtimeSinceStartup;
            Plugin.LogInfo("[mulligan] swap pressed " + _swappedName + " (" + card.Id + ")");
            Speech.Say("Swapping " + _swappedName + ".", true);
        }

        /// <summary>The panel's button for a card in your hand: what the mouse clicks to swap it.</summary>
        private static CardButtonControl CardButton(Card card)
        {
            try
            {
                var buttons = Panel()?.TryCast<GwentUnity.UI.UIMulliganPanel>()?.m_CardContainer?.CardButtons;
                for (int i = 0; buttons != null && i < buttons.Count; i++)
                    if (buttons[i]?.Card != null && buttons[i].Card.Id == card.Id) return buttons[i];
            }
            catch { }
            return null;
        }

        // Every build while a swap is in flight: once the sent card has left and a new one arrived, say so and
        // land on the new card. If nothing arrives (an empty deck), report the swap after a few seconds. A click
        // the game didn't take leaves the card in hand: dropped after those seconds, logged, not said.
        private void CheckSwap(List<Card> hand)
        {
            if (_before == null) return;
            bool gone = !hand.Exists(c => c.Id == _swapped);
            var fresh = hand.Find(c => !_before.Contains(c.Id));
            bool late = Time.realtimeSinceStartup - _swapAt > 3f;
            if (late && !gone)
            {
                _before = null;
                Plugin.LogInfo("[mulligan] swap of " + _swappedName + " not made");
                return;
            }
            if (!(gone && fresh != null) && !(gone && late)) return;
            _before = null;
            Speech.Say("Swapped " + _swappedName + ". " + Swaps(Left(_gc)) + " left.", false);
            if (fresh != null) GraphNavigator.Suggest(CardId(fresh));
        }

        // ---- pressing the panel's buttons ----

        // Finishing (E) is the panel's own button, pressed while the key is down and let go with it — the
        // mouse's press, so the button's hold (if any), its sounds and its click run as for a sighted player,
        // rather than the internal AskFinishMulligan.
        private ButtonControl _held;
        private KeyCode[] _heldKeys;

        private void Press(ButtonControl button, params KeyCode[] keys)
        {
            Release();
            if (!BattleInput.SendButton(button, CursorButtonEventArgs.EState.ButtonDown)) return;
            _held = button;
            _heldKeys = keys;
        }

        private void Release()
        {
            var button = _held;
            _held = null;
            if (button == null) return;
            try { Plugin.LogInfo("[mulligan] let go at hold " + button.HoldValue.ToString("0.00")); } catch { }
            BattleInput.ReleaseButton(button);
        }

        public override void Tick()
        {
            if (_held == null) return;
            if (!Array.Exists(_heldKeys, Keys.Held)) Release();
            else BattleInput.HoldButton(_held);
        }

        public override void OnUnfocus() => Release();
        public override void OnPop() => Release();

        private void Finish()
        {
            var gc = _gc;
            if (gc == null) return;
            var me = gc.PlayerManager.LocalUser;
            bool can = false;
            try { can = me.CanMulligan; } catch { }
            // The panel lingers through its closing animation after both hands are kept, while the flag still
            // reads true; with no mulligan request open there is nothing left to finish.
            if (!can || !Narrator.InMulligan()) { Speech.Say("Waiting for the opponent.", true); return; }
            ButtonControl button = null;
            try { button = Panel()?.TryCast<GwentUnity.UI.UIMulliganPanel>()?.GetFinishMulliganButton(); } catch { }
            Press(button, KeyCode.E);
            if (_held == null)
            {
                Plugin.LogInfo("[mulligan] finish button not found");
                Speech.Say("Couldn't finish the mulligan.", true);
                return;
            }
            Plugin.LogInfo("[mulligan] finish pressed");
        }

        // ---- keys ----

        public override AControl GameSelection() => null; // the game's card picker isn't followed; swaps go to the game directly

        private CardKeys _cardKeys;
        private List<ScreenKey> _keys;

        public override IReadOnlyList<ScreenKey> ExtraKeys
        {
            get
            {
                if (_keys != null) return _keys;
                _cardKeys = new CardKeys(FocusedCard, () => _gc?.PlayerManager);
                _keys = new List<ScreenKey>(_cardKeys.Keys)
                {
                    new ScreenKey { Key = KeyCode.E, Run = Finish, Hint = "E, keep the rest and finish" },
                    new ScreenKey { Key = KeyCode.A, Run = () => { if (_gc != null) Speech.Say(Battle.StatusText(_gc), true); }, Hint = "A, score" },
                };
                return _keys;
            }
        }

        public override void OnCursorMoved(GraphNode node)
        {
            _cardKeys?.Reset();
            base.OnCursorMoved(node);
        }

        private Card FocusedCard()
        {
            var node = GraphNavigator.CurrentNode;
            return node?.Id.StructuralKey is string key && _cards.TryGetValue(key, out var card) ? card : null;
        }
    }
}
