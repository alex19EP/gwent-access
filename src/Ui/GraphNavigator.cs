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
    /// stops, Home/End jump to the ends, Enter activates, Backspace is the secondary action, Space reads the
    /// details. Escape stays the game's own Back. Ctrl/Alt chords are left to the legacy hotkeys.
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

        /// <summary>True while a mod screen owns the keyboard and speech for the game's current UI. Legacy paths
        /// (the selection narrator) stand down on it — one keypress, one model (spec §10.5).</summary>
        public static bool Owns => _screen != null;

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
            _spokenKey = null;
            _spokenNode = null;
            _syncedKey = null;
            _observedGame = IntPtr.Zero; // the first frame follows whatever the game has selected
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

        public static void ScreenClosed(Screen screen) => _states.Remove(screen);

        /// <summary>Module dispose: forget every screen and give the game its keys back.</summary>
        public static void Reset()
        {
            Attach(null);
            _states.Clear();
            GameKeyBlock.Release();
        }

        public static void Tick()
        {
            bool keyboard = Keys.Update();

            _claims.Clear();
            if (_screen != null)
            {
                foreach (var k in NavKeys) _claims.Add(k);
                foreach (var k in _screen.ExtraKeys) _claims.Add(k.Key);
            }
            GameKeyBlock.Apply(_claims);

            if (_graph == null) return;
            if (keyboard) HandleKeys();
            Settle();
        }

        // ---- the differ ----

        private static void Settle()
        {
            if (!_graph.Rerender()) return; // no content this frame; the cursor survives for the next one
            FollowGame();
            var node = _graph.CurrentNode;
            if (node == null) return;
            if (_spokenKey == null || !_spokenKey.Equals(node.Id))
            {
                // Queued: a landing nobody keyed follows the screen name or the feedback that caused it.
                Say(GraphAnnouncer.Compose(_spokenNode, node), interrupt: false);
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

        private static void HandleKeys()
        {
            if (Keys.Alt) return;
            foreach (var k in _screen.ExtraKeys)
                if (k.Ctrl == Keys.Ctrl && k.Shift == Keys.Shift && Keys.Down(k.Key))
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
            else if (Keys.Down(KeyCode.Return) || Keys.Down(KeyCode.KeypadEnter)) Activate();
            else if (Keys.Down(KeyCode.Backspace)) Run(_graph.CurrentNode?.Vtable.OnSecondary, _graph.Secondary);
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
            var move = _graph.MoveStop(dir, _screen.Wrap);
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

        /// <summary>Re-speak the focused control with its full context ("where am I").</summary>
        public static void SayWhere()
        {
            if (_graph == null || !_graph.Rerender()) return;
            var node = _graph.CurrentNode;
            if (node != null) Say(GraphAnnouncer.ComposeFull(node), interrupt: true);
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
