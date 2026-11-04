using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The leaderboards (<see cref="UIMainMenuLeaderboardPanel"/>): a ranking table a page at a time, the region
    /// and Pro Rank filters, the player's own record pinned below, and the paging buttons (Top Players, Page Up,
    /// Page Down). The fallback read the table's cells by screen position. Tab stops, as drawn:
    ///
    /// - Filters: the region toggles, the shown one "selected".
    /// - The table: a row per player, read as its columns are headed: "1, Name, Title, Ranking Points 9876,
    ///   Level 40, Victories 10, Defeats 3, Draws 0".
    /// - Your record: the same, for the player's own row.
    /// - The bar: Top Players, Page Up, Page Down, Exit.
    ///
    /// Page Down and Page Up turn the page, Home goes to the top players, as the game's own keys say; each goes
    /// through the bar's own button (else the panel's input handler). When the new page has come, the cursor
    /// goes to its first row, which reads it.
    /// </summary>
    internal sealed class LeaderboardScreen : Screen
    {
        public override string Key => "leaderboard";
        public override int Layer => 1;
        public override bool Overlays => true;

        public override string ScreenName => GameText.Capitalized(GameText.Get("leaderboard_window_name", "Leaderboard"));

        public override bool IsActive() => Panel() != null;

        private static UIMainMenuLeaderboardPanel Panel()
        {
            var p = CrimsonNodes.ActivePanel<UIMainMenuLeaderboardPanel>();
            if (p == null) return null;
            var v = p.Visibility;
            return v == CrimsonUIBehaviour.EVisibility.Shown || v == CrimsonUIBehaviour.EVisibility.Showing ? p : null;
        }

        // The game parks its selection on the tenth row and moves it as the page fills.
        public override AControl GameSelection() => null;

        public override IReadOnlyList<ScreenKey> ExtraKeys => _keys ??= new List<ScreenKey>
        {
            new ScreenKey { Key = KeyCode.PageDown, Run = () => Turn(p => p.m_NextPage, p => p.HandleInputNextPage()),
                Hint = Loc.T("Page Down, next page") },
            new ScreenKey { Key = KeyCode.PageUp, Run = () => Turn(p => p.m_PreviousPage, p => p.HandleInputPreviousPage()),
                Hint = Loc.T("Page Up, previous page") },
            new ScreenKey { Key = KeyCode.Home, Ctrl = true, Run = () => Turn(p => p.m_TopPage, p => p.HandleInputTopPage()),
                Hint = Loc.T("Control+Home, {0}", GameText.Get("input_leaderboards_top_player", "Top Players")) },
        };
        private List<ScreenKey> _keys;

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();

            var filters = p.m_FilterView;
            if (CrimsonNodes.Shown(filters))
            {
                var toggles = CrimsonNodes.Leaves(filters, used);
                if (toggles.Count > 0)
                {
                    b.BeginStop("lb:filters");
                    foreach (var c in toggles)
                    {
                        // The regions are tabs: picking one shows its board.
                        var v = CrimsonNodes.WithRole(CrimsonNodes.Button(c), Loc.T("tab"));
                        var t = c.TryCast<ToggleButtonControl>();
                        if (t != null)
                            v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                            {
                                new NodeAnnouncement(() => CrimsonNodes.Toggled(t) ? Loc.T("selected") : null, live: true, kind: AnnouncementKinds.Selected),
                            };
                        b.AddItem(CrimsonNodes.Id(c), v);
                    }
                }
            }

            var table = p.m_RankTableView;
            b.BeginStop("lb:table");
            if (CrimsonNodes.Shown(p.m_LoadingSpinner)) b.AddLabel(ControlId.Structural("lb:loading"), () => Loc.T("Loading"));
            var rows = table?.m_Rows;
            int page = Page(p);
            for (int i = 0; rows != null && i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null || !row.gameObject.activeInHierarchy) continue;
                string text = Row(table, row);
                if (text.Length == 0) continue;
                if (row.ButtonControl != null) used.Add(row.ButtonControl.Pointer);
                // Keyed by page and place: a new page lands afresh.
                AddRow(b, ControlId.Structural("lb:row:" + page + ":" + i), row, () => Row(table, row));
            }

            var mine = p.m_CurrentPlayerRecordView?.m_RankPlayerRecordView;
            if (mine != null && mine.gameObject.activeInHierarchy && Row(table, mine).Length > 0)
            {
                if (mine.ButtonControl != null) used.Add(mine.ButtonControl.Pointer);
                b.BeginStop("lb:mine");
                AddRow(b, ControlId.Structural("lb:mine"), mine, () => Row(table, mine));
            }

            var bar = CrimsonNodes.BarButtons(p, used);
            if (bar.Count > 0)
            {
                b.BeginStop("lb:bar");
                IntPtr next = IntPtr.Zero, prev = IntPtr.Zero, top = IntPtr.Zero;
                try { next = p.m_NextPage?.Button?.Pointer ?? IntPtr.Zero; prev = p.m_PreviousPage?.Button?.Pointer ?? IntPtr.Zero; top = p.m_TopPage?.Button?.Pointer ?? IntPtr.Zero; } catch { }
                foreach (var c in bar)
                {
                    var v = CrimsonNodes.Button(c);
                    // The page buttons turn the table's page, as their keys do (a click paged the filters).
                    if (c.Pointer == next) v.OnActivate = () => Turn(x => x.m_NextPage, x => x.HandleInputNextPage());
                    else if (c.Pointer == prev) v.OnActivate = () => Turn(x => x.m_PreviousPage, x => x.HandleInputPreviousPage());
                    else if (c.Pointer == top) v.OnActivate = () => Turn(x => x.m_TopPage, x => x.HandleInputTopPage());
                    b.AddItem(CrimsonNodes.Id(c), v);
                }
            }
        }

        private static void AddRow(GraphBuilder b, ControlId id, RankPlayerRecordView row, Func<string> text)
        {
            b.AddItem(id, new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(text, kind: AnnouncementKinds.Label) },
                OnTooltip = () => Speech.Say(text(), true),
            });
        }

        // A row as its columns are headed: "1, Name, Title, Ranking Points 9876, Level 40, Victories 10, …".
        private static string Row(RankTableView table, RankPlayerRecordView r)
        {
            var parts = new List<string>();
            try
            {
                parts.Add(Drawn(r.m_PositionLabel));
                parts.Add(Drawn(r.m_NameLabel));
                parts.Add(Drawn(r.m_TitleLabel));
                parts.Add(Cell(table?.m_MMRLabel, "leaderboard_table_mmr", "Ranking Points", r.m_MMRLabel));
                parts.Add(Cell(table?.m_LVLLabel, "leaderboard_table_level", "Level", r.m_LevelLabel));
                parts.Add(Cell(table?.m_WinLabel, "leaderboard_table_victories", "Victories", r.m_VictoriesLabel));
                parts.Add(Cell(table?.m_LossLabel, "leaderboard_table_defeats", "Defeats", r.m_DefeatsLabel));
                parts.Add(Cell(table?.m_DrawLabel, "leaderboard_table_draws", "Draws", r.m_DrawsLabel));
                if (CrimsonNodes.Shown(r.m_NotInProRankText)) parts.AddRange(CrimsonNodes.Texts(r.m_NotInProRankText.transform));
            }
            catch (Exception e) { Plugin.LogInfo("[leaderboard] row read failed: " + e.Message); }
            return UiText.Join(parts.ToArray());
        }

        // A cell named by its column's header ("Victories 10"), the game's word when the header isn't drawn.
        private static string Cell(Label header, string key, string fallback, TMPro.TMP_Text value)
        {
            string v = Drawn(value);
            if (v == null) return null;
            string name = header != null ? CrimsonNodes.Text(header) : "";
            if (name.Length == 0) name = GameText.Get(key, fallback);
            return GameText.Capitalized(name) + " " + v;
        }

        private static string Drawn(TMPro.TMP_Text t)
        {
            try
            {
                if (t == null || !t.isActiveAndEnabled) return null;
                string s = UiText.Clean(t.text);
                return s.Length > 0 ? s : null;
            }
            catch { return null; }
        }

        private static int Page(UIMainMenuLeaderboardPanel p)
        {
            try { return p.m_RankTableView?.m_CurrentPage?.PageIndex ?? -1; } catch { return -1; }
        }

        // ---- paging ----

        // The page the player turned from, until the new one is shown.
        private static int _turnedFrom = int.MinValue;

        private static void Turn(Func<UIMainMenuLeaderboardPanel, InputButtonContainer.PairBinding> binding, Action<UIMainMenuLeaderboardPanel> handler)
        {
            var p = Panel();
            if (p == null) return;
            try
            {
                var button = binding(p)?.Button;
                if (button != null && !CrimsonNodes.Shown(button))
                {
                    // The game hides Page Up on the first page and Page Down on the last.
                    Speech.Say(Loc.T("No more pages."), true);
                    return;
                }
                _turnedFrom = Page(p);
                // The panel's own page handler, as its page key calls it. Clicking the Page Down button acted on
                // the list holding the game's selection, the region filters, and picked Pro Rank instead.
                handler(p);
            }
            catch (Exception e) { Plugin.LogInfo("[leaderboard] page turn failed: " + e.Message); }
        }

        public override void Tick()
        {
            if (_turnedFrom == int.MinValue) return;
            var p = Panel();
            if (p == null) { _turnedFrom = int.MinValue; return; }
            int page = Page(p);
            if (page == _turnedFrom || CrimsonNodes.Shown(p.m_LoadingSpinner)) return;
            var rows = p.m_RankTableView?.m_Rows;
            for (int i = 0; rows != null && i < rows.Length; i++)
            {
                if (rows[i] == null || !rows[i].gameObject.activeInHierarchy) continue;
                if (!GraphNavigator.FocusNode(ControlId.Structural("lb:row:" + page + ":" + i))) return; // not built yet
                break;
            }
            _turnedFrom = int.MinValue;
        }

        public override void OnPop() { _turnedFrom = int.MinValue; }
    }
}
