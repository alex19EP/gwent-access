using System;
using System.Collections.Generic;
using Access.Core.Graph;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Runs the focused <see cref="Screen"/> on the graph kernel (spec §7). Every frame: claim this screen's keys
    /// from the game (<see cref="GameKeyBlock"/>), dispatch this frame's presses, then rebuild the screen,
    /// reconcile the cursor and — if the focused identity differs from the one last spoken — speak the
    /// path-diffed line once. That differ is the only place a landing is announced, whatever moved it: a key,
    /// a rebuild that removed the focused control, or the screen appearing.
    ///
    /// Keys (while a screen is focused): arrows move (Left/Right adjust a slider first), Tab/Shift+Tab cycle
    /// stops, Home/End jump to the ends (on screens of cards, of the row: <see cref="RowEdge"/>), Enter activates, Backspace is the item's secondary action or else the
    /// screen's Back, Space reads the details. Escape stays the game's own Back. Ctrl and Alt chords are left
    /// to the screens' own keys.
    ///
    /// Return to the opener: a control Enter activated that then leaves the graph opened something in its
    /// place (a picker, a list, the next menu page). When it comes back (that thing closed), the cursor
    /// returns to it, and holds the game's selection there for a moment — the game itself tends to drop
    /// its selection on some other control (the mode card) as a panel closes. Screens opt out with
    /// <see cref="Screen.ReturnsToOpener"/>.
    ///
    /// Shaped after RTAccess's and WrathAccess's GraphNavigator, without type-ahead or sounds (yet).
    /// </summary>
    internal static class GraphNavigator
    {
        // One cursor per live screen: a covered screen keeps its place; a popped one starts fresh.
        private static readonly Dictionary<Screen, GraphState> _states = new Dictionary<Screen, GraphState>();
        private static Screen _screen;
        private static GraphState _state;
        private static KeyGraph _graph;

        // The differ's memory: the identity (and node, for the path diff) last spoken.
        private static ControlId _spokenKey;
        private static GraphNode _spokenNode;

        // Write-back memory: the identity last handed to the screen's OnCursorMoved.
        private static ControlId _syncedKey;


        private static readonly KeyCode[] NavKeys =
        {
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.Tab, KeyCode.Home, KeyCode.End, KeyCode.Return, KeyCode.KeypadEnter,
            KeyCode.Backspace, KeyCode.Space,
        };
        private static readonly HashSet<KeyCode> _claims = new HashSet<KeyCode>();

        static GraphNavigator()
        {
            GraphAnnouncer.PositionText = (i, n) => i + " of " + n;
            GraphAnnouncer.ExpandedStateText = expanded => expanded ? "expanded" : "collapsed";
        }

        public static void Attach(Screen screen)
        {
            if (ReferenceEquals(screen, _screen)) return;
            _screen = screen;
            GraphAnnouncer.MinPositionCount = screen != null && screen.PositionOfOne ? 1 : 2;
            _spokenKey = null;
            _spokenNode = null;
            _syncedKey = null;
            _observedGame = IntPtr.Zero; // the first frame follows whatever the game has selected
            _activated = null;
            _hold = null;
            if (screen == null)
            {
                _state = null;
                _graph = null;
                return;
            }
            if (!_states.TryGetValue(screen, out _state))
                _states[screen] = _state = new GraphState();
            _graph = new KeyGraph(() => Render(screen), _state);
        }

        public static void ScreenClosed(Screen screen)
        {
            _states.Remove(screen);
            _openers.Remove(screen);
        }

        /// <summary>Module dispose: forget every screen and give the game its keys back.</summary>
        public static void Reset()
        {
            Attach(null);
            _states.Clear();
            _openers.Clear();
            GameKeyBlock.Release();
        }

        public static void Tick()
        {
            bool keyboard = Keys.Update();

            _claims.Clear();
            if (_screen != null)
            {
                foreach (var k in NavKeys)
                    if (!_screen.LeavesToGame(k)) _claims.Add(k);
                foreach (var k in _screen.ExtraKeys) _claims.Add(k.Key);
            }
            GameKeyBlock.Apply(_claims);

            if (_graph == null) return;
            if (keyboard) HandleKeys();
            if (keyboard) _screen.Tick();
            Settle();
        }

        // ---- the differ ----

        private static void Settle()
        {
            if (!_graph.Rerender()) return; // no content this frame; the cursor survives for the next one
            ReturnToOpener();
            if (!Hold()) FollowGame();
            var node = _graph.CurrentNode;
            if (node == null) return;
            if (_spokenKey == null || !_spokenKey.Equals(node.Id))
            {
                // Queued: a landing nobody keyed follows the screen name or the feedback that caused it.
                bool quiet = false;
                try { quiet = _screen.QuietLanding(node); }
                catch (Exception e) { ScreenManager.LogOnce(_screen, "QuietLanding", e); }
                if (!quiet) Say(GraphAnnouncer.Compose(_spokenNode, node), interrupt: false);
                Spoken(node);
            }
            SyncGame(node);
            WatchLive(node);
        }

        private static void Spoken(GraphNode node)
        {
            _spokenKey = node.Id;
            _spokenNode = node;
        }

        // The game's own selection as last observed. Following only CHANGES of it is what tells the game's
        // moves from the mod's: a write-back the game applies reads back as the cursor (nothing to follow), and
        // one it refuses leaves the observed value unchanged (nothing to follow either).
        private static IntPtr _observedGame;

        private static void FollowGame()
        {
            CrimsonUI.AControl sel;
            try { sel = _screen.GameSelection(); }
            catch (Exception e) { ScreenManager.LogOnce(_screen, "GameSelection", e); return; }
            var ptr = sel?.Pointer ?? IntPtr.Zero;
            if (ptr == _observedGame) return;
            _observedGame = ptr;
            if (sel == null) return;
            // The cursor is on a control the game won't select (VERSUS PLAYER while it is locked): the game puts
            // its selection back where it was, which is a refusal, not a move to follow.
            if (_graph.CurrentNode?.Id.Reference is CrimsonUI.AControl here && !Selectable(here)) return;
            // The nearest node at or above the selection: a node can stand for a composite control whose inner
            // parts the game selects (a settings row's switcher and its arrows).
            for (var t = sel.transform; t != null; t = t.parent)
            {
                var key = CrimsonNodes.PathKey(t, null);
                var target = _graph.Current.NodeAt(ControlId.Structural(key));
                if (target == null) continue;
                var cur = _graph.CurrentNode;
                if (cur != null && Equals(cur.Id.StructuralKey, key)) return;
                _graph.Focus(target.Id);
                _syncedKey = target.Id; // the game is already there; nothing to write back
                return;
            }
        }

        private static bool Selectable(CrimsonUI.AControl c)
        {
            try { return c.IsCurrentlySelectable(); } catch { return true; }
        }

        // ---- return to the opener ----

        private const float OpenerWindow = 2f;  // an activation that hasn't removed its control by then opened nothing
        private const float HoldSeconds = 1.5f;
        private const int MaxOpeners = 8;

        // Per live screen, the activated controls that left the graph, oldest first.
        private static readonly Dictionary<Screen, List<ControlId>> _openers = new Dictionary<Screen, List<ControlId>>();

        // The control Enter last activated, until it leaves the graph or the window passes.
        private static ControlId _activated;
        private static float _activatedAt;

        // A returned-to control the cursor and the game's selection are held on, until a deadline or a key.
        private static ControlId _hold;
        private static float _holdUntil;
        private static int _holdFrame;

        private static void Activated(GraphNode node)
        {
            if (!_screen.ReturnsToOpener) return;
            _activated = node.Id;
            _activatedAt = Time.realtimeSinceStartup;
        }

        private static void ReturnToOpener()
        {
            if (!_screen.ReturnsToOpener) return;
            var render = _graph.Current;
            float now = Time.realtimeSinceStartup;
            if (_activated != null)
            {
                if (render.NodeAt(_activated) == null)
                {
                    if (!_openers.TryGetValue(_screen, out var stack)) _openers[_screen] = stack = new List<ControlId>();
                    stack.Remove(_activated);
                    stack.Add(_activated);
                    if (stack.Count > MaxOpeners) stack.RemoveAt(0);
                    _activated = null;
                }
                else if (now - _activatedAt > OpenerWindow) _activated = null;
            }

            // The newest opener back in the graph wins; whatever it opened since is gone with it.
            if (!_openers.TryGetValue(_screen, out var openers)) return;
            for (int i = openers.Count - 1; i >= 0; i--)
            {
                if (render.NodeAt(openers[i]) == null) continue;
                var id = openers[i];
                openers.RemoveRange(i, openers.Count - i);
                if (!_graph.Focus(id)) return;
                _hold = _graph.CurrentNode.Id;
                _holdUntil = now + HoldSeconds;
                _holdFrame = Time.frameCount;
                return;
            }
        }

        // While holding: keep the cursor on the held control and put the game's selection back on it, instead
        // of following the game. A key pressed after the hold began releases it, as does the deadline.
        private static bool Hold()
        {
            if (_hold == null) return false;
            var node = _graph.Current.NodeAt(_hold);
            if (node == null || Time.realtimeSinceStartup > _holdUntil || Time.frameCount != _holdFrame && Keys.AnyDown())
            {
                _hold = null;
                return false;
            }
            if (_graph.CurrentNode == null || !_graph.CurrentNode.Id.Equals(node.Id)) _graph.Focus(node.Id);
            if (node.Id.Reference is CrimsonUI.AControl c)
            {
                CrimsonUI.AControl sel = null;
                try { sel = _screen.GameSelection(); } catch { }
                if (sel == null || sel.Pointer != c.Pointer)
                    try { _screen.OnCursorMoved(node); }
                    catch (Exception e) { ScreenManager.LogOnce(_screen, "OnCursorMoved", e); }
                _observedGame = c.Pointer; // the game sits here once the hold ends; nothing to follow then
            }
            _syncedKey = node.Id;
            return true;
        }

        private static void SyncGame(GraphNode node)
        {
            if (_syncedKey != null && _syncedKey.Equals(node.Id)) return;
            _syncedKey = node.Id;
            try { _screen.OnCursorMoved(node); }
            catch (Exception e) { ScreenManager.LogOnce(_screen, "OnCursorMoved", e); }
        }

        // ---- the live watch: a focused node's Live parts speak when their value changes ----

        private static ControlId _liveKey;
        private static readonly List<string> _liveValues = new List<string>();

        private static void WatchLive(GraphNode node)
        {
            var anns = GraphAnnouncer.EffectiveAnnouncements(node);
            bool baseline = _liveKey == null || !_liveKey.Equals(node.Id) || _liveValues.Count != anns.Count;
            if (baseline) { _liveKey = node.Id; _liveValues.Clear(); }
            for (int i = 0; i < anns.Count; i++)
            {
                string v = null;
                if (anns[i].Live)
                    try { v = anns[i].Text?.Invoke(); } catch { }
                if (baseline) { _liveValues.Add(v); continue; }
                if (!anns[i].Live || string.Equals(_liveValues[i], v)) continue;
                _liveValues[i] = v;
                if (!string.IsNullOrEmpty(v)) Say(v, interrupt: false);
            }
        }

        // ---- input ----

        /// <summary>Logs each key pressed while a graph screen has the navigator, with its modifiers, whether the
        /// screen left it to the game, and the node it was pressed on, so a log reads as the session went.
        /// Escape is always the game's (Back) and logs as such. Each key logs at most once a frame: a screen can
        /// bind one key several times (C and Shift+C), and a key it binds can also be a navigation key.</summary>
        private static void LogKeys()
        {
            _logged.Clear();
            foreach (var k in NavKeys) LogKey(k, _screen.LeavesToGame(k));
            foreach (var k in _screen.ExtraKeys) LogKey(k.Key, false);
            LogKey(KeyCode.Escape, true);
        }

        // The keys logged this frame.
        private static readonly HashSet<KeyCode> _logged = new HashSet<KeyCode>();

        private static void LogKey(KeyCode k, bool game)
        {
            if (!Keys.Down(k) || !_logged.Add(k)) return;
            string mods = (Keys.Ctrl ? "Ctrl+" : "") + (Keys.Shift ? "Shift+" : "") + (Keys.Alt ? "Alt+" : "");
            string at = null;
            try
            {
                var node = _graph?.CurrentNode;
                if (node != null)
                    foreach (var a in GraphAnnouncer.EffectiveAnnouncements(node))
                        if (a.Kind == AnnouncementKinds.Label) { at = a.Text?.Invoke(); break; }
            }
            catch { }
            Plugin.LogInfo("[key] " + mods + k + (game ? " (game)" : "") + " on " + _screen.Key + (at != null ? " @ " + at : ""));
        }

        // A screen key pressed this frame; the arrows also repeat while held (Up/Down reading a card's lines).
        private static bool Pressed(KeyCode k) =>
            k == KeyCode.UpArrow || k == KeyCode.DownArrow || k == KeyCode.LeftArrow || k == KeyCode.RightArrow
                ? Keys.DownOrRepeat(k) : Keys.Down(k);

        private static void HandleKeys()
        {
            try { LogKeys(); } catch { }
            if (Keys.Alt) return;
            foreach (var k in _screen.ExtraKeys)
                if (k.Ctrl == Keys.Ctrl && k.Shift == Keys.Shift && Pressed(k.Key) && (k.When == null || k.When()))
                {
                    try { k.Run(); }
                    catch (Exception e) { ScreenManager.LogOnce(_screen, "key " + k.Key, e); }
                    return;
                }
            if (Keys.Ctrl) return;

            if (Keys.DownOrRepeat(KeyCode.UpArrow)) Arrow(GraphDir.Up);
            else if (Keys.DownOrRepeat(KeyCode.DownArrow)) Arrow(GraphDir.Down);
            else if (Keys.DownOrRepeat(KeyCode.LeftArrow)) Arrow(GraphDir.Left);
            else if (Keys.DownOrRepeat(KeyCode.RightArrow)) Arrow(GraphDir.Right);
            else if (Keys.Down(KeyCode.Tab)) Tab(Keys.Shift ? -1 : 1);
            else if (Keys.Down(KeyCode.Home)) Edge(GraphDir.Up);
            else if (Keys.Down(KeyCode.End)) Edge(GraphDir.Down);
            else if (Keys.Down(KeyCode.Return) && !_screen.LeavesToGame(KeyCode.Return)
                || Keys.Down(KeyCode.KeypadEnter) && !_screen.LeavesToGame(KeyCode.KeypadEnter)) Activate();
            else if (Keys.Down(KeyCode.Backspace))
            {
                if (_graph.CurrentNode?.Vtable.OnSecondary != null) Run(_graph.CurrentNode.Vtable.OnSecondary, _graph.Secondary);
                else Back();
            }
            else if (Keys.Down(KeyCode.Space)) Details();
        }

        private static void Arrow(GraphDir dir)
        {
            var node = _graph.CurrentNode;
            if (node == null) return;
            if ((dir == GraphDir.Left || dir == GraphDir.Right) && node.Vtable.OnAdjust != null)
            {
                _graph.TryAdjust(dir == GraphDir.Right ? 1 : -1, large: false);
                SpeakState();
                return;
            }
            var move = _graph.Move(dir);
            if (move.Moved) { Announce(move); return; }
            if (dir == GraphDir.Left || dir == GraphDir.Right)
            {
                var tree = dir == GraphDir.Right ? _graph.TreeRight() : _graph.TreeLeft();
                switch (tree.Kind)
                {
                    case KeyGraph.TreeMove.Expanded:
                    case KeyGraph.TreeMove.Collapsed:
                        var n = _graph.CurrentNode;
                        if (n != null) { Say(GraphAnnouncer.LeafText(n), interrupt: true); Spoken(n); }
                        return;
                    case KeyGraph.TreeMove.EmptyGroup:
                        Say("Empty.", interrupt: true);
                        return;
                    case KeyGraph.TreeMove.Descended:
                    case KeyGraph.TreeMove.Ascended:
                        Announce(tree.Move);
                        return;
                }
            }
        }

        private static void Tab(int dir)
        {
            var move = _graph.MoveStop(dir, true); // Tab past the last stop returns to the first
            if (move.Moved) Announce(move);
        }

        private static void Edge(GraphDir dir)
        {
            var node = _graph.CurrentNode;
            if (node == null) return;
            var move = KeyGraph.InTree(node) ? _graph.MoveToSiblingEdge(dir == GraphDir.Up) : _graph.MoveToEdge(dir);
            if (move.Moved) Announce(move);
        }

        private static void Activate()
        {
            var node = _graph.CurrentNode;
            if (node == null) return;
            Plugin.LogInfo("[nav] activate " + node.Id.StructuralKey);
            Activated(node);
            Run(node.Vtable.OnActivate, () => _graph.Activate());
        }

        private static void Details()
        {
            var node = _graph.CurrentNode;
            if (node == null) return;
            if (node.Vtable.OnTooltip != null) _graph.Tooltip();
            else Say("No details.", interrupt: true);
        }

        // Run a node hook; then speak the (possibly re-resolved) node's state line as synchronous feedback.
        private static void Run(Action hook, Func<bool> op)
        {
            if (hook == null) return;
            try { op(); }
            catch (Exception e) { ScreenManager.LogOnce(_screen, "node action", e); }
            SpeakState();
        }

        // Backspace with nothing of the item's own to do: the screen's Back, never a silent key.
        private static void Back()
        {
            bool done = false;
            try { done = _screen.Back(); }
            catch (Exception e) { ScreenManager.LogOnce(_screen, "Back", e); }
            if (!done) Say("Nothing to go back to here.", interrupt: true);
        }

        private static void SpeakState()
        {
            var st = _graph.CurrentNode?.Vtable.StateText;
            if (st == null) return;
            Say(st(), interrupt: true);
            _liveKey = null; // rebaseline: the change was just spoken
        }

        private static void Announce(MoveResult move)
        {
            if (move.To == null) return;
            Say(GraphAnnouncer.Compose(move.From, move.To, move.TransitionLabel), interrupt: true);
            Spoken(move.To);
        }

        /// <summary>The focused node, for a screen key that acts on it.</summary>
        public static GraphNode CurrentNode => _graph?.CurrentNode;

        /// <summary>Move the cursor to <paramref name="id"/> and speak the landing as a key move does (a
        /// screen's jump key). False when the node isn't in the current render.</summary>
        public static bool FocusNode(ControlId id)
        {
            if (_graph == null) return false;
            var from = _graph.CurrentNode;
            if (!_graph.Focus(id)) return false;
            Announce(new MoveResult { Moved = true, From = from, To = _graph.CurrentNode });
            return true;
        }

        /// <summary>From a <see cref="Screen.Build"/>: land on <paramref name="id"/> in this render (a card that
        /// just arrived). The differ speaks it like any landing.</summary>
        public static void Suggest(ControlId id)
        {
            if (_state != null) _state.NextSuggestedMove = id;
        }

        /// <summary>For a screen key: an arrow press (Tab as Down on a one-list screen).</summary>
        public static void Step(GraphDir dir) { if (_graph != null) Arrow(dir); }

        /// <summary>For a screen key: Home/End along the focused row only (spec §7.4), to its first or last item,
        /// where <see cref="Edge"/> goes to the top or bottom of the whole stop. On screens of cards a row is a
        /// hand or a board row, and End from the hand would otherwise land on your leader. Silent when already
        /// there, as at any edge.</summary>
        public static void RowEdge(bool first)
        {
            if (_graph?.CurrentNode == null) return;
            var move = _graph.MoveToEdge(first ? GraphDir.Left : GraphDir.Right);
            if (move.Moved) Announce(move);
        }

        /// <summary>For a screen key: Enter on the focused node, as the key does.</summary>
        public static void ActivateCurrent() { if (_graph != null) Activate(); }

        /// <summary>The nodes of the current render, in declaration order (empty when none).</summary>
        public static IReadOnlyList<GraphNode> Nodes()
        {
            if (_graph == null || !_graph.Rerender() || _graph.Current == null) return Array.Empty<GraphNode>();
            return _graph.Current.Order;
        }

        /// <summary>A node's own name, its label announcement as spoken.</summary>
        public static string LabelOf(GraphNode node)
        {
            try
            {
                foreach (var a in GraphAnnouncer.EffectiveAnnouncements(node))
                    if (a.Kind == AnnouncementKinds.Label) return a.Text?.Invoke();
            }
            catch { }
            return null;
        }

        /// <summary>Re-speak the focused control with its full context ("where am I").</summary>
        public static void SayWhere() => SayWhere(false);

        /// <summary>F9: the focused control with its full context, after the screen's name when asked.</summary>
        public static void SayWhere(bool withScreen)
        {
            if (_graph == null || !_graph.Rerender()) { if (withScreen) Say("Nothing to read here.", interrupt: true); return; }
            var node = _graph.CurrentNode;
            if (node == null) return;
            string name = withScreen ? _screen?.ScreenName : null;
            string full = GraphAnnouncer.ComposeFull(node);
            Say(string.IsNullOrEmpty(name) ? full : name + ", " + full, interrupt: true);
        }

        // ---- render ----

        // Isolated (spec §7.9): a Build that throws mid-transition renders nothing this frame instead of
        // escaping into the frame tick; the cursor survives and the next good render reconciles back.
        private static GraphRender Render(Screen screen)
        {
            try
            {
                var b = new GraphBuilder(_state.Expanded);
                screen.Build(b);
                return b.Build();
            }
            catch (Exception e)
            {
                ScreenManager.LogOnce(screen, "Build", e);
                return null;
            }
        }

        private static void Say(string text, bool interrupt)
        {
            if (!string.IsNullOrWhiteSpace(text)) Speech.Say(text, interrupt);
        }

        public static string Describe()
        {
            var node = _graph?.CurrentNode;
            return "screen: " + (_screen?.Key ?? "none")
                + "\nfocus: " + (node?.Id.StructuralKey ?? "-")
                + "\nnodes: " + (_graph?.Current?.Order.Count ?? 0)
                + "\nblocked game keys: " + GameKeyBlock.Describe() + "\n";
        }
    }
}
