using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The deck builder's main panel (<see cref="UIDBPanel"/>): the deck list on the left, the collection grid on
    /// the right, the provision filters and the reset / mill-spares buttons above the grid, the side filter
    /// dropdowns, and the input bar ("Библиотека колод", "Выйти"). Stops, in the game's reading order:
    ///
    /// - Decks: "НОВАЯ КОЛОДА" and one item per deck (its name and leader ability). A deck's options list
    ///   (edit, rename, duplicate, delete…) follows it while the game shows it open.
    /// - Cards: the collection grid as a flat list in the grid's own order, built from the grid's content list,
    ///   not its controls: the grid is virtual (only the rows on screen have controls) and draws its cards
    ///   through a portal. Landing scrolls the grid to the card and selects it (<see
    ///   AVirtualScrollRect.ScrollToElement"/>), as the gamepad does, then raises the grid's hover. Enter is
    ///   the grid's left click, Backspace its right click — the mouse path, so the game's sounds play.
    ///   PageUp/PageDown move a grid row.
    /// - Filters: the provision toggles, reset, mill spares.
    /// - Side filters: the dropdowns (faction, crafting cost, card type, ownership, premium, rarity, card set)
    ///   and their options while shown.
    /// - Buttons: the input bar's captioned buttons, clicked through their container's callback.
    ///
    /// Overlays the game opens on top (the mill-spares window, the card preview, popups) are screens of a
    /// higher layer and take the keyboard while shown.
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
                if (quit != null && quit.gameObject.activeInHierarchy) { Navigator.ClickControl(quit); return true; }
                // A starter deck can't be changed, so its only Exit is the no-save one: nothing is lost.
                var noSave = edit?.m_QuitWithoutSavingBinding;
                bool starter = false;
                try { starter = DeckBuilder.Deck()?.IsStarterDeck == true; } catch { }
                if (starter && noSave != null && noSave.gameObject.activeInHierarchy) { Navigator.ClickControl(noSave); return true; }
            }
            catch { }
            return PressBack();
        }

        // The panel, when the input layer that has the input holds it.
        private static UIDBPanel Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var c = attached[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                var p = c.TryCast<UIDBPanel>();
                if (p != null) return p;
            }
            return null;
        }

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

            BuildDecks(b, p, used);
            BuildDeck(b, p, used);
            if (collection != null) BuildCards(b, collection);
            if (collection != null) BuildFilters(b, collection.FilterContainer, used);
            BuildLeaves(b, "db:side-filters", p.GetComponentInChildren<UIDBFilters>(false), used);
            BuildBar(b, p, used);
        }

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
                // A deck's options (rename, duplicate, delete...) unfold on a right click: Backspace.
                if (dropdown != null && dropdown.MainButton?.Pointer == c.Pointer)
                {
                    var bc = c.TryCast<ButtonControl>();
                    var d = dropdown;
                    v.OnSecondary = () => Navigator.MouseClick(bc, right: true);
                    v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                    {
                        new NodeAnnouncement(() => ListShown(d) ? "options shown" : null, live: true, kind: AnnouncementKinds.Value),
                    };
                }
                b.AddItem(CrimsonNodes.Id(c), v);
            }
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
                var texts = CrimsonNodes.Texts(c);
                // The stat counters ("25", "20", "-") are numbers beside icons; the totals line says them in words.
                if (texts.Count == 0 || texts.TrueForAll(t => t == "-" || int.TryParse(t, out _))) continue;
                rows.Add(c);
            }
            if (rows.Count == 0) return;
            b.BeginStop("db:deck");
            string name = null;
            try { var d = DeckBuilder.Deck(); name = d == null ? null : UiText.Clean(d.LocalizedName ?? d.Name ?? ""); } catch { }
            b.PushContext(string.IsNullOrEmpty(name) ? "Deck" : name);
            b.AddItem(ControlId.Structural("db:deck:totals"), new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(() => DeckBuilder.Stats(), live: true, kind: AnnouncementKinds.Label) },
            });
            foreach (var c in rows)
            {
                used.Add(c.Pointer);
                var control = c;
                b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, () => DeckRow(control)));
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
            foreach (var entry in _cards)
            {
                var card = entry.Card;
                var data = entry.Data;
                b.AddItem(entry.Id, new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => CardReader.Summary(card.Card), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => Premium(card) ? "premium" : null, kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => Owned(card), live: true, kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => New(data) ? "new" : null, kind: AnnouncementKinds.Value),
                    },
                    OnActivate = () => Click(data, false),
                    OnSecondary = () => Click(data, true),
                    OnTooltip = () => Speech.Say(CardReader.Whole(card.Card), true),
                });
            }
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
                return n == 0 ? "not owned" : "x" + n;
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
        private static readonly EditBox _search = new EditBox(SearchField, "Search");

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
            Speech.Say(n == 0 ? "no cards" : n == 1 ? "1 card" : n + " cards", false);
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
                    new NodeAnnouncement(() => DropdownOpen(dropdown) ? "expanded" : "collapsed", live: true, kind: AnnouncementKinds.Value),
                };
                return head;
            }
            var v = CrimsonNodes.Button(c);
            var toggle = c.TryCast<ToggleButtonControl>();
            if (toggle == null) return v;
            v.Announcements = new List<NodeAnnouncement>(v.Announcements)
            {
                new NodeAnnouncement(() => Toggled(toggle) ? "checked" : "not checked", live: true, kind: AnnouncementKinds.Value),
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
            var list = new List<AControl>();
            foreach (var bar in p.GetComponentsInChildren<InputButtonContainer>(false))
            {
                if (bar.GetComponentInParent<UIDeckListContainer>() != null) continue;
                var children = bar.GetChildren();
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                    if (CrimsonNodes.Texts(c).Count == 0 || !used.Add(c.Pointer)) continue;
                    list.Add(c);
                }
            }
            if (list.Count == 0) return;
            b.BeginStop("db:bar");
            foreach (var c in list) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        // ---- keys ----

        public override IReadOnlyList<ScreenKey> ExtraKeys => _keys;

        private static readonly ScreenKey[] _keys =
        {
            new ScreenKey { Key = KeyCode.PageDown, Run = () => MoveRow(1), Hint = "Page Down, next row of cards" },
            new ScreenKey { Key = KeyCode.PageUp, Run = () => MoveRow(-1), Hint = "Page Up, previous row of cards" },
            new ScreenKey { Key = KeyCode.F, Ctrl = true, Run = FocusSearch, Hint = "Control F, search cards" },
        };

        // Ctrl+F: the cursor to the search box, the caret in it, ready to type.
        private static void FocusSearch()
        {
            var field = SearchField();
            if (field == null) { Speech.Say("No search box here.", true); return; }
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

        private static bool Toggled(ToggleButtonControl t)
        {
            try { return t.IsToggled; } catch { return false; }
        }
    }
}
