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
    /// and melee rows, your melee and ranged rows, your hand, your leader. Zones as in Hearthstone Access and
    /// Accessible Arena: Left/Right move along the row you are in (silent at the ends), the zone keys below and
    /// Tab change the row, and Up/Down read the focused card line by line (on a spot with no card they say
    /// where the cursor is). Each card, yours and the opponent's alike, says only its short line
    /// (<see cref="CardReader.Summary"/>, Hearthstone Access's); Down then reads its abilities line by line, I
    /// explains its keywords, K reads its kind and base power, Space the whole card.
    ///
    /// Playing works like a gamepad player's (Hearthstone Access in spirit): the game's own selection — the
    /// hover the opponent sees too — follows the cursor, and Enter is the game's A, so picking up, ordering,
    /// placing and targeting go through the game's flow. Enter on something that can't be used says why
    /// instead. Picking up a card turns the board into the gaps of the rows it may go to ("between A and B";
    /// the game's ghost card follows) until the game lets go of it (a special put down and waiting for Enter
    /// to confirm it still keeps them); while the game asks for targets or a row, the board stays browsable and
    /// Tab cycles the valid ones. Backspace is the game's cancel; E ends the turn or passes by holding the game's
    /// coin for as long as E is held — the game's own hold, so letting go early cancels — saying the score (or
    /// that orders are still ready) as the hold starts.
    ///
    /// Jumps (Hearthstone Access letters): C your hand, B / Shift+B your melee / ranged row, G / Shift+G the
    /// opponent's melee / ranged row, V your
    /// leader, F the opponent's leader; Shift+C the opponent's hand size, A the score. The number row jumps to
    /// the board rows as the first version of the mod numbered them (1 your melee, 2 your ranged, 3 the
    /// opponent's melee, 4 the opponent's ranged; while placing, to that row's spots), and Shift+number reads
    /// the whole row without moving. M opens your emotes (Gwent: <c>EmoteScreen</c>).
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
            if (BeforeTheDeal(_gc)) return false;
            return Plays.Read(_gc).Choice == null; // a choice has its own screen
        }

        // The board shows before the opening hand is dealt; with nothing in hand the cursor would land on the
        // opponent's leader, just before the mulligan names both leaders. Wait for the deal.
        private static bool BeforeTheDeal(GameController gc)
        {
            try { return Narrator.TurnsStartedIn != gc.Pointer && Battle.HandCount(gc, Battle.Me(gc)) == 0; }
            catch { return false; }
        }

        private static bool HasBattlePanel(CrimsonInputLayer layer) => CrimsonNodes.ActivePanel<UIBattlePanel>(layer) != null;

        // ---- the board ----

        public override void Build(GraphBuilder b)
        {
            var gc = _gc ?? Battle.Controller();
            if (gc == null) return;
            _plays = Plays.Read(gc);
            if (AwaitingChoiceFollowUp()) return;
            _cards.Clear();
            _rowOf.Clear();
            _locationOf.Clear();
            _gaps.Clear();
            _cycle.Clear();

            // The gaps stay up for as long as the game holds the card: while it is placed, and while a special put
            // down waits for Enter to confirm it. That second step is the same request one state on (its result
            // Invalid → PendingPlace, Plays.Placing → Plays.Confirming); building the board for it made the
            // cursor's gap vanish, and the navigator fell back to the board's start, the first card in hand,
            // announced while the game was still waiting on the special.
            var held = _plays.Held;
            if (held != null && _plays.Targets == null && _plays.Rows == null)
            {
                // Back on the board once the card is down, the cursor lands on it, as on a card played from the
                // hand (which keeps its id): also for one the game made (a choice's "create and play"), which was
                // never in the hand, and would otherwise land on the first card in hand.
                try { _last = ControlId.Structural("battle:card:" + held.CardIdToPlay); } catch { }
                BuildGaps(b, gc);
            }
            else BuildBoard(b, gc);

            NoticeRequest();
        }

        // A choice just answered ("create and play", "choose a row"): the battle screen is back a frame or two
        // before the game opens the request for the chosen card, and its landing (the first card in hand) was
        // announced just before "Place X." / "Choose a row for X.". Until that request opens, or for
        // ChoiceFollowUpWait at most (a choice whose card resolves on its own asks nothing more), the screen
        // declares nothing ("no content yet", see Screen.Build): the navigator keeps the cursor and says nothing,
        // and the first render after it lands and speaks as before.
        private const float ChoiceFollowUpWait = 1f;

        private bool AwaitingChoiceFollowUp() =>
            _plays.Pending == null && Time.realtimeSinceStartup - ChoiceScreen.AnsweredAt < ChoiceFollowUpWait;

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
                // A leader row also says whose it is, as the player panel beside it shows: name and title.
                b.PushContext(row.Name, row.Scored ? RowDetail(cards, loc) : row.Where == ELocation.Leader ? PlayerTag(row.Mine) : null);
                b.StartRow();
                var rowIds = new List<ControlId>();
                if (cards.Count == 0)
                {
                    var id = ControlId.Structural(row.Where == ELocation.Leader ? LeaderKey(row.Key) : "battle:empty:" + row.Key);
                    b.AddItem(id, new NodeVtable
                    {
                        Announcements = new[] { NodeAnnouncement.Static("empty") },
                        OnActivate = () => EnterOnRow(loc),
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

        /// <summary>What a board row shows beside its cards: "score 12, Frost, 2 turns left, full". The score,
        /// the effect lying on the row (a hazard or a boon: the row's graphic, with the turns left on its timer
        /// icon) and "full" once the row holds as many units as it can (9; the game takes no more).</summary>
        private static string RowDetail(List<Card> cards, Location loc)
        {
            var parts = new List<string> { "score " + Score(cards) };
            string effects = RowEffects(loc);
            if (effects != null) parts.Add(effects);
            try { if (loc != null && loc.IsFull()) parts.Add("full"); } catch { }
            return string.Join(", ", parts);
        }

        /// <summary>The row's effects in the game's words, with the turns left when the effect's icon counts
        /// them: "Frost, 2 turns left". Null when the row has none.</summary>
        internal static string RowEffects(Location loc)
        {
            if (loc == null) return null;
            ELocationToken status = 0;
            try { status = loc.Status.Value; } catch { }
            if (status == 0) return null;
            var names = new List<string>();
            foreach (ELocationToken one in Enum.GetValues(typeof(ELocationToken)))
                if (one != 0 && (status & one) == one) names.Add(Narrator.RowEffectName(one));
            if (names.Count == 0) return null;
            int turns = -1;
            try { int t = loc.AbilityData.Timer.Value; if (t > 0 && t != AEntityTimer.NO_TIMER) turns = t; } catch { }
            string text = string.Join(", ", names);
            return turns > 0 ? text + ", " + (turns == 1 ? "1 turn left" : turns + " turns left") : text;
        }

        /// <summary>A card's line on the board. A leader card is led by the character standing beside it
        /// ("Эмгыр вар Эмрейс, Двойная игра, leader, Нильфгаард, …"), as a sighted player sees both.</summary>
        private static string Label(Card card, PlayerManager pm)
        {
            string summary = CardReader.Summary(card, pm);
            try
            {
                if (card.Position.Location != ELocation.Leader) return summary;
                string who = Battle.LeaderCharacter(card.Position.PlayerId);
                return who != null ? who + ", " + summary : summary;
            }
            catch { return summary; }
        }

        private NodeVtable CardNode(Card card, PlayerManager pm, Location loc)
        {
            return new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Label(card, pm), kind: AnnouncementKinds.Label),
                    // While the game asks for targets the valid ones glow and the rest dim: the glow is said on
                    // landing, the dimmed say nothing. Not live: the request's own "Choose a target" covers
                    // the moment it opens under the cursor.
                    new NodeAnnouncement(() => _plays.Targets != null && !_plays.IsChosen(card) && _plays.IsTarget(card) ? "valid target" : null),
                    new NodeAnnouncement(() => _plays.IsChosen(card) ? "chosen" : null, live: true, kind: AnnouncementKinds.Selected),
                },
                OnActivate = () => EnterOnCard(card, loc),
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
            // Nothing known to be valid (seen online, where the client's request can come without its spots):
            // offer your own rows rather than an empty board — the game's A refuses a wrong spot itself.
            bool anyValid = false;
            foreach (var row in Rows)
            {
                if (row.Where != ELocation.Melee && row.Where != ELocation.Ranged) continue;
                try { if (_plays.MayPlaceIn(gc.BoardManager.GetLocation(row.Mine ? me : them, row.Where))) anyValid = true; } catch { }
            }
            if (!anyValid && !_loggedNoSpots)
            {
                _loggedNoSpots = true;
                Plugin.LogInfo("[plays] placing with no known spots; offering your rows");
            }
            foreach (var row in Rows)
            {
                if (row.Where != ELocation.Melee && row.Where != ELocation.Ranged) continue;
                var who = row.Mine ? me : them;
                Location loc = null;
                try { loc = gc.BoardManager.GetLocation(who, row.Where); } catch { }
                if (loc == null) continue;
                if (anyValid ? !_plays.MayPlaceIn(loc) : !row.Mine) continue;
                try { if (loc.RemainingCapacity <= 0) continue; } catch { }
                var cards = Battle.Cards(gc, who, row.Where);
                b.PushContext(row.Name, RowDetail(cards, loc));
                b.StartRow();
                var rowIds = new List<ControlId>();
                for (int i = 0; i <= cards.Count; i++)
                {
                    var id = ControlId.Structural("battle:gap:" + row.Key + ":" + i);
                    b.AddItem(id, new NodeVtable
                    {
                        Announcements = new[] { NodeAnnouncement.Static(GapText(cards, i, pm)) },
                        OnActivate = () => { }, // Enter is the game's A here
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

        private bool _loggedNoSpots;

        private static string GapText(List<Card> cards, int i, PlayerManager pm)
        {
            if (cards.Count == 0) return "empty row";
            if (i == 0) return "before " + CardReader.Name(cards[0], pm);
            if (i == cards.Count) return "after " + CardReader.Name(cards[cards.Count - 1], pm);
            return "between " + CardReader.Name(cards[i - 1], pm) + " and " + CardReader.Name(cards[i], pm);
        }

        // ---- the request being answered: say what the game asks, land on a sensible spot ----

        private int _noticed = -1;

        // The noticed request's confirmation step was said: a special put down goes from placing to waiting for
        // Enter under the same request id, so the id alone never saw it. Said once per request.
        private bool _noticedConfirm;

        private void NoticeRequest()
        {
            var req = _plays.Pending;
            int id = -1;
            try { id = req?.Id ?? -1; } catch { }
            if (id == _noticed)
            {
                if (req != null && !_noticedConfirm && ReferenceEquals(req, _plays.Confirming)) SayConfirm();
                return;
            }
            _noticed = id;
            _noticedConfirm = false;
            _mirrorNow = true;
            if (req == null) return;

            string name = RequesterName();
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
            else if (_plays.Confirming != null)
                SayConfirm();
        }

        private string RequesterName()
        {
            var requester = _plays.Requester();
            return requester != null ? CardReader.Name(requester, _gc.PlayerManager) : "the card";
        }

        private void SayConfirm()
        {
            _noticedConfirm = true;
            Speech.Say("Play " + RequesterName() + "? Enter plays it, Backspace puts it back.", true);
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
            // A special waiting for its confirmation is already put down: Enter confirms it wherever the cursor
            // is, and the ghost card is not moved on.
            if (_gaps.TryGetValue(key, out var gap)) { if (_plays.Placing != null) BattleInput.Ghost(gap.Row, gap.Index); return; }
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
        /// something there; otherwise the node's handler says why not.</summary>
        public override bool LeavesToGame(KeyCode key)
        {
            if (key != KeyCode.Return && key != KeyCode.KeypadEnter) return false;
            // A held special: the game's A confirms it wherever the cursor is (it has no spot to choose).
            if (_plays.Confirming != null && _plays.Targets == null && _plays.Rows == null && _plays.Choice == null) return true;
            var node = GraphNavigator.CurrentNode;
            if (node == null) return false;
            var k = (string)node.Id.StructuralKey;
            if (_mirrorNow) Mirror(k);
            if (_gaps.ContainsKey(k)) return true;
            _cards.TryGetValue(k, out var card);
            _locationOf.TryGetValue(k, out var loc);
            // A play the mod started the mouse's way picks its targets and rows the mouse's way (EnterOnCard /
            // EnterOnRow), not through the game's A.
            // (The order's request can arrive a few frames after the tap: the flag outlives that gap.)
            if (BattleInput.MouseFlow && _plays.Targets == null && _plays.Rows == null && _plays.Pending == null
                && Time.realtimeSinceStartup - BattleInput.MouseFlowAt > 2f) BattleInput.MouseFlow = false;
            if (BattleInput.MouseFlow && (_plays.Targets != null || _plays.Rows != null)) return false;
            if (_plays.Targets != null) return card != null && _plays.IsTarget(card);
            if (_plays.Rows != null) return loc != null && _plays.IsTargetRow(loc);
            if (card != null && KeyboardOrder(card)) return false; // the mod taps it (EnterOnCard)
            return card != null && _plays.MyTurn && _plays.Pending == null && Mine(card) && _plays.CanUse(card);
        }

        // An order (the leader's ability, a unit's Order) ready on your turn, in the game's mouse-and-keyboard
        // mode: Enter uses it through the turn handler (BattleInput.Tap), which reports a refusal so it can be
        // said, where the game's A stays silent. In controller mode the game's A is the controller's own way.
        private bool KeyboardOrder(Card card)
        {
            if (ControllerMode.Force || !_plays.MyTurn || _plays.Pending != null || !Mine(card)) return false;
            bool inHand;
            try { inHand = card.Position.Location == ELocation.Hand; } catch { return false; }
            return !inHand && _plays.CanOrder(card);
        }

        // ---- Enter left to the game, and ignored ----

        // The game ignores its A silently when the tutorial has locked input, or the play isn't possible after
        // all. An Enter handed to the game that changes nothing it can be seen to change (the play mode, the
        // open request, the hand and board) within the grace period is "Not available right now".
        private string _enterBefore;
        private float _enterAt = -1;

        private void WatchEnter()
        {
            float now = Time.realtimeSinceStartup;
            if ((Keys.Down(KeyCode.Return) || Keys.Down(KeyCode.KeypadEnter)) && !Keys.Ctrl && !Keys.Alt)
            {
                if (LeavesToGame(KeyCode.Return)) { _enterBefore = PlayState(); _enterAt = now; }
                return;
            }
            if (_enterAt < 0 || now - _enterAt < 1.5f) return;
            _enterAt = -1;
            if (PlayState() == _enterBefore) Speech.Say("Not available right now.", true);
        }


        private string PlayState()
        {
            var gc = _gc;
            if (gc == null) return "";
            string P(object o) => o == null ? "-" : "+";
            int hand = 0, board = 0;
            try
            {
                var me = Battle.Me(gc);
                hand = Battle.Cards(gc, me, ELocation.Hand).Count;
                foreach (var l in new[] { ELocation.Melee, ELocation.Ranged }) board += Battle.Cards(gc, me, l).Count;
            }
            catch { }
            // Chosen: a multi-step target (Geralt's Aard) marks its first pick "chosen" without leaving the
            // targeting, and that counts as something happening.
            int chosen = 0;
            try { chosen = _plays.Chosen; } catch { }
            return BattleInput.Mode + "|" + P(_plays.Pending) + P(_plays.Placing) + P(_plays.Confirming) + P(_plays.Targets) + P(_plays.Rows) + P(_plays.Choice) + "|" + chosen + "|" + hand + "|" + board;
        }

        private bool Mine(Card c)
        {
            try { return c.Position.PlayerId == Battle.Me(_gc); } catch { return false; }
        }

        // What a claimed Enter says (the game's A only gets the ones that do something).
        private void EnterOnCard(Card card, Location loc)
        {
            if (_plays.Targets != null)
            {
                if (BattleInput.MouseFlow && _plays.IsTarget(card)) { if (!BattleInput.PickTarget(card)) Speech.Say("Not available right now.", true); return; }
                Speech.Say("Not a valid target.", true); return;
            }
            if (_plays.Rows != null)
            {
                if (BattleInput.MouseFlow && loc != null && _plays.IsTargetRow(loc)) { if (!BattleInput.PickRow(loc)) Speech.Say("Not available right now.", true); return; }
                Speech.Say("Not a valid row.", true); return;
            }
            if (!Mine(card)) { Speech.Say(_plays.MyTurn ? "Nothing to use." : "Not your turn.", true); return; }
            if (KeyboardOrder(card)) { if (!BattleInput.Tap(card)) Speech.Say("Not available right now.", true); return; }
            Speech.Say(_plays.WhyNot(card) ?? "Nothing to use.", true);
        }

        private void EnterOnRow(Location loc)
        {
            if (_plays.Rows != null && BattleInput.MouseFlow && _plays.IsTargetRow(loc)) { if (!BattleInput.PickRow(loc)) Speech.Say("Not available right now.", true); return; }
            if (_plays.Rows != null) Speech.Say("Not a valid row.", true);
        }

        // ---- E: the coin, held as the game holds it ----

        // The coin is pressed while E is down and let go with it, as for a mouse player. Its settings (read live)
        // make passing a half-second hold that a click can't do — the game's own guard against a pass by
        // accident, in place of a question — and ending the turn a plain click, which the mod guards the same
        // way. A cancelled hold is silent, as the coin emptying is for a sighted player.
        private bool _holdingCoin;

        // E held to end the turn: when the hold started (-1 when not holding), and how long it takes — the game's
        // own pass hold.
        private float _endTurnSince = -1;
        private const float EndTurnHold = 0.5f;

        private void PressE()
        {
            if (_plays.Targets != null || _plays.Rows != null)
            {
                if (_plays.MaxTargets <= 1) { Speech.Say("Choose with Enter, or Backspace to cancel.", true); return; }
                if (_plays.Chosen < _plays.MinTargets) { Speech.Say("Choose at least " + _plays.MinTargets + ".", true); return; }
                if (!BattleInput.FinishTargets()) Speech.Say("Can't finish yet.", true);
                return;
            }
            if (_plays.Placing != null) { Speech.Say("Place the card first, or Backspace to put it back.", true); return; }
            if (_plays.Confirming != null) { Speech.Say("Enter plays the card, Backspace puts it back.", true); return; }
            if (!_plays.MyTurn) { Speech.Say("Not your turn.", true); return; }
            var mode = BattleInput.CoinMode;
            string said;
            switch (mode)
            {
                case UIBoardPassWidget.EButtonMode.Pass:
                    said = "Hold to pass." + ScoreLine();
                    break;
                case UIBoardPassWidget.EButtonMode.EndTurn:
                    // The game's end-turn hold has no duration (the coin is a plain click); a blind player can't
                    // see the coin they're about to press, so the mod holds it as long as the pass: the click is
                    // only sent once E has been held that long.
                    _endTurnSince = Time.realtimeSinceStartup;
                    Speech.Say("Hold to end the turn." + (_plays.OrdersReady() ? " You still have orders ready." : ""), true);
                    return;
                default:
                    Speech.Say("Can't end the turn now.", true);
                    return;
            }
            ReleaseCoin();
            if (!BattleInput.PressCoin()) { Speech.Say("Couldn't press the button.", true); return; }
            _holdingCoin = true;
            Speech.Say(said, true);
        }

        /// <summary>" You're ahead, 12 to 9, opponent hasn't passed." — what a sighted player reads off the
        /// board before letting go of the coin.</summary>
        private string ScoreLine()
        {
            try
            {
                var me = _gc.PlayerManager.LocalUser;
                var opp = me.OpponentPlayer;
                int a = me.CurrentScore, b = opp.CurrentScore;
                string lead = a > b ? "You're ahead" : a < b ? "You're behind" : "You're tied";
                return " " + lead + ", " + a + " to " + b + (opp.HasPassed ? ", opponent has passed." : ", opponent hasn't passed.");
            }
            catch { return ""; }
        }

        private void ReleaseCoin()
        {
            if (!_holdingCoin) return;
            _holdingCoin = false;
            Plugin.LogInfo("[input] coin let go at hold " + BattleInput.CoinHold.ToString("0.00") + " (front " + Keys.GameInFront()
                + ", focused " + Application.isFocused + ", E held " + Keys.Held(KeyCode.E) + ", focused screen " + (ScreenManager.Current?.Key ?? "none") + ")");
            BattleInput.ReleaseCoin();
        }

        public override void Tick()
        {
            PileScreen.CheckRefused();
#if GWENT
            EmoteScreen.WatchOpen();
#endif
            WatchEnter();
            WatchCancel();
            if (_endTurnSince >= 0)
            {
                if (!Keys.Held(KeyCode.E)) _endTurnSince = -1; // let go early: nothing, as with the pass
                else if (Time.realtimeSinceStartup - _endTurnSince >= EndTurnHold)
                {
                    _endTurnSince = -1;
                    Plugin.LogInfo("[input] end-turn hold done");
                    if (BattleInput.PressCoin()) BattleInput.ReleaseCoin();
                }
            }
            if (!_holdingCoin) return;
            if (!Keys.Held(KeyCode.E)) ReleaseCoin();
            else BattleInput.HoldCoin();
        }

        // A popup or panel took over: an Enter being watched did something.
        public override void OnUnfocus() { _endTurnSince = -1; _enterAt = -1; ReleaseCoin(); }
        public override void OnPop() { _endTurnSince = -1; ReleaseCoin(); }

        // ---- Backspace: the game's cancel ----

        private void Cancel()
        {
            var req = _plays.Pending;
            if (req == null && BattleInput.Mode == EBattleNavigationMode.Normal) { Speech.Say("Nothing to cancel.", true); return; }
            bool can = true;
            try { if (req != null) can = req.CanCancel; } catch { }
            if (!can) { Speech.Say("This can't be cancelled.", true); return; }
            // Said once the game has left the play or targeting: some steps ignore its B (a multi-charge order
            // asking for its next target), and "Cancelled." was said for those too.
            if (BattleInput.Back()) { _cancelMode = BattleInput.Mode; _cancelAt = Time.realtimeSinceStartup; }
        }

        private EBattleNavigationMode _cancelMode;
        private float _cancelAt = -1;

        private void WatchCancel()
        {
            if (_cancelAt < 0) return;
            if (BattleInput.Mode != _cancelMode) { _cancelAt = -1; Speech.Say("Cancelled.", true); return; }
            if (Time.realtimeSinceStartup - _cancelAt < 0.8f) return;
            _cancelAt = -1;
            Speech.Say("This can't be cancelled now.", true);
        }

        // ---- Tab: the next valid play, target or row ----

        private void Cycle(int dir)
        {
            if (_cycle.Count == 0)
            {
                Speech.Say(_plays.Targets != null ? "No valid targets." : _plays.Rows != null ? "No valid rows."
                    : _plays.Placing != null ? "Choose a spot with the arrows."
                    : _plays.Confirming != null ? "Enter plays the card, Backspace puts it back." : _plays.MyTurn ? "No valid plays." : "Not your turn.", true);
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
                // Up/Down always read the card here; on a spot with no card (an empty row, a gap while placing)
                // they say where the cursor is.
                _cardKeys = new CardKeys(FocusedCard, Players, always: true, noCard: GraphNavigator.SayWhere);
                _keys = new List<ScreenKey>(_cardKeys.Keys)
                {
                    new ScreenKey { Key = KeyCode.Tab, Run = () => Cycle(1), Hint = "Tab, next valid play or target" },
                    new ScreenKey { Key = KeyCode.Tab, Shift = true, Run = () => Cycle(-1), Hint = "Shift Tab, previous valid play or target" },
                    new ScreenKey { Key = KeyCode.Backspace, Run = Cancel, Hint = "Backspace, cancel" },
                    new ScreenKey { Key = KeyCode.E, Run = PressE, Hint = "E, end turn or pass" },
                    new ScreenKey { Key = KeyCode.C, Run = () => JumpTo("hand"), Hint = "C, your hand" },
                    new ScreenKey { Key = KeyCode.C, Shift = true, Run = SayOpponentHand, Hint = "Shift C, opponent's hand size" },
                    new ScreenKey { Key = KeyCode.B, Run = () => JumpTo("my-melee"), Hint = "B, your melee row" },
                    new ScreenKey { Key = KeyCode.B, Shift = true, Run = () => JumpTo("my-ranged"), Hint = "Shift B, your ranged row" },
                    new ScreenKey { Key = KeyCode.G, Run = () => JumpTo("opp-melee"), Hint = "G, opponent's melee row" },
                    new ScreenKey { Key = KeyCode.G, Shift = true, Run = () => JumpTo("opp-ranged"), Hint = "Shift G, opponent's ranged row" },
                    new ScreenKey { Key = KeyCode.V, Run = () => JumpTo("my-leader"), Hint = "V, your leader" },
                    new ScreenKey { Key = KeyCode.F, Run = () => JumpTo("opp-leader"), Hint = "F, opponent's leader" },
                    new ScreenKey { Key = KeyCode.A, Run = SayScore, Hint = "A, score" },
                    new ScreenKey { Key = KeyCode.T, Run = SayTurn, Hint = "T, whose turn it is" },
                    new ScreenKey { Key = KeyCode.T, Shift = true, Run = SayTimer, Hint = "Shift T, time left in the turn" },
                    new ScreenKey { Key = KeyCode.O, Run = LogScreen.Toggle, Hint = "O, battle log: everything said this match, newest first" },
                    new ScreenKey { Key = KeyCode.D, Run = () => PileScreen.Open(deck: true, mine: true), Hint = "D, your deck" },
                    new ScreenKey { Key = KeyCode.Y, Run = () => PileScreen.Open(deck: false, mine: true), Hint = "Y, your graveyard" },
                    new ScreenKey { Key = KeyCode.Y, Shift = true, Run = () => PileScreen.Open(deck: false, mine: false), Hint = "Shift Y, opponent's graveyard" },
#if GWENT
                    new ScreenKey { Key = KeyCode.M, Run = EmoteScreen.Open, Hint = "M, emotes" },
#endif
                };
                _keys.AddRange(RowKeys(() => _gc, JumpTo));
                return _keys;
            }
        }

        // The number row, in the order the first version of the mod used (its Ctrl+1 to 4), which players
        // already know: your melee, your ranged, the opponent's melee, the opponent's ranged.
        private static readonly string[] NumberRows = { "my-melee", "my-ranged", "opp-melee", "opp-ranged" };

        /// <summary>The number row's keys over the board rows: Shift+1 to 4 read a row without moving; the plain
        /// number jumps to it with <paramref name="jump"/>, or, where the board can't be moved over (a choice
        /// open on top of it: <see cref="ChoiceScreen"/>), reads it too — a sighted player looks at the board
        /// before picking.</summary>
        internal static List<ScreenKey> RowKeys(Func<GameController> gc, Action<string> jump)
        {
            var keys = new List<ScreenKey>();
            for (int i = 0; i < NumberRows.Length; i++)
            {
                var row = Array.Find(Rows, r => r.Key == NumberRows[i]);
                var key = KeyCode.Alpha1 + i;
                string name = row.Name.ToLowerInvariant();
                keys.Add(jump != null
                    ? new ScreenKey { Key = key, Run = () => jump(row.Key), Hint = (i + 1) + ", " + name }
                    : new ScreenKey { Key = key, Run = () => SayRow(gc(), row), Hint = (i + 1) + ", read " + name });
                keys.Add(new ScreenKey { Key = key, Shift = true, Run = () => SayRow(gc(), row), Hint = "Shift " + (i + 1) + ", read " + name });
            }
            return keys;
        }

        /// <summary>A whole row read at once without moving the cursor: "Your ranged row, score 12, Frost, 2
        /// turns left, 2 cards: …; …".</summary>
        private static void SayRow(GameController gc, Row row)
        {
            if (gc == null) return;
            var who = row.Mine ? Battle.Me(gc) : Battle.Them(gc);
            var cards = Battle.Cards(gc, who, row.Where);
            string head = row.Name + ", " + RowDetail(cards, RowLocation(gc, row));
            if (cards.Count == 0) { Speech.Say(head + ", empty", true); return; }
            var labels = new List<string>();
            foreach (var c in cards) labels.Add(Label(c, gc.PlayerManager));
            Speech.Say(head + ", " + (cards.Count == 1 ? "1 card" : cards.Count + " cards") + ": " + string.Join("; ", labels), true);
        }

        private static Location RowLocation(GameController gc, Row row)
        {
            try { return gc.BoardManager.GetLocation(row.Mine ? Battle.Me(gc) : Battle.Them(gc), row.Where); }
            catch { return null; }
        }

        private void JumpTo(string row)
        {
            // While placing, the board is the rows' gaps: land on the row's last gap, where placing starts.
            if (_gaps.Count > 0)
            {
                string prefix = "battle:gap:" + row + ":";
                string lastGap = null;
                foreach (var key in _gaps.Keys)
                    if (key.StartsWith(prefix, StringComparison.Ordinal)) lastGap = key;
                var target = Array.Find(Rows, r => r.Key == row);
                string name = target?.Name;
                var here = (string)GraphNavigator.CurrentNode?.Id.StructuralKey;
                bool full = false;
                try { full = target != null && _gc != null && RowLocation(_gc, target)?.IsFull() == true; } catch { }
                if (lastGap == null) Speech.Say("Can't place in " + name?.ToLowerInvariant() + (full ? ", full." : "."), true);
                else if (here != null && here.StartsWith(prefix, StringComparison.Ordinal)) Speech.Say(name, true);
                else GraphNavigator.FocusNode(ControlId.Structural(lastGap));
                return;
            }

            // Already there: moving would be silent (the differ speaks only a change), so say it again.
            var node = GraphNavigator.CurrentNode;
            if (node != null && _rowOf.TryGetValue((string)node.Id.StructuralKey, out var at) && at == row)
            {
                var card = FocusedCard();
                var here = Array.Find(Rows, r => r.Key == row);
                string name = here?.Name;
                // A board row also says what the row shows (score, effect, full), as landing in it does.
                if (here != null && here.Scored && _gc != null)
                    name += ", " + RowDetail(Battle.Cards(_gc, here.Mine ? Battle.Me(_gc) : Battle.Them(_gc), here.Where), RowLocation(_gc, here));
                Speech.Say((name != null ? name + ", " : "") + (card != null ? Label(card, Players()) : "empty"), true);
                return;
            }
            // The card last visited in that row, if it is still there: a card played from the hand keeps its
            // id on the board, and C landed on it there.
            if (_lastInRow.TryGetValue(row, out var id) && _rowOf.TryGetValue((string)id.StructuralKey, out var still) && still == row
                && GraphNavigator.FocusNode(id)) return;
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
            // The counter on the opponent's panel ("3/10": cards in hand of the limit) when it is drawn.
            string shown = PanelText(false, "RightSideVerticalGroup/HandCountView/Hand Icon/Hand Card Number");
            if (shown != null && shown.Contains(" of ")) { Speech.Say("Opponent's hand, " + shown + " cards", true); return; }
            int n = Battle.HandCount(_gc, Battle.Them(_gc));
            Speech.Say("Opponent's hand, " + (n == 1 ? "1 card" : n + " cards"), true);
        }

        // The player panels beside the board (UIBoardPlayerControl under the battle dialogue's PlayerViewAnchors:
        // BottomPlayer is you, TopPlayer the opponent), found once per battle.
        private static UnityEngine.Transform _top, _bottom;

        private static UnityEngine.Transform PlayerPanel(bool mine)
        {
            var t = mine ? _bottom : _top;
            try { if (t != null && t.gameObject != null) return t; } catch { }
            var root = UnityEngine.GameObject.Find("MainCanvas/UIBattleDialoguePrefab(Clone)/PlayerViewAnchors");
            if (root == null) return null;
            var anchor = root.transform.Find(mine ? "BottomPlayer" : "TopPlayer");
            var panel = anchor != null && anchor.childCount > 0 ? anchor.GetChild(0) : null;
            if (mine) _bottom = panel; else _top = panel;
            return panel;
        }

        // A drawn text in a player panel, cleaned; null when it isn't shown.
        private static string PanelText(bool mine, string path)
        {
            try
            {
                var t = PlayerPanel(mine)?.Find(path)?.GetComponent<TMPro.TMP_Text>();
                if (t == null || !t.isActiveAndEnabled || t.color.a < 0.3f) return null;
                string s = UiText.Clean(t.text);
                return s.Length > 0 ? s : null;
            }
            catch { return null; }
        }

        // "Monsters, CARDSMITH": the player's name and title as their panel shows them.
        private static string PlayerTag(bool mine)
        {
            var parts = new List<string>();
            foreach (var part in new[] { "Vertical/PlayerInfo/Slideable/NameAndTitle/Name", "Vertical/PlayerInfo/Slideable/NameAndTitle/Title" })
            {
                var s = PanelText(mine, part);
                if (s != null) parts.Add(s);
            }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        // T: whose turn, and who has passed, as the turn banner and the pass markers show it.
        private void SayTurn()
        {
            var gc = _gc;
            if (gc == null) return;
            string turn = _plays.MyTurn ? GameText.Get("gwent_message_your_turn_main", "Your turn") : GameText.Get("gwent_message_opponent_turn_main", "Opponent's turn");
            var parts = new List<string> { Narrator.Sentence(turn) };
            try
            {
                var me = gc.PlayerManager.LocalUser;
                if (me.HasPassed) parts.Add("You have passed.");
                if (me.OpponentPlayer.HasPassed) parts.Add("Opponent has passed.");
            }
            catch { }
            Speech.Say(string.Join(" ", parts), true);
        }

        // Shift+T: the turn clock, yours or the opponent's, as a sighted player glances at it (Accessible
        // Arena's timer key).
        private void SayTimer()
        {
            var gc = _gc;
            if (gc == null) return;
            float mine = TurnTimer.SecondsLeft(gc);
            if (mine >= 0) { Speech.Say(Mathf.CeilToInt(mine) + " seconds left in your turn.", true); return; }
            float theirs = TurnTimer.OpponentSecondsLeft(gc);
            if (theirs >= 0) { Speech.Say(Mathf.CeilToInt(theirs) + " seconds left in the opponent's turn.", true); return; }
            Speech.Say(_plays.MyTurn ? "No turn timer." : "Opponent's turn, no timer.", true);
        }

        private void SayScore()
        {
            if (_gc != null) Speech.Say(Battle.StatusText(_gc), true);
        }
    }
}
