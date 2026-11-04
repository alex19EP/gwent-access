using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentUnity.MainMenu;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The search for an opponent after PLAY: the main menu's card stack switches to its matchmaking look, with
    /// the message ("Searching for a worthy opponent", later "Opponent found!"), a running timer, and the bar's
    /// Cancel. Before, Enter on PLAY landed the cursor on whatever the game selected (the currencies) and nothing
    /// said the search was on. While the stack is matchmaking this screen takes the keyboard:
    ///
    /// - The status: the message the stack shows, with its description; when it changes, the new one is read.
    /// - Cancel ("Stop searching"): the bar's own button, clicked the mouse's way. Backspace does the same.
    /// - T reads the timer as drawn.
    /// </summary>
    internal sealed class MatchmakingScreen : Screen
    {
        public override string Key => "matchmaking";
        public override int Layer => 1;

        public override bool IsActive() => Stack() != null;

        /// <summary>The main menu's card stack while it is in its matchmaking state.</summary>
        private static UIMainMenuCardStackPanel Stack()
        {
            try
            {
                var s = CrimsonNodes.ActivePanel<UIMainMenuContentPanel>()?.m_StackPanel;
                return CrimsonNodes.Shown(s) && s.IsMatchmakingState() ? s : null;
            }
            catch { return null; }
        }

        // The game moves its selection around the menu during the search; the cursor stays on the status.
        public override AControl GameSelection() => null;

        public override bool Back() => Cancel();

        public override IReadOnlyList<ScreenKey> ExtraKeys => _keys ??= new List<ScreenKey>
        {
            new ScreenKey { Key = KeyCode.T, Run = () => Speech.Say(Timer() ?? Loc.T("No timer shown."), true), Hint = Loc.T("T, time searching") },
        };
        private List<ScreenKey> _keys;

        public override void Build(GraphBuilder b)
        {
            var s = Stack();
            if (s == null) return;
            b.BeginStop("matchmaking");
            string status = Status(s);
            // Keyed by its words: a new message ("Opponent found!") lands afresh and is read.
            var id = ControlId.Structural("matchmaking:status:" + status);
            b.SetStart(id);
            b.AddItem(id, new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(() => status, kind: AnnouncementKinds.Label) },
                OnTooltip = () => Speech.Say(UiText.Join(status, Timer()), true),
            });
            var cancel = CancelButton(s);
            if (cancel != null)
            {
                var v = CrimsonNodes.Button(cancel, () => CrimsonNodes.Label(cancel) is string l && l.Length > 0 && !l.StartsWith("unreadable")
                    ? l : GameText.Get("cancel_arena_matchmaking", "Stop Searching"));
                v.OnActivate = () => Cancel();
                b.AddItem(CrimsonNodes.Id(cancel), v);
            }
        }

        // The stack's message and description; the game's "Searching for a worthy opponent" until it draws one.
        private static string Status(UIMainMenuCardStackPanel s)
        {
            string text = UiText.Join(CrimsonNodes.Text(s.m_MessageTitle), CrimsonNodes.Text(s.m_MessageDescription));
            return text.Length > 0 ? text : GameText.Get("panel_matchmaking_searching", "Searching for an opponent");
        }

        // The timer beside the message, as drawn ("00:42").
        private static string Timer()
        {
            var s = Stack();
            var t = s?.m_TimeoutLabel;
            if (t == null || !CrimsonNodes.Shown(t)) return null;
            string text = CrimsonNodes.Text(t.m_TextLabel);
            return text.Length > 0 ? text : null;
        }

        // The bar's Back while the stack is matchmaking is its cancel: the captioned button, else its icon twin.
        private static ButtonControl CancelButton(UIMainMenuCardStackPanel s)
        {
            try
            {
                var binding = s.m_BackButton;
                if (binding == null) return null;
                if (CrimsonNodes.Shown(binding.Button)) return binding.Button;
                return CrimsonNodes.Shown(binding.IconButton) ? binding.IconButton : null;
            }
            catch { return null; }
        }

        // The game's own cancel: the bar's button the mouse's way, else the stack's Back handler, as the key does.
        private static bool Cancel()
        {
            var s = Stack();
            if (s == null) return false;
            var button = CancelButton(s);
            if (button != null) { Navigator.ClickControl(button); return true; }
            try { s.HandleBack(); return true; }
            catch (System.Exception e) { Plugin.LogInfo("[matchmaking] cancel failed: " + e.Message); return false; }
        }
    }
}
