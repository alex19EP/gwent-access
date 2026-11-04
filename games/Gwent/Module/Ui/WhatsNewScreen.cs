using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentVisuals;
using GwentVisuals.WelcomeScreen;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// "What's new!" (<see cref="UIWelcomeScreenPanel"/>), the news window from the main menu: news tiles in a
    /// left and a right column, the game's version and season, and its buttons. The fallback read every text
    /// as a line of its own in screen order. Here each tile is one item, read as drawn (title, subtitle,
    /// description, the time left on it), the left column then the right, top to bottom; Enter clicks the
    /// tile as the mouse does (it opens what the tile advertises). Then the version and season, then the
    /// buttons. Backspace is the window's Back.
    /// </summary>
    internal sealed class WhatsNewScreen : Screen
    {
        public override string Key => "whats-new";
        public override int Layer => 1;
        public override bool Overlays => true;
        public override string ScreenName => GameText.Get("mainmenu_welcomescreen_button_tooltip", "What's new!");

        public override bool IsActive() => Panel() != null;

        private static UIWelcomeScreenPanel Panel()
        {
            var p = CrimsonNodes.ActivePanel<UIWelcomeScreenPanel>();
            if (p == null) return null;
            var v = p.Visibility;
            return v == CrimsonUIBehaviour.EVisibility.Shown || v == CrimsonUIBehaviour.EVisibility.Showing ? p : null;
        }

        public override AControl GameSelection() => null;

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();

            var tiles = new List<UIWelcomeScreenSectionControl>();
            foreach (var side in new[] { p.m_LeftPanels, p.m_RightPanels })
            {
                var root = side?.Root;
                if (!CrimsonNodes.Shown(root)) continue;
                var column = new List<UIWelcomeScreenSectionControl>(root.GetComponentsInChildren<UIWelcomeScreenSectionControl>(false));
                column.Sort((x, y) => y.transform.position.y.CompareTo(x.transform.position.y));
                tiles.AddRange(column);
            }
            if (tiles.Count > 0)
            {
                b.BeginStop("news:tiles");
                foreach (var t in tiles)
                {
                    var tile = t;
                    if (!used.Add(tile.Pointer)) continue;
                    var v = CrimsonNodes.Button(tile, () => Tile(tile));
                    v.OnActivate = () => Navigator.MouseClick(tile);
                    b.AddItem(CrimsonNodes.Id(tile), v);
                }
            }

            string version = CrimsonNodes.Text(p.m_VersionLabel), season = CrimsonNodes.Text(p.m_SeasonLabel);
            if (version.Length > 0 || season.Length > 0)
            {
                b.BeginStop("news:info");
                if (season.Length > 0) b.AddLabel(ControlId.Structural("news:season"), () => CrimsonNodes.Text(p.m_SeasonLabel));
                if (version.Length > 0) b.AddLabel(ControlId.Structural("news:version"), () => CrimsonNodes.Text(p.m_VersionLabel));
            }

            var buttons = CrimsonNodes.BarButtons(p, used);
            foreach (var c in CrimsonNodes.Leaves(p, used)) buttons.Add(c);
            if (buttons.Count > 0)
            {
                b.BeginStop("news:bar");
                foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        // A tile as drawn: its title, subtitle, description, then the time left on it.
        private static string Tile(UIWelcomeScreenSectionControl t)
        {
            var parts = new List<string>();
            try
            {
                parts.Add(CrimsonNodes.Text(t.m_Title));
                parts.Add(CrimsonNodes.Text(t.m_Subtitle));
                parts.Add(CrimsonNodes.Text(t.m_Description));
                var timer = t.m_Timer;
                if (timer != null && CrimsonNodes.Shown(timer)) parts.Add(CrimsonNodes.Text(timer.m_TimerText));
            }
            catch { }
            string text = UiText.Join(parts.ToArray());
            return text.Length > 0 ? text : CrimsonNodes.Label(t);
        }
    }
}
