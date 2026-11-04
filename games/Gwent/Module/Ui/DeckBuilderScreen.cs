using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The deck builder's main panel (<see cref="UIDBPanel"/>), laid out as Accessible Arena lays out MTG Arena's:
    /// groups a Tab apart, cards moved through with Left/Right and read line by line with Up/Down (the model
    /// every screen of cards here shares, <see cref="CardKeys"/>). Stops, in Tab order:
    ///
    /// - Decks (the deck list, no deck open): "НОВАЯ КОЛОДА" and one item per deck (its name, leader ability, and
    ///   what makes it invalid when it is). Right arrow shows the deck's options (rename, duplicate, delete…),
    ///   which follow it; Left hides them.
    /// - Collection: the grid as one row in its own order (stratagems, then provisions high to low), built from
    ///   the grid's content list (the grid is virtual). Landing scrolls the grid to the card and raises its
    ///   hover. Enter adds the card (the grid's left click); Shift+Enter opens the game's preview (its right
    ///   click). Page Up/Down move a grid row; Ctrl+Page Up/Down jump to the next or previous provision cost.
    /// - Your deck (a deck open): the leader ability, the stratagem, then the cards, one row. Enter takes a
    ///   copy out; Ctrl+Enter adds another; Shift+Enter previews.
    /// - Deck stats: cards, units and provisions against the game's limits, live.
    /// - Filters: the search box (Ctrl+F from anywhere) and the provision toggles, reset, mill spares; then the
    ///   side filter dropdowns; then the input bar's buttons.
    ///
    /// Backspace is Back: with a deck open, its Exit (which asks whether to save). Overlays the game opens on top
    /// (the mill-spares window, the card preview, popups) are screens of a higher layer.
    /// </summary>
    internal sealed class DeckBuilderScreen : Screen
    {
        public override string Key => "deck-builder";
        public override int Layer => 0;
        public override string ScreenName => Title();

        public override bool IsActive() => Panel() != null;

        /// <summary>Backspace off a card or deck: while a deck is open, its Exit, which asks whether to save (never
        /// "Exit without saving"); its bar binds no Back key. On the deck list, the game's Back.</summary>
        public override bool Back()
        {
            try
            {
                var edit = DeckBuilder.Edit;
                var quit = edit?.m_QuitAndSaveBinding;
                if (quit != null && quit.gameObject.activeInHierarchy) { Plugin.LogInfo("[deck builder] back: quit and save"); Navigator.ClickControl(quit); return true; }
                // A starter deck can't be changed, so its only Exit is the no-save one: nothing is lost.
                var noSave = edit?.m_QuitWithoutSavingBinding;
                bool starter = false;
                try { starter = DeckBuilder.Deck()?.IsStarterDeck == true; } catch { }
                if (starter && noSave != null && noSave.gameObject.activeInHierarchy) { Plugin.LogInfo("[deck builder] back: starter, no save"); Navigator.ClickControl(noSave); return true; }
            }
            catch { }
            // The deck list: its Exit, the last button of its bar, clicked as the mouse does. Neither the bar's
            // back-key entry nor the panel's HandleBackButtonClicked left the deck builder (Backspace did nothing
            // and said nothing); the Exit button's click does.
            try
            {
                var p = Panel();
                var bar = p != null ? CrimsonNodes.BarButtons(p) : null;
                var exit = bar != null && bar.Count > 0 ? bar[bar.Count - 1].TryCast<ButtonControl>() : null;
                if (exit != null && Navigator.MouseClick(exit))
                {
                    Plugin.LogInfo("[deck builder] back: " + CrimsonNodes.Label(exit));
                    return true;
                }
            }
            catch (Exception e) { Plugin.LogInfo("[deck builder] back failed: " + e.Message); }
            return PressBack();
        }

        // The panel, when the input layer that has the input holds it.
        private static UIDBPanel Panel() => CrimsonNodes.ActivePanel<UIDBPanel>();

        // The title banner ("КОНСТРУКТОР КОЛОДЫ").
        private static string Title()
        {
            var p = Panel();
            var banner = p?.TitleBanner;
            if (banner == null) return null;
            var parts = CrimsonNodes.Texts(banner);
            return parts.Count > 0 ? parts[0] : null;
        }

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();
            var collection = p.GetComponentInChildren<UIDBCollectionContainer>(false);

            bool editing = DeckBuilder.Edit != null;
            if (!editing) BuildDecks(b, p, used);
            if (collection != null) BuildCards(b, collection);
            BuildDeck(b, p, used);
            if (editing) BuildStats(b);
            if (collection != null) BuildFilters(b, collection.FilterContainer, used);
            BuildLeaves(b, "db:side-filters", p.GetComponentInChildren<UIDBFilters>(false), used);
            BuildBar(b, p, used);

            // Opening a deck lands on the collection's first card, ready to add (the game left its selection on a
            // filter: "Milling settings"); for a moment the game's selection isn't followed.
            IntPtr edit = IntPtr.Zero;
            try { edit = DeckBuilder.Edit?.Pointer ?? IntPtr.Zero; } catch { }
            if (edit != _seenEdit) { _seenEdit = edit; _seenEditAt = Time.realtimeSinceStartup; }
            // Once the deck has loaded (the game's "Loading Deck" popup comes and goes in the first moments).
            if (edit != _landedEdit && _cards.Count > 0 && Time.realtimeSinceStartup - _seenEditAt > 0.8f)
            {
                _landedEdit = edit;
                if (edit != IntPtr.Zero)
                {
                    GraphNavigator.Suggest(_cards[0].Id);
                    _openedAt = Time.realtimeSinceStartup;
                }
            }
        }

        private static IntPtr _landedEdit, _seenEdit;
        private static float _seenEditAt;
        private static float _openedAt = -10;

        // While a deck opens (its "Loading Deck" popup comes and goes) the cursor's passing landings aren't read.
        public override bool QuietLanding(GraphNode node) =>
            _seenEdit != IntPtr.Zero && Time.realtimeSinceStartup - _seenEditAt < 0.8f;

        public override AControl GameSelection() =>
            Time.realtimeSinceStartup - _openedAt < 1.5f ? null : base.GameSelection();

        // ---- decks ----

        private static void BuildDecks(GraphBuilder b, UIDBPanel p, HashSet<IntPtr> used)
        {
            var list = p.GetComponentInChildren<UIDeckListContainer>(false);
            if (list == null) return;
            b.BeginStop("db:decks");
            foreach (var c in CrimsonNodes.Leaves(list, used))
            {
                // A deck's options list stays selectable while folded away; it counts only while shown.
                var dropdown = c.GetComponentInParent<DropdownContainer>();
                if (dropdown != null && dropdown.MainButton?.Pointer != c.Pointer && !ListShown(dropdown)) continue;
                // A deck reads all its words: its name and its leader ability ("Стартовая колода Чудовищ, Белый Хлад").
                var control = c;
                var v = CrimsonNodes.Button(c, () => AllTexts(control));
                // A deck's options (rename, duplicate, delete...) unfold on a right click: Right arrow (see keys).
                if (dropdown != null && dropdown.MainButton?.Pointer == c.Pointer)
                {
                    var d = dropdown;
                    var deck = c.TryCast<UIDeckButtonControl>() ?? c.GetComponentInParent<UIDeckButtonControl>();
                    v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                    {
                        new NodeAnnouncement(() => Invalid(deck), kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => ListShown(d) ? Loc.T("options shown") : null, live: true, kind: AnnouncementKinds.Value),
                    };
                }
                b.AddItem(CrimsonNodes.Id(c), v);
            }
        }

        /// <summary>What makes a deck unplayable, as the game's validation finds it ("invalid: 20 cards, minimum
        /// 25"). Null for a valid deck or a starter deck.</summary>
        private static string Invalid(UIDeckButtonControl button)
        {
            try
            {
                var v = button?.Deck?.GetValidation();
                if (v == null || v.IsStarterDeck || v.IsDeckValid) return null;
                var why = new List<string>();
                if (!v.IsTotalCardCountCorrect) why.Add(Loc.P(v.TotalCardCount, "{0} card, minimum 25", "{0} cards, minimum 25"));
                if (!v.IsUnitCountCorrect) why.Add(Loc.P(v.UnitCardCount, "{0} unit, minimum 13", "{0} units, minimum 13"));
                if (!v.IsProvisionUsageCorrect) why.Add(Loc.T("provisions {0} over the limit", v.ProvisionUsed));
                if (!v.IsDeckFactionsCorrect) why.Add(Loc.T("cards of another faction"));
                if (!v.IsBronzeHighestCopyCountCorrect || !v.IsGoldHighestCopyCountCorrect || !v.IsSilverHighestCopyCountCorrect) why.Add(Loc.T("too many copies"));
                return why.Count > 0 ? Loc.T("invalid: {0}", string.Join(", ", why)) : Loc.T("invalid");
            }
            catch { return null; }
        }

        // ---- the deck being edited ----

        /// <summary>
        /// While a deck is open the left panel shows it (DeckPickerPrefab/DeckCards): its leader ability slot and
        /// one row per card (name, provisions, copies), with the deck's totals the game checks it against. One
        /// stop, first: the totals (cards of 25, units of 13, provisions of the limit, from the game's own
        /// validation), then the rows as drawn. Enter on a row is its click, which takes the card out; on the
        /// leader it opens the ability choice. This replaces the older Ctrl+D (totals) and Ctrl+H (list).
        /// </summary>
        private static void BuildDeck(GraphBuilder b, UIDBPanel p, HashSet<IntPtr> used)
        {
            Transform root = null;
            foreach (var t in p.GetComponentsInChildren<Transform>(false))
                if (t.name == "DeckCards") { root = t; break; }
            if (root == null) return;
            var rows = new List<AControl>();
            foreach (var c in root.GetComponentsInChildren<AControl>(false))
            {
                if (!c.IsCurrentlySelectable() || used.Contains(c.Pointer)) continue;
                // The deck header's options (Rename, Change Ability) stay selectable while folded away.
                var dd = c.GetComponentInParent<DropdownContainer>();
                if (dd != null && dd.MainButton?.Pointer != c.Pointer && !ListShown(dd)) continue;
                // Card rows only (leader, stratagem, cards): the counters beside them ("25", "-", an empty deck's
                // "MIN 25") are the deck stats stop's, in words.
                if (c.TryCast<UIDeckCardButtonControl>() == null) continue;
                rows.Add(c);
            }
            if (rows.Count == 0) return;
            b.BeginStop("db:deck");
            string name = null;
            try { var d = DeckBuilder.Deck(); name = d == null ? null : UiText.Clean(d.LocalizedName ?? d.Name ?? ""); } catch { }
            b.PushContext(string.IsNullOrEmpty(name) ? Loc.T("Deck") : name);
            b.StartRow();
            foreach (var c in rows)
            {
                used.Add(c.Pointer);
                var control = c;
                b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, () => DeckRow(control)));
            }
            b.EndRow();
            b.PopContext();
        }

        /// <summary>The deck's totals against the game's limits, one item each, live: what a sighted player reads
        /// on the counters above the deck.</summary>
        private static void BuildStats(GraphBuilder b)
        {
            b.BeginStop("db:stats");
            b.PushContext(Loc.T("Deck stats"));
            for (int i = 0; i < 3; i++)
            {
                int part = i;
                b.AddItem(ControlId.Structural("db:stats:" + i), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => DeckBuilder.StatPart(part), live: true, kind: AnnouncementKinds.Label) },
                });
            }
            b.PopContext();
        }

        // A deck row as the collection reads cards ("Geralt of Rivia, power 3, 10 provisions"), with its copies
        // ("x2"); rows that aren't cards (the leader slot) read their words.
        private static string DeckRow(AControl c)
        {
            try
            {
                var row = c.TryCast<UIDeckCardButtonControl>() ?? c.GetComponentInParent<UIDeckCardButtonControl>();
                var card = row?.Card?.Card;
                if (card != null)
                {
                    string copies = CrimsonNodes.Texts(c).Find(t => t.Length > 1 && t[0] == 'x' && int.TryParse(t.Substring(1), out _));
                    return CardReader.Summary(card) + (copies != null ? ", " + copies : "");
                }
            }
            catch { }
            return AllTexts(c);
        }

        private static bool ListShown(DropdownContainer d)
        {
            try { return d.IsListVisible; } catch { return false; }
        }

        // ---- the collection grid ----

        // The grid's content, cached between renders: reading 2000+ entries through the interop every frame
        // costs more than the frame. Rebuilt when the list's size or its ends change (a filter, a sort).
        private static readonly List<(IVirtualContentData Data, ControlId Id, CollectionCard Card)> _cards = new();
        private static int _cardsCount = -1;
        private static IntPtr _cardsFirst, _cardsLast;

        private static VirtualCardGrid _grid;

        private static void BuildCards(GraphBuilder b, UIDBCollectionContainer collection)
        {
            var grid = collection.CardGridContainer;
            if (grid == null) return;
            _grid = grid;
            Refresh(grid);
            if (_cards.Count == 0) return;
            b.BeginStop("db:cards");
            b.PushContext(Loc.T("Collection"));
            b.StartRow();
            foreach (var entry in _cards)
            {
                var card = entry.Card;
                var data = entry.Data;
                b.AddItem(entry.Id, new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => CardReader.Summary(card.Card), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => Premium(card) ? Loc.T("premium") : null, kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => Owned(card), live: true, kind: AnnouncementKinds.Value),
                        // Not live: adding or removing a copy is said whole by DeckBuilder.Announce ("Added X, 2 in
                        // deck. …"); live, "1 in deck" was said first and then again in that line.
                        new NodeAnnouncement(() => InDeck(card), kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => New(data) ? Loc.T("new") : null, kind: AnnouncementKinds.Value),
                    },
                    OnActivate = () => Click(data, false),
                    OnTooltip = () => Speech.Say(CardReader.Whole(card.Card), true),
                });
            }
            b.EndRow();
            b.PopContext();
        }

        // Copies of the card in the deck being edited ("1 in deck"), as the card's ribbon shows them.
        private static string InDeck(CollectionCard cc)
        {
            try
            {
                var d = DeckBuilder.Deck();
                if (d == null || DeckBuilder.Edit == null) return null;
                int n = d.GetCardCount(cc);
                return n > 0 ? Loc.T("{0} in deck", n) : null;
            }
            catch { return null; }
        }

        private static void Refresh(VirtualCardGrid grid)
        {
            int n = grid.GetContentListCount();
            IntPtr first = n > 0 ? grid.GetContentAt(0).Pointer : IntPtr.Zero;
            IntPtr last = n > 0 ? grid.GetContentAt(n - 1).Pointer : IntPtr.Zero;
            if (n == _cardsCount && first == _cardsFirst && last == _cardsLast) return;
            _cards.Clear();
            for (int i = 0; i < n; i++)
            {
                var d = grid.GetContentAt(i);
                var cc = d?.TryCast<VirtualCardContentData>()?.CardReference;
                if (cc == null) continue;
                string key;
                try { key = "db:card:" + cc.Template.Id + ":" + (cc.Definition.IsPremium ? "p" : "n"); }
                catch { key = "db:card:#" + i; }
                _cards.Add((d, ControlId.Referenced(d, key), cc));
            }
            _cardsCount = n; _cardsFirst = first; _cardsLast = last;
            Plugin.LogInfo("[db] grid content: " + _cards.Count + " cards");
        }

        private static bool Premium(CollectionCard cc)
        {
            try { return cc.Definition.IsPremium; } catch { return false; }
        }

        private static bool New(IVirtualContentData d)
        {
            try { return d.TryCast<VirtualCardContentData>()?.ShowNewIcon == true; } catch { return false; }
        }

        // The copies owned, as the card's counter ribbon shows them; a card not owned is greyed out.
        private static string Owned(CollectionCard cc)
        {
            try
            {
                int n = cc.m_CollectionInstances?.Count ?? 0;
                return n == 0 ? Loc.T("not owned") : "x" + n;
            }
            catch { return null; }
        }

        // Scrolls the grid to the card and selects it, then returns its live element.
        private static VirtualCardContainerElement Show(IVirtualContentData d)
        {
            var grid = _grid;
            if (grid == null || d == null) return null;
            grid.ScrollToElement(d, false, true);
            var sel = CrimsonInputManager.Instance?.ActiveLayer?.SelectedControl;
            var el = sel?.TryCast<VirtualCardContainerElement>();
            return el != null && el.m_Content != null && el.m_Content.Pointer == d.Pointer ? el : null;
        }

        private static void Click(IVirtualContentData d, bool right)
        {
            var el = Show(d);
            if (el == null) { Plugin.LogInfo("[db] card element not live"); return; }
            if (right) _grid.HandleCardRightClicked(el);
            else _grid.HandleCardLeftClicked(el);
        }

        /// <summary>Landing on a grid card scrolls it into view and selects it, then raises the grid's hover (the
        /// card lifts and its hover sound plays, as under the mouse). Other nodes are Crimson controls.</summary>
        public override void OnCursorMoved(GraphNode node)
        {
            _search.OnCursorMoved(node);
            // The search box never keeps the game's selection: a frame after it is selected the game puts the
            // selection back where it was, and the cursor would follow it there (Shift+Tab from the side
            // filters bounced straight back). So landing on it leaves the game's selection alone; Enter starts
            // editing through the field itself.
            if (node?.Id.Reference is AControl c && SearchField() is InputFieldControl field && c.Pointer == field.Pointer) return;
            if (node?.Id.Reference is IVirtualContentData d)
            {
                var el = Show(d);
                if (el != null)
                    try { _grid.HandleCardHovered(el); }
                    catch (Exception e) { Plugin.LogInfo("[db] hover failed: " + e.Message); }
                return;
            }
            base.OnCursorMoved(node);
        }

        // ---- search ----

        // The card search box ("Поиск") at the head of the top filter bar. It isn't one of the bar's container
        // children, so the bar's leaves miss it; it is offered first in the filters stop.
        private static readonly EditBox _search = new EditBox(SearchField, Loc.Mark("Search"));

        private static InputFieldControl SearchField()
        {
            var field = Panel()?.GetComponentInChildren<UIDBFilterContainer>(false)?.m_SearchInputField;
            return field != null && field.gameObject.activeInHierarchy ? field : null;
        }

        public override bool LeavesToGame(KeyCode key) => _search.LeavesToGame(key);

        public override void OnPop()
        {
            _search.Reset();
            _spokenCount = -1;
        }

        // The card count as last spoken, and when the list last changed: a search or filter change speaks the
        // new count once the list has settled (typing changes it on every letter).
        private static int _spokenCount = -1;
        private static IntPtr _countEdit;
        private static float _countSettleUntil;
        private static float _countChangedAt;

        /// <summary>The search box's edit-box echo, then the card count after a search or filter change, queued
        /// behind the echo. A sighted player sees the grid refill; the count is what the refill says.</summary>
        public override void Tick()
        {
            _search.Tick();
            int n = _cards.Count;
            // Opening or leaving a deck refilters the grid to its faction: not the player's filter change, and the
            // count isn't news (it was said as "2437 cards" on every return to the deck list).
            IntPtr edit = IntPtr.Zero;
            try { edit = DeckBuilder.Edit?.Pointer ?? IntPtr.Zero; } catch { }
            if (edit != _countEdit) { _countEdit = edit; _spokenCount = -1; _countSettleUntil = Time.realtimeSinceStartup + 2f; }
            if (Time.realtimeSinceStartup < _countSettleUntil) { _spokenCount = n; return; }
            if (_spokenCount < 0) { _spokenCount = n; return; }
            if (n == _spokenCount) { _countChangedAt = 0; return; }
            if (_countChangedAt == 0) { _countChangedAt = Time.realtimeSinceStartup; return; }
            if (Time.realtimeSinceStartup - _countChangedAt < 0.6f) return;
            _spokenCount = n;
            _countChangedAt = 0;
            Speech.Say(n == 0 ? Loc.T("no cards") : Loc.P(n, "{0} card", "{0} cards"), false);
        }

        // ---- filters and bar ----

        // The top filter bar: the search box, then its controls (the provision toggles, reset, mill spares).
        private static void BuildFilters(GraphBuilder b, AControl bar, HashSet<IntPtr> used)
        {
            if (bar == null || !bar.gameObject.activeInHierarchy) return;
            b.BeginStop("db:filters");
            var field = SearchField();
            if (field != null && used.Add(field.Pointer)) b.AddItem(CrimsonNodes.Id(field), _search.Vtable(field));
            foreach (var c in CrimsonNodes.Leaves(bar, used)) b.AddItem(CrimsonNodes.Id(c), Vtable(c));
        }

        private static void BuildLeaves(GraphBuilder b, string stop, AControl root, HashSet<IntPtr> used)
        {
            if (root == null || !root.gameObject.activeInHierarchy) return;
            var leaves = CrimsonNodes.Leaves(root, used);
            if (leaves.Count == 0) return;
            b.BeginStop(stop);
            foreach (var c in leaves) b.AddItem(CrimsonNodes.Id(c), Vtable(c));
        }

        private static NodeVtable Vtable(AControl c)
        {
            // A side filter's dropdown head ("Фракция" over its value "Все") reads both, and whether its options
            // are shown; it is a toggle in the game, but its state is the list's, not a choice.
            var dropdown = c.GetComponentInParent<UIDBFilterDropdown>();
            if (dropdown != null && c.TryCast<GwentUnity.UI.PlayerProfile.UIDropdownButton>() != null)
            {
                var head = CrimsonNodes.Button(c, () => AllTexts(c));
                head.Announcements = new List<NodeAnnouncement>(head.Announcements)
                {
                    new NodeAnnouncement(() => DropdownOpen(dropdown) ? Loc.T("expanded") : Loc.T("collapsed"), live: true, kind: AnnouncementKinds.Value),
                };
                return head;
            }
            var v = CrimsonNodes.Button(c);
            var toggle = c.TryCast<ToggleButtonControl>();
            if (toggle == null) return v;
            v.Announcements = new List<NodeAnnouncement>(v.Announcements)
            {
                new NodeAnnouncement(() => CrimsonNodes.Toggled(toggle) ? Loc.T("checked") : Loc.T("not checked"), live: true, kind: AnnouncementKinds.Value),
            };
            return v;
        }

        private static bool DropdownOpen(UIDBFilterDropdown d)
        {
            try { return d.m_ContentShown; } catch { return false; }
        }

        private static string AllTexts(AControl c)
        {
            var parts = CrimsonNodes.Texts(c);
            return parts.Count > 0 ? string.Join(", ", parts) : CrimsonNodes.Label(c);
        }

        // The input bar's captioned buttons: never selectable (the game reaches them by key), clicked through
        // the container's own callback. Glyph labels ("Создать колоду") are key hints, not buttons.
        private static void BuildBar(GraphBuilder b, UIDBPanel p, HashSet<IntPtr> used)
        {
            var list = CrimsonNodes.BarButtons(p, used, bar => bar.GetComponentInParent<UIDeckListContainer>() != null);
            if (list.Count == 0) return;
            b.BeginStop("db:bar");
            foreach (var c in list) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        // ---- keys ----

        private CardKeys _cardKeys;
        private List<ScreenKey> _keyList;

        public override IReadOnlyList<ScreenKey> ExtraKeys
        {
            get
            {
                if (_keyList != null) return _keyList;
                _cardKeys = new CardKeys(FocusedCard, () => null); // Up/Down read only while a card is focused
                _keyList = new List<ScreenKey>(_cardKeys.Keys)
                {
                    // Always claimed: off a card it must not fall through to Enter (NEW DECK, a filter...).
                    new ScreenKey { Key = KeyCode.Return, Shift = true, Run = () => { if (FocusedCard() != null) Preview(); else Speech.Say(Loc.T("No card."), true); }, Hint = Loc.T("Shift Enter, the card's preview") },
                    new ScreenKey { Key = KeyCode.Return, Ctrl = true, Run = AddCopy, When = OnDeckRow, Hint = Loc.T("Control Enter on a card of your deck, add another copy") },
                    new ScreenKey { Key = KeyCode.PageDown, Run = () => MoveRow(1), Hint = Loc.T("Page Down, next row of cards") },
                    new ScreenKey { Key = KeyCode.PageUp, Run = () => MoveRow(-1), Hint = Loc.T("Page Up, previous row of cards") },
                    new ScreenKey { Key = KeyCode.PageDown, Ctrl = true, Run = () => JumpSection(1), Hint = Loc.T("Control Page Down, next provision cost") },
                    new ScreenKey { Key = KeyCode.PageUp, Ctrl = true, Run = () => JumpSection(-1), Hint = Loc.T("Control Page Up, previous provision cost") },
                    new ScreenKey { Key = KeyCode.RightArrow, Run = () => DeckOptions(true), When = OnDeck, Hint = Loc.T("Right arrow on a deck, its options") },
                    new ScreenKey { Key = KeyCode.LeftArrow, Run = () => DeckOptions(false), When = OnDeck, Hint = null },
                    new ScreenKey { Key = KeyCode.F, Ctrl = true, Run = FocusSearch, Hint = Loc.T("Control F, search cards") },
                };
                return _keyList;
            }
        }

        /// <summary>The card under the cursor: a collection card, or a row of the deck (leader, stratagem, card).</summary>
        private static GwentGameplay.Card FocusedCard()
        {
            var node = GraphNavigator.CurrentNode;
            if (node == null) return null;
            try
            {
                if (node.Id.Reference is IVirtualContentData d) return d.TryCast<VirtualCardContentData>()?.CardReference?.Card;
                if (node.Id.Reference is AControl c) return c.TryCast<UIDeckCardButtonControl>()?.Card?.Card;
            }
            catch { }
            return null;
        }

        private static UIDeckCardButtonControl FocusedRow() =>
            GraphNavigator.CurrentNode?.Id.Reference is AControl c ? c.TryCast<UIDeckCardButtonControl>() : null;

        private static bool OnDeckRow() => FocusedRow() is UIDeckCardButtonControl r && r.Card?.Card != null;

        // The deck list's main button (or one of its shown options) under the cursor, no deck open.
        private static DropdownContainer FocusedDeck()
        {
            if (DeckBuilder.Edit != null || GraphNavigator.CurrentNode?.Id.Reference is not AControl c) return null;
            var d = c.GetComponentInParent<DropdownContainer>();
            if (d == null || c.GetComponentInParent<UIDeckListContainer>() == null) return null;
            return d.MainButton?.Pointer == c.Pointer || ListShown(d) ? d : null;
        }

        private static bool OnDeck() => FocusedDeck() != null;

        // Right shows a deck's options (the game's right click on it), Left hides them; the cursor goes to the deck.
        private static void DeckOptions(bool show)
        {
            var d = FocusedDeck();
            if (d == null) return;
            if (ListShown(d) == show) { Speech.Say(show ? Loc.T("options shown") : Loc.T("options hidden"), true); return; }
            var main = d.MainButton;
            // Back on the deck itself first (from one of its options), quietly: the change is what is said.
            if (GraphNavigator.CurrentNode?.Id.Reference is not AControl cur || cur.Pointer != main.Pointer)
                GraphNavigator.FocusNode(CrimsonNodes.Id(main));
            Navigator.MouseClick(main, right: true);
            if (!show) Speech.Say(Loc.T("options hidden"), true); // showing is said by the deck's live "options shown"
        }

        // Shift+Enter: the game's card preview (a grid card's right click; a deck row's).
        private static void Preview()
        {
            var node = GraphNavigator.CurrentNode;
            if (node?.Id.Reference is IVirtualContentData d) { Click(d, true); return; }
            var row = FocusedRow();
            if (row != null) Navigator.MouseClick(row.TryCast<ButtonControl>(), right: true);
        }

        // Ctrl+Enter on a deck row: one more copy, through the collection grid's own add (the row's card there).
        private static void AddCopy()
        {
            var card = FocusedRow()?.Card;
            if (card == null) return;
            foreach (var e in _cards)
            {
                bool same;
                try { same = e.Card.Template.Id == card.Template.Id && e.Card.Definition.IsPremium == card.Definition.IsPremium; } catch { same = false; }
                if (!same) continue;
                int before = -1;
                try { before = DeckBuilder.Deck()?.GetCardCount(e.Card) ?? -1; } catch { }
                Click(e.Data, false);
                // The game refuses a copy over the limit (one of a gold card, two of a bronze) without a word.
                var cc = e.Card;
                MainThread.Later(0.5f, () =>
                {
                    int after = -1;
                    try { after = DeckBuilder.Deck()?.GetCardCount(cc) ?? -1; } catch { }
                    if (after == before) Speech.Say(Loc.T("No more copies of this card can be added."), true);
                });
                return;
            }
            Speech.Say(Loc.T("Not in the collection as filtered."), true);
        }

        // Ctrl+Page Up/Down: the first card of the next or previous provision cost (the grid's own sections:
        // stratagems, then provisions high to low), said with its cost.
        private static void JumpSection(int dir)
        {
            var cur = GraphNavigator.CurrentNode;
            int i = cur == null ? -1 : _cards.FindIndex(e => e.Id.Equals(cur.Id));
            if (i < 0) { Speech.Say(Loc.T("Not on the collection."), true); return; }
            int Cost(int k) { try { return _cards[k].Card.Template.Provision; } catch { return -1; } }
            int here = Cost(i), j = i;
            if (dir > 0) { while (j < _cards.Count && Cost(j) == here) j++; }
            else
            {
                while (j >= 0 && Cost(j) == here) j--;      // into the previous section...
                if (j < 0) { Speech.Say(Loc.T("First section"), true); return; }
                int prev = Cost(j);
                while (j > 0 && Cost(j - 1) == prev) j--;     // ...to its first card
            }
            if (j >= _cards.Count) { Speech.Say(Loc.T("Last section"), true); return; }
            int cost = Cost(j);
            Speech.Say(cost == 0 ? Loc.T("Stratagems") : Loc.P(cost, "{0} provision", "{0} provisions"), true);
            GraphNavigator.FocusNode(_cards[j].Id);
        }

        // Ctrl+F: the cursor to the search box, the caret in it, ready to type.
        private static void FocusSearch()
        {
            var field = SearchField();
            if (field == null) { Speech.Say(Loc.T("No search box here."), true); return; }
            GraphNavigator.FocusNode(CrimsonNodes.Id(field));
            try { field.ActivateInputField(); }
            catch (Exception e) { Plugin.LogInfo("[db] activate search failed: " + e.Message); }
        }

        // PageUp/PageDown on the card list: one grid row (the grid's current column count) up or down.
        private static void MoveRow(int dir)
        {
            var cur = GraphNavigator.CurrentNode;
            if (cur?.Id.Reference is not IVirtualContentData) return;
            int i = _cards.FindIndex(e => e.Id.Equals(cur.Id));
            if (i < 0) return;
            int cols = 6;
            try { if (_grid != null && _grid.m_CurrentColumns > 0) cols = _grid.m_CurrentColumns; } catch { }
            int j = Math.Clamp(i + dir * cols, 0, _cards.Count - 1);
            if (j != i) GraphNavigator.FocusNode(_cards[j].Id);
        }
    }
}
