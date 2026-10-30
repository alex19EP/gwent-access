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
        private static Screen _focused;

        /// <summary>The screen the navigator is attached to, or null when no mod screen is up.</summary>
        public static Screen Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        public static void Register(Screen s) => _registered.Add(s);

        /// <summary>Would a mod screen own the UI if polled right now? Legacy narrators ask this instead of
        /// <see cref="GraphNavigator.Owns"/> when they run before this frame's poll — a popup closing hands the
        /// selection back to the menu one frame before the stack notices (spec §10.5, the rising edge).</summary>
        public static bool AnyActive()
        {
            foreach (var s in _registered)
                if (SafeIsActive(s)) return true;
            return false;
        }

        public static void Tick()
        {
            var desired = new List<Screen>();
            foreach (var s in _registered)
                if (SafeIsActive(s)) desired.Add(s);
            desired.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : _registered.IndexOf(a).CompareTo(_registered.IndexOf(b)));

            for (int i = _stack.Count - 1; i >= 0; i--)
                if (!desired.Contains(_stack[i]))
                {
                    var gone = _stack[i];
                    Safe(gone.OnPop, gone, "OnPop");
                    GraphNavigator.ScreenClosed(gone);
                }
            foreach (var s in desired)
                if (!_stack.Contains(s)) Safe(s.OnPush, s, "OnPush");
            _stack = desired;

            var cur = Current;
            if (ReferenceEquals(cur, _focused)) return;
            if (_focused != null) Safe(_focused.OnUnfocus, _focused, "OnUnfocus");
            _focused = cur;
            Plugin.LogInfo("[screens] focus: " + (cur?.Key ?? "none (game)"));
            if (cur != null)
            {
                Safe(cur.OnFocus, cur, "OnFocus");
                var name = cur.ScreenName;
                if (!string.IsNullOrEmpty(name)) Speech.Say(name);
            }
            GraphNavigator.Attach(cur);
        }

        /// <summary>Forget the stack (module dispose). No hooks run: the game is unaffected by our screens.</summary>
        public static void Reset()
        {
            _stack.Clear();
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
