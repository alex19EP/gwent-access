using System;
using System.Collections.Generic;
using System.Linq;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The shop. One panel showing one view at a time, each built as on screen:
    ///
    /// The front page: the promotions, the category tiles (kegs, powder, cosmetics, packs, single player,
    /// event), and the "open supplies" button that leads to the player's own kegs.
    ///
    /// A category page (kegs, powder, cosmetics, packs, events, single player, open supplies): its tiles in
    /// the order drawn, under the section headings the game draws between them; then the quantity/price
    /// options; then the details panel of the product the cursor is on — its title, time left, description
    /// lines, the quantity, the buy button. The game shows a product's details on hover, so landing on a tile
    /// is all it takes; the buy button and the quantity arrows are mouse-only in the game (a gamepad uses
    /// glyph keys), so the details stop gives them to the keyboard. Every price says its currency, named as
    /// the game names the currency counters above (a real-money price already carries its symbol).
    ///
    /// Enter is the game's own click everywhere: buying goes through the game's purchase flow, confirmations,
    /// store overlays and sounds included. The window's bottom bar (Back/Exit, Open) closes the graph.
    ///
    /// Active while the shop panel is attached to the input layer that has the input; popups and the product
    /// preview take the input and step in above it.
    /// </summary>
    internal sealed class ShopScreen : Screen
    {
        public override string Key => "shop";
        public override int Layer => 1;
        public override string ScreenName => GameText.Get("shopmenu_title", "Shop");

        // While a keg is being opened, its own screen has the page.
        public override bool IsActive() => Panel() != null && KegOpeningScreen.Keg() == null;

        // The front page's category tiles by kind, as last drawn: they name the pages they lead to, however
        // the page was reached (a promotion opens the packs page too).
        private static readonly Dictionary<ShopTileType, string> TileTitles = new Dictionary<ShopTileType, string>();

        private static UIShopMenuPanel Panel() => CrimsonNodes.ActivePanel<UIShopMenuPanel>();

        /// <summary>The view the panel shows: the one active child of its views root.</summary>
        private static AControl ShownView(UIShopMenuPanel p)
        {
            var root = p.m_ViewsRoot;
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                var t = root.GetChild(i);
                if (!t.gameObject.activeInHierarchy) continue;
                var c = t.GetComponent<AControl>();
                if (c != null && c.TryCast<UIShopShutterView>() == null) return c;
            }
            return null;
        }

        // The landing write-back selects a control in the game and raises its hover. The buy button, the
        // quantity arrows and the details lines aren't selectable in the game; landing on them leaves the
        // game's selection on the product they belong to.
        public override void OnCursorMoved(GraphNode node)
        {
            if (node?.Id.Reference is AControl c && !CrimsonNodes.Selectable(c)) return;
            base.OnCursorMoved(node);
        }


        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            // Mid-transition: the shutter covers the page; declare nothing until the next view shows.
            if (p.m_ShutterView != null && p.m_ShutterView.gameObject.activeInHierarchy) return;
            var view = ShownView(p);
            if (view == null) return;

            bool front = view.TryCast<UIMainShopView>() != null;
            if (front) NoteTileTitles(view);
            string title = front ? null : ViewTitle(view);
            if (title != null) b.PushContext(title);

            var used = new HashSet<IntPtr>();
            var products = new List<AControl>();
            var prices = new List<AControl>();
            var other = new List<(Transform Group, List<AControl> Controls)>();
            foreach (var c in CrimsonNodes.Leaves(view, used))
            {
                if (Skipped(c)) continue;
                if (c.GetComponentInParent<UIGeneralDescriptionView>() != null) continue;
                if (c.GetComponentInParent<UIProductTilesView>() != null) prices.Add(c);
                else if (TileOf(c) != null) products.Add(c);
                else
                {
                    var group = LayoutParent(c, view);
                    int at = other.FindIndex(g => g.Group.Pointer == group.Pointer);
                    if (at < 0) other.Add((group, new List<AControl> { c }));
                    else other[at].Controls.Add(c);
                }
            }

            if (products.Count > 0) AddProducts(b, view, products);
            if (prices.Count > 0)
            {
                b.BeginStop("prices");
                foreach (var c in prices)
                {
                    var tile = c;
                    b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, () => PricedLabel(tile)));
                }
            }
            foreach (var d in view.GetComponentsInChildren<UIGeneralDescriptionView>(false)) AddDetails(b, d);
            for (int i = 0; i < other.Count; i++)
            {
                b.BeginStop("group:" + other[i].Group.name);
                foreach (var c in other[i].Controls) AddOther(b, c);
            }
            if (front) AddSupplies(b, view.Cast<UIMainShopView>(), used);

            if (title != null) b.PopContext();
            AddWindowButtons(b, p, used);
        }

        // The single-player page's screenshot strip: pictures only, nothing a word could say. The details
        // panel's scroll area: selectable only so a gamepad can scroll the text the details stop reads.
        private static bool Skipped(AControl c)
        {
            string name = c.gameObject.name;
            return name.StartsWith("ImageScrollerButton") || name == "Collider";
        }

        // The troll with the barrel, "Open supplies": the mouse's way into the opening page. The game keeps it
        // out of keyboard navigation (never currently selectable), so the leaf walk doesn't find it; it is
        // offered as its own stop whenever the game lets it be clicked.
        private static void AddSupplies(GraphBuilder b, UIMainShopView view, HashSet<IntPtr> used)
        {
            var c = view.m_TrollBarrelButton;
            if (c == null || !c.gameObject.activeInHierarchy || !c.IsSelectable || !used.Add(c.Pointer)) return;
            b.BeginStop("supplies");
            b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, () => AllTexts(c)));
        }

        // ---- the page's name ----

        // A category page is named as the front-page tile that leads to it; before the front page was seen,
        // by the game's own page titles (the packs page has none: its key names only the starter packs).
        private static string ViewTitle(AControl view)
        {
            if (view.TryCast<UIProductOpeningView>() != null) return GameText.Get("shopmenu_opening_product_title", "Open supplies");
            (ShopTileType Kind, string Key)? page =
                view.TryCast<UIKegsView>() != null ? (ShopTileType.CardKegs, "shopmenu_kegs_title")
                : view.TryCast<UIMeteoritePowderView>() != null ? (ShopTileType.MeteoritePowder, "shopmenu_powder_title")
                : view.TryCast<UICosmeticShopView>() != null ? (ShopTileType.Bundles, "shopmenu_trinkets_title")
                : view.TryCast<UIPacksView>() != null ? (ShopTileType.Packs, null)
                : view.TryCast<UICampaignsShopView>() != null ? (ShopTileType.Campaigns, null)
                : view.TryCast<UIEventsView>() != null ? (ShopTileType.Events, null)
                : null;
            if (page == null) return null;
            if (TileTitles.TryGetValue(page.Value.Kind, out var title)) return title;
            return page.Value.Key == null ? null : GameText.Get(page.Value.Key, null);
        }

        private static void NoteTileTitles(AControl view)
        {
            foreach (var t in view.GetComponentsInChildren<UIShopTile>(false))
            {
                try
                {
                    string title = UiText.NullIfEmpty(UiText.Clean(t.m_TileTitle?.Text?.text));
                    if (title != null) TileTitles[t.m_Type] = title;
                }
                catch { }
            }
        }

        // ---- the front page and other loose controls ----

        // The nearest layout container (a row or column of tiles) a control sits in below the view; one stop
        // each, so the promotions, the categories and the supplies button are separate stops.
        private static Transform LayoutParent(AControl c, AControl view)
        {
            for (var t = c.transform.parent; t != null && t.Pointer != view.transform.Pointer; t = t.parent)
            {
                var a = t.GetComponent<AContainer>();
                if (a != null && (a.TryCast<HorizontalContainer>() != null || a.TryCast<VerticalContainer>() != null
                    || a.TryCast<GridContainer>() != null)) return t;
            }
            return view.transform;
        }

        // A front-page tile reads all its words ("Выгодно!, Стартовый набор").
        private static void AddOther(GraphBuilder b, AControl c)
        {
            var v = CrimsonNodes.Button(c, () => PromoLabel(c) ?? AllTexts(c));
            v.OnTooltip = () => Speech.Say(PromoLabel(c) ?? CrimsonNodes.Details(c), true);
            b.AddItem(CrimsonNodes.Id(c), v);
        }

        /// <summary>A promotion tile: its headline and what it sells ("Выгодно!, Набор «Чудовища»"), then its
        /// countdown while one shows. Read from the promotion itself: selecting the tile blanks its labels for a
        /// moment while it refreshes them, so the words on screen at the moment of landing can be missing.
        /// Null for any other control.</summary>
        private static string PromoLabel(AControl c)
        {
            try
            {
                var tile = c.GetComponentInParent<UIPromoTile>();
                var data = tile?.m_PromotionData;
                if (data == null) return null;
                var parts = new List<string>();
                foreach (var s in new[] { data.Title, data.Description })
                    if (UiText.NullIfEmpty(UiText.Clean(s)) is string text && !parts.Contains(text)) parts.Add(text);
                foreach (var t in tile.GetComponentsInChildren<TMPro.TMP_Text>(false))
                    if (t.gameObject.name == "TimerText" && UiText.NullIfEmpty(UiText.Clean(t.text)) is string timer) parts.Add(timer);
                return parts.Count > 0 ? string.Join(", ", parts) : null;
            }
            catch { return null; }
        }

        // ---- product tiles ----

        /// <summary>The product tile a control belongs to: a grid tile (kegs, cosmetics, packs, events, the
        /// player's own kegs) or a single-player campaign tile.</summary>
        private static Component TileOf(AControl c) =>
            (Component)c.GetComponentInParent<UIBigProductTile>() ?? c.GetComponentInParent<UICampaignTile>();

        // Tiles in the order drawn (top to bottom, then left to right), each under the section heading drawn
        // above it ("Осенний цикл", "Бочки фракций"); the grid's own child order puts the headings last.
        private static void AddProducts(GraphBuilder b, AControl view, List<AControl> products)
        {
            var entries = new List<(float Y, float X, AControl Tile, string Heading)>();
            foreach (var c in products)
            {
                var pos = TileOf(c).transform.position;
                entries.Add((pos.y, pos.x, c, null));
            }
            foreach (var s in view.GetComponentsInChildren<UIProductSection>(false))
            {
                string heading = UiText.NullIfEmpty(string.Join(", ", CrimsonNodes.Texts(s)));
                if (heading == null) continue;
                var pos = s.transform.position;
                entries.Add((pos.y, pos.x, null, heading));
            }
            // A heading spans the grid's row; within a row, left to right. Rows a few pixels apart are one row.
            entries = entries.OrderByDescending(e => Mathf.Round(e.Y / 8f)).ThenBy(e => e.Tile == null ? 0 : 1).ThenBy(e => e.X).ToList();

            b.BeginStop("products");
            bool inSection = false;
            foreach (var e in entries)
            {
                if (e.Heading != null)
                {
                    if (inSection) b.PopContext();
                    b.PushContext(e.Heading);
                    inSection = true;
                    continue;
                }
                var c = e.Tile;
                var v = CrimsonNodes.Button(c, () => TileLabel(c));
                // Space: the details panel, which shows the product under the cursor.
                v.OnTooltip = () =>
                {
                    var d = view.GetComponentInChildren<UIGeneralDescriptionView>(false);
                    var lines = d == null ? null : DetailLines(d);
                    Speech.Say(lines != null && lines.Count > 0 ? string.Join(". ", lines) : CrimsonNodes.Details(c), true);
                };
                b.AddItem(CrimsonNodes.Id(c), v);
            }
            if (inSection) b.PopContext();
        }

        // A tile's words. The player's own kegs show only a count ("x2"); the keg's name is its product's.
        private static string TileLabel(AControl c)
        {
            var parts = new List<string>();
            try
            {
                var own = c.GetComponentInParent<UIKegAndPackTile>()?.GetProductData();
                if (own != null && UiText.NullIfEmpty(UiText.Clean(own.Title)) is string name) parts.Add(name);
            }
            catch { }
            foreach (var s in CrimsonNodes.Texts(c)) if (!parts.Contains(s)) parts.Add(s);
            return parts.Count > 0 ? string.Join(", ", parts) : CrimsonNodes.Label(c);
        }

        // ---- the details panel ----

        internal static void AddDetails(GraphBuilder b, UIGeneralDescriptionView d)
        {
            var lines = DetailLines(d);
            var quantity = d.m_QuantityControl;
            bool hasQuantity = quantity != null && quantity.gameObject.activeInHierarchy;
            var buy = d.m_BuyButton;
            bool hasBuy = buy != null && buy.gameObject.activeInHierarchy && (d.m_ButtonBuyRoot == null || d.m_ButtonBuyRoot.activeInHierarchy);
            var gift = d.m_GiftButton;
            bool hasGift = gift != null && gift.gameObject.activeInHierarchy;
            var preview = d.m_PreviewButton;
            bool hasPreview = preview != null && preview.gameObject.activeInHierarchy;
            if (lines.Count == 0 && !hasQuantity && !hasBuy && !hasGift && !hasPreview) return;

            string key = CrimsonNodes.PathKey(d.transform, null);
            b.BeginStop("details");
            // Lines keyed by position: hovering another product rewrites them in place, under the cursor.
            for (int i = 0; i < lines.Count; i++)
            {
                int index = i;
                b.AddItem(ControlId.Structural(key + ":line" + i), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => LineAt(d, index), kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(string.Join(". ", DetailLines(d)), true),
                });
            }
            if (hasQuantity) b.AddItem(ControlId.Referenced(quantity, CrimsonNodes.PathKey(quantity.transform, null)), Quantity(quantity));
            if (hasBuy) b.AddItem(CrimsonNodes.Id(buy), CrimsonNodes.Button(buy, () => PricedLabel(buy)));
            if (hasGift) b.AddItem(CrimsonNodes.Id(gift), CrimsonNodes.Button(gift, () => AllTexts(gift)));
            // The preview button is an icon; a gamepad player sees it as the preview action's key hint.
            if (hasPreview) b.AddItem(CrimsonNodes.Id(preview), CrimsonNodes.Button(preview, () => GameText.Get("input_preview", "Preview")));
        }

        private static string LineAt(UIGeneralDescriptionView d, int index)
        {
            var lines = DetailLines(d);
            return index < lines.Count ? lines[index] : null;
        }

        /// <summary>What the details panel shows, line by line: badge, title, time left, the description's
        /// paragraphs (a bundle lists its items one per paragraph), the additional text.</summary>
        private static List<string> DetailLines(UIGeneralDescriptionView d)
        {
            var lines = new List<string>();
            void Add(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return;
                foreach (var part in raw.Replace("\\n", "\n").Split('\n'))
                {
                    string s = UiText.Clean(part);
                    if (s.Length > 0 && !lines.Contains(s)) lines.Add(s);
                }
            }
            try
            {
                if (d.m_Badge != null && d.m_Badge.activeInHierarchy) Add(d.m_BadgeValue?.text);
                if (CrimsonNodes.Shown(d.m_TitleText)) Add(d.m_TitleText.text);
                if (d.m_ProductTimerObject != null && d.m_ProductTimerObject.activeInHierarchy)
                {
                    string timer = string.Join(" ", CrimsonNodes.Texts(d.m_ProductTimerObject.transform));
                    Add(timer);
                }
                if (CrimsonNodes.Shown(d.m_DescriptionText)) Add(d.m_DescriptionText.text);
                if (CrimsonNodes.Shown(d.m_AdditionalText)) Add(d.m_AdditionalText.text);
            }
            catch (Exception e) { Plugin.LogInfo("[shop] details failed: " + e.Message); }
            return lines;
        }

        /// <summary>The quantity picker: its question ("Сколько?") and the number; Left/Right are its arrows,
        /// clicked as the mouse clicks them, so the price and the buy button follow.</summary>
        private static NodeVtable Quantity(UIQuantityControl q)
        {
            string Title() => UiText.NullIfEmpty(string.Join(" ", CrimsonNodes.Texts(q.m_QuantityTitle))) ?? "Quantity";
            string Value() => UiText.NullIfEmpty(UiText.Clean(q.m_QuantityText?.text));
            return new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(Title, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(Value, live: true, kind: AnnouncementKinds.Value),
                },
                OnAdjust = (dir, large) =>
                {
                    var arrow = dir > 0 ? q.m_RightButton : q.m_LeftButton;
                    if (arrow != null && arrow.gameObject.activeInHierarchy) Navigator.ClickControl(arrow);
                },
                StateText = Value,
                OnTooltip = () => Speech.Say(Title() + " " + Value(), true),
            };
        }

        // ---- prices ----

        /// <summary>A control's words with each price in it said with its currency: "x1, 100 Руда",
        /// "Купить, 100 Руда", "x2, 2,99 $".</summary>
        private static string PricedLabel(AControl c)
        {
            var widgets = c.GetComponentsInChildren<UIShopPriceWidget>(false);
            var priceTexts = new HashSet<string>();
            var prices = new List<string>();
            foreach (var w in widgets)
            {
                string amount = UiText.Clean(w.m_Price?.text);
                if (amount.Length == 0) continue;
                priceTexts.Add(amount);
                prices.Add(PriceText(w, amount));
            }
            var parts = CrimsonNodes.Texts(c).Where(s => !priceTexts.Contains(s)).ToList();
            parts.AddRange(prices);
            return parts.Count > 0 ? string.Join(", ", parts) : CrimsonNodes.Label(c);
        }

        private static string PriceText(UIShopPriceWidget w, string amount)
        {
            string currency = CurrencyName(w);
            string s = currency == null ? amount : amount + " " + currency;
            // A struck-through price is the old one, beside the discounted price.
            if (w.m_CrossOut != null && w.m_CrossOut.activeInHierarchy) s += ", old price";
            return s;
        }

        /// <summary>The name of a price's currency, as the currency counters above the shop name theirs on
        /// hover ("Руда"). The price shows the currency as an icon: the widget's own ore, scraps, powder and
        /// reward-point icons, or (event currency) the counter's own icon. Null for a real-money price.</summary>
        private static string CurrencyName(UIShopPriceWidget w)
        {
            try
            {
                if (w.m_CurrencyIcon == null || !w.m_CurrencyIcon.gameObject.activeInHierarchy) return null;
                string icon = SpriteName(w.m_CurrencyIcon);
                if (icon == null) return null;
                var overlay = UnityEngine.Object.FindObjectOfType<UIPlayerInfoOverlay>();
                if (overlay == null) return null;
                AControl counter = null;
                if (icon == w.m_OreIcon?.name) counter = overlay.m_GoldButton;
                else if (icon == w.m_ScrapIcon?.name) counter = overlay.m_ScrapsButton;
                else if (icon == w.m_PowderIcon?.name) counter = overlay.m_PowderButton;
                else if (icon == w.m_RewardPointsIcon?.name) counter = overlay.m_RewardButton;
                else
                {
                    foreach (var c in new AControl[] { overlay.m_EventButton, overlay.m_GoldButton, overlay.m_ScrapsButton, overlay.m_PowderButton, overlay.m_RewardButton })
                        if (c != null && SpriteNames(c).Contains(icon)) { counter = c; break; }
                }
                return counter == null ? null : CrimsonNodes.TooltipText(counter);
            }
            catch (Exception e) { Plugin.LogInfo("[shop] currency failed: " + e.Message); return null; }
        }

        private static string SpriteName(Component c)
        {
            foreach (var img in c.GetComponentsInChildren<UnityEngine.UI.Image>(false))
                if (img.sprite != null) return img.sprite.name.Replace("(Clone)", "");
            return null;
        }

        private static HashSet<string> SpriteNames(Component c)
        {
            var names = new HashSet<string>();
            foreach (var img in c.GetComponentsInChildren<UnityEngine.UI.Image>(false))
                if (img.sprite != null) names.Add(img.sprite.name.Replace("(Clone)", ""));
            return names;
        }

        // ---- the window ----

        // The bottom bar (Back or Exit, Open, the icon buttons) and the user agreement link beside the buy
        // button, in the game's order.
        private static void AddWindowButtons(GraphBuilder b, UIShopMenuPanel p, HashSet<IntPtr> used)
        {
            var rest = CrimsonNodes.BarButtons(new[] { p.Buttons, p.ButtonsLeft, p.ButtonsRight }, used);
            var eula = p.m_EulaLink;
            if (eula != null && eula.gameObject.activeInHierarchy && CrimsonNodes.Selectable(eula) && used.Add(eula.Pointer)) rest.Add(eula);
            if (rest.Count == 0) return;
            b.BeginStop("buttons");
            foreach (var c in rest) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        // ---- helpers ----

        private static string AllTexts(AControl c)
        {
            var parts = CrimsonNodes.Texts(c);
            return parts.Count > 0 ? string.Join(", ", parts) : CrimsonNodes.Label(c);
        }
    }
}
