using System;
using System.Collections.Generic;
using System.Text;
using Access.Core.Graph;
using CrimsonUI;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Graph nodes over Gwent's own UI controls (CrimsonUI's AControl tree): finding the controls a sighted
    /// player can use right now, giving each a structural id that survives the game rebuilding the object, and
    /// a vtable that reads the control's text and clicks it through the game's own handler.
    /// </summary>
    internal static class CrimsonNodes
    {
        /// <summary>Every usable leaf control under <paramref name="root"/>, in the game's child order: active
        /// in the scene and selectable right now. Containers are walked, not returned. A control can sit in two
        /// containers (the main menu's event button is also in a ProxyEventContainer); it is returned once, and
        /// passing the same <paramref name="seen"/> to several calls keeps it once across roots too.</summary>
        public static List<AControl> Leaves(AControl root, HashSet<IntPtr> seen = null)
        {
            var found = new List<AControl>();
            Walk(root, found, seen ?? new HashSet<IntPtr>(), 0);
            return found;
        }

        private static void Walk(AControl c, List<AControl> found, HashSet<IntPtr> seen, int depth)
        {
            if (c == null || depth > 16 || !c.gameObject.activeInHierarchy) return;
            var container = c.TryCast<AContainer>();
            if (container == null)
            {
                if (c.IsCurrentlySelectable() && !Noise(c) && seen.Add(c.Pointer)) found.Add(c);
                return;
            }
            var children = container.GetChildren();
            if (children == null) return;
            for (int i = 0; i < children.Count; i++) Walk(children[i], found, seen, depth + 1);
        }

        // Selectable but nothing to a player: a key-glyph label with no words (the bar's "Label Basic Glyph",
        // shown beside a button for its key) and a list's scrollbar ("Vertical Scroll Bar Prefab").
        private static bool Noise(AControl c)
        {
            try
            {
                string n = c.gameObject.name.Replace(" ", "");
                if (n.IndexOf("ScrollBar", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return c.TryCast<GlyphLabelControl>() != null && Texts(c).Count == 0;
            }
            catch { return false; }
        }

        /// <summary>The first shown control of type <typeparamref name="T"/> attached to the input layer that has
        /// the input (a screen's panel), or to <paramref name="layer"/>.</summary>
        public static T ActivePanel<T>(CrimsonInputLayer layer = null) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            layer ??= CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var c = attached[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                var p = c.TryCast<T>();
                if (p != null) return p;
            }
            return null;
        }

        /// <summary>
        /// The buttons of the shown button bars (<see cref="InputButtonContainer"/>) under <paramref name="root"/>,
        /// in bar order. The game never makes them selectable (it reaches them by glyph key), so
        /// <see cref="Leaves"/> misses them; clicking goes through the bar's own callback. A bar also draws an icon
        /// twin of a captioned button (the shop's Exit beside a "Back" arrow, each Journey toggle twice) and spare
        /// empty slots: only the buttons the bar registers are its own, or where it registers none, the captioned
        /// ones. <paramref name="used"/> keeps a button already offered elsewhere out; <paramref name="skip"/>
        /// leaves out a bar another part of the screen reads.
        /// </summary>
        public static List<AControl> BarButtons(Component root, HashSet<IntPtr> used = null, Func<InputButtonContainer, bool> skip = null)
            => root == null ? new List<AControl>() : BarButtons(root.GetComponentsInChildren<InputButtonContainer>(false), used, skip);

        public static List<AControl> BarButtons(IEnumerable<InputButtonContainer> bars, HashSet<IntPtr> used = null, Func<InputButtonContainer, bool> skip = null)
        {
            var list = new List<AControl>();
            used ??= new HashSet<IntPtr>();
            foreach (var bar in bars)
            {
                if (bar == null || !bar.gameObject.activeInHierarchy || (skip != null && skip(bar))) continue;
                var registered = Registered(bar);
                var children = bar.GetChildren();
                for (int i = 0; children != null && i < children.Count; i++)
                {
                    var c = children[i];
                    if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                    if (registered != null ? !registered.Contains(c.Pointer) : Texts(c).Count == 0) continue;
                    if (used.Add(c.Pointer)) list.Add(c);
                }
            }
            return list;
        }

        private static HashSet<IntPtr> Registered(InputButtonContainer bar)
        {
#if GWENT
            try
            {
                var map = bar.m_Buttons;
                if (map == null || map.Count == 0) return null;
                var set = new HashSet<IntPtr>();
                foreach (var kv in map) if (kv.Key != null) set.Add(kv.Key.Pointer);
                return set;
            }
            catch { return null; }
#else
            return null; // Thronebreaker's older bar: no registry to read
#endif
        }

#if GWENT
        /// <summary>
        /// Scroll a virtual list (<see cref="AVirtualScrollRect"/>, <see cref="AInfiniteVirtualScrollRect"/>) so
        /// its item at <paramref name="index"/> is drawn, as a gamepad player's selection scrolls it: a long
        /// list (Journey's 100 steps, the cycle's levels) only draws the items in view. ScrollToIndex takes the
        /// tween's easing, a DOTween type the mod has no reference to, and the game throws on a missing one, so
        /// the call is made by reflection.
        /// </summary>
        public static void ScrollTo(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase list, int index)
        {
            if (list == null) return;
            try
            {
                var type = list.GetType();
                if (!_scrolls.TryGetValue(type, out var call))
                {
                    var m = type.GetMethod("ScrollToIndex");
                    var nullable = m.GetParameters()[3].ParameterType; // Il2CppSystem.Nullable<Ease>
                    var ease = Activator.CreateInstance(nullable, Enum.Parse(nullable.GetGenericArguments()[0], "OutQuad"));
                    _scrolls[type] = call = (m, ease);
                }
                call.Method.Invoke(list, new object[] { index, true, false, call.Ease });
            }
            catch (Exception e) { Plugin.LogInfo("[scroll] " + (e.InnerException ?? e).Message); }
        }

        private static readonly Dictionary<Type, (System.Reflection.MethodInfo Method, object Ease)> _scrolls =
            new Dictionary<Type, (System.Reflection.MethodInfo, object)>();
#endif

        /// <summary>The node id for a control: its full scene path (<see cref="PathKey"/>), backed by the control.</summary>
        public static ControlId Id(AControl c) => ControlId.Referenced(c, PathKey(c.transform, null));

        /// <summary>A stable id for a control: its GameObject path below <paramref name="root"/>, with sibling
        /// indexes so look-alike clones ("SocialFeatureButton(Clone)") stay distinct. Survives the game
        /// destroying and recreating the same control.</summary>
        public static string PathKey(Transform t, Transform root)
        {
            var sb = new StringBuilder();
            for (; t != null && (root == null || t.Pointer != root.Pointer); t = t.parent)
                sb.Insert(0, "/" + t.name + "#" + t.GetSiblingIndex());
            return sb.ToString();
        }

        /// <summary>The control's distinct visible texts (TextMeshPro and legacy UI text), rich-text stripped.</summary>
        public static List<string> Texts(Component c)
        {
            var parts = new List<string>();
            try
            {
                foreach (var t in c.GetComponentsInChildren<TMPro.TMP_Text>(false)) AddText(parts, t.text);
                foreach (var t in c.GetComponentsInChildren<UnityEngine.UI.Text>(false)) AddText(parts, t.text);
            }
            catch { }
            return parts;
        }

        private static void AddText(List<string> parts, string raw)
        {
            string s = UiText.Clean(raw);
            if (s.Length > 0 && !s.StartsWith("[MISSING]") && !UiText.IsPlaceholder(s) && !parts.Contains(s)) parts.Add(s);
        }

        /// <summary>
        /// A button-like control: label = its first text (the title; icon-only controls fall back to the legacy
        /// describer's names), Space = all its texts, Enter = the game's click. <paramref name="label"/>
        /// overrides the label when the recipe knows better.
        /// </summary>
        public static NodeVtable Button(AControl c, Func<string> label = null)
        {
            var anns = new List<NodeAnnouncement>
            {
                new NodeAnnouncement(label ?? (() => Label(c)), kind: AnnouncementKinds.Label),
                new NodeAnnouncement(() => SafeEnabled(c) ? null : Loc.T("unavailable"), live: true, kind: AnnouncementKinds.Enabled),
            };
#if GWENT // cosmetics thumbnails: Gwent's vanity UI, absent from Thronebreaker
            var thumb = c.TryCast<VirtualSkinThumbnailButton>();
            if (thumb != null)
            {
                if (label == null) anns[0] = new NodeAnnouncement(() => VanityLabel(thumb), kind: AnnouncementKinds.Label);
                anns.Add(new NodeAnnouncement(() => thumb.m_VanityItem?.IsLocked == true
                    ? GameText.Get("cosmetic_locked", "locked") : null, live: true, kind: AnnouncementKinds.Value));
                anns.Add(new NodeAnnouncement(() => thumb.CanShowSelected && Shown(thumb.m_SelectedFrame) ? Loc.T("selected") : null,
                    live: true, kind: AnnouncementKinds.Selected));
                // The description panel beside the picker shows the choice under the cursor (its group, "Basic",
                // and what it is): read with the picker's choices, as a sighted player reads the panel. The slot
                // list's thumbnails don't change it.
                if (thumb.GetComponentInParent<GwentUnity.UICollapsibleVanityList>() != null)
                    anns.Add(new NodeAnnouncement(() => VanityDescription(VanityLabel(thumb)), live: true, kind: AnnouncementKinds.Tooltip));
            }
#endif
            return new NodeVtable
            {
                Announcements = anns,
                // A disabled button does nothing when clicked: say so rather than stay silent.
                OnActivate = () => { if (SafeEnabled(c)) Navigator.ClickControl(c); else Speech.Say(Loc.T("Not available right now."), true); },
                OnTooltip = () => Speech.Say(Details(c), true),
            };
        }

#if GWENT
        /// <summary>
        /// A cosmetics thumbnail (the deck screen's customise buttons, the cosmetics picker's items and tabs)
        /// is a picture only; it reads as what the picture shows: the item's own name, after the category
        /// tooltip the game gives a customise button on hover ("Изменить рубашку"), then its visible unlock
        /// progress ("0/3").
        /// </summary>
        // The words of the cosmetics description panel, less the item's name already said.
        private static string VanityDescription(string label)
        {
            try
            {
                var panel = UnityEngine.Object.FindObjectOfType<GwentUnity.UIVanitySimpleDescription>();
                if (panel == null || !panel.gameObject.activeInHierarchy) return null;
                // The panel describes the previewed choice, which isn't always the one under the cursor: read it
                // only on the choice whose name it shows.
                var name = UiText.Clean(panel.m_NameLabel?.Text?.text ?? "");
                // A choice's label starts with its name ("Hallowed Woods Cardback, 0 of 4"); a substring test would
                // match the equipped "Cardback" inside every cardback's name.
                if (string.IsNullOrEmpty(name) || label == null
                    || !(label.Equals(name, StringComparison.OrdinalIgnoreCase) || label.StartsWith(name + ",", StringComparison.OrdinalIgnoreCase))) return null;
                var parts = new List<string>();
                foreach (var t in Texts(panel))
                    if (label.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0 && !parts.Contains(t)) parts.Add(t);
                return parts.Count > 0 ? string.Join(", ", parts) : null;
            }
            catch { return null; }
        }

        private static string VanityLabel(VirtualSkinThumbnailButton t)
        {
            var parts = new List<string>();
            try
            {
                if (VanityTooltip(t) is string tip) parts.Add(tip);
                var key = t.m_VanityItem?.NameKey;
                if (key != null && GameText.Get(key, null) is string name && !parts.Contains(name)) parts.Add(name);
            }
            catch { }
            foreach (var s in Texts(t)) if (!parts.Contains(s)) parts.Add(s);
            return parts.Count > 0 ? string.Join(", ", parts) : UiText.DescribeSelection(t);
        }

        // The hover tooltip of a customise button ("Change Game Board"), from the stack panel's config for its
        // type: the main menu's own buttons, and the cosmetics picker's slot list, which shows the same tooltips on
        // hover. The picker's choices share a type with their slot but have no tooltip.
        private static string VanityTooltip(VirtualSkinThumbnailButton t)
        {
            var panel = t.GetComponentInParent<GwentUnity.MainMenu.UIMainMenuCardStackPanel>();
            var buttons = panel?.m_VanityButtons;
            bool own = false;
            for (int i = 0; buttons != null && i < buttons.Count && !own; i++) own = buttons[i]?.Pointer == t.Pointer;
            if (!own)
            {
                if (!InSlotList(t)) return null;
                panel = null;
                foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<GwentUnity.MainMenu.UIMainMenuCardStackPanel>())
                    if (o != null && o.m_ButtonConfigs != null) { panel = o; break; }
                if (panel == null) return null;
            }
            foreach (var cfg in panel.m_ButtonConfigs)
                if (cfg.VanityType == t.VanityType) return GameText.Get(cfg.TooltipKey, null);
            return null;
        }

        /// <summary>A thumbnail of the cosmetics picker's slot list (board, music, leader skin, cardback, coin).</summary>
        internal static bool InSlotList(AControl t)
        {
            var inv = t.GetComponentInParent<GwentUnity.UIVanityInventory>();
            var list = inv?.m_CategoryList;
            return list != null && t.transform.IsChildOf(list.transform);
        }
#endif

#if GWENT
        internal static string DeckRowText(AControl c)
        {
            try
            {
                var row = c.TryCast<GwentVisuals.UIDeckCardButtonControl>();
                var card = row?.Card?.Card;
                if (card == null) return null;
                string copies = Texts(c).Find(t => t.Length > 1 && t[0] == 'x' && int.TryParse(t.Substring(1), out _));
                return CardReader.Summary(card) + (copies != null ? ", " + copies : "");
            }
            catch { return null; }
        }
#endif

        /// <summary>Drawn now: the object is active in the scene. False for a destroyed one.</summary>
        public static bool Shown(Component c)
        {
            try { return c != null && c.gameObject.activeInHierarchy; } catch { return false; }
        }

        public static bool Shown(GameObject g)
        {
            try { return g != null && g.activeInHierarchy; } catch { return false; }
        }

        /// <summary>The control's layer can select it now. False for a destroyed one.</summary>
        public static bool Selectable(AControl c)
        {
            try { return c != null && c.IsCurrentlySelectable(); } catch { return false; }
        }

        public static bool Toggled(ToggleButtonControl t)
        {
            try { return t != null && t.IsToggled; } catch { return false; }
        }

        /// <summary>A label's words, cleaned; "" while it is hidden or gone.</summary>
        public static string Text(Label l)
        {
            try { return l != null && l.gameObject.activeInHierarchy ? UiText.Clean(l.Text?.text ?? "") : ""; } catch { return ""; }
        }

        public static string Label(AControl c)
        {
            try
            {
#if GWENT
                // A deck list row (the deck builder's, Draft's finished deck) is a card with its provisions and
                // copies drawn beside it: read as the collection reads cards, with its copies ("x2").
                if (DeckRowText(c) is string row) return row;
#endif
                var parts = Texts(c);
                if (parts.Count > 1) return parts[0];
                if (NotificationText(c) is string note) return note;
                if (Tooltipped(c, parts) is string tipped) return tipped;
                if (parts.Count == 0 && IconName(c) is string icon) return icon;
                return UiText.DescribeSelection(c);
            }
            catch (Exception e) { return "unreadable control (" + e.GetType().Name + ")"; }
        }

        /// <summary>
        /// An icon whose only words are its hover tooltip (the menu bar's buttons), or a counter that shows
        /// just a number beside an icon (the currencies: "200" beside the ore icon): read as a mouse player
        /// reads it by pointing, the tooltip first, then the number ("Руда, 200"). Null for any other control.
        /// </summary>
        private static string Tooltipped(AControl c, List<string> texts)
        {
            if (texts.Count > 1 || texts.Count == 1 && !IsCount(texts[0])) return null;
            if (!(TooltipText(c) is string tip)) return null;
            return texts.Count == 0 ? tip : tip + ", " + texts[0];
        }

        /// <summary>
        /// A notification's background is its clickable body, but holds no words: the message ("Получить
        /// награды по контракту: …") is the notification's text label beside it. Null for any other control.
        /// </summary>
        private static string NotificationText(AControl c)
        {
#if !GWENT // Thronebreaker has no notification widgets
            return null;
#else
            if (c.TryCast<GwentUnity.UINotificationBackgroundWidget>() == null) return null;
            var label = c.GetComponentInParent<GwentUnity.UINotificationControl>()?.m_TextLabel;
            if (label == null) return null;
            var parts = Texts(label);
            return parts.Count > 0 ? string.Join(". ", parts) : null;
#endif
        }

        private static bool IsCount(string s)
        {
            foreach (char ch in s)
                if (!char.IsDigit(ch) && ch != ' ' && ch != ' ' && ch != ',' && ch != '.' && ch != '/') return false;
            return true;
        }

        /// <summary>
        /// The text of the control's hover tooltip when the game gives it a plain one (a <c>SimpleTooltip</c>),
        /// translated; null if it has none, or another kind (a card's, the profile's XP boost). The tooltip is
        /// built the way the game builds it on hover, by its own creator. Its text is a translation key, or (the
        /// currencies) text the game already translated, though both are flagged as keys.
        /// </summary>
        public static string TooltipText(AControl c)
        {
#if !GWENT // Thronebreaker's AControl has no TooltipRequestCreator; its tooltips are still to be found
            return null;
#else
            try
            {
                var tip = c.TooltipRequestCreator?.Invoke()?.TooltipData?.TryCast<GwentUnity.UI.SimpleTooltip>();
                string text = tip?.Text;
                if (string.IsNullOrEmpty(text)) return null;
                if (tip.IsLocKey && GameText.Get(text, null) is string translated) return Formatted(translated, tip);
                // An untranslated key ("social_facebook_name") is no words at all.
                if (text.IndexOf('_') >= 0 && text.IndexOf(' ') < 0) return null;
                text = UiText.Clean(text);
                return text.Length > 0 ? text : null;
            }
            catch { return null; }
#endif
        }

#if GWENT
        /// <summary>A translated tooltip with its parameters filled in ("Сменить задание ({0}/{1})" → "Сменить
        /// задание (1/1)"). They come boxed from the game, and a boxed int's <c>ToString</c> through the interop
        /// reads garbage, so ints are unboxed; a parameter of any other value type leaves the text as it is.</summary>
        private static string Formatted(string text, GwentUnity.UI.SimpleTooltip tip)
        {
            try
            {
                var given = tip.LocalizationParams;
                if (given == null || given.Length == 0) return text;
                var args = new object[given.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    var p = given[i];
                    string type = p?.GetIl2CppType().FullName;
                    if (type == "System.Int32") args[i] = p.Unbox<int>();
                    else if (type == null || type == "System.String") args[i] = p?.ToString();
                    else return text;
                }
                return string.Format(text, args);
            }
            catch { return text; }
        }
#endif

        // An icon-only action button (the back arrow) names itself by its action icon: "Back". Other icon
        // buttons are known by their icon picture; the game has no words for these.
        private static string IconName(AControl c)
        {
#if GWENT // Thronebreaker has no ActionIconController
            var ic = c.GetComponent<ActionIconController>();
            if (ic != null)
            {
                var name = ic.m_IconType.ToString();
                return name == "None" ? null : UiText.FriendlyName(name);
            }
#endif
            foreach (var img in c.GetComponentsInChildren<UnityEngine.UI.Image>(false))
                if (img.sprite != null && IconSprites.TryGetValue(img.sprite.name.Replace("(Clone)", ""), out var known))
                    return Loc.T(known);
            return null;
        }

        private static readonly Dictionary<string, string> IconSprites = new Dictionary<string, string>
        {
            { "vanity_selection_grid_open_icon", Loc.Mark("Show all") },  // the cosmetics picker's grid window
            { "ui_icons_cards_hide", Loc.Mark("Full preview") },          // the cosmetics picker hiding its panels
            { "ui_icons_cards_show", Loc.Mark("Exit full preview") },     // the same button while the panels are hidden
        };

        public static string Details(AControl c)
        {
            var parts = Texts(c);
            if (NotificationText(c) is string note) return note;
            if (Tooltipped(c, parts) is string tipped) return tipped;
            return parts.Count > 0 ? string.Join(". ", parts) : Loc.T("No details.");
        }

        private static bool SafeEnabled(AControl c)
        {
            try { return c.IsEnabled; } catch { return true; }
        }
    }
}
