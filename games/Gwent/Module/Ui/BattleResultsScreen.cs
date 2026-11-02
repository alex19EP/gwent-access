using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentVisuals;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The battle results after a match (GwentVisuals.UIBattleResultsPanel, on its own BattleResults input layer):
    /// the result banner ("Победа!" / "Поражение"), both players' score columns per round, the reward banners
    /// the game fills in after a ranked or seasonal match, both players' cards (name and title; the local
    /// player's opens their profile), the "good game" button in a match against a person, and the input bar
    /// ("ВЫХОД", and the journey button where the game shows it).
    ///
    /// The banner is the screen's name, so it is spoken first. One stop reads the rounds ("Раунд 1: 41 : 39",
    /// the local player's score first, as the bottom player's column is on the left), then the reward banners,
    /// the players and the good-game button; the input bar is its own stop. Enter on a player's card is the
    /// game's own (the game's selection follows the cursor there); on the other buttons the mod clicks the one
    /// under the cursor. Escape stays the game's Back.
    /// </summary>
    internal sealed class BattleResultsScreen : Screen
    {
        public override string Key => "battle-results";
        public override int Layer => 1;
        public override string ScreenName => Banner(Panel());

        public override bool IsActive() => Panel() != null;

        // How many reward banners the last render had, to tell when the first one arrives.
        private int _bannerCount;

        public override void OnPush() => _bannerCount = 0;

        // The results panel, when the input layer that has the input holds it.
        private static UIBattleResultsPanel Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var c = attached[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                var p = c.TryCast<UIBattleResultsPanel>();
                if (p != null) return p;
            }
            return null;
        }

        // The shown result banner's label: the win, loss or draw banner, whichever the game turned on.
        private static string Banner(UIBattleResultsPanel panel)
        {
            if (panel == null) return null;
            foreach (var go in new[] { panel.m_BannerWin, panel.m_BannerLoose, panel.m_BannerDraw })
            {
                try
                {
                    if (go == null || !go.activeInHierarchy) continue;
                    var parts = CrimsonNodes.Texts(go.transform);
                    if (parts.Count > 0) return parts[0];
                }
                catch { }
            }
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var panel = Panel();
            if (panel == null) return;

            ControlId last = null;
            b.BeginStop("results");
            var mine = Scores(panel.m_Player1Content);
            var theirs = Scores(panel.m_Player2Content);
            string round = GameText.Get("gwent_round", "Round");
            for (int i = 0; i < System.Math.Max(mine.Count, theirs.Count); i++)
            {
                string line = round + " " + (i + 1) + ": " + At(mine, i) + " : " + At(theirs, i);
                var id = ControlId.Structural("results:round:" + i);
                b.AddItem(last = id, new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => line, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(line, true),
                });
                if (i == 0) b.SetStart(id);
            }

            // The reward banners slide in after "Вперед": the cursor lands on the first as it arrives, so it is
            // read as a sighted player sees it come in.
            var banners = RewardBanners(panel);
            if (banners.Count > 0 && _bannerCount == 0) GraphNavigator.Suggest(ControlId.Structural("results:reward:0"));
            _bannerCount = banners.Count;
            for (int i = 0; i < banners.Count; i++)
            {
                var banner = banners[i];
                b.AddItem(last = ControlId.Structural("results:reward:" + i), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => BannerText(banner), kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(BannerText(banner) ?? "", true),
                });
            }

            foreach (var info in new[] { panel.m_P1Info, panel.m_P2Info })
            {
                var button = SafeVanity(info);
                if (button == null) continue;
                var widget = info;
                // The second panel is the opponent's, as its place on screen shows; an AI opponent's panel draws
                // no name, only its title, which alone didn't say whose it was.
                bool opponent = info == panel.m_P2Info;
                System.Func<string> text = () => opponent ? "Opponent, " + PlayerText(widget) : PlayerText(widget);
                if (Selectable(button))
                    b.AddItem(last = CrimsonNodes.Id(button), CrimsonNodes.Button(button, text));
                else
                    b.AddItem(last = CrimsonNodes.Id(button), new NodeVtable
                    {
                        Announcements = new[] { new NodeAnnouncement(text, kind: AnnouncementKinds.Label) },
                        OnTooltip = () => Speech.Say(text() ?? "", true),
                    });
            }

            // The game turns the good-game button on (m_GoodGameButton) only once it binds it: in a match against
            // a person after the server closes the game, or in the new-player flow's mock; in Practice it stays
            // off. After a send the game disables it, which the button's live "unavailable" reads.
            var gg = panel.m_GoodGameButtonControl;
            if (gg != null && Shown(gg) && GoodGameOn(panel))
                b.AddItem(last = CrimsonNodes.Id(gg), CrimsonNodes.Button(gg,
                    () => GameText.Get("input_send_good_game", "Good game")));

            b.BeginStop("results:buttons");
            ControlId firstButton = null;
            foreach (var c in BarButtons(panel))
            {
                var id = CrimsonNodes.Id(c);
                firstButton ??= id;
                b.AddItem(id, CrimsonNodes.Button(c));
            }
            // The bar (Exit, Continue) is its own Tab stop, and Down past the last result reaches it too, so the
            // list doesn't seem to end before the way out.
            if (last != null && firstButton != null)
            {
                b.Connect(last, GraphDir.Down, firstButton);
                b.Connect(firstButton, GraphDir.Up, last);
            }
        }

        // One player's round scores in round order (kept as shown; two rounds can have the same score).
        private static List<string> Scores(UIBattleResultsScoresContent content)
        {
            var list = new List<string>();
            try
            {
                if (content == null || !content.gameObject.activeInHierarchy) return list;
                foreach (var t in content.GetComponentsInChildren<TMPro.TMP_Text>(false))
                    if (t.name == "RoundScore")
                    {
                        string s = UiText.Clean(t.text);
                        if (s.Length > 0) list.Add(s);
                    }
            }
            catch { }
            return list;
        }

        private static string At(List<string> list, int i) => i < list.Count ? list[i] : "-";

        // The reward banners the game spawned under its banner row (ranked, daily, level and reward progress).
        private static List<UIBattleResultsRewardBanner> RewardBanners(UIBattleResultsPanel panel)
        {
            var list = new List<UIBattleResultsRewardBanner>();
            try
            {
                var root = panel.m_RewardsBannerRoot;
                if (root == null) return list;
                foreach (var banner in root.GetComponentsInChildren<UIBattleResultsRewardBanner>(false))
                    if (banner.gameObject.activeInHierarchy && CrimsonNodes.Texts(banner).Count > 0) list.Add(banner);
            }
            catch { }
            return list;
        }

        private static string BannerText(UIBattleResultsRewardBanner banner)
        {
            try
            {
                // A banner leads with its big number (rank, level) before its title: read the title first
                // ("Рейтинг 30", "Опыт 3, 46 / 150 ОПЫТ").
                var parts = CrimsonNodes.Texts(banner);
                if (parts.Count > 1 && int.TryParse(parts[0], out _))
                {
                    string number = parts[0];
                    parts.RemoveAt(0);
                    parts[0] = parts[0] + " " + number;
                }
                return parts.Count > 0 ? string.Join(", ", parts) : null;
            }
            catch { return null; }
        }

        // A player's card: their name and title (an AI opponent's name is blank, so only its title shows).
        private static string PlayerText(UIBattleResultsPlayerInfoWidget info)
        {
            try
            {
                var parts = CrimsonNodes.Texts(info);
                return parts.Count > 0 ? string.Join(", ", parts) : null;
            }
            catch { return null; }
        }

        private static ButtonControl SafeVanity(UIBattleResultsPlayerInfoWidget info)
        {
            try
            {
                var button = info?.m_VanityButton;
                return button != null && Shown(button) ? button : null;
            }
            catch { return null; }
        }

        // The bar's captioned buttons: never selectable (the game reaches them by key), clicked through the
        // container's own callback.
        private static List<AControl> BarButtons(UIBattleResultsPanel panel)
        {
            return CrimsonNodes.BarButtons(panel);
        }

        private static bool GoodGameOn(UIBattleResultsPanel panel)
        {
            try { return panel.m_GoodGameButton != null && panel.m_GoodGameButton.activeInHierarchy; }
            catch { return false; }
        }

        // The local player's card opens their profile; an AI opponent's card is disabled, a picture only.
        private static bool Selectable(AControl c)
        {
            try { return c.IsCurrentlySelectable(); } catch { return false; }
        }

        private static bool Shown(UnityEngine.Component c)
        {
            try { return c != null && c.gameObject.activeInHierarchy; } catch { return false; }
        }

        /// <summary>The cursor starts on the rounds, not on the player card the game selects.</summary>
        public override AControl GameSelection() => null;

        /// <summary>Enter is the game's own on a player's card (the game's selection follows the cursor there);
        /// on the other buttons the mod clicks the one under the cursor. Escape is always the game's Back.</summary>
        public override bool LeavesToGame(UnityEngine.KeyCode key)
        {
            if (key != UnityEngine.KeyCode.Return && key != UnityEngine.KeyCode.KeypadEnter) return false;
            try { return GraphNavigator.CurrentNode?.Id.Reference is AControl c && c.IsCurrentlySelectable(); }
            catch { return false; }
        }
    }
}
