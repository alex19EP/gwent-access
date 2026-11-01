using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// F1: the mod's help, a list to arrow through, laid out like Accessible Arena's help menu (the player
    /// asked for the card-game mods' pattern): a short line on opening, then sections read as context —
    /// the keys of the screen it was opened over (each screen's own <see cref="ScreenKey.Hint"/>s), the
    /// navigation keys every mod screen shares, and the keys that work anywhere. F1, Escape or Backspace closes
    /// it and the screen underneath gets its cursor back where it was.
    ///
    /// Mod state, not game state: <see cref="IsActive"/> is the open flag, the one screen allowed one, since
    /// nothing in the game shows it. It sits above every other layer, so it also covers the older hotkey
    /// screens (which then stand down while it is open, as for any mod screen).
    /// </summary>
    internal sealed class HelpScreen : Screen
    {
        public override string Key => "help";
        public override int Layer => 10;
        public override bool ReturnsToOpener => false;

        private static bool _open;
        private static Screen _covered;

        public override bool IsActive() => _open;

        /// <summary>F1 (from the hotkey thread, on the game thread): open over the current screen, or close.</summary>
        public static void Toggle()
        {
            if (_open) { Close(); return; }
            var top = ScreenManager.Current;
            _covered = top is HelpScreen ? null : top;
            _open = true;
            Plugin.LogInfo("[help] open over " + (_covered?.Key ?? "the game"));
        }

        private static void Close()
        {
            if (!_open) return;
            _open = false;
            _covered = null;
            Speech.Say("Help closed.", true);
        }

        public override string ScreenName => "Help. Up and Down to move, F1 or Escape to close.";

        public override void OnPop() { _open = false; _covered = null; }

        public override void Build(GraphBuilder b)
        {
            // Closed by a key this frame: draw nothing, or the navigator re-reads the item it was on.
            if (!_open) return;
            int n = 0;
            var covered = _covered;
            b.BeginStop("help");

            // The screen it was opened over: its own keys, as it describes them.
            var own = new List<string>();
            if (covered != null)
                foreach (var k in covered.ExtraKeys)
                    if (!string.IsNullOrEmpty(k.Hint) && !own.Contains(k.Hint)) own.Add(k.Hint);
            if (own.Count > 0)
            {
                b.PushContext(Title(covered) + " keys");
                foreach (var h in own) Line(b, ref n, h);
                b.PopContext();
            }

            if (covered != null)
            {
                b.PushContext("Moving around");
                Line(b, ref n, "Up and Down arrows, previous or next item");
                Line(b, ref n, "Left and Right arrows, along a row, or change a setting");
                Line(b, ref n, "Tab and Shift Tab, next or previous section");
                Line(b, ref n, "Home and End, first or last item");
                Line(b, ref n, "Enter, use the item");
                Line(b, ref n, "Space, details, or read it again");
                Line(b, ref n, "Backspace, cancel or go back where the screen allows it");
                Line(b, ref n, "Escape, the game's own Back or menu");
                b.PopContext();
            }
            else
            {
                // Over a screen no mod screen handles yet: the older hotkeys are the way around it.
                b.PushContext("This screen");
                Line(b, ref n, "F2 and F3, previous or next item");
                Line(b, ref n, "Shift F2 and Shift F3, previous or next section");
                Line(b, ref n, "F4, click the item");
                b.PopContext();
            }

            b.PushContext("Anywhere");
            Line(b, ref n, "F1, open or close this help");
            Line(b, ref n, "F7, repeat the last thing spoken");
            Line(b, ref n, "Shift F7, repeat the last popup or tutorial message; again for older ones");
            Line(b, ref n, "F9, read the whole screen");
            Line(b, ref n, "F6, the game's own key hints for this screen");
            Line(b, ref n, "F5, switch controller mode by hand. It follows your controller on its own");
            Line(b, ref n, "Shift F1, card details on the older screens");
            Line(b, ref n, "F8, accessibility on or off, so a sighted helper can play. Off, Gwent Access is silent and gives the game every key back");
            b.PopContext();
        }

        private static void Line(GraphBuilder b, ref int n, string text)
        {
            var id = ControlId.Structural("help:" + n++);
            string t = text;
            b.AddItem(id, new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(() => t, kind: AnnouncementKinds.Label) },
                OnTooltip = () => Speech.Say(t, true),
            });
        }

        // "battle" → "Battle", "main-menu" → "Main menu".
        private static string Title(Screen s)
        {
            string k = (s.Key ?? "screen").Replace('-', ' ');
            return k.Length == 0 ? "Screen" : char.ToUpperInvariant(k[0]) + k.Substring(1);
        }

        public override AControl GameSelection() => null;
        public override void OnCursorMoved(GraphNode node) { }

        // Escape is claimed while help is up, so it closes help instead of reaching the game's Back.
        private List<ScreenKey> _keys;
        public override IReadOnlyList<ScreenKey> ExtraKeys => _keys ??= new List<ScreenKey>
        {
            new ScreenKey { Key = KeyCode.Escape, Run = Close, Hint = "Escape, close help" },
            new ScreenKey { Key = KeyCode.Backspace, Run = Close, Hint = "Backspace, close help" },
        };
    }
}
