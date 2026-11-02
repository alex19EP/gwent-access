using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The settings window: the category list, then the open category's options followed by the window's buttons
    /// (apply, restore defaults, exit, back), as a sighted player sees them below the options — two Tab stops.
    /// Enter on a category opens it and moves into its options; Escape or Backspace in the options goes back up
    /// to the open category (a gamepad's B), and Escape on the categories is the game's own Back, closing the
    /// window.
    ///
    /// Options come straight from the open category's entry list, so rows scrolled out of view are reachable.
    /// Each option is one item: its title and current value, Left/Right step the value through the game's own
    /// switcher (the value is spoken back), Space reads the whole row. Active while the settings panel is
    /// attached to the input layer that has the input; a confirmation popup takes the input and the screen steps
    /// aside for it.
    /// </summary>
    internal sealed class SettingsScreen : Screen
    {
        public override string Key => "settings";
        public override string ScreenName => "Settings";
        public override int Layer => 1;

        public override bool IsActive() => Panel() != null;

        private static UISettingsPanel Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var p = attached[i]?.TryCast<UISettingsPanel>();
                if (p != null && p.gameObject.activeInHierarchy) return p;
            }
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var panel = Panel();
            if (panel == null) return;

            var categories = new List<AControl>();
            var buttons = new List<AControl>();
            var left = panel.m_LeftPanel?.transform;
            foreach (var c in CrimsonNodes.Leaves(panel))
            {
                if (c.GetComponentInParent<UISettingsEntry>() != null) continue; // a row's arrows and value
                if (left != null && c.transform.IsChildOf(left)) categories.Add(c);
                else buttons.Add(c);
            }
            // The window's buttons (apply, reset, exit) are never selectable: the game reaches them by glyph
            // keys. Offer them anyway; the click goes through the container's own callback.
            var seen = new HashSet<System.IntPtr>();
            foreach (var c in buttons) seen.Add(c.Pointer);
            buttons.AddRange(CrimsonNodes.BarButtons(panel, seen));

            if (categories.Count > 0)
            {
                b.BeginStop("categories");
                b.PushContext("Categories");
                foreach (var c in categories) b.AddItem(CrimsonNodes.Id(c), Category(c, categories));
                b.PopContext();
            }

            _categoryIds.Clear();
            foreach (var c in categories) _categoryIds[CrimsonNodes.PathKey(c.transform, null)] = c;
            _optionKeys.Clear();
            var rows = Rows(panel);
            if (rows.Count > 0 || buttons.Count > 0)
            {
                b.BeginStop("options");
                if (rows.Count > 0)
                {
                    b.PushContext("Options");
                    foreach (var e in rows) { var id = RowId(e); _optionKeys.Add((string)id.StructuralKey); b.AddItem(id, Row(e)); }
                    b.PopContext();
                }
                foreach (var c in buttons) { var id = CrimsonNodes.Id(c); _optionKeys.Add((string)id.StructuralKey); b.AddItem(id, CrimsonNodes.Button(c)); }
            }

            // Enter on a category moved into it: land on its first option once the new rows are drawn (a
            // different first row than before, or after a short wait when the same category was reopened).
            if (_enterOptionsAt > 0 && rows.Count > 0)
            {
                var first = RowId(rows[0]);
                string firstKey = (string)first.StructuralKey;
                if (firstKey != _firstBefore || UnityEngine.Time.realtimeSinceStartup - _enterOptionsAt > 0.4f)
                {
                    _enterOptionsAt = -1;
                    GraphNavigator.Suggest(first);
                }
                else if (UnityEngine.Time.realtimeSinceStartup - _enterOptionsAt > 2f) _enterOptionsAt = -1;
            }
            _lastFirstRow = rows.Count > 0 ? (string)RowId(rows[0]).StructuralKey : null;
        }

        // This render's category controls (by path) and the keys of the options stop (rows and buttons).
        private readonly Dictionary<string, AControl> _categoryIds = new Dictionary<string, AControl>();
        private readonly HashSet<string> _optionKeys = new HashSet<string>();

        // A pending move into a category's options: when Enter asked for it (-1: none) and the first row then.
        private static float _enterOptionsAt = -1;
        private static string _firstBefore, _lastFirstRow;

        private bool InOptions()
        {
            var key = GraphNavigator.CurrentNode?.Id.StructuralKey as string;
            return key != null && _optionKeys.Contains(key);
        }

        // Escape / Backspace in the options: back up to the open category.
        private void BackToCategories()
        {
            AControl target = null;
            foreach (var kv in _categoryIds)
                if (kv.Key == _opened || kv.Value.TryCast<ToggleButtonControl>()?.IsToggled == true) { target = kv.Value; if (kv.Key == _opened) break; }
            if (target == null) foreach (var kv in _categoryIds) { target = kv.Value; break; }
            if (target == null || !GraphNavigator.FocusNode(CrimsonNodes.Id(target))) Speech.Say("Categories", true);
        }

        private List<ScreenKey> _optionKeysList;

        /// <summary>In the options, Escape and Backspace go back to the categories (claimed from the game); on the
        /// categories Escape stays the game's Back. Read each frame, so the claim follows the cursor.</summary>
        public override IReadOnlyList<ScreenKey> ExtraKeys
        {
            get
            {
                if (!InOptions()) return System.Array.Empty<ScreenKey>();
                return _optionKeysList ??= new List<ScreenKey>
                {
                    new ScreenKey { Key = UnityEngine.KeyCode.Escape, Run = BackToCategories, Hint = "Escape or Backspace, back to the categories" },
                    new ScreenKey { Key = UnityEngine.KeyCode.Backspace, Run = BackToCategories, Hint = null },
                };
            }
        }

        // The open category's entries in the game's order; entries the platform hides stay inactive and are skipped.
        private static List<UISettingsEntry> Rows(UISettingsPanel panel)
        {
            var rows = new List<UISettingsEntry>();
            var entries = panel.m_SettingsInnerPanel?.m_CurrentCategory?.m_Entries;
            if (entries == null) return rows;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i]?.Entry;
                if (e != null && e.gameObject.activeInHierarchy && e.Switcher != null) rows.Add(e);
            }
            return rows;
        }

        // The category last opened with Enter, by path: the cursor click opens a category without setting its
        // toggle (the toggles then all read off), so the open one is known from the click instead.
        private static string _opened;

        // A category button: its name, and "selected" on the open one.
        private static NodeVtable Category(AControl c, List<AControl> all)
        {
            var v = CrimsonNodes.Button(c);
            var toggle = c.TryCast<ToggleButtonControl>();
            if (toggle == null) return v;
            string key = CrimsonNodes.PathKey(c.transform, null);
            var click = v.OnActivate;
            v.OnActivate = () =>
            {
                _opened = key;
                _firstBefore = _lastFirstRow;
                _enterOptionsAt = UnityEngine.Time.realtimeSinceStartup;
                click?.Invoke();
            };
            v.Announcements = new[]
            {
                v.Announcements[0],
                new NodeAnnouncement(() => Selected(toggle, key, all) ? "selected" : null, live: true, kind: AnnouncementKinds.Selected),
            };
            return v;
        }

        private static bool Selected(ToggleButtonControl toggle, string key, List<AControl> all)
        {
            try
            {
                if (toggle.IsToggled) return true;
                foreach (var c in all) if (c.TryCast<ToggleButtonControl>()?.IsToggled == true) return false;
            }
            catch { }
            return key == _opened;
        }

        // A row is keyed by its switcher's path (the game selects the switcher's main button or an arrow inside
        // it, which the navigator follows up to here) and backed by the main button, the control the game
        // highlights for the row.
        private static ControlId RowId(UISettingsEntry e)
        {
            var sw = e.Switcher;
            AControl main = sw.m_MainControl;
            return ControlId.Referenced(main ?? sw.Cast<AControl>(), CrimsonNodes.PathKey(sw.transform, null));
        }

        private static NodeVtable Row(UISettingsEntry e)
        {
            var sw = e.Switcher;
            return new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Title(e), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Value(sw), live: true, kind: AnnouncementKinds.Value),
                    new NodeAnnouncement(() => Enabled(sw) ? null : "unavailable", live: true, kind: AnnouncementKinds.Enabled),
                },
                OnAdjust = (dir, large) =>
                {
                    if (!Enabled(sw)) return;
                    if (dir > 0) sw.SelectNextValue();
                    else sw.SelectPreviousValue();
                },
                StateText = () => Enabled(sw) ? Value(sw) : "unavailable",
                OnTooltip = () =>
                {
                    var info = Info(sw);
                    Speech.Say(Title(e) + ": " + Value(sw) + (info != null ? ". " + info : ""), true);
                },
            };
        }

        // The game ships an explanation for some values that its PC layout never shows ("All sounds will be
        // muted when the game window loses focus"): keyed panel_settings_entry_info_<setting>_<value>, next to
        // the value's own key panel_settings_entry_<setting>_<value>.
        private const string ValuePrefix = "panel_settings_entry_";

        private static string Info(Switcher sw)
        {
            string id;
            try { id = sw.Value?.Text; } catch { return null; }
            if (id == null || !id.StartsWith(ValuePrefix)) return null;
            var table = GameText.Table;
            string key = ValuePrefix + "info_" + id.Substring(ValuePrefix.Length);
            if (table == null || !table.ContainsKey(key)) return null;
            return UiText.Clean(table[key]).Replace(" {0}", "");
        }

        private static string Title(UISettingsEntry e)
        {
            var parts = e.m_Title != null ? CrimsonNodes.Texts(e.m_Title) : null;
            return parts != null && parts.Count > 0 ? parts[0] : UiText.RowTitle(e.Switcher);
        }

        private static string Value(Switcher sw)
        {
            var parts = CrimsonNodes.Texts(sw);
            if (parts.Count > 0) return string.Join(", ", parts);
            try { return sw.Value?.Text ?? ""; } catch { return ""; }
        }

        private static bool Enabled(Switcher sw)
        {
            try { return sw.IsEnabled; } catch { return true; }
        }
    }
}
