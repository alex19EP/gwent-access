using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The settings window: the category list, the open category's options, then the window's buttons (save,
    /// back, reset…) — one Tab stop each.
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
            foreach (var bar in panel.GetComponentsInChildren<InputButtonContainer>(false))
            {
                var children = bar.GetChildren();
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                    if (!buttons.Exists(x => x.Pointer == c.Pointer)) buttons.Add(c);
                }
            }

            if (categories.Count > 0)
            {
                b.BeginStop("categories");
                b.PushContext("Categories");
                foreach (var c in categories) b.AddItem(CrimsonNodes.Id(c), Category(c));
                b.PopContext();
            }

            var rows = Rows(panel);
            if (rows.Count > 0)
            {
                b.BeginStop("options");
                b.PushContext("Options");
                foreach (var e in rows) b.AddItem(RowId(e), Row(e));
                b.PopContext();
            }

            if (buttons.Count > 0)
            {
                b.BeginStop("buttons");
                foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
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

        // A category button: its name, and "selected" on the open one.
        private static NodeVtable Category(AControl c)
        {
            var v = CrimsonNodes.Button(c);
            var toggle = c.TryCast<ToggleButtonControl>();
            if (toggle == null) return v;
            v.Announcements = new[]
            {
                v.Announcements[0],
                new NodeAnnouncement(() => toggle.IsToggled ? "selected" : null, live: true, kind: AnnouncementKinds.Selected),
            };
            return v;
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
            var table = Localization.LocalizationManager.Instance?.Translations;
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
