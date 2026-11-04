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

        // Whether the player pressed Send Good Game on this screen, so the button greying out is said as "sent".
        private bool _goodGameSent;

        public override void OnPush() { _bannerCount = 0; _goodGameSent = false; }

        private static bool Enabled(AControl c)
        {
            try { return c.IsEnabled; } catch { return true; }
        }

        // The results panel, when the input layer that has the input holds it.
        private static UIBattleResultsPanel Panel() => CrimsonNodes.ActivePanel<UIBattleResultsPanel>();

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
                // The Journey banner opens its quest list under the bar once it has filled: each quest is its own
                // item, read as the Journey side window reads it ("..., progress 4 of 10, Reward: 20 ..."), so a
                // quest's counter isn't glued onto the banner and taken for a list position.
                var quests = Quests(banner);
                for (int j = 0; j < quests.Count; j++)
                {
                    var quest = quests[j];
                    b.AddItem(last = ControlId.Structural("results:reward:" + i + ":quest:" + j), new NodeVtable
                    {
                        Announcements = new[] { new NodeAnnouncement(() => SideWindowScreen.JourneyQuestText(quest), kind: AnnouncementKinds.Label) },
                        OnTooltip = () => Speech.Say(SideWindowScreen.JourneyQuestText(quest), true),
                    });
                }
            }

            foreach (var info in new[] { panel.m_P1Info, panel.m_P2Info })
            {
                var button = SafeVanity(info);
                if (button == null) continue;
                var widget = info;
                // The second panel is the opponent's, as its place on screen shows; an AI opponent's panel draws
                // no name, only its title, which alone didn't say whose it was.
                bool opponent = info == panel.m_P2Info;
                System.Func<string> text = () => opponent
                    ? GameText.Capitalized(GameText.Get("input_go_first_opponent", "Opponent")) + ", " + PlayerText(widget)
                    : PlayerText(widget);
                if (CrimsonNodes.Selectable(button))
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
            // off. After a send the game disables it (greys it out): that state change, right after Enter, is said
            // as "sent", since it is the send going through; a button the game shows disabled before any send
            // (the opponent left) reads "unavailable" on landing, as any disabled button does.
            var gg = panel.m_GoodGameButtonControl;
            if (gg != null && CrimsonNodes.Shown(gg) && GoodGameOn(panel))
            {
                System.Func<string> label = () => GameText.Get("input_send_good_game", "Good game");
                b.AddItem(last = CrimsonNodes.Id(gg), new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => Enabled(gg) ? null : _goodGameSent ? Loc.T("sent") : Loc.T("unavailable"),
                            live: true, kind: AnnouncementKinds.Enabled),
                    },
                    OnActivate = () =>
                    {
                        if (!Enabled(gg)) { Speech.Say(Loc.T("Not available right now."), true); return; }
                        _goodGameSent = true;
                        Navigator.ClickControl(gg);
                    },
                    OnTooltip = () => Speech.Say(label(), true),
                });
            }

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
                // A banner leads with its big number (rank, level) before its title: read the title first, then
                // the number named as what it is ("Ranked, Rank 30", "Experience, Level 6, 173 of 225 XP",
                // "Journey, Level 3, 8 of 24, 9 bonus left"). The quest list is read as items of its own.
                var parts = BannerTexts(banner);
                if (parts.Count > 1 && int.TryParse(parts[0], out _))
                {
                    string number = parts[0];
                    parts.RemoveAt(0);
                    string word = NumberWord(banner);
                    if (word != null) parts.Insert(1, word + " " + number);
                    else parts[0] = parts[0] + " " + number;
                }
                return parts.Count > 0 ? string.Join(", ", parts) : null;
            }
            catch { return null; }
        }

        // What a banner's big number is: the rank on the ranked banner, the level on the experience and Journey
        // banners; null for the others (daily, arena), read as before ("title number").
        private static string NumberWord(UIBattleResultsRewardBanner banner)
        {
            switch (banner.m_CurrentMode)
            {
                case UIBattleResultsRewardBanner.BannerMode.Rank: return GameText.Get("outcome_rank", "Rank");
                case UIBattleResultsRewardBanner.BannerMode.Experience:
                case UIBattleResultsRewardBanner.BannerMode.Journey: return GameText.Get("player_profile_level", "Level");
                default: return null;
            }
        }

        // A banner's shown texts in order, each once, less those of its quest list.
        private static List<string> BannerTexts(UIBattleResultsRewardBanner banner)
        {
            var parts = new List<string>();
            var skip = new List<UnityEngine.Transform>();
            try { if (banner.m_QuestListRoot != null) skip.Add(banner.m_QuestListRoot); } catch { }
            try { if (banner.m_ScrollArea != null) skip.Add(banner.m_ScrollArea.transform); } catch { }
            try { if (banner.m_QuestList != null) skip.Add(banner.m_QuestList.transform); } catch { }
            foreach (var q in Quests(banner)) skip.Add(q.transform);
            foreach (var t in banner.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                bool inQuests = false;
                foreach (var root in skip) if (t.transform.IsChildOf(root)) { inQuests = true; break; }
                if (inQuests) continue;
                string s = UiText.Clean(t.text);
                if (s.Length > 0 && !s.StartsWith("[MISSING]") && !UiText.IsPlaceholder(s) && !parts.Contains(s)) parts.Add(s);
            }
            return parts;
        }

        // The Journey banner's shown quests, in list order (under its quest list root, wherever the game puts
        // the list's scroll area).
        private static List<GwentUnity.UIQuestJourneyListItem> Quests(UIBattleResultsRewardBanner banner)
        {
            var list = new List<GwentUnity.UIQuestJourneyListItem>();
            try
            {
                foreach (var q in banner.GetComponentsInChildren<GwentUnity.UIQuestJourneyListItem>(false)) list.Add(q);
            }
            catch { }
            return list;
        }

        // A player's card: their name and title (an AI opponent's name is blank, so only its title shows), then
        // its badges named as the Versus banner names them ("Current Rank 30"): the badge's bare number beside
        // the name read as if it were part of the title. The card's plain texts if it has no plate.
        private static string PlayerText(UIBattleResultsPlayerInfoWidget info)
        {
            try
            {
                if (VersusScreen.Plate(info.m_BasePlayerInfoInstance) is string plate) return plate;
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
                return button != null && CrimsonNodes.Shown(button) ? button : null;
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
