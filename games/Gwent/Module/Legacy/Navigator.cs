using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;
using CrimsonUI;

namespace GwentAccess
{
    /// <summary>
    /// Our own "object navigation": walk every clickable thing on screen with F2/F3,
    /// move the game's selection there (so Enter works), or click it directly with F4.
    /// Works even on screens the game's own arrow-key navigation can't reach.
    /// Gwent's legacy layer; the click itself (<see cref="ClickControl"/>) is shared, in src/Shared/NavigatorClick.cs.
    /// </summary>
    internal static partial class Navigator
    {
        private static IntPtr _current = IntPtr.Zero;
        public static PopupControl ActivePopup;
        private static AControl _currentControl;

        private static float EffectiveAlpha(Component c)
        {
            float a = 1f;
            try { foreach (var g in c.GetComponentsInParent<CanvasGroup>(false)) a *= g.alpha; } catch { }
            return a;
        }

        private static int PanelOrder(Component c)
        {
            try { var p = c.GetComponentInParent<APanel>(); return p != null ? p.SortOrder : -1; } catch { return -1; }
        }

        /// <summary>Everything clickable on screen, topmost panel first, then top-to-bottom, left-to-right.</summary>
        public static List<AControl> Items()
        {
            var found = new List<(AControl c, int order, float y, float x)>();
            var all = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<AControl>());
            foreach (var o in all)
            {
                var c = o.TryCast<AControl>();
                if (c == null) continue;
                try
                {
                    if (!c.gameObject.activeInHierarchy || !c.IsEnabled) continue;
                    if (c.TryCast<GlyphLabelControl>() != null) continue;
                    string on = c.gameObject.name;
                    if (on == "TooltipTarget" || on == "Icon" || on == "LookDisabledHighlightControl" || on.StartsWith("Button_BasicIconUI") || on.StartsWith("CardButtonControl")) continue;
                    bool clickable = c.TryCast<ButtonControl>() != null || c.TryCast<CrimsonUI.Button>() != null;
                    bool selectable = false;
                    try { selectable = c.IsCurrentlySelectable(); } catch { }
                    if (!clickable && !selectable) continue;
                    if (EffectiveAlpha(c) < 0.05f) continue;
                    var rt = c.GetComponent<RectTransform>();
                    if (rt != null && (rt.rect.width < 2 || rt.rect.height < 2)) continue;
                    // Skip controls nested inside another clickable control we'd already list (e.g. arrows inside a switcher are fine, but labels aren't controls anyway).
                    Vector3 pos = c.transform.position;
                    found.Add((c, PanelOrder(c), pos.y, pos.x));
                }
                catch { }
            }
            // During a match, hide main-menu controls that are still loaded behind the battle.
            try
            {
                if (Battle.Controller() != null)
                {
                    found = found.Where(f =>
                    {
                        var p = f.c.GetComponentInParent<APanel>();
                        string n = p != null ? p.gameObject.name : "";
                        return n.Contains("Battle") || n.Contains("Mulligan") || n.Contains("Popup") || n.Contains("Choice") || n.Contains("CardPicker") || n.Contains("Results") || p == null;
                    }).ToList();
                }
            }
            catch { }
            // If a popup is open, only offer its choices so Enter can't hit something behind it.
            try
            {
                foreach (var po in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PopupControl>()))
                {
                    var pc = po.TryCast<PopupControl>();
                    if (pc == null || !pc.gameObject.activeInHierarchy) continue;
                    var inPopup = found.Where(f => f.c.transform.IsChildOf(pc.transform)).ToList();
                    if (inPopup.Count > 0) { found = inPopup; ActivePopup = pc; break; }
                }
            }
            catch { }
            // Full-screen windows that sit on top of the main menu: only offer their own items.
            try
            {
                foreach (var name in new[] { "UISettingsPrefab", "UIDailyLoginRewardPrefab", "UIRewardPopupPrefab" })
                {
                    var inIt = found.Where(f => { var pp = f.c.GetComponentInParent<APanel>(); return pp != null && pp.gameObject.name.StartsWith(name); }).ToList();
                    if (inIt.Count > 0) { found = inIt; break; }
                }
            }
            catch { }
            CustomSections = null;
            try
            {
                var settings = SettingsItems(found);
                if (settings != null) return settings;
            }
            catch (Exception e) { Plugin.LogInfo("Settings list failed: " + e.Message); }
            try
            {
                var db = DeckBuilderItems(found);
                if (db != null) return db;
            }
            catch (Exception e) { Plugin.LogInfo("Deck builder list failed: " + e.Message); }
            // Settings rows: the arrows and the switcher are separate controls; list each row once.
            try
            {
                var seenRows = new HashSet<IntPtr>();
                var keep = new List<(AControl c, int order, float y, float x)>();
                // Prefer the Switcher itself for each row.
                foreach (var f in found.OrderBy(f => f.c.TryCast<Switcher>() != null ? 0 : 1))
                {
                    GwentUnity.UISettingsEntry e = null;
                    try { e = f.c.GetComponentInParent<GwentUnity.UISettingsEntry>(); } catch { }
                    if (e != null) { if (!seenRows.Add(e.Pointer)) continue; }
                    keep.Add(f);
                }
                found = keep;
            }
            catch { }
            return found
                .OrderByDescending(f => f.order)
                .ThenByDescending(f => Mathf.Round(f.y / 25f))
                .ThenBy(f => f.x)
                .Select(f => f.c)
                .ToList();
        }

        public static List<(string name, int start, int count)> CustomSections;

        /// <summary>
        /// The settings window: category tabs, then every row of the open category (one item per row), then the buttons.
        /// Rows are taken straight from the game's settings entries so rows scrolled out of view are still reachable.
        /// </summary>
        private static List<AControl> SettingsItems(List<(AControl c, int order, float y, float x)> found)
        {
            GwentUnity.UISettingsPanel panel = null;
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<GwentUnity.UISettingsPanel>()))
            {
                var sp = o.TryCast<GwentUnity.UISettingsPanel>();
                if (sp != null && sp.gameObject.activeInHierarchy) { panel = sp; break; }
            }
            if (panel == null) return null;
            var tabs = new List<AControl>();
            var buttons = new List<(AControl c, float y, float x)>();
            foreach (var f in found)
            {
                if (!f.c.transform.IsChildOf(panel.transform)) continue;
                if (f.c.GetComponentInParent<GwentUnity.UISettingsEntry>() != null) continue;
                if (f.c.GetComponentInParent<Switcher>() != null) continue;
                if (UiText.DebugPath(f.c).Contains("ButtonsContainer")) tabs.Add(f.c);
                else buttons.Add((f.c, f.y, f.x));
            }
            tabs = tabs.OrderByDescending(t => Mathf.Round(t.transform.position.y / 10f)).ThenBy(t => t.transform.position.x).ToList();
            var rows = new List<AControl>();
            var seen = new HashSet<IntPtr>();
            foreach (var e in panel.GetComponentsInChildren<GwentUnity.UISettingsEntry>(false))
            {
                Switcher sw = null;
                try { sw = e.Switcher; } catch { }
                AControl ctl = sw != null ? sw.Cast<AControl>() : null;
                if (ctl == null || !ctl.gameObject.activeInHierarchy) continue;
                if (!seen.Add(ctl.Pointer)) continue;
                rows.Add(ctl);
            }
            Plugin.LogInfo("Settings: " + tabs.Count + " tabs, " + rows.Count + " rows, " + buttons.Count + " buttons");
            var list = new List<AControl>();
            list.AddRange(tabs);
            list.AddRange(rows);
            list.AddRange(buttons.OrderByDescending(b => Mathf.Round(b.y / 25f)).ThenBy(b => b.x).Select(b => b.c));
            var secs = new List<(string, int, int)>();
            if (tabs.Count > 0) secs.Add(("Categories", 0, tabs.Count));
            if (rows.Count > 0) secs.Add(("Options", tabs.Count, rows.Count));
            if (buttons.Count > 0) secs.Add(("Buttons", tabs.Count + rows.Count, buttons.Count));
            CustomSections = secs;
            return list;
        }

        /// <summary>Deck builder: your deck's cards, then the collection grid, then filters, then everything else.</summary>
        private static List<AControl> DeckBuilderItems(List<(AControl c, int order, float y, float x)> found)
        {
            bool inDb = found.Any(f => { var p = f.c.GetComponentInParent<APanel>(); return p != null && p.gameObject.name.StartsWith("UIDBPrefab"); });
            if (!inDb) return null;
            var deck = new List<(AControl c, float y, float x)>();
            var coll = new List<(AControl c, float y, float x)>();
            var filters = new List<(AControl c, float y, float x)>();
            var decks = new List<(AControl c, float y, float x)>();
            var other = new List<(AControl c, int order, float y, float x)>();
            foreach (var f in found)
            {
                string path = UiText.DebugPath(f.c);
                if (path.Contains("DeckButtonPrefab") && path.Contains("DropdownList")) continue; // each deck's hidden options menu: use Ctrl+U / Ctrl+N instead
                if (f.c.TryCast<GwentVisuals.UIDeckCardButtonControl>() != null || f.c.GetComponentInParent<GwentVisuals.UIDeckCardButtonControl>() != null) deck.Add((f.c, f.y, f.x));
                else if (path.Contains("CardGrid") || f.c.TryCast<CardButtonControl>() != null) coll.Add((f.c, f.y, f.x));
                else if (path.Contains("Filter")) filters.Add((f.c, f.y, f.x));
                else if (f.c.TryCast<GwentVisuals.UIDeckButtonControl>() != null || path.Contains("DeckButtonPrefab")) decks.Add((f.c, f.y, f.x));
                else other.Add(f);
            }
            Func<List<(AControl c, float y, float x)>, IEnumerable<AControl>> sort = l => l.OrderByDescending(t => Mathf.Round(t.y / 25f)).ThenBy(t => t.x).Select(t => t.c);
            var list = new List<AControl>();
            var secs = new List<(string, int, int)>();
            void AddSec(string name, IEnumerable<AControl> items)
            {
                var arr = items.ToList();
                if (arr.Count == 0) return;
                secs.Add((name, list.Count, arr.Count));
                list.AddRange(arr);
            }
            AddSec("Your deck", sort(deck));
            AddSec("Your decks", sort(decks));
            AddSec("Collection", sort(coll));
            AddSec("Filters", sort(filters));
            AddSec("Other buttons", other.OrderByDescending(o => o.order).ThenByDescending(o => Mathf.Round(o.y / 25f)).ThenBy(o => o.x).Select(o => o.c));
            CustomSections = secs;
            return list;
        }

        public static void Move(int dir)
        {
            ActivePopup = null;
            var items = Items();
            Plugin.LogInfo("Navigator: " + items.Count + " items");
            if (items.Count == 0) { Speech.Say("Nothing to navigate here.", true); return; }
            int idx = items.FindIndex(c => c.Pointer == _current);
            if (idx < 0)
            {
                // Start from whatever the game has selected, if it's in the list.
                var sel = SelectedControlPatch.Current;
                if (sel != null) idx = items.FindIndex(c => c.Pointer == sel.Pointer);
            }
            idx = idx < 0 ? (dir > 0 ? 0 : items.Count - 1) : (idx + dir + items.Count) % items.Count;
            Focus(items, idx, null);
        }

        /// <summary>Shift+F3 / Shift+F2: jump to the first item of the next / previous group on screen.</summary>
        public static void MoveSection(int dir)
        {
            ActivePopup = null;
            var items = Items();
            if (items.Count == 0) { Speech.Say("Nothing to navigate here.", true); return; }
            var secs = CustomSections ?? Sections(items);
            Plugin.LogInfo("Sections: " + string.Join(" | ", secs.Select(x => x.name + " (" + x.start + "-" + (x.start + x.count - 1) + ")")));
            int idx = items.FindIndex(c => c.Pointer == _current);
            if (idx < 0) { var sel = SelectedControlPatch.Current; if (sel != null) idx = items.FindIndex(c => c.Pointer == sel.Pointer); }
            int cur = idx < 0 ? -1 : secs.FindIndex(x => idx >= x.start && idx < x.start + x.count);
            int next = cur < 0 ? (dir > 0 ? 0 : secs.Count - 1) : (cur + dir + secs.Count) % secs.Count;
            var sec = secs[next];
            Focus(items, sec.start, sec.name + ", section " + (next + 1) + " of " + secs.Count + ", " + sec.count + " items");
        }


        /// <summary>
        /// Split the screen's items into groups by where they sit in the UI tree (top bar, tabs, list, bottom buttons...).
        /// Items are already in reading order; a group is a run of items sharing an ancestor.
        /// </summary>
        private static List<(string name, int start, int count)> Sections(List<AControl> items)
        {
            var chains = items.Select(c => { var l = new List<Transform>(); var t = c.transform; while (t != null) { l.Add(t); t = t.parent; } l.Reverse(); return l; }).ToList();
            // groups: list of index lists
            var groups = new List<(List<int> idx, Transform root)> { (Enumerable.Range(0, items.Count).ToList(), null) };
            var unsplittable = new HashSet<int>();
            for (int guard = 0; guard < 12 && groups.Count < 9; guard++)
            {
                int gi = -1, best = 10;
                for (int i = 0; i < groups.Count; i++) if (!unsplittable.Contains(groups[i].idx[0]) && groups[i].idx.Count > best) { best = groups[i].idx.Count; gi = i; }
                if (gi < 0) break;
                var g = groups[gi];
                int depth = 0;
                while (true)
                {
                    var first = chains[g.idx[0]];
                    if (depth >= first.Count) break;
                    var t = first[depth];
                    if (g.idx.All(i => depth < chains[i].Count && chains[i][depth].Pointer == t.Pointer)) depth++; else break;
                }
                // children of the common ancestor at index `depth`
                var sub = new List<(List<int> idx, Transform root)>();
                foreach (var i in g.idx)
                {
                    var key = depth < chains[i].Count ? chains[i][depth] : chains[i][chains[i].Count - 1];
                    if (sub.Count > 0 && sub[sub.Count - 1].root.Pointer == key.Pointer) sub[sub.Count - 1].idx.Add(i);
                    else
                    {
                        int existing = sub.FindIndex(x => x.root.Pointer == key.Pointer);
                        if (existing >= 0) sub[existing].idx.Add(i); else sub.Add((new List<int> { i }, key));
                    }
                }
                int singles = sub.Count(x => x.idx.Count == 1);
                if (sub.Count < 2 || sub.Count > 10 || singles > sub.Count / 2 + 1) { unsplittable.Add(g.idx[0]); continue; }
                groups.RemoveAt(gi);
                groups.InsertRange(gi, sub);
            }
            // Turn groups into runs in reading order.
            var owner = new int[items.Count];
            for (int gi = 0; gi < groups.Count; gi++) foreach (var i in groups[gi].idx) owner[i] = gi;
            var result = new List<(string, int, int)>();
            int s0 = 0;
            for (int i = 1; i <= items.Count; i++)
            {
                if (i == items.Count || owner[i] != owner[s0])
                {
                    var root = groups[owner[s0]].root;
                    result.Add((SectionName(root, items[s0]), s0, i - s0));
                    s0 = i;
                }
            }
            return result;
        }

        private static string SectionName(Transform root, AControl first)
        {
            string n = "";
            try
            {
                if (root != null)
                {
                    var p = root.GetComponent<APanel>();
                    n = UiText.FriendlyName(root.gameObject.name);
                    if (p != null) n = Screens.CleanName(n);
                }
                if (n.Length == 0) { var p = first.GetComponentInParent<APanel>(); if (p != null) n = Screens.CleanName(UiText.FriendlyName(p.gameObject.name)); }
            }
            catch { }
            return n.Length > 0 ? n : "Group";
        }

        private static void Focus(List<AControl> items, int idx, string prefix)
        {
            var target = items[idx];
            try { DeckBuilder.SearchWanted = target.gameObject.name == "TextSearch"; } catch { }
            _current = target.Pointer;
            _currentControl = target;

            // Move the game's own selection there so Enter works; suppress the duplicate announcement.
            SelectedControlPatch.Suppress(target);
            try { target.ParentLayer?.RequestControlSelection(target); } catch (Exception e) { Plugin.LogInfo("RequestControlSelection failed: " + e.Message); }

            string text = UiText.DescribeSelection(target);
            NoteShopItem(target, text);
            string screen = Screens.CheckScreenChange(target);
            if (!string.IsNullOrEmpty(screen)) text = screen + ". " + text;
            Plugin.LogInfo("Navigator -> " + UiText.DebugPath(target) + " => " + text);
            // In popups, Enter and Escape are fixed to Accept and Cancel regardless of the highlight, so say the key.
            if (ActivePopup != null && target.transform.IsChildOf(ActivePopup.transform))
            {
                string label = UiText.Describe(target, 2);
                string key = null;
                foreach (var line in KeyHints.HintLines(ActivePopup))
                {
                    int colon = line.LastIndexOf(": ");
                    if (colon > 0 && string.Equals(line.Substring(0, colon), label, StringComparison.OrdinalIgnoreCase)) { key = line.Substring(colon + 2); break; }
                }
                if (key != null) text += ". Press " + (key == "ESC" ? "Escape" : key == "Return" ? "Enter" : key) + " for this, or F4";
                else text += ". F4 to choose it";
            }
            if (prefix != null) text = prefix + ". " + text;
            Speech.Say(text + ", " + (idx + 1) + " of " + items.Count, true);
        }

        /// <summary>Ctrl+Q: press the screen's own Exit / Back button (for screens where Escape doesn't leave).</summary>
        public static void ClickExit()
        {
            if (DeckBuilder.SearchWanted) { DeckBuilder.SearchWanted = false; }
            if (DeckBuilder.Quit()) return;
            var items = Items();
            foreach (var want in new[] { "exit", "save and exit", "back", "close", "done", "cancel" })
            {
                foreach (var c in items)
                {
                    string t = "";
                    try { t = UiText.Describe(c, 2).Trim().ToLowerInvariant(); } catch { }
                    if (t == want)
                    {
                        _currentControl = c; _current = c.Pointer;
                        Speech.Say("Pressing " + want + ".", true);
                        Click();
                        return;
                    }
                }
            }
            Speech.Say("No exit button found here. Try Escape.", true);
        }

        /// <summary>Enter on a deck-builder bottom-bar button: the game ignores it, so run the button's action.</summary>
        public static void EnterOnBottomButton()
        {
            try
            {
                var c = SelectedControlPatch.Current;
                if (c == null || c.TryCast<ButtonControl>() == null) return;
                if (c.GetComponentInParent<InputButtonContainer>() == null) return;
                var p = c.GetComponentInParent<APanel>();
                if (p == null || !p.gameObject.name.StartsWith("UIDBPrefab")) return;
                _currentControl = c; _current = c.Pointer;
                Click();
            }
            catch (Exception e) { Plugin.LogInfo("Enter on bottom button failed: " + e.Message); }
        }

        public static string LastShopItem;
        public static void NoteShopItem(AControl c, string text)
        {
            try { if (UiText.DebugPath(c).Contains("ListTile(Clone)")) LastShopItem = text; } catch { }
        }
        private static float _buyArmedUntil = -1;
        private static IntPtr _buyArmed = IntPtr.Zero;

        /// <summary>Ctrl+B in the shop: buy the selected item with Ore. Asks for a second press; never touches real-money buttons.</summary>
        public static void Buy()
        {
            // The shop's graph screen offers the buy button itself (Enter on it, in its details stop).
            if (Ui.GraphNavigator.Owns || Ui.ScreenManager.AnyActive()) return;
            ButtonControl buy = null;
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<ButtonControl>()))
            {
                var b = o.TryCast<ButtonControl>();
                if (b == null || b.gameObject.name != "Button_ShopBuy" || !b.gameObject.activeInHierarchy) continue;
                try { if (!b.IsEnabled) continue; } catch { }
                buy = b; break;
            }
            if (buy == null) { Speech.Say("No buy button here. In the shop, pick an item, then its price option, like x1 100, then press Control B.", true); return; }
            string price = UiText.Describe(buy);
            string low = price.ToLowerInvariant();
            if (price.Contains("$") || price.Contains("€") || price.Contains("£") || low.Contains("steam"))
            { Speech.Say("That item costs real money, " + price + ". The mod won't buy it.", true); return; }
            float now = Time.realtimeSinceStartup;
            if (_buyArmed == buy.Pointer && now < _buyArmedUntil)
            {
                _buyArmed = IntPtr.Zero; _buyArmedUntil = -1;
                Speech.Say("Buying.", true);
                _currentControl = buy; _current = buy.Pointer;
                Click();
                return;
            }
            string item = LastShopItem ?? "";
            _buyArmed = buy.Pointer; _buyArmedUntil = now + 6f;
            Speech.Say("Buy " + (item.Length > 0 ? item + " " : "") + "for " + price.Replace("Buy Now", "").Trim(' ', ',') + " Ore? Press Control B again to confirm.", true);
        }

        public static void Click()
        {
            var c = _currentControl ?? SelectedControlPatch.Current;
            if (c == null) { Speech.Say("Nothing selected. Use F3 to pick something first.", true); return; }
            if (!c.gameObject.activeInHierarchy) { Speech.Say("That item is gone. Press F3 again.", true); return; }
            ClickControl(c); // src/Shared/NavigatorClick.cs
        }
    }
}
