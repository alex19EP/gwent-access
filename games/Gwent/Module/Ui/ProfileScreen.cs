using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity.UI.PlayerProfile;
using Il2CppInterop.Runtime;
using Modules.Achievements.Entities;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The player profile, as on screen:
    ///
    /// The tab bar (summary, cosmetics, contracts, ranked score); the banner (name, title, the stats beside
    /// it — highest rank, cards, cycle level, thanks — each read as its hover tooltip names it, then its
    /// number); then the open tab's controls, grouped by the rows and columns they are laid out in, each read
    /// by its words or, for an icon, by its tooltip and the numbers drawn beside it; the overview texts no
    /// control carries (contract progress, per-category progress, all wins); then the contract lists.
    ///
    /// A contract list (the contracts tab's recent list and each category) draws only the cards in view, so
    /// its stop walks the list's data instead: every contract, read as the card shows it — title, whether it
    /// is done (and when), ready to claim (with what) or how far along, then its description and reward.
    /// Landing scrolls the game's list to the card and selects it, as the game does for a gamepad; Enter
    /// claims a contract ready to claim, else clicks the card; T presses the card's track button. Both go
    /// through the mouse path, so the card's sounds play.
    ///
    /// Active while the profile's input layer has the input; the pickers and popups it opens take the input
    /// and step in above it. Escape is the game's Back, which closes the window.
    /// </summary>
    internal sealed class ProfileScreen : Screen
    {
        public override string Key => "profile";
        public override int Layer => 1;
        public override string ScreenName => Loc.T("Profile");

        // Active once the tabs are drawn (or after three seconds, for a window without them): the window's first
        // frames hold only its Exit/Back bar, and a cursor placed then stayed on Exit instead of the open tab.
        public override bool IsActive()
        {
            var p = Panel();
            if (p == null) { _shownAt = -1; return false; }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_shownAt < 0) _shownAt = now;
            bool tabs = false;
            try
            {
                var menu = p.m_ProfileTabContainer?.m_Menu;
                tabs = menu != null && menu.gameObject.activeInHierarchy && menu.GetComponentsInChildren<UIProfileMenuItem>(false).Length > 0;
            }
            catch { }
            return tabs || now - _shownAt > 3f;
        }

        private static float _shownAt = -1;

        private static UIPlayerProfilePanel _panel;

        private static UIPlayerProfilePanel Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null || layer.Priority != UIPlayerProfilePanel.PROFILE_INPUT_LAYER) return null;
            try
            {
                if (_panel == null || !_panel.gameObject.activeInHierarchy)
                    _panel = UnityEngine.Object.FindObjectOfType<UIPlayerProfilePanel>();
                return _panel != null && _panel.gameObject.activeInHierarchy ? _panel : null;
            }
            catch { _panel = null; return null; }
        }

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();
            AddTabs(b, p, used);
            AddBanner(b, p, used);
            var view = ShownView(p);
            if (view != null) AddView(b, view, used);
            AddWindowButtons(b, p, used);
        }

        // ---- the tab bar ----

        // The tabs are toggles a mouse clicks and a gamepad switches with its shoulder buttons; they are never
        // the layer's selection.
        private static void AddTabs(GraphBuilder b, UIPlayerProfilePanel p, HashSet<IntPtr> used)
        {
            var menu = p.m_ProfileTabContainer?.m_Menu;
            if (menu == null || !menu.gameObject.activeInHierarchy) return;
            var items = menu.GetComponentsInChildren<UIProfileMenuItem>(false);
            if (items.Length == 0) return;
            b.BeginStop("tabs");
            b.PushContext(Loc.T("Tabs"));
            foreach (var it in items)
            {
                used.Add(it.Pointer);
                var id = CrimsonNodes.Id(it);
                b.AddItem(id, Toggle(it, () => UiText.Clean(it.m_Label?.Text?.text)));
                // The window opens on the open tab.
                if (CrimsonNodes.Toggled(it)) b.SetStart(id);
            }
            b.PopContext();
        }

        // The game parks its selection on the Exit button when the window opens, and moves it about while a
        // tab builds its view; following it would drop the cursor there. The cursor stays where the player
        // puts it.
        public override AControl GameSelection() => null;

        private static NodeVtable Toggle(AControl c, Func<string> label = null)
        {
            var v = CrimsonNodes.Button(c, label ?? (() => Describe(c)));
            // An option of an open dropdown list ("Collection Progress" under the stats' column picker) says
            // whether it is the column shown, as the list highlights it.
            var box = c.GetComponentInParent<UIDropdownContainer>();
            if (box != null && c.TryCast<UIDropdownButton>() != null && UnderListItems(c.transform, box.transform))
                v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                {
                    new NodeAnnouncement(() => SafeChosen(box, c) ? Loc.T("selected") : null, live: true, kind: AnnouncementKinds.Selected),
                };
            var trinket = c.TryCast<UIATrinketItem>();
            if (trinket != null)
                v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                {
                    new NodeAnnouncement(() => Equipped(trinket) ? GameText.Get("interface_mainmenu_equipped", "Equipped") : null,
                        live: true, kind: AnnouncementKinds.Selected),
                };
            var t = c.TryCast<ToggleButtonControl>();
            if (t != null)
                v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                {
                    new NodeAnnouncement(() => CrimsonNodes.Toggled(t) ? Loc.T("selected") : null, live: true, kind: AnnouncementKinds.Selected),
                };
            return v;
        }

        private static bool UnderListItems(Transform t, Transform box)
        {
            for (; t != null && t.Pointer != box.Pointer; t = t.parent) if (t.name == "ListItemsRoot") return true;
            return false;
        }

        // The game keeps no selected id for these lists (-1); the dropdown's head shows the current choice's words.
        private static bool SafeChosen(UIDropdownContainer box, AControl c)
        {
            try
            {
                string mine = string.Join(" ", CrimsonNodes.Texts(c));
                foreach (var b in box.GetComponentsInChildren<UIDropdownButton>(false))
                    if (!UnderListItems(b.transform, box.transform))
                        return mine.Length > 0 && string.Join(" ", CrimsonNodes.Texts(b)) == mine;
            }
            catch { }
            return false;
        }

        // ---- the banner ----

        private static void AddBanner(GraphBuilder b, UIPlayerProfilePanel p, HashSet<IntPtr> used)
        {
            var banner = p.m_ProfileBanner;
            if (banner == null || !banner.gameObject.activeInHierarchy) return;
            b.BeginStop("banner");
            var info = banner.m_PlayerInfo;
            if (info != null && info.gameObject.activeInHierarchy)
            {
                // The name, and the avatar it sits beside: one item. The avatar is the cosmetics button (Enter opens
                // the avatar and border picker, as clicking it does); read by its own texts it repeated the name and
                // title as a third item ("blindndanger, CARDSMITH") after the name and the title.
                var name = info.m_PlayerNameText;
                var cosmetics = info.m_CosmeticsButton;
                bool avatar = CrimsonNodes.Shown(cosmetics) && used.Add(cosmetics.Pointer);
                if (avatar)
                {
                    var v = CrimsonNodes.Button(cosmetics, () => UiText.Clean(name?.text));
                    v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                    {
                        new NodeAnnouncement(() => CrimsonNodes.TooltipText(cosmetics) ?? Loc.T("avatar and border"), kind: AnnouncementKinds.Role),
                    };
                    b.AddItem(CrimsonNodes.Id(cosmetics), v);
                }
                else if (CrimsonNodes.Shown(name))
                    b.AddItem(ControlId.Structural("profile:name"), new NodeVtable
                    {
                        Announcements = new[] { new NodeAnnouncement(() => UiText.Clean(name.text), kind: AnnouncementKinds.Label) },
                    });
                // The title under the name opens the title picker when clicked.
                var title = info.m_PlayerTitleButton;
                if (CrimsonNodes.Shown(title) && used.Add(title.Pointer))
                {
                    var titleText = info.m_PlayerTitleText;
                    var v = CrimsonNodes.Button(title, () => UiText.Clean(titleText?.text));
                    v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                    {
                        new NodeAnnouncement(() => GameText.Get("player_profile_titles", "Titles"), kind: AnnouncementKinds.Role),
                    };
                    b.AddItem(CrimsonNodes.Id(title), v);
                }
            }
            foreach (var c in CrimsonNodes.Leaves(banner, used)) b.AddItem(CrimsonNodes.Id(c), Toggle(c));
        }

        // ---- the open tab ----

        /// <summary>The tab's view: the one active child of the tab container's content root.</summary>
        private static AControl ShownView(UIPlayerProfilePanel p)
        {
            var root = p.m_ProfileTabContainer?.transform.Find("Content/ContentRoot");
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                var t = root.GetChild(i);
                if (t.gameObject.activeInHierarchy && t.GetComponent<AControl>() is AControl c) return c;
            }
            return null;
        }

        private void AddView(GraphBuilder b, AControl view, HashSet<IntPtr> used)
        {
            // The controls, a stop per row or column they are laid out in. The contract cards are the lists'
            // (below); the search field takes typing, which the mod's keys don't hand to the game.
            var groups = new List<(Transform Group, List<AControl> Controls)>();
            foreach (var c in CrimsonNodes.Leaves(view, used))
            {
                if (c.GetComponentInParent<UINewAchievementList>() != null || c.TryCast<InputFieldControl>() != null) continue;
                var group = LayoutParent(c, view);
                int at = groups.FindIndex(g => g.Group.Pointer == group.Pointer);
                if (at < 0) groups.Add((group, new List<AControl> { c }));
                else groups[at].Controls.Add(c);
            }
            foreach (var g in groups)
            {
                b.BeginStop("group:" + CrimsonNodes.PathKey(g.Group, view.transform));
                string heading = GroupHeading(g.Group, view.transform);
                if (heading != null) b.PushContext(heading);
                foreach (var c in g.Controls) b.AddItem(CrimsonNodes.Id(c), Toggle(c));
                if (heading != null) b.PopContext();
            }

            AddOverview(b, view, groups.Count == 0);

            foreach (var list in view.GetComponentsInChildren<UINewAchievementList>(false)) AddContracts(b, list, view);
        }

        /// <summary>The heading drawn over a group ("Showcase Contracts" over the showcased contracts): the nearest
        /// ancestor below the view with a shown text child named Label, Title or Header that is no control's
        /// caption. Null when there is none.</summary>
        private static string GroupHeading(Transform group, Transform view)
        {
            for (var t = group; t != null && t.Pointer != view.Pointer; t = t.parent)
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (child.name != "Label" && child.name != "Title" && child.name != "Header") continue;
                    var text = child.GetComponent<TMPro.TMP_Text>();
                    if (!CrimsonNodes.Shown(text)) continue;
                    var owner = child.GetComponentInParent<AControl>(); // a caption belongs to a control below t
                    if (owner != null && owner.transform.Pointer != t.Pointer && owner.transform.IsChildOf(t)) continue;
                    string s = UiText.Clean(text.text);
                    if (s.Length > 0) return s;
                }
            return null;
        }

        /// <summary>The nearest row or column (a layout container) a control sits in below the view.</summary>
        // A dropdown is a container of its own; the dropdowns side by side (and their open lists) share the
        // row the dropdowns sit in.
        private static Transform LayoutParent(AControl c, AControl view)
        {
            var from = c.GetComponentInParent<UIDropdownContainer>()?.transform.parent ?? c.transform.parent;
            for (var t = from; t != null && t.Pointer != view.transform.Pointer; t = t.parent)
            {
                var a = t.GetComponent<AContainer>();
                if (a != null && (a.TryCast<HorizontalContainer>() != null || a.TryCast<VerticalContainer>() != null
                    || a.TryCast<GridContainer>() != null)) return t;
            }
            return view.transform;
        }

        // What the tab shows that no control carries: the contracts progress and score, each category's
        // progress, the all-wins count; on a tab with no controls at all (ranked score before rank 25), every
        // text it shows.
        private static void AddOverview(GraphBuilder b, AControl view, bool bare)
        {
            var lines = new List<(string Key, Func<string> Text)>();
            foreach (var pr in view.GetComponentsInChildren<UIAchievementsProgress>(false))
            {
                var c = pr;
                lines.Add((CrimsonNodes.PathKey(c.transform, view.transform), () => Joined(CrimsonNodes.Texts(c))));
            }
            foreach (var bar in view.GetComponentsInChildren<UIAchievementCategoryProgressBar>(false))
            {
                var c = bar;
                lines.Add((CrimsonNodes.PathKey(c.transform, view.transform), () => Joined(LabelFirst(c, c.m_Label?.transform))));
            }
            foreach (var w in view.GetComponentsInChildren<UIFactionCardWinStatsContainer>(false))
            {
                var desc = w.transform.Find("AllWinsDescriptionText");
                var count = w.transform.Find("AllWinsText");
                if (desc != null && count != null && desc.gameObject.activeInHierarchy)
                    lines.Add((CrimsonNodes.PathKey(desc, view.transform), () => Joined(CrimsonNodes.Texts(desc), CrimsonNodes.Texts(count))));
            }
            if (bare)
                foreach (var t in view.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    var text = t;
                    lines.Add((CrimsonNodes.PathKey(t.transform, view.transform), () => UiText.NullIfEmpty(UiText.Clean(text.text))));
                }
            lines.RemoveAll(l => l.Text() == null);
            if (lines.Count == 0) return;
            b.BeginStop("overview");
            foreach (var l in lines)
            {
                var text = l.Text;
                b.AddItem(ControlId.Structural("profile:overview" + l.Key), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(text, live: true, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(text(), true),
                });
            }
        }

        // ---- the contract lists ----

        /// <summary>One contract in a list: the list's data entry, which outlives the card scrolled in for it.</summary>
        private sealed class Contract
        {
            public UINewAchievementList List;
            public AchievementListData Data;
        }

        // The entries as last walked, per list: walking hundreds of entries every frame is wasted while the
        // list is unchanged (same list, same count, same first entry).
        private readonly Dictionary<IntPtr, (int Count, IntPtr First, List<(Contract Entry, ulong Id)> Entries)> _lists =
            new Dictionary<IntPtr, (int, IntPtr, List<(Contract, ulong)>)>();

        private void AddContracts(GraphBuilder b, UINewAchievementList list, AControl view)
        {
            var entries = Entries(list);
            if (entries == null || entries.Count == 0) return;
            b.BeginStop("contracts:" + CrimsonNodes.PathKey(list.transform, view.transform));
            string heading = ListHeading(list);
            if (heading != null) b.PushContext(heading);
            foreach (var (entry, id) in entries)
            {
                var e = entry;
                b.AddItem(ControlId.Referenced(e, "profile:contract:" + list.Pointer + ":" + id), new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => Name(e), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => State(e), live: true, kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => Joined(new List<string> { Description(e), Reward(e) }), kind: AnnouncementKinds.Tooltip),
                    },
                    OnActivate = () => Activate(e),
                    OnTooltip = () => Speech.Say(Joined(new List<string> { Name(e), State(e), Description(e), Reward(e), Steps(e) }), true),
                });
            }
            if (heading != null) b.PopContext();
        }

        private List<(Contract Entry, ulong Id)> Entries(UINewAchievementList list)
        {
            try
            {
                var sr = list.ScrollRect;
                int n = sr == null ? 0 : sr.GetContentListCount();
                IntPtr first = n > 0 ? sr.GetContentAt(0)?.Pointer ?? IntPtr.Zero : IntPtr.Zero;
                if (_lists.TryGetValue(list.Pointer, out var known) && known.Count == n && known.First == first) return known.Entries;
                var entries = new List<(Contract, ulong)>();
                for (int i = 0; i < n; i++)
                {
                    var d = sr.GetContentAt(i)?.TryCast<AchievementListData>();
                    if (d?.Achievement == null) continue;
                    entries.Add((new Contract { List = list, Data = d }, d.Achievement.Cast<IPublicAchievement>().Id));
                }
                _lists[list.Pointer] = (n, first, entries);
                return entries;
            }
            catch (Exception e) { Plugin.LogInfo("[profile] contracts failed: " + e.Message); return null; }
        }

        // The recent list sits under its heading ("Недавние"); a category's list under the open menu item.
        private static string ListHeading(UINewAchievementList list)
        {
            var recent = list.GetComponentInParent<UIRecentAchievements>();
            if (recent != null) return UiText.NullIfEmpty(UiText.Clean(recent.m_Label?.Text?.text))
                ?? UiText.NullIfEmpty(Joined(CrimsonNodes.Texts(recent.GetComponentInParent<UIAchievementsSummary>()?.transform.Find("HeaderLabel"))));
            return null;
        }

        /// <summary>The achievement a card shows for the entry: a sequence shows its step in progress.</summary>
        private static IAchievement Achievement(Contract e)
        {
            try { return e.Data.GetAchievementToShow() ?? e.Data.Achievement; } catch { return null; }
        }

        private static IPublicAchievement Public(Contract e) => Achievement(e)?.Cast<IPublicAchievement>();

        private static string Name(Contract e)
        {
            try { return UiText.NullIfEmpty(UiText.Clean(Public(e)?.Name)) ?? Loc.T("Contract"); } catch { return Loc.T("Contract"); }
        }

        private static string Description(Contract e)
        {
            try { return UiText.NullIfEmpty(UiText.Clean(Public(e)?.Description)); } catch { return null; }
        }

        /// <summary>
        /// What the card shows of the contract's state: done ("Выполнено, 31.10.2026", the card's date), ready
        /// to claim (its claim button: "Получить: 1 Очки награды"), or its progress bar ("15/50"); then whether
        /// it is tracked, as the contracts menu names the tracked ones.
        /// </summary>
        private static string State(Contract e)
        {
            try
            {
                var a = Achievement(e);
                var pa = a?.Cast<IPublicAchievement>();
                if (a == null || pa == null) return null;
                var parts = new List<string>();
                if (a.IsClaimable)
                    parts.Add(Joined(new List<string> { GameText.Get(UIAchievement.ACHIEVEMENTS_CLAIM_LOC_KEY, "Claim:"), Rewards(pa) }, " "));
                else if (pa.IsUnlocked)
                {
                    parts.Add(GameText.Get("daily_quest_completed", "Completed"));
                    parts.Add(DateUnlocked(a, e));
                }
                else
                {
                    var pp = a.PointProgress;
                    if (pp != null && pp.PointsRequired > 1) parts.Add(Loc.T("{0} of {1}", pp.PointsEarned, pp.PointsRequired)); // drawn "2/5"
                }
                if (a.IsTracked) parts.Add(GameText.Get("achievements_tracking", "Tracked"));
                return Joined(parts);
            }
            catch (Exception ex) { Plugin.LogInfo("[profile] state failed: " + ex.Message); return null; }
        }

        /// <summary>
        /// The day the contract was done, as the card draws it ("01.11.2026": the game's localized short date gave "11/1/2026"),
        /// or null. Read straight from the achievement's <c>&lt;DateUnlocked&gt;k__BackingField</c>, because the
        /// interop's <c>DateUnlocked</c> (the property and the field accessor alike) misreads a
        /// <c>Nullable&lt;DateTime&gt;</c>: il2cpp boxes a nullable as its bare DateTime (or null), and the
        /// interop wraps that box as if it held the whole nullable, so HasValue reads a byte of the ticks and
        /// Value the 8 bytes past the box: a different garbage date on every read ("01.01.0001",
        /// "22.03.4393"), which the live state then re-spoke every frame. The field holds the real nullable in
        /// place: hasValue, then the DateTime 8 bytes on (mscorlib's layout).
        /// </summary>
        private static unsafe string DateUnlocked(IAchievement a, Contract e)
        {
            var pub = a.TryCast<PublicAchievement>();
            if (pub == null) return null;
            if (_dateField == IntPtr.Zero)
                _dateField = IL2CPP.GetIl2CppField(Il2CppClassPointerStore<PublicAchievement>.NativeClassPtr, "<DateUnlocked>k__BackingField");
            if (_dateField == IntPtr.Zero) return null;
            byte* field = (byte*)pub.Pointer + IL2CPP.il2cpp_field_get_offset(_dateField);
            if (*field == 0) return null;
            var date = *(Il2CppSystem.DateTime*)(field + 8);
            string said = UiText.NullIfEmpty(date.ToLocalTime().ToString("dd.MM.yyyy"));
            CheckDrawnDate(e, said);
            return said;
        }

        private static IntPtr _dateField;

        // The card in view draws the date itself; a difference from the date said is logged once, so the format
        // can be matched to it.
        private static readonly HashSet<string> _datesChecked = new HashSet<string>();

        private static void CheckDrawnDate(Contract e, string said)
        {
            try
            {
                var label = Card(e)?.m_DateText;
                if (!CrimsonNodes.Shown(label)) return;
                string drawn = CrimsonNodes.Text(label);
                if (string.IsNullOrEmpty(drawn) || !_datesChecked.Add(drawn)) return;
                if (said == null || drawn.IndexOf(said, StringComparison.Ordinal) < 0)
                    Plugin.LogInfo("[profile] contract date drawn '" + drawn + "', said '" + said + "'");
            }
            catch { }
        }

        private static string Reward(Contract e)
        {
            try
            {
                var pa = Public(e);
                string r = pa == null ? null : Rewards(pa);
                return r == null ? null : GameText.Get(UIAchievement.ACHIEVEMENTS_REWARD_LOC_KEY, "Reward:") + " " + r;
            }
            catch { return null; }
        }

        private static string Rewards(IPublicAchievement pa)
        {
            var rewards = pa.Rewards;
            var parts = new List<string>();
            for (int i = 0; rewards != null && i < rewards.Count; i++)
            {
                string s = RewardText.Of(rewards[i]) ?? "";
                if (s.Length > 0 && !parts.Contains(s)) parts.Add(s);
            }
            return Joined(parts);
        }

        // A sequence's steps, as its card's details list them under the progress bar: each target and its
        // reward. Only the card in view has them drawn.
        private static string Steps(Contract e)
        {
            var card = Card(e);
            var details = card?.GetComponentInParent<UIAchievementListItem>()?.m_AchievementDetailsUI;
            if (details == null || !details.gameObject.activeInHierarchy || details.TryCast<UISequenceAchievementDetails>() == null) return null;
            return Joined(CrimsonNodes.Texts(details));
        }

        /// <summary>The card drawn for the entry, if it is scrolled into view.</summary>
        private static UIAchievement Card(Contract e)
        {
            try
            {
                var item = e.Data.GetContainer()?.TryCast<UIAchievementListItem>();
                if (item == null || !item.gameObject.activeInHierarchy || item.Data?.Pointer != e.Data.Pointer) return null;
                var card = item.Achievement;
                return card != null && card.gameObject.activeInHierarchy ? card : null;
            }
            catch { return null; }
        }

        // ---- landing on a contract ----

        private Contract _landing;
        private int _landingFrames;

        // Landing on a contract scrolls the list to it; its card is drawn a frame or so later, and Tick then
        // selects it and raises its hover, as a gamepad's move or the mouse would.
        public override void OnCursorMoved(GraphNode node)
        {
            _landing = null;
            if (node?.Id.Reference is Contract e)
            {
                try { e.List.ScrollRect?.ScrollToElement(e.Data.Cast<CrimsonUI.IVirtualContentData>(), false, false); }
                catch (Exception ex) { Plugin.LogInfo("[profile] scroll failed: " + ex.Message); }
                _landing = e;
                _landingFrames = 0;
                return;
            }
            if (node?.Id.Reference is AControl c && !CrimsonNodes.Selectable(c))
            {
                // A tab or a banner title: never the layer's selection; the hover still plays.
                try { c.OnHover?.Invoke(c); } catch { }
                return;
            }
            base.OnCursorMoved(node);
        }

        public override void Tick()
        {
            var e = _landing;
            if (e == null) return;
            var card = Card(e);
            if (card == null)
            {
                if (++_landingFrames > 30) _landing = null;
                return;
            }
            _landing = null;
            var main = card.m_MainButtonControl;
            if (main == null || !main.gameObject.activeInHierarchy) return;
            var layer = main.ParentLayer;
            if (layer != null && (layer.SelectedControl == null || layer.SelectedControl.Pointer != main.Pointer))
                layer.RequestControlSelection(main);
            try { main.OnHover?.Invoke(main); } catch { }
        }

        public override void OnPop()
        {
            _landing = null;
            _lists.Clear();
        }

        // ---- the card's buttons ----

        private static void Activate(Contract e)
        {
            var card = Card(e);
            if (card == null) return;
            var claim = card.m_ClaimButton;
            if (CrimsonNodes.Shown(claim) && Achievement(e) is IAchievement a && a.IsClaimable) Navigator.MouseClick(claim);
            else Navigator.MouseClick(card.m_MainButtonControl);
        }

        public override IReadOnlyList<ScreenKey> ExtraKeys => _keys ??= new[]
        {
            new ScreenKey { Key = KeyCode.T, Run = Track, Hint = Loc.T("T, track the contract") },
        };
        private static ScreenKey[] _keys;

        // The card's track button ("Выбрать"), shown on contracts not yet done.
        private static void Track()
        {
            if (!(GraphNavigator.CurrentNode?.Id.Reference is Contract e)) return;
            var card = Card(e);
            var track = card?.m_TrackButton;
            if (!CrimsonNodes.Shown(track)) return;
            Navigator.MouseClick(track);
        }

        // ---- the window ----

        // The bottom bar (Exit).
        private static void AddWindowButtons(GraphBuilder b, UIPlayerProfilePanel p, HashSet<IntPtr> used)
        {
            var rest = CrimsonNodes.BarButtons(p, used, bar => bar.GetComponentInParent<UINewAchievementList>() != null);
            if (rest.Count == 0) return;
            b.BeginStop("buttons");
            foreach (var c in rest) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        // ---- reading a control ----

        /// <summary>
        /// A control's words. An icon with none (a stat, a progress diamond, a faction row, a showcase slot's
        /// button) reads as a mouse player reads it by pointing: its hover tooltip ("Высший ранг"), then the
        /// words drawn around it — the nearest enclosing object that has any ("30"; "31/240, 0").
        /// </summary>
        private static string Describe(AControl c)
        {
            if (TrinketText(c) is string trinket) return trinket;
            var own = CrimsonNodes.Texts(c);
            if (own.Count > 0)
            {
                // Only a number (the cycle level's "1"): named as the tooltip names it, as for an icon.
                if (own.TrueForAll(IsCount) && (CrimsonNodes.TooltipText(c) ?? StatName(c)) is string name)
                    own.Insert(0, name);
                return Joined(own);
            }
            if (FactionRow(c) is string row) return row;
            string tip = CrimsonNodes.TooltipText(c) ?? StatName(c);
            var around = new List<string>();
            var t = c.transform.parent;
            for (int i = 0; i < 3 && t != null && around.Count == 0; i++, t = t.parent) around = TitleFirst(t);
            // A number is named by the tooltip ("Высший ранг, 30"); a named thing comes first and the tooltip,
            // what clicking it does, after ("Геральт из Ривии, Счет: 0, …, Измените контракт для демонстрации").
            var parts = new List<string>();
            bool named = around.Count > 0 && !IsCount(around[0]);
            if (tip != null && !named) parts.Add(tip);
            // Drawn words the tooltip already says ("Highest" under the rank whose tooltip is "Highest Rank") go.
            foreach (var s in around)
                if (!parts.Contains(s) && !(tip != null && !IsCount(s) && tip.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) parts.Add(s);
            if (tip != null && named && !parts.Contains(tip)) parts.Add(tip);
            return parts.Count > 0 ? Joined(parts) : CrimsonNodes.Label(c);
        }

        /// <summary>
        /// An item of the cosmetics tab's avatar, border and title pickers (<see cref="UIATrinketItem"/>): a
        /// picture (or a title's words) a sighted player knows by sight. Named by the item's own name, then
        /// "Locked" when the player doesn't own it, as the picker greys it out. Equipped is said apart (see
        /// <see cref="Toggle"/>). Null for any other control.
        /// </summary>
        private static string TrinketText(AControl c)
        {
            var item = c.TryCast<UIATrinketItem>();
            if (item == null) return null;
            var parts = new List<string>();
            try
            {
                var def = item.m_Data?.Item;
                string key = def?.NameLocalizationKey;
                if (key != null && GameText.Get(key, null) is string name) parts.Add(name);
                if (parts.Count == 0) parts.AddRange(CrimsonNodes.Texts(c));
                if (def != null && !def.IsOwned) parts.Add(GameText.Get("cosmetic_locked", "Locked"));
            }
            catch (Exception e) { Plugin.LogInfo("[profile] trinket read failed: " + e.Message); }
            return parts.Count > 0 ? Joined(parts) : null;
        }

        private static bool Equipped(UIATrinketItem item)
        {
            try { return CrimsonNodes.Shown(item.m_Equiped) || item.m_Data?.IsEquiped == true; } catch { return false; }
        }

        private static bool IsCount(string s)
        {
            foreach (char ch in s)
                if (!char.IsDigit(ch) && ch != ' ' && ch != ',' && ch != '.' && ch != '/') return false;
            return s.Length > 0;
        }

        // The banner's cycle-level stat has no tooltip; the main menu's badge for the same number is named by
        // the season's cycle ("Уровень осеннего цикла"), drawn by its shield picture.
        private static string StatName(AControl c)
        {
            Transform stat = c.transform;
            while (stat != null && stat.name != "Ouroboros") stat = stat.parent;
            if (stat == null) return null;
            string season = MainMenuScreen.CycleSeason(stat);
            if (season == null)
                try
                {
                    var badge = UnityEngine.Object.FindObjectOfType<GwentUnity.UIPlayerInfoOverlay>()?.m_BasePlayerInfoInstance?.m_OuroborosLevelInstance;
                    if (badge != null) season = MainMenuScreen.CycleSeason(badge);
                }
                catch { }
            season ??= MainMenuScreen.LastCycleSeason;
            return season == null ? GameText.Get("interface_profile_ouroboros_level", "Season cycle level")
                : GameText.Get("interface_profile_ouroboros_level_" + season, "Season cycle level");
        }

        /// <summary>A faction row of the summary's stats: the faction (its icon's tooltip), then each number
        /// under the name of the column its dropdown shows ("Заполнение коллекции 25/204, Все победы 0").
        /// Null for any other control.</summary>
        private static string FactionRow(AControl c)
        {
            var box = c.GetComponentInParent<UIFactionCardWinStatsContainer>();
            if (box == null) return null;
            var row = c.transform.parent;
            if (row == null) return null;
            string Column(string dropdown) =>
                Joined(CrimsonNodes.Texts(box.transform.Find("FiltersRoot/" + dropdown + "(Clone)/UIDropdownPrefab(Clone)/MainButtonRoot")));
            string Text(string path)
            {
                var t = row.Find(path);
                return t != null && t.gameObject.activeInHierarchy ? Joined(CrimsonNodes.Texts(t)) : null;
            }
            var parts = new List<string> { CrimsonNodes.TooltipText(c) };
            string progress = Text("ProgressBarRoot/UIProgressBarPrefab(Clone)/ProgressText");
            if (progress != null) parts.Add(Joined(new List<string> { Column("UIFactionCardStatsDropdownPrefab"), progress }, " "));
            string wins = Text("Banner/Wins");
            if (wins != null) parts.Add(Joined(new List<string> { Column("UIFactionWinStatsDropdownPrefab"), wins }, " "));
            return Joined(parts);
        }

        // An object's texts with its title first (a showcase slot draws its points and date above the title).
        private static List<string> TitleFirst(Transform t)
        {
            var texts = CrimsonNodes.Texts(t);
            foreach (var tmp in t.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (tmp.gameObject.name != "Title") continue;
                string title = UiText.Clean(tmp.text);
                if (texts.Remove(title)) texts.Insert(0, title);
                break;
            }
            return texts;
        }

        private static List<string> LabelFirst(Component c, Transform label)
        {
            var texts = CrimsonNodes.Texts(c);
            if (label == null) return texts;
            var first = CrimsonNodes.Texts(label);
            foreach (var s in texts) if (!first.Contains(s)) first.Add(s);
            return first;
        }

        // ---- helpers ----

        private static string Joined(List<string> parts, string sep = ", ")
        {
            parts?.RemoveAll(string.IsNullOrEmpty);
            return parts == null || parts.Count == 0 ? null : string.Join(sep, parts);
        }

        private static string Joined(List<string> a, List<string> b)
        {
            var all = new List<string>(a);
            all.AddRange(b);
            return Joined(all);
        }
    }
}
