using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentVisuals;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The starter deck pick (the main menu's PickStarterDeck sub-state, run by the new-player flow): five faction
    /// cards under "Выберите стартовую колоду!"; picking one slides it aside and opens its description, three
    /// pages (the leader ability and two key mechanics) with "Вперед" paging on and, on the last page, "Выбрать
    /// колоду" opening the game's confirm popup. Only that popup's confirm saves the pick to the account; every
    /// other press is local, and "Назад" returns to the faction cards.
    ///
    /// Both panels stay attached to the input layer the whole time; which one is in use is told by which has
    /// selectable controls (the faction buttons, or the description's page toggles). The faction cards read
    /// name then pitch. On the description the page toggles are one row: landing on a page selects it in the
    /// game (as a gamepad does), and its text is a live part, so it speaks as soon as the game shows it. The
    /// input bar's buttons follow as their own stop. The confirm popup takes the input layer, so this screen
    /// stands down and the popup reads as any other.
    /// </summary>
    internal sealed class StarterDeckScreen : Screen
    {
        public override string Key => "starter-deck";
        public override int Layer => 1;
        public override string ScreenName => Header();

        public override bool IsActive() => Panels(out _, out _);

        // The two pick panels, when the input layer that has the input holds them.
        private static bool Panels(out UIPickStarterDeck pick, out UIPickStarterDeckDescription desc)
        {
            pick = null; desc = null;
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return false;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var c = attached[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                pick ??= c.TryCast<UIPickStarterDeck>();
                desc ??= c.TryCast<UIPickStarterDeckDescription>();
            }
            return pick != null;
        }

        // The pick panel's title and subtitle ("Выберите стартовую колоду!, Не волнуйтесь, вы разблокируете все…").
        private static string Header()
        {
            if (!Panels(out var pick, out _)) return null;
            var title = pick.transform.Find("SafeArea/Content/TitleContainer");
            if (title == null) return null;
            var parts = CrimsonNodes.Texts(title);
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        public override void Build(GraphBuilder b)
        {
            if (!Panels(out var pick, out var desc)) return;
            var pages = desc != null ? Pages(desc) : new List<ToggleButtonControl>();
            if (pages.Count > 0) BuildDescription(b, pick, desc, pages);
            else BuildFactions(b, pick);
        }

        // ---- the faction cards ----

        private static void BuildFactions(GraphBuilder b, UIPickStarterDeck pick)
        {
            b.BeginStop("starter:factions");
            var others = new List<AControl>();
            foreach (var c in CrimsonNodes.Leaves(pick))
            {
                var button = c;
                // Only the faction cards here; the panel's settings icon (FTUXSettings) read as a nameless
                // "6 of 6" and, being a toggle, was clicked by landing on it. It gets its own stop below.
                if (FactionText(button, "Title") == null) { others.Add(button); continue; }
                b.AddItem(CrimsonNodes.Id(button), new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => FactionText(button, "Title"), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => FactionText(button, "Description"), kind: AnnouncementKinds.Tooltip),
                    },
                    OnActivate = () => Navigator.ClickControl(button),
                    OnTooltip = () => Speech.Say(string.Join(", ", CrimsonNodes.Texts(button)), true),
                });
            }
            if (others.Count > 0)
            {
                b.BeginStop("starter:other");
                foreach (var c in others) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        // A faction card's "Title" or "Description" text.
        private static string FactionText(AControl card, string part)
        {
            try
            {
                var t = card.transform.Find("SafeArea/DescriptionGroup/" + part);
                if (t == null) return null;
                var parts = CrimsonNodes.Texts(t);
                return parts.Count > 0 ? parts[0] : null;
            }
            catch { return null; }
        }

        // ---- the description ----

        private static void BuildDescription(GraphBuilder b, UIPickStarterDeck pick, UIPickStarterDeckDescription desc,
            List<ToggleButtonControl> pages)
        {
            b.BeginStop("starter:pages");
            b.PushContext(PickedFaction(pick));
            b.StartRow("starter:pages");
            foreach (var page in pages)
            {
                var toggle = page;
                var id = CrimsonNodes.Id(toggle);
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => CrimsonNodes.Label(toggle), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => CrimsonNodes.Toggled(toggle) ? Loc.T("selected") : null, live: true, kind: AnnouncementKinds.Selected),
                        new NodeAnnouncement(() => CrimsonNodes.Toggled(toggle) ? PageText(desc, toggle) : null, live: true, kind: AnnouncementKinds.Tooltip),
                    },
                    OnActivate = () => Navigator.ClickControl(toggle),
                    OnTooltip = () => Speech.Say(PageText(desc, toggle) ?? "", true),
                });
                if (CrimsonNodes.Toggled(toggle)) b.SetStart(id);
            }
            b.EndRow();
            b.PopContext();

            // The input bar: "Вперед" / "Выбрать колоду" and "Назад" (the icon-only buttons are gamepad glyphs).
            b.BeginStop("starter:buttons");
            foreach (var c in BarButtons(desc))
                b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));

            // The settings icon, after the bar.
            foreach (var c in CrimsonNodes.Leaves(desc))
                if (c.TryCast<ToggleButtonControl>() is ToggleButtonControl t && t.GetComponentInParent<FactionDescriptionElement>() == null)
                {
                    b.BeginStop("starter:settings");
                    b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
                }
        }

        // The page toggles, when the description is the panel in use.
        private static List<ToggleButtonControl> Pages(UIPickStarterDeckDescription desc)
        {
            var list = new List<ToggleButtonControl>();
            try
            {
                // Only the description pages (each a FactionDescriptionElement's toggle): the panel's settings icon
                // (FTUXSettingsButton) is a toggle too, and read as a page "Options" that landing on opened.
                foreach (var t in desc.GetComponentsInChildren<ToggleButtonControl>(false))
                    if (t.IsCurrentlySelectable() && t.GetComponentInParent<FactionDescriptionElement>() != null) list.Add(t);
            }
            catch { }
            return list;
        }

        // The card slid aside for the description: the faction's name.
        private static string PickedFaction(UIPickStarterDeck pick)
        {
            try
            {
                var t = pick.transform.Find("SafeArea/Content/DescriptionTransform");
                var title = t?.GetComponentInChildren<TMPro.TMP_Text>(false);
                return title != null ? UiText.Clean(title.text) : null;
            }
            catch { return null; }
        }

        // The page's text, rich text (the card links' colour and link tags) stripped — only once the window
        // shows this page's text (its label carries the page's key, faction_<faction>_<id>_desc_text): the text
        // cross-fades after the toggle, and reading it early spoke the previous page's words under this page's
        // name. Until then null; the live announcement speaks it when it arrives.
        private static string PageText(UIPickStarterDeckDescription desc, ToggleButtonControl page)
        {
            try
            {
                var element = page?.GetComponentInParent<FactionDescriptionElement>();
                if (element != null)
                {
                    string want = string.Format(UIPickStarterDeckDescription.KEY_DESCRIPTION_TEXT_FORMAT, element.m_Faction, element.m_Id);
                    if (desc.m_Description?.m_LocalizationKey != want) return null;
                }
                var t = desc.transform.Find("SafeArea/DescriptionWindow/Content/Blend2/Description")?.GetComponent<TMPro.TMP_Text>();
                string s = t != null ? UiText.Clean(t.text) : null;
                return string.IsNullOrEmpty(s) || UiText.IsPlaceholder(s) ? null : s;
            }
            catch { return null; }
        }

        // The bar's captioned buttons: never selectable (the game reaches them by key), clicked through the
        // container's own callback, as on the tutorial panels.
        private static List<AControl> BarButtons(UIPickStarterDeckDescription desc)
        {
            return CrimsonNodes.BarButtons(desc);
        }

        /// <summary>On the description the cursor follows the shown page, not the game's selection: "Вперед"
        /// pages on without moving the selection, so the two drift apart.</summary>
        public override AControl GameSelection()
        {
            if (Panels(out _, out var desc) && desc != null)
                foreach (var page in Pages(desc))
                    if (CrimsonNodes.Toggled(page)) return page;
            return base.GameSelection();
        }

        /// <summary>Landing on a page shows it. The game does that on selection only for keyboard and gamepad
        /// input (not in mouse mode), so a page left unshown after the selection write is clicked, as a mouse
        /// player clicks it.</summary>
        public override void OnCursorMoved(GraphNode node)
        {
            base.OnCursorMoved(node);
            // Only a description page: other toggles (the settings icon) open things when clicked.
            if (node?.Id.Reference is AControl c && c.TryCast<ToggleButtonControl>() is ToggleButtonControl page && !CrimsonNodes.Toggled(page)
                && page.GetComponentInParent<FactionDescriptionElement>() != null)
                Navigator.ClickControl(page);
        }

        /// <summary>Enter is the game's own on a faction card or a page (the game's selection follows the cursor
        /// there, so its key does what a keyboard player gets); on the input bar's buttons the mod clicks the one
        /// under the cursor. Escape is always the game's Back.</summary>
        public override bool LeavesToGame(UnityEngine.KeyCode key)
        {
            if (key != UnityEngine.KeyCode.Return && key != UnityEngine.KeyCode.KeypadEnter) return false;
            try { return GraphNavigator.CurrentNode?.Id.Reference is AControl c && c.IsCurrentlySelectable(); }
            catch { return false; }
        }
    }
}
