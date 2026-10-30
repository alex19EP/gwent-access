using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentGameplay;
using GwentUnity;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The battle board as one graph, laid out the way it sits on screen: opponent's leader, opponent's ranged
    /// and melee rows, your melee and ranged rows, your hand, your leader. Up/Down move between rows keeping
    /// the spot along the row (rows are centred, so the card nearest above or below); Left/Right move along a
    /// row, silent at the ends. Each card reads <see cref="CardReader.Summary"/>; Ctrl+Up/Down step through its
    /// lines, I explains its keywords (just the current line's, once Ctrl+Up/Down is on an ability), K reads its
    /// kind and base power, Space the whole card.
    ///
    /// Jumps (Hearthstone Access letters): C your hand, B your melee row, G the opponent's melee row, V your
    /// leader, F the opponent's leader; Shift+C the opponent's hand size, A the score.
    ///
    /// Browsing is private: the game's own highlight (which the opponent sees) only follows the cursor onto
    /// your hand cards, so Enter still picks one up through the game. While the game is asking for a row or a
    /// target, or a panel (mulligan, card preview, menu) has the input, the screen steps aside and the game's
    /// flow runs as before.
    /// </summary>
    internal sealed class BattleScreen : Screen
    {
        public override string Key => "battle";

        private sealed class Row
        {
            public string Key;
            public string Name;
            public bool Mine;
            public ELocation Where;
            public bool Scored;
        }

        // Top of the screen to the bottom.
        private static readonly Row[] Rows =
        {
            new Row { Key = "opp-leader", Name = "Opponent's leader", Mine = false, Where = ELocation.Leader },
            new Row { Key = "opp-ranged", Name = "Opponent's ranged row", Mine = false, Where = ELocation.Ranged, Scored = true },
            new Row { Key = "opp-melee", Name = "Opponent's melee row", Mine = false, Where = ELocation.Melee, Scored = true },
            new Row { Key = "my-melee", Name = "Your melee row", Mine = true, Where = ELocation.Melee, Scored = true },
            new Row { Key = "my-ranged", Name = "Your ranged row", Mine = true, Where = ELocation.Ranged, Scored = true },
            new Row { Key = "hand", Name = "Your hand", Mine = true, Where = ELocation.Hand },
            new Row { Key = "my-leader", Name = "Your leader", Mine = true, Where = ELocation.Leader },
        };

        // This frame's cards by node key, for the keys that act on the focused card.
        private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();
        private readonly Dictionary<string, string> _rowOf = new Dictionary<string, string>();

        // Where the cursor was in each row (a jump back lands there) and overall (the screen steps aside while
        // the game asks for a row, and comes back to the same card, wherever it went).
        private readonly Dictionary<string, ControlId> _lastInRow = new Dictionary<string, ControlId>();
        private ControlId _last;

        private GameController _gc;
        private BattleSelectionHandlerComponent _selection;

        public override bool IsActive()
        {
            _gc = Battle.Controller();
            if (_gc == null) return false;
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null || layer.Priority != CrimsonInputLayer.EPriority.Base) return false;
            if (!HasBattlePanel(layer)) return false;
            var sel = Selection();
            return sel == null || sel.SelectionMode == EBattleNavigationMode.Normal;
        }

        private static bool HasBattlePanel(CrimsonInputLayer layer)
        {
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var p = attached[i]?.TryCast<UIBattlePanel>();
                if (p != null && p.gameObject.activeInHierarchy) return true;
            }
            return false;
        }

        private BattleSelectionHandlerComponent Selection()
        {
            try
            {
                if (_selection != null && _selection.Pointer != IntPtr.Zero && _selection.gameObject != null) return _selection;
            }
            catch { }
            _selection = UnityEngine.Object.FindObjectOfType<BattleSelectionHandlerComponent>();
            return _selection;
        }

        // ---- the board ----

        public override void Build(GraphBuilder b)
        {
            var gc = _gc ?? Battle.Controller();
            if (gc == null) return;
            var pm = gc.PlayerManager;
            var me = Battle.Me(gc);
            var them = Battle.Them(gc);
            _cards.Clear();
            _rowOf.Clear();

            var ids = new List<List<ControlId>>();
            ControlId firstInHand = null;
            b.BeginStop("board");
            foreach (var row in Rows)
            {
                var cards = Battle.Cards(gc, row.Mine ? me : them, row.Where);
                b.PushContext(row.Name, row.Scored ? "score " + Score(cards) : null);
                b.StartRow();
                var rowIds = new List<ControlId>();
                if (cards.Count == 0)
                {
                    var id = ControlId.Structural("battle:empty:" + row.Key);
                    b.AddItem(id, new NodeVtable { Announcements = new[] { NodeAnnouncement.Static("empty") } });
                    rowIds.Add(id);
                    _rowOf[(string)id.StructuralKey] = row.Key;
                }
                foreach (var card in cards)
                {
                    var id = ControlId.Structural("battle:card:" + card.Id);
                    b.AddItem(id, CardNode(card, pm));
                    rowIds.Add(id);
                    _cards[(string)id.StructuralKey] = card;
                    _rowOf[(string)id.StructuralKey] = row.Key;
                    if (row.Where == ELocation.Hand && firstInHand == null) firstInHand = id;
                }
                b.EndRow();
                b.PopContext();
                ids.Add(rowIds);
            }

            // Up/Down: the card at the same place along the next row. Rows are centred with even spacing, so
            // place = offset from the row's middle.
            for (int r = 0; r < ids.Count; r++)
                for (int i = 0; i < ids[r].Count; i++)
                {
                    if (r > 0) b.Connect(ids[r][i], GraphDir.Up, ids[r - 1][Across(i, ids[r].Count, ids[r - 1].Count)]);
                    if (r < ids.Count - 1) b.Connect(ids[r][i], GraphDir.Down, ids[r + 1][Across(i, ids[r].Count, ids[r + 1].Count)]);
                }

            var start = _last != null && _rowOf.ContainsKey((string)_last.StructuralKey) ? _last : firstInHand;
            if (start != null) b.SetStart(start);
        }

        private static int Across(int i, int from, int to)
        {
            double offset = i - (from - 1) / 2.0;
            int j = (int)Math.Floor(offset + (to - 1) / 2.0 + 0.5);
            return Math.Clamp(j, 0, to - 1);
        }

        private static int Score(List<Card> cards)
        {
            int total = 0;
            foreach (var c in cards)
                try { if ((c.Template.Type & ECardType.Unit) != 0) total += c.Data.Power.CurrentPower; } catch { }
            return total;
        }

        private static NodeVtable CardNode(Card card, PlayerManager pm)
        {
            return new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(() => CardReader.Summary(card, pm), kind: AnnouncementKinds.Label) },
                OnTooltip = () => Speech.Say(CardReader.Whole(card, pm), true),
            };
        }

        // ---- the game's highlight ----

        public override AControl GameSelection() => null; // the battle cursor is the game's own; never followed

        public override bool LeavesToGame(KeyCode key) => key == KeyCode.Return || key == KeyCode.KeypadEnter;

        public override void OnCursorMoved(GraphNode node)
        {
            if (node == null) return;
            _last = node.Id;
            if (_rowOf.TryGetValue((string)node.Id.StructuralKey, out var row)) _lastInRow[row] = node.Id;
            _cardKeys?.Reset();

            // Only hand cards: the opponent sees the highlight, and a hand card needs it for Enter.
            if (!_cards.TryGetValue((string)node.Id.StructuralKey, out var card) || row != "hand") return;
            try
            {
                var sel = Selection();
                var view = ViewOf(card);
                if (sel == null || view == null) return;
                var current = sel.LocalUserSelection;
                if (current != null && current.Pointer == view.Pointer) return;
                sel.Select(view.Cast<ISelectable>(), sel.LocalUserId, true);
            }
            catch (Exception e) { Plugin.LogInfo("[battle] highlight failed: " + e.Message); }
        }

        private static CardBattleView ViewOf(Card card)
        {
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<CardBattleView>()))
            {
                var v = o.TryCast<CardBattleView>();
                try { if (v?.Card != null && v.Card.Id == card.Id) return v; } catch { }
            }
            return null;
        }

        // ---- keys ----

        private CardKeys _cardKeys;
        private List<ScreenKey> _keys;

        public override IReadOnlyList<ScreenKey> ExtraKeys
        {
            get
            {
                if (_keys != null) return _keys;
                _cardKeys = new CardKeys(FocusedCard, Players);
                _keys = new List<ScreenKey>(_cardKeys.Keys)
                {
                    new ScreenKey { Key = KeyCode.C, Run = () => JumpTo("hand"), Hint = "C, your hand" },
                    new ScreenKey { Key = KeyCode.C, Shift = true, Run = SayOpponentHand, Hint = "Shift C, opponent's hand size" },
                    new ScreenKey { Key = KeyCode.B, Run = () => JumpTo("my-melee"), Hint = "B, your melee row" },
                    new ScreenKey { Key = KeyCode.G, Run = () => JumpTo("opp-melee"), Hint = "G, opponent's melee row" },
                    new ScreenKey { Key = KeyCode.V, Run = () => JumpTo("my-leader"), Hint = "V, your leader" },
                    new ScreenKey { Key = KeyCode.F, Run = () => JumpTo("opp-leader"), Hint = "F, opponent's leader" },
                    new ScreenKey { Key = KeyCode.A, Run = SayScore, Hint = "A, score" },
                };
                return _keys;
            }
        }

        private void JumpTo(string row)
        {
            if (_lastInRow.TryGetValue(row, out var id) && GraphNavigator.FocusNode(id)) return;
            foreach (var kv in _rowOf)
                if (kv.Value == row && GraphNavigator.FocusNode(ControlId.Structural(kv.Key))) return;
        }

        private Card FocusedCard()
        {
            var node = GraphNavigator.CurrentNode;
            return node != null && _cards.TryGetValue((string)node.Id.StructuralKey, out var card) ? card : null;
        }

        private PlayerManager Players() => _gc?.PlayerManager;

        private void SayOpponentHand()
        {
            if (_gc == null) return;
            int n = Battle.HandCount(_gc, Battle.Them(_gc));
            Speech.Say("Opponent's hand, " + (n == 1 ? "1 card" : n + " cards"), true);
        }

        private void SayScore()
        {
            if (_gc != null) Speech.Say(Battle.StatusText(_gc), true);
        }
    }
}
