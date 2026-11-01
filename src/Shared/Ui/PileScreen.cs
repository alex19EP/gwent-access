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
    /// The battle's pile views: your deck (the cards still in it), your graveyard and the opponent's graveyard,
    /// which a sighted player opens with the Preview input (right click) on the deck or a graveyard. Nobody can
    /// open the opponent's deck. <see cref="BattleScreen"/>'s D, Y and Shift+Y open them through the game's own
    /// <c>APlayerHandlerComponent.OpenDeck</c> / <c>OpenGraveyard</c>, so the game's panel, sound and the
    /// tutorial's input locks all apply. This screen reads the panel the game then shows: its cards as one row,
    /// read like a choice (Ctrl+Up/Down, I, K, Space). Backspace closes it through the game's own close; Escape
    /// stays the game's Back, which closes it too.
    /// </summary>
    internal sealed class PileScreen : Screen
    {
        public override string Key => "pile";
        public override int Layer => 1;
        public override bool ReturnsToOpener => false;

        private enum Pile { None, Deck, Graveyard }

        private Pile _open;
        private GameController _gc;

        public override bool IsActive()
        {
            _open = OpenPile();
            if (_open == Pile.None) return false;
            _askedAt = -1;
            _gc = Battle.Controller();
            return _gc != null;
        }

        private static Pile OpenPile()
        {
            try
            {
                if (Shown(APlayerHandlerComponent.m_DeckPanel)) return Pile.Deck;
                if (Shown(APlayerHandlerComponent.m_GraveyardPanel)) return Pile.Graveyard;
            }
            catch { }
            return Pile.None;
        }

        private static bool Shown(UIBattleChoicePanel p)
        {
            if (p == null || !p.gameObject.activeInHierarchy) return false;
            var v = p.Visibility;
            return v == CrimsonUIBehaviour.EVisibility.Shown || v == CrimsonUIBehaviour.EVisibility.Showing;
        }

        private static UIBattleChoicePanel Panel(Pile pile) =>
            pile == Pile.Deck ? APlayerHandlerComponent.m_DeckPanel : APlayerHandlerComponent.m_GraveyardPanel;

        /// <summary>The panel's own title ("Your graveyard", "Opponent's graveyard", "Deck"), with the count.</summary>
        public override string ScreenName
        {
            get
            {
                string title = null;
                try { title = UiText.Clean(Panel(_open)?.m_Title?.Text?.text); } catch { }
                if (string.IsNullOrEmpty(title)) title = _open == Pile.Deck ? "Your deck" : "Graveyard";
                int n = Cards().Count;
                return title + ", " + (n == 0 ? "empty" : n == 1 ? "1 card" : n + " cards");
            }
        }

        // The panel's cards, in the order the game lays them out.
        private List<Card> Cards()
        {
            var list = new List<Card>();
            try
            {
                var src = _open == Pile.Deck ? APlayerHandlerComponent.m_DeckCards : APlayerHandlerComponent.m_GraveyardCards;
                if (src != null) for (int i = 0; i < src.Count; i++) if (src[i] != null) list.Add(src[i]);
            }
            catch (Exception e) { Plugin.LogInfo("[pile] read failed: " + e.Message); }
            return list;
        }

        private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();

        public override void Build(GraphBuilder b)
        {
            if (_open == Pile.None || _gc == null) return;
            // The panel shows its cards face up (your own deck, a public graveyard): read them as shown, without
            // the match's visibility check, as the choice panel does.
            PlayerManager pm = null;
            _cards.Clear();
            var cards = Cards();
            b.BeginStop("pile");
            if (cards.Count == 0)
            {
                b.AddLabel(ControlId.Structural("pile:empty"), () => "Empty");
                return;
            }
            b.StartRow();
            foreach (var card in cards)
            {
                var id = ControlId.Structural("pile:card:" + card.Id);
                _cards[(string)id.StructuralKey] = card;
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => CardReader.WithAbilities(card, pm), kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(CardReader.Whole(card, pm), true),
                });
            }
            b.EndRow();
        }

        /// <summary>Open a pile the way the game's Preview input does on the board. Says why when the game
        /// refuses (the tutorial has locked it, or it's not available now).</summary>
        public static void Open(bool deck, bool mine)
        {
            var gc = Battle.Controller();
            if (gc == null) return;
            try
            {
                var who = mine ? Battle.Me(gc) : Battle.Them(gc);
                var h = TurnHandler();
                if (h == null) { Speech.Say("Can't open that now.", true); return; }
                if (deck) h.OpenDeck(who, false);
                else h.OpenGraveyard(who, false);
                _askedAt = Time.realtimeSinceStartup;
                Plugin.LogInfo("[pile] open " + (deck ? "deck" : "graveyard") + " of " + who + " via " + h.GetIl2CppType().Name);
            }
            catch (Exception e) { Plugin.LogInfo("[pile] open failed: " + e); Speech.Say("Couldn't open it.", true); }
        }

        // When D or Y last asked the game to open a pile; the game refuses silently (the tutorial's input locks,
        // a play in progress), so if no panel shows soon after, the battle screen says so.
        private static float _askedAt = -1;

        /// <summary>From the battle screen's Tick: a pile asked for that never opened is "Not available right
        /// now". (The panel loads asynchronously the first time, hence the grace period.)</summary>
        public static void CheckRefused()
        {
            if (_askedAt < 0) return;
            if (OpenPile() != Pile.None) { _askedAt = -1; return; }
            if (Time.realtimeSinceStartup - _askedAt < 1.5f) return;
            _askedAt = -1;
            Speech.Say("Not available right now.", true);
        }

        // The player handler that has the input now: your turn's or the opponent's turn's (both can open piles).
        private static APlayerHandlerComponent TurnHandler()
        {
            var bh = BattleInput.Handler();
            if (bh == null) return null;
            APlayerHandlerComponent local = bh.m_LocalPlayerTurnHandlerComponent, opp = bh.m_OpponentPlayerTurnHandlerComponent;
            try { if (local != null && local.IsEnabled) return local; } catch { }
            try { if (opp != null && opp.IsEnabled) return opp; } catch { }
            return local ?? opp;
        }

        private void Close()
        {
            try
            {
                var h = TurnHandler();
                if (h == null) return;
                if (_open == Pile.Deck) h.HandleCloseDeckPanel();
                else h.HandleCloseGraveyardPanel();
            }
            catch (Exception e) { Plugin.LogInfo("[pile] close failed: " + e.Message); }
        }

        // ---- keys ----

        public override AControl GameSelection() => null;
        public override void OnCursorMoved(GraphNode node) => _cardKeys?.Reset();

        private CardKeys _cardKeys;
        private List<ScreenKey> _keys;

        public override IReadOnlyList<ScreenKey> ExtraKeys
        {
            get
            {
                if (_keys != null) return _keys;
                _cardKeys = new CardKeys(FocusedCard, () => null); // face up, as in Build
                _keys = new List<ScreenKey>(_cardKeys.Keys)
                {
                    new ScreenKey { Key = KeyCode.Backspace, Run = Close, Hint = "Backspace, close" },
                    new ScreenKey { Key = KeyCode.A, Run = () => { if (_gc != null) Speech.Say(Battle.StatusText(_gc), true); }, Hint = "A, score" },
                };
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
