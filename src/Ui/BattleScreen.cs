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
    /// lines, I explains its keywords, K reads its kind and base power, Space the whole card.
    ///
    /// Playing works like a gamepad player's (Hearthstone Access in spirit): the game's own selection — the
    /// hover the opponent sees too — follows the cursor, and Enter is the game's A, so picking up, ordering,
    /// placing and targeting go through the game's flow. Enter on something that can't be used says why
    /// instead. Picking up a card turns the board into the gaps of the rows it may go to ("between A and B";
    /// the game's ghost card follows); while the game asks for targets or a row, the board stays browsable and
    /// Tab cycles the valid ones. Backspace is the game's cancel; E ends the turn or passes through the game's
    /// coin, asking first as Hearthstone Access does (always before passing, before ending the turn while an
    /// order is still ready) — E or Enter confirms, any other key drops the question.
    ///
    /// Jumps (Hearthstone Access letters): C your hand, B your melee row, G the opponent's melee row, V your
    /// leader, F the opponent's leader; Shift+C the opponent's hand size, A the score.
    /// </summary>
    internal sealed class BattleScreen : Screen
    {
        public override string Key => "battle";

        // A played card's hand slot is refilled by the next card under the same path.
        public override bool ReturnsToOpener => false;

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

        // This frame's nodes by key: the card behind it, the board row it sits in, the row's location, and for
        // a gap (while placing) the row and index it stands for.
        private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();
        private readonly Dictionary<string, string> _rowOf = new Dictionary<string, string>();
        private readonly Dictionary<string, Location> _locationOf = new Dictionary<string, Location>();
        private readonly Dictionary<string, (Location Row, int Index)> _gaps = new Dictionary<string, (Location, int)>();

        // Tab's stops this frame: usable cards, valid targets or valid rows, in order.
        private readonly List<ControlId> _cycle = new List<ControlId>();

        // Where the cursor was in each row (a jump back lands there) and overall (back from placing or a
        // targeting, the cursor returns to the same card).
        private readonly Dictionary<string, ControlId> _lastInRow = new Dictionary<string, ControlId>();
        private ControlId _last;

        private GameController _gc;
        private Plays _plays = new Plays();

        public override bool IsActive()
        {
            _gc = Battle.Controller();
            if (_gc == null) return false;
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null || layer.Priority != CrimsonInputLayer.EPriority.Base) return false;
            if (!HasBattlePanel(layer)) return false;
            return Plays.Read(_gc).Choice == null; // a choice has its own screen
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

        // ---- the board ----

        public override void Build(GraphBuilder b)
        {
            var gc = _gc ?? Battle.Controller();
            if (gc == null) return;
            _plays = Plays.Read(gc);
            _cards.Clear();
            _rowOf.Clear();
            _locationOf.Clear();
            _gaps.Clear();
            _cycle.Clear();
            DropConfirm();
            CheckPass();

            if (_plays.Placing != null && _plays.Targets == null && _plays.Rows == null) BuildGaps(b, gc);
            else BuildBoard(b, gc);

            NoticeRequest();
        }

        private void BuildBoard(GraphBuilder b, GameController gc)
        {
            var pm = gc.PlayerManager;
            var me = Battle.Me(gc);
            var them = Battle.Them(gc);

            var ids = new List<List<ControlId>>();
            ControlId firstInHand = null;
            b.BeginStop("board");
            foreach (var row in Rows)
            {
                var who = row.Mine ? me : them;
                var cards = Battle.Cards(gc, who, row.Where);
                Location loc = null;
                try { loc = gc.BoardManager.GetLocation(who, row.Where); } catch { }
                b.PushContext(row.Name, row.Scored ? "score " + Score(cards) : null);
                b.StartRow();
                var rowIds = new List<ControlId>();
                if (cards.Count == 0)
                {
                    var id = ControlId.Structural(row.Where == ELocation.Leader ? LeaderKey(row.Key) : "battle:empty:" + row.Key);
                    b.AddItem(id, new NodeVtable
                    {
                        Announcements = new[] { NodeAnnouncement.Static("empty") },
                        OnActivate = Act(() => EnterOnRow(loc)),
                    });
                    rowIds.Add(id);
                    Remember(id, null, row.Key, loc);
                }
                foreach (var card in cards)
                {
                    var id = ControlId.Structural(row.Where == ELocation.Leader ? LeaderKey(row.Key) : "battle:card:" + card.Id);
                    b.AddItem(id, CardNode(card, pm, loc));
                    rowIds.Add(id);
                    Remember(id, card, row.Key, loc);
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

            BuildCycle(gc, ids);

            var start = _last != null && _rowOf.ContainsKey((string)_last.StructuralKey) ? _last : firstInHand;
            if (start != null) b.SetStart(start);
        }

        // A leader slot keeps one key whether the leader sits in it or is away resolving its order, so a cursor
        // resting there stays put — and quiet — while the leader acts, rather than falling to the next row.
        private static string LeaderKey(string row) => "battle:leader:" + row;

        private static string CardKey(GameController gc, Card card)
        {
            try
            {
                if (card.Position.Location == ELocation.Leader)
                    return LeaderKey(card.Position.PlayerId == Battle.Me(gc) ? "my-leader" : "opp-leader");
            }
            catch { }
            return "battle:card:" + card.Id;
        }

        private void Remember(ControlId id, Card card, string row, Location loc)
        {
            var key = (string)id.StructuralKey;
            if (card != null) _cards[key] = card;
            _rowOf[key] = row;
            if (loc != null) _locationOf[key] = loc;
        }

        // Tab's stops: the valid targets, the valid rows (their first node), or the cards you can use, hand first.
        private void BuildCycle(GameController gc, List<List<ControlId>> ids)
        {
            if (_plays.Targets != null)
            {
                foreach (var kv in _cards)
                    if (_plays.IsTarget(kv.Value)) _cycle.Add(ControlId.Structural(kv.Key));
                _cycle.Sort((a, b) => Order(a, ids).CompareTo(Order(b, ids)));
                return;
            }
            if (_plays.Rows != null)
            {
                foreach (var rowIds in ids)
                    if (rowIds.Count > 0 && _locationOf.TryGetValue((string)rowIds[0].StructuralKey, out var loc) && _plays.IsTargetRow(loc))
                        _cycle.Add(rowIds[0]);
                return;
            }
            foreach (var c in _plays.Usable()) _cycle.Add(ControlId.Structural(CardKey(gc, c)));
        }

        private static int Order(ControlId id, List<List<ControlId>> ids)
        {
            int n = 0;
            foreach (var row in ids)
                foreach (var x in row)
                {
                    if (x.Equals(id)) return n;
                    n++;
                }
            return n;
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

        private NodeVtable CardNode(Card card, PlayerManager pm, Location loc)
        {
            return new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(() => CardReader.Summary(card, pm), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => _plays.IsChosen(card) ? "chosen" : null, live: true, kind: AnnouncementKinds.Selected),
                },
                OnActivate = Act(() => EnterOnCard(card, loc)),
                OnTooltip = () => Speech.Say(CardReader.Whole(card, pm), true),
            };
        }

        // ---- placing: the gaps of the rows the picked-up card may go to ----

        private void BuildGaps(GraphBuilder b, GameController gc)
        {
            var pm = gc.PlayerManager;
            var me = Battle.Me(gc);
            var them = Battle.Them(gc);
            var ids = new List<List<ControlId>>();
            ControlId start = null;
            b.BeginStop("place");
            foreach (var row in Rows)
            {
                if (row.Where != ELocation.Melee && row.Where != ELocation.Ranged) continue;
                var who = row.Mine ? me : them;
                Location loc = null;
                try { loc = gc.BoardManager.GetLocation(who, row.Where); } catch { }
                if (loc == null || !_plays.MayPlaceIn(loc)) continue;
                try { if (loc.RemainingCapacity <= 0) continue; } catch { }
                var cards = Battle.Cards(gc, who, row.Where);
                b.PushContext(row.Name, "score " + Score(cards));
                b.StartRow();
                var rowIds = new List<ControlId>();
                for (int i = 0; i <= cards.Count; i++)
                {
                    var id = ControlId.Structural("battle:gap:" + row.Key + ":" + i);
                    b.AddItem(id, new NodeVtable
                    {
                        Announcements = new[] { NodeAnnouncement.Static(GapText(cards, i, pm)) },
                        OnActivate = Act(() => { }), // Enter is the game's A here; only a pending question reaches this
                    });
                    _gaps[(string)id.StructuralKey] = (loc, i);
                    rowIds.Add(id);
                }
                if (row.Key == "my-melee" || start == null) start = rowIds[rowIds.Count - 1];
                b.EndRow();
                b.PopContext();
                ids.Add(rowIds);
            }
            for (int r = 0; r < ids.Count; r++)
                for (int i = 0; i < ids[r].Count; i++)
                {
                    if (r > 0) b.Connect(ids[r][i], GraphDir.Up, ids[r - 1][Across(i, ids[r].Count, ids[r - 1].Count)]);
                    if (r < ids.Count - 1) b.Connect(ids[r][i], GraphDir.Down, ids[r + 1][Across(i, ids[r].Count, ids[r + 1].Count)]);
                }
            if (start != null) b.SetStart(start);
        }

        private static string GapText(List<Card> cards, int i, PlayerManager pm)
        {
            if (cards.Count == 0) return "empty row";
            if (i == 0) return "before " + CardReader.Name(cards[0], pm);
            if (i == cards.Count) return "after " + CardReader.Name(cards[cards.Count - 1], pm);
            return "between " + CardReader.Name(cards[i - 1], pm) + " and " + CardReader.Name(cards[i], pm);
        }

        // ---- the request being answered: say what the game asks, land on a sensible spot ----

        private int _noticed = -1;

        private void NoticeRequest()
        {
            var req = _plays.Pending;
            int id = -1;
            try { id = req?.Id ?? -1; } catch { }
            if (id == _noticed) return;
            _noticed = id;
            _mirrorNow = true;
            if (req == null) return;

            var requester = _plays.Requester();
            string name = requester != null ? CardReader.Name(requester, _gc.PlayerManager) : "the card";
            if (_plays.Targets != null)
            {
                int max = _plays.MaxTargets;
                Speech.Say("Choose " + (max > 1 ? "up to " + max + " targets" : "a target") + " for " + name + ".", true);
                LandOnCycle();
            }
            else if (_plays.Rows != null)
            {
                Speech.Say("Choose a row for " + name + ".", true);
                LandOnCycle();
            }
            else if (_plays.Placing != null)
                Speech.Say("Place " + name + ".", true);
        }

        // Onto the first valid option, unless the cursor already sits on one.
        private void LandOnCycle()
        {
            if (_cycle.Count == 0) return;
            var cur = GraphNavigator.CurrentNode?.Id;
            if (cur != null && _cycle.Exists(x => x.Equals(cur))) return;
            GraphNavigator.Suggest(_cycle[0]);
        }

        // ---- the game's hover follows the cursor ----

        public override AControl GameSelection() => null; // the game's battle selection is driven from here, never followed

        private bool _mirrorNow;

        public override void OnCursorMoved(GraphNode node)
        {
            if (node == null) return;
            var key = (string)node.Id.StructuralKey;
            if (_rowOf.TryGetValue(key, out var row))
            {
                _last = node.Id;
                _lastInRow[row] = node.Id;
            }
            _cardKeys?.Reset();
            Mirror(key);
        }

        // Settle calls OnCursorMoved only when the cursor changes; a new request over the same card (an order
        // asking for targets) needs the game's selection put back where the cursor is.
        public override void OnFocus() => _mirrorNow = true;

        private void Mirror(string key)
        {
            _mirrorNow = false;
            if (_gaps.TryGetValue(key, out var gap)) { BattleInput.Ghost(gap.Row, gap.Index); return; }
            if (_plays.Rows != null)
            {
                if (_locationOf.TryGetValue(key, out var loc) && _plays.IsTargetRow(loc)) BattleInput.Hover(loc);
                return;
            }
            if (!_cards.TryGetValue(key, out var card)) return;
            if (_plays.Targets != null && !_plays.IsTarget(card)) return; // the game only selects valid targets
            BattleInput.Hover(card);
        }

        // ---- Enter ----

        /// <summary>Enter goes to the game (its A, on the selection that follows the cursor) whenever it would do
        /// something there; otherwise the node's handler says why not. A pending question takes Enter too.</summary>
        public override bool LeavesToGame(KeyCode key)
        {
            if (key != KeyCode.Return && key != KeyCode.KeypadEnter) return false;
            if (_confirm != null) return false;
            var node = GraphNavigator.CurrentNode;
            if (node == null) return false;
            var k = (string)node.Id.StructuralKey;
            if (_mirrorNow) Mirror(k);
            if (_gaps.ContainsKey(k)) return true;
            _cards.TryGetValue(k, out var card);
            _locationOf.TryGetValue(k, out var loc);
            if (_plays.Targets != null) return card != null && _plays.IsTarget(card);
            if (_plays.Rows != null) return loc != null && _plays.IsTargetRow(loc);
            return card != null && _plays.MyTurn && _plays.Pending == null && Mine(card) && _plays.CanUse(card);
        }

        private bool Mine(Card c)
        {
            try { return c.Position.PlayerId == Battle.Me(_gc); } catch { return false; }
        }

        // What a claimed Enter says (the game's A only gets the ones that do something).
        private void EnterOnCard(Card card, Location loc)
        {
            if (_plays.Targets != null) { Speech.Say("Not a valid target.", true); return; }
            if (_plays.Rows != null) { Speech.Say("Not a valid row.", true); return; }
            if (!Mine(card)) { Speech.Say(_plays.MyTurn ? "Nothing to use." : "Not your turn.", true); return; }
            Speech.Say(_plays.WhyNot(card) ?? "Nothing to use.", true);
        }

        private void EnterOnRow(Location loc)
        {
            if (_plays.Rows != null) Speech.Say("Not a valid row.", true);
        }

        // ---- E: the coin, asking first as Hearthstone Access does ----

        private Action _confirm;
        private int _confirmFrame;

        private Action Act(Action a) => () => { if (!TakeConfirm()) a(); };

        private bool TakeConfirm()
        {
            if (_confirm == null) return false;
            var run = _confirm;
            _confirm = null;
            run();
            return true;
        }

        private void Ask(string question, Action run)
        {
            _confirm = run;
            _confirmFrame = Time.frameCount;
            Speech.Say(question, true);
        }

        // Any key but E or Enter drops the question.
        private void DropConfirm()
        {
            if (_confirm == null || Time.frameCount == _confirmFrame || !Keys.AnyDown()) return;
            if (Keys.Down(KeyCode.E) || Keys.Down(KeyCode.Return) || Keys.Down(KeyCode.KeypadEnter)) return;
            _confirm = null;
        }

        private void PressE()
        {
            if (TakeConfirm()) return;
            if (_plays.Targets != null || _plays.Rows != null)
            {
                if (_plays.MaxTargets <= 1) { Speech.Say("Choose with Enter, or Backspace to cancel.", true); return; }
                if (_plays.Chosen < _plays.MinTargets) { Speech.Say("Choose at least " + _plays.MinTargets + ".", true); return; }
                if (!BattleInput.FinishTargets()) Speech.Say("Can't finish yet.", true);
                return;
            }
            if (_plays.Placing != null) { Speech.Say("Place the card first, or Backspace to put it back.", true); return; }
            if (!_plays.MyTurn) { Speech.Say("Not your turn.", true); return; }
            switch (BattleInput.CoinMode)
            {
                case UIBoardPassWidget.EButtonMode.Pass:
                    Ask(PassQuestion(), () => { if (Coin("Passing.")) _passAskedAt = Time.realtimeSinceStartup; });
                    return;
                case UIBoardPassWidget.EButtonMode.EndTurn:
                    if (_plays.OrdersReady()) Ask("You still have orders ready. Are you sure?", () => Coin("Ending turn."));
                    else Coin("Ending turn.");
                    return;
                default:
                    Speech.Say("Can't end the turn now.", true);
                    return;
            }
        }

        private string PassQuestion()
        {
            string score = "";
            try
            {
                var me = _gc.PlayerManager.LocalUser;
                var opp = me.OpponentPlayer;
                int a = me.CurrentScore, b = opp.CurrentScore;
                string lead = a > b ? "You're ahead" : a < b ? "You're behind" : "You're tied";
                score = " " + lead + ", " + a + " to " + b + (opp.HasPassed ? ", opponent has passed." : ", opponent hasn't passed.");
            }
            catch { }
            return "Pass the round?" + score;
        }

        private static bool Coin(string said)
        {
            if (BattleInput.PressCoin()) { Speech.Say(said, true); return true; }
            Speech.Say("Couldn't press the button.", true);
            return false;
        }

        // A pass the button accepted but the game never made (seen once, cause unknown): said, not left silent.
        private float _passAskedAt = -1;

        private void CheckPass()
        {
            if (_passAskedAt < 0 || Time.realtimeSinceStartup - _passAskedAt < 2f) return;
            bool passed = NarratePass.MineAt >= _passAskedAt;
            _passAskedAt = -1;
            if (passed) return;
            Plugin.LogInfo("[input] pass pressed but not made; coin now " + BattleInput.CoinMode);
            Speech.Say("The pass didn't go through. Press E again.", false);
        }

        // ---- Backspace: the game's cancel ----

        private void Cancel()
        {
            var req = _plays.Pending;
            if (req == null && BattleInput.Mode == EBattleNavigationMode.Normal) return;
            bool can = true;
            try { if (req != null) can = req.CanCancel; } catch { }
            if (!can) { Speech.Say("This can't be cancelled.", true); return; }
            if (BattleInput.Back()) Speech.Say("Cancelled.", true);
        }

        // ---- Tab: the next valid play, target or row ----

        private void Cycle(int dir)
        {
            if (_cycle.Count == 0)
            {
                Speech.Say(_plays.Targets != null ? "No valid targets." : _plays.Rows != null ? "No valid rows."
                    : _plays.Placing != null ? "Choose a spot with the arrows." : _plays.MyTurn ? "No valid plays." : "Not your turn.", true);
                return;
            }
            var cur = GraphNavigator.CurrentNode?.Id;
            int at = cur == null ? -1 : _cycle.FindIndex(x => x.Equals(cur));
            int next = at < 0 ? (dir > 0 ? 0 : _cycle.Count - 1) : (at + dir + _cycle.Count) % _cycle.Count;
            GraphNavigator.FocusNode(_cycle[next]);
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
                    new ScreenKey { Key = KeyCode.Tab, Run = () => Cycle(1), Hint = "Tab, next valid play or target" },
                    new ScreenKey { Key = KeyCode.Tab, Shift = true, Run = () => Cycle(-1), Hint = "Shift Tab, previous valid play or target" },
                    new ScreenKey { Key = KeyCode.Backspace, Run = Cancel, Hint = "Backspace, cancel" },
                    new ScreenKey { Key = KeyCode.E, Run = PressE, Hint = "E, end turn or pass" },
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
