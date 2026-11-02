using System;
using System.Collections.Generic;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The screen stack (spec §9), resolved by poll-and-diff every frame: each registered screen's
    /// <see cref="Screen.IsActive"/> is asked, screens that went inactive are popped, new ones pushed, and the
    /// navigator is attached to the top (highest <see cref="Screen.Layer"/>, ties to the later registration).
    /// Polling instead of event subscription keeps the stack right while the game destroys and rebuilds its
    /// panels at will.
    /// </summary>
    internal static class ScreenManager
    {
        private static readonly List<Screen> _registered = new List<Screen>();
        private static List<Screen> _stack = new List<Screen>();
        // Screens that left the stack, with when: their cursors are kept while an overlay of a higher layer is up
        // (or for CoverGrace, across the frames between one panel going and the overlay's arriving).
        private static readonly Dictionary<Screen, float> _covered = new Dictionary<Screen, float>();
        private const float CoverGrace = 0.5f;
        // The name each screen was last announced by: one screen can stand for several panels (the fallback panel
        // screen), and coming back as a different one is a new screen to the player.
        private static readonly Dictionary<Screen, string> _names = new Dictionary<Screen, string>();
        private static Screen _focused;

        /// <summary>The screen the navigator is attached to, or null when no mod screen is up.</summary>
        public static Screen Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        public static void Register(Screen s) => _registered.Add(s);

        public static void Tick()
        {
            var desired = new List<Screen>();
            foreach (var s in _registered)
                if (SafeIsActive(s)) desired.Add(s);
            desired.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : _registered.IndexOf(a).CompareTo(_registered.IndexOf(b)));

            int topLayer = desired.Count > 0 ? desired[desired.Count - 1].Layer : int.MinValue;
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (!desired.Contains(_stack[i]))
                {
                    var gone = _stack[i];
                    Safe(gone.OnPop, gone, "OnPop");
                    // Maybe an overlay of a higher layer took the game's input from it (the card preview over the
                    // deck builder, a popup over a menu): the game hides the screen's panel from the active input
                    // layer without closing it. Keep its cursor for when the overlay goes (below).
                    _covered[gone] = UnityEngine.Time.realtimeSinceStartup;
                }
            bool uncovered = false;
            foreach (var s in desired)
                if (!_stack.Contains(s))
                {
                    Safe(s.OnPush, s, "OnPush");
                    if (_covered.Remove(s) && ReferenceEquals(s, desired[desired.Count - 1])) uncovered = true;
                }
            // A screen that left with no overlay above it, or whose overlay went without it coming back, is closed.
            float now = UnityEngine.Time.realtimeSinceStartup;
            List<Screen> closed = null;
            foreach (var kv in _covered)
                if (topLayer <= kv.Key.Layer && now - kv.Value > CoverGrace) (closed ??= new List<Screen>()).Add(kv.Key);
            if (closed != null)
                foreach (var c in closed) { _covered.Remove(c); GraphNavigator.ScreenClosed(c); }
            var below = _stack;
            _stack = desired;

            var cur = Current;
            // A screen that stayed live under the one that went (the main menu under the help) is returned to too.
            if (cur != null && below.Contains(cur)) uncovered = true;
            if (ReferenceEquals(cur, _focused)) return;
            if (_focused != null) Safe(_focused.OnUnfocus, _focused, "OnUnfocus");
            _focused = cur;
            Plugin.LogInfo("[screens] focus: " + (cur?.Key ?? "none (game)") + (uncovered ? " (back from an overlay)" : ""));
            if (cur != null)
            {
                Safe(cur.OnFocus, cur, "OnFocus");
                // Back from an overlay the player is where they were: only the item is spoken, not the screen again.
                var name = cur.ScreenName;
                bool same = uncovered && !cur.NamedOnReturn && _names.TryGetValue(cur, out var was) && was == name;
                _names[cur] = name;
                if (!same && !string.IsNullOrEmpty(name)) Speech.Say(name);
            }
            GraphNavigator.Attach(cur);
        }

        /// <summary>The mod switched off: unfocus and pop every screen (so a held key such as the end-turn coin
        /// is let go) and give the game its keys back. The next <see cref="Tick"/> rebuilds the stack from what
        /// the game shows.</summary>
        public static void Suspend()
        {
            if (_focused != null) Safe(_focused.OnUnfocus, _focused, "OnUnfocus");
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var gone = _stack[i];
                Safe(gone.OnPop, gone, "OnPop");
                GraphNavigator.ScreenClosed(gone);
            }
            _stack = new List<Screen>();
            _covered.Clear();
            _focused = null;
            GraphNavigator.Reset();
        }

        /// <summary>Forget the stack (module dispose). No hooks run: the game is unaffected by our screens.</summary>
        public static void Reset()
        {
            _stack.Clear();
            _covered.Clear();
            _names.Clear();
            _focused = null;
            _registered.Clear();
        }

        public static string Describe()
        {
            var parts = new List<string>();
            foreach (var s in _stack) parts.Add(s.Key + "@" + s.Layer);
            return parts.Count == 0 ? "none" : string.Join(" < ", parts);
        }

        private static bool SafeIsActive(Screen s)
        {
            try { return s.IsActive(); }
            catch (Exception e)
            {
                LogOnce(s, "IsActive", e);
                return false;
            }
        }

        private static void Safe(Action a, Screen s, string hook)
        {
            try { a(); }
            catch (Exception e) { LogOnce(s, hook, e); }
        }

        // A hook that throws usually throws every frame until the game state settles: log each (screen, hook,
        // exception type) once.
        private static readonly HashSet<string> _logged = new HashSet<string>();

        internal static void LogOnce(Screen s, string hook, Exception e)
        {
            if (_logged.Add(s?.Key + "|" + hook + "|" + e.GetType().Name))
                Plugin.LogError("[screens] " + s?.Key + "." + hook + " threw: " + e);
        }
    }
}
