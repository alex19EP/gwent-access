using System.Collections.Generic;
using Access.Core.Graph;
using GwentUnity;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The Versus banner before a match (<see cref="IntroPanel"/>): the two players' plates, then the coin toss's
    /// "who goes first". The fallback read it top to bottom, so the opponent's plate (at the top) came first and
    /// a title drawn on both plates ("CARDSMITH, blindndanger, CARDSMITH") sounded like one plate read twice.
    /// This reads the plates as plates: the player's own first (name, title, then its badges: level, rank),
    /// then "Opponent:" and the opponent's, each part said once. The banner is up only for seconds and grows
    /// while it is (the plates fade in, the toss result follows): it is one item keyed by its words, and when
    /// they grow only the new part is said; Space reads it all.
    /// </summary>
    internal sealed class VersusScreen : Screen
    {
        public override string Key => "versus";
        public override int Layer => 1;
        public override bool NamedOnReturn => true;

        public override string ScreenName => Loc.T("Versus");

        public override bool IsActive() => Panel() != null;

        private static IntroPanel Panel()
        {
            var p = CrimsonNodes.ActivePanel<IntroPanel>();
            if (p == null) return null;
            var v = p.Visibility;
            return v == CrimsonUI.CrimsonUIBehaviour.EVisibility.Shown || v == CrimsonUI.CrimsonUIBehaviour.EVisibility.Showing ? p : null;
        }

        // What was last said, part by part (the player, the opponent, who goes first): a part that changes is
        // said again, the rest not.
        private List<string> _said = new List<string>();
        private string _message, _fresh;

        public override void OnPush() { _said = new List<string>(); _message = _fresh = null; }

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var parts = new List<string>();
            if (Plate(p.m_BotBasePlayerInfoInstance) is string mine) parts.Add(mine);
            if (Plate(p.m_TopBasePlayerInfoInstance) is string theirs)
                parts.Add(GameText.Capitalized(GameText.Get("input_go_first_opponent", "Opponent")) + ": " + theirs);
            if (Drawn(p.m_WhoStartsText) is string who) parts.Add(who);
            if (parts.Count == 0) return;

            string message = string.Join(". ", parts);
            if (message != _message)
            {
                var fresh = parts.FindAll(x => !_said.Contains(x));
                _fresh = fresh.Count > 0 && fresh.Count < parts.Count ? string.Join(". ", fresh) : message;
                _message = message;
                _said = parts;
            }
            string said = _fresh;
            b.BeginStop("versus");
            b.AddItem(ControlId.Structural("versus:" + message), new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(() => said, kind: AnnouncementKinds.Label) },
                OnTooltip = () => Speech.Say(message, true),
            });
        }

        /// <summary>A player's plate as drawn: name, title, then the badges beside them, each named as the
        /// main menu's profile banner names it ("Rank, 30").</summary>
        internal static string Plate(UIBasePlayerInfo info)
        {
            if (!CrimsonNodes.Shown(info)) return null;
            var parts = new List<string>();
            void Add(string s) { if (!string.IsNullOrEmpty(s) && !parts.Contains(s)) parts.Add(s); }
            try
            {
                Add(Drawn(info.m_NameLabel?.Text));
                Add(Drawn(info.m_TitleLabel?.Text));
                Add(Badge(info.m_LevelInfoInstance, GameText.Get("player_profile_level", "Level")));
                Add(Badge(info.m_RankedInfoInstance, GameText.Get("interface_profile_current_rank", "Rank")));
            }
            catch { }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        private static string Badge(Component badge, string name)
        {
            if (!CrimsonNodes.Shown(badge)) return null;
            var texts = CrimsonNodes.Texts(badge);
            return texts.Count > 0 ? name + " " + string.Join(", ", texts) : null;
        }

        // A text as a sighted player sees it: shown and not faded out; null otherwise.
        private static string Drawn(TMPro.TMP_Text t)
        {
            try
            {
                if (t == null || !t.isActiveAndEnabled || PanelScreen.Faded(t)) return null;
                string s = UiText.Clean(t.text);
                return s.Length > 0 && !UiText.IsPlaceholder(s) ? s : null;
            }
            catch { return null; }
        }
    }
}
