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
    /// Ctrl+Up/Down, I, K, Space), Enter swaps the focused card, E keeps the rest and ends your mulligan; Tab
    /// reaches the panel's own buttons (finish, hide cards), which the game itself only offers on glyph keys.
    ///
    /// Swaps go straight to the game (<see cref="Player.AskMulliganCard"/>), so the cursor needs no game
    /// highlight. When the replacement arrives the swap is announced and the cursor lands on the new card.
    /// </summary>
    internal sealed class MulliganScreen : Screen
    {
        public override string Key => "mulligan";
        public override int Layer => 1;

        public override string ScreenName
        {
            get
            {
                var gc = Battle.Controller();
                if (gc == null) return "Mulligan";
                string round = "";
                try { round = ", round " + (gc.RoundManager.CurrentRoundIndex + 1); } catch { }
                return "Mulligan" + round + ", " + Swaps(Left(gc)) + " left. Enter swaps a card, E keeps the rest.";
            }
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
                if (p != null && p.gameObject.activeInHierarchy) return p;
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
                        Announcements = new[] { new NodeAnnouncement(() => CardReader.Summary(card, pm), kind: AnnouncementKinds.Label) },
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

            _before = new HashSet<ushort>();
            foreach (var c in Battle.Cards(gc, Battle.Me(gc), ELocation.Hand)) _before.Add(c.Id);
            _swapped = card.Id;
            _swappedName = UiText.Clean(Localization.LocalizationHelper.GetCardName(card));
            _swapAt = Time.realtimeSinceStartup;
            try
            {
                me.AskMulliganCard(card.Id);
                Plugin.LogInfo("[mulligan] swap " + _swappedName + " (" + card.Id + ")");
                Speech.Say("Swapping " + _swappedName + ".", true);
            }
            catch (Exception e)
            {
                _before = null;
                Plugin.LogInfo("[mulligan] swap failed: " + e);
                Speech.Say("Swap failed.", true);
            }
        }

        // Every build while a swap is in flight: once the sent card has left and a new one arrived, say so and
        // land on the new card. If nothing arrives (an empty deck), report the swap after a few seconds.
        private void CheckSwap(List<Card> hand)
        {
            if (_before == null) return;
            bool gone = !hand.Exists(c => c.Id == _swapped);
            var fresh = hand.Find(c => !_before.Contains(c.Id));
            bool late = Time.realtimeSinceStartup - _swapAt > 3f;
            if (!(gone && fresh != null) && !(gone && late)) return;
            _before = null;
            Speech.Say("Swapped " + _swappedName + ". " + Swaps(Left(_gc)) + " left.", false);
            if (fresh != null) GraphNavigator.Suggest(CardId(fresh));
        }

        private void Finish()
        {
            var gc = _gc;
            if (gc == null) return;
            var me = gc.PlayerManager.LocalUser;
            bool can = false;
            try { can = me.CanMulligan; } catch { }
            if (!can) { Speech.Say("Waiting for the opponent.", true); return; }
            try
            {
                me.AskFinishMulligan();
                Plugin.LogInfo("[mulligan] finish");
                Speech.Say("Keeping your hand.", true);
            }
            catch (Exception e)
            {
                Plugin.LogInfo("[mulligan] finish failed: " + e);
                Speech.Say("Couldn't finish the mulligan.", true);
            }
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
