using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The match's narration kept for review (Accessible Arena's game log on O, Hearthstone Access's history):
    /// every line <see cref="Narrator"/> spoke this match — the opponent's plays, effects, turns, passes, round
    /// ends — so a line cut off by a keypress isn't lost. Gwent shows its own history window to sighted players,
    /// so this tells nothing they can't look up.
    ///
    /// O on the battle screen opens it as a list, newest first; O, Escape or Backspace closes it and the board
    /// gets its cursor back. Mod state like the help: <see cref="IsActive"/> is the open flag (while a match runs).
    /// </summary>
    internal sealed class LogScreen : Screen
    {
        public override string Key => "log";
        public override int Layer => 5;
        public override bool ReturnsToOpener => false;

        private static readonly List<string> _lines = new List<string>();
        private static System.IntPtr _match;
        private static bool _open;

        /// <summary>From <see cref="Narrator.Say"/>: keep a spoken line (a new match starts a new log; a line
        /// repeated straight after itself is kept once).</summary>
        public static void Add(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                var gc = Battle.Controller();
                var ptr = gc != null ? gc.Pointer : System.IntPtr.Zero;
                if (ptr != System.IntPtr.Zero && ptr != _match) { _match = ptr; _lines.Clear(); }
            }
            catch { }
            if (_lines.Count > 0 && _lines[_lines.Count - 1] == text) return;
            _lines.Add(text);
            if (_lines.Count > 500) _lines.RemoveAt(0);
        }

        public static void Toggle()
        {
            if (_open) { Close(); return; }
            _open = true;
            Plugin.LogInfo("[log] open, " + _lines.Count + " lines");
        }

        private static void Close()
        {
            if (!_open) return;
            _open = false;
            Speech.Say(Loc.T("Log closed."), true);
        }

        public override bool IsActive()
        {
            if (_open && Battle.Controller() == null) _open = false; // the match ended under it
            return _open;
        }

        public override void OnPop() => _open = false;

        public override string ScreenName => _lines.Count == 0 ? Loc.T("Battle log, nothing yet.") : Loc.P(_lines.Count, "Battle log, {0} line, newest first.", "Battle log, {0} lines, newest first.");

        public override void Build(GraphBuilder b)
        {
            if (!_open) return;
            b.BeginStop("log");
            if (_lines.Count == 0)
            {
                b.AddLabel(ControlId.Structural("log:empty"), () => Loc.T("Nothing yet"));
                return;
            }
            // Newest first; keyed from the oldest so a line keeps its id as new ones arrive.
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                string t = _lines[i];
                // Drop the line's closing stop, so the position after it reads "…opponent 6, 1 of 4".
                string shown = t.EndsWith(".") ? t.Substring(0, t.Length - 1) : t;
                b.AddItem(ControlId.Structural("log:" + i), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => shown, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(t, true),
                });
            }
        }

        public override AControl GameSelection() => null;
        public override void OnCursorMoved(GraphNode node) { }

        private List<ScreenKey> _keys;
        public override IReadOnlyList<ScreenKey> ExtraKeys => _keys ??= new List<ScreenKey>
        {
            new ScreenKey { Key = KeyCode.O, Run = Close, Hint = Loc.T("O, close the log") },
            new ScreenKey { Key = KeyCode.Escape, Run = Close, Hint = Loc.T("Escape, close the log") },
            new ScreenKey { Key = KeyCode.Backspace, Run = Close, Hint = Loc.T("Backspace, close the log") },
        };
    }
}
