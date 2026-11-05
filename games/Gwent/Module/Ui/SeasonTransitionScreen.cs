using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The season change (<see cref="UISeasonTransitionPanel"/>), shown once a month on the first visit of a
    /// new season: first the season that ended (the rank reached, the reward for it), then the season that
    /// starts (its title, the ranked score carried over); More info and Close in the bar. The fallback read
    /// it as loose texts while its numbers counted up. Here:
    ///
    /// - The title ("Season of the Bear ended"): its own item, keyed by the part shown, so when the panel moves
    ///   on to the new season the cursor goes to the new title, which reads it.
    /// - The part's texts, a line each, read live (the numbers count up as they are drawn), then the reward
    ///   in the game's words ("200 Reward Points").
    /// - The bar: More info, Close, Back.
    /// </summary>
    internal sealed class SeasonTransitionScreen : Screen
    {
        public override string Key => "season-transition";
        public override int Layer => 1;
        public override bool NamedOnReturn => true;

        public override bool IsActive() => Panel() != null;

        private static UISeasonTransitionPanel Panel()
        {
            var p = CrimsonNodes.ActivePanel<UISeasonTransitionPanel>();
            if (p == null) return null;
            var v = p.Visibility;
            return v == CrimsonUIBehaviour.EVisibility.Shown || v == CrimsonUIBehaviour.EVisibility.Showing ? p : null;
        }

        public override AControl GameSelection() => null;

        // Which part the panel shows: "end", "start", or "" before either.
        private static string Part(UISeasonTransitionPanel p, out AControl view)
        {
            view = null;
            try
            {
                if (CrimsonNodes.Shown(p.StartOfSeasonView)) { view = p.StartOfSeasonView; return "start"; }
                if (CrimsonNodes.Shown(p.EndOfSeasonView)) { view = p.EndOfSeasonView; return "end"; }
            }
            catch { }
            return "";
        }

        private static string Title(UISeasonTransitionPanel p)
        {
            var t = p.m_TitleContainer;
            if (!CrimsonNodes.Shown(t)) return "";
            return UiText.Join(CrimsonNodes.Text(t.m_TitleLabel), CrimsonNodes.Text(t.m_SeasonNameLabel));
        }

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();
            string part = Part(p, out var view);

            b.BeginStop("season:content");
            string title = Title(p);
            if (title.Length > 0) b.AddLabel(ControlId.Structural("season:title:" + part), () => Title(p));
            if (view != null)
            {
                var lines = CrimsonNodes.Texts(view);
                for (int i = 0; i < lines.Count; i++)
                {
                    int index = i;
                    var v = view;
                    b.AddLabel(ControlId.Structural("season:" + part + ":" + i), () =>
                    {
                        var now = CrimsonNodes.Texts(v);
                        return index < now.Count ? now[index] : null;
                    });
                }
                if (part == "end" && Reward(p.EndOfSeasonView) is string reward)
                    b.AddLabel(ControlId.Structural("season:reward"), () => GameText.Get("achievements_reward", "Reward:") + " " + reward);
                foreach (var c in CrimsonNodes.Leaves(view, used)) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }

            var bar = CrimsonNodes.BarButtons(p, used);
            if (bar.Count > 0)
            {
                b.BeginStop("season:bar");
                foreach (var c in bar) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        private static string Reward(UIEndOfSeason end)
        {
            try { return RewardText.Of(end?.m_Reward); } catch { return null; }
        }

        // The part last seen: when the panel moves on to the new season, the cursor goes to its title.
        private string _part;

        public override void Tick()
        {
            var p = Panel();
            if (p == null) return;
            string part = Part(p, out _);
            if (part == _part) return;
            if (_part != null && Title(p).Length > 0 && !GraphNavigator.FocusNode(ControlId.Structural("season:title:" + part))) return;
            _part = part;
        }

        public override void OnPop() => _part = null;
    }
}
