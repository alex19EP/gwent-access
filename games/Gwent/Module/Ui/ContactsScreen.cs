using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The friend list: the social panel's Contacts window, opened from the friends button in the social bar
    /// (main menu or battle). Three Tab stops, as the window is drawn top to bottom:
    ///
    /// <list type="bullet">
    /// <item><b>search</b>: the "find a friend" field, its search button, and what the search found (one
    /// contact, or the game's message).</item>
    /// <item><b>contacts</b>: the game's foldout sections (recent opponents, pending friend requests, friends),
    /// each a group reading its title and counter ("Друзья, 0/0"). Each contact under it is a group too, reading
    /// its name and status as drawn ("Zefinae, Неизвестно"); expanding it unfolds the game's action buttons
    /// for that contact (view profile, chat, challenge, add or remove friend, accept or decline a request or a
    /// match invite), which are its children. Expanding and collapsing click the game's own foldout header,
    /// so its animation and sounds play; the groups mirror the game's open state (spec §9).</item>
    /// <item><b>links</b>: the social-media buttons at the bottom.</item>
    /// </list>
    ///
    /// The window shares the main menu's input layer, so this screen sits above <see cref="MainMenuScreen"/>
    /// while the window is open and steps aside when it closes (Escape is the game's Back).
    ///
    /// Enter on the search field starts editing it, as clicking it does. While it is being edited the editing
    /// keys are left to the game (Space, Backspace, Left/Right, Home/End edit the text; Enter searches); Tab
    /// and Up/Down leave the field. The field is not a native control the screen reader could follow, so the
    /// screen echoes it as one would: entering and leaving edit mode, typed and deleted characters, and the
    /// caret's moves.
    /// </summary>
    internal sealed class ContactsScreen : Screen
    {
        public override string Key => "contacts";
        public override int Layer => 1;

        // The search field, read and echoed as an edit box; the search result as last spoken, for Tick.
        private readonly EditBox _search = new EditBox(SearchField, Loc.Mark("Find a friend"));
        private string _result;

        private static InputFieldControl SearchField() =>
            Window()?.GetComponentInChildren<SearchSectionRenderer>(false)?.InputFieldControl;

        // The window has no title of its own; the game names it in its friends section.
        public override string ScreenName => GameText.Get("contacts_section_friends", "Friends");

        public override bool IsActive() => Window() != null;

        /// <summary>The open Contacts window of a social panel attached to the input layer that has the input.</summary>
        private static ContactsWindow Window() => OpenWindow<ContactsWindow>(SocialPanel.SocialPanelSection.Contacts);

        /// <summary>The window of a social panel attached to the input layer that has the input, while that
        /// panel has the given section open.</summary>
        internal static T OpenWindow<T>(SocialPanel.SocialPanelSection section) where T : Component
        {
            var panel = CrimsonNodes.ActivePanel<SocialPanel>();
            try
            {
                if (panel == null || !panel.IsWindowOpen || panel.CurrentSection != section) return null;
            }
            catch { return null; }
            return panel.GetComponentInChildren<T>(false);
        }

        public override void OnPop()
        {
            _search.Reset();
            _result = null;
        }

        private AControl _opener;
        public override void OnFocus() => _opener = SocialOpener();

        /// <summary>Backspace: close the window the way a mouse player does on desktop, by pressing the social bar
        /// button that opened it again (the window's own close buttons are mobile-only). Escape is not the
        /// game's Back here: it opens the main menu's "quit?" question.</summary>
        public override bool Back() => CloseSocialWindow(_opener, "friend");

        public override IReadOnlyList<ScreenKey> ExtraKeys => _keys ??= new List<ScreenKey>
        {
            new ScreenKey { Key = KeyCode.Escape, Run = () => { if (!CloseSocialWindow(_opener, "friend")) Speech.Say(Loc.T("Nothing to go back to here."), true); },
                When = () => !_search.Editing(), Hint = Loc.T("Escape or Backspace, close the friend list") },
        };
        private List<ScreenKey> _keys;

        /// <summary>The social bar button the game has selected as a window opens: the one the player pressed.</summary>
        internal static AControl SocialOpener()
        {
            try
            {
                var sel = CrimsonInputManager.Instance?.ActiveLayer?.SelectedControl;
                if (sel == null) return null;
                return sel.TryCast<GwentUnity.SocialButton>() != null || sel.GetComponentInParent<GwentUnity.SocialButton>() != null ? sel : null;
            }
            catch { return null; }
        }

        /// <summary>Press the opener again; without one (the window was open before this screen knew), the
        /// shown social bar button whose icon is the section's ("friend", "quest").</summary>
        internal static bool CloseSocialWindow(AControl opener, string icon)
        {
            try
            {
                if (opener == null || !opener.gameObject.activeInHierarchy)
                {
                    opener = null;
                    foreach (var sb in UnityEngine.Object.FindObjectsOfType<GwentUnity.SocialButton>())
                    {
                        if (sb == null || !sb.gameObject.activeInHierarchy) continue;
                        foreach (var img in sb.GetComponentsInChildren<UnityEngine.UI.Image>(false))
                            if (img.sprite != null && img.sprite.name.ToLowerInvariant().Contains(icon)) { opener = sb; break; }
                        if (opener != null) break;
                    }
                }
                if (opener == null) return false;
                Navigator.ClickControl(opener);
                return true;
            }
            catch { return false; }
        }

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;

            var search = w.GetComponentInChildren<SearchSectionRenderer>(false);
            if (search != null) AddSearch(b, search);

            var sections = w.GetComponentsInChildren<ContactsSectionRenderer>(false);
            if (sections.Length > 0)
            {
                b.BeginStop("contacts");
                foreach (var s in sections) AddSection(b, s);
            }

            var links = new List<AControl>();
            foreach (var s in w.GetComponentsInChildren<SocialButtonsSection>(false))
                links.AddRange(CrimsonNodes.Leaves(s));
            if (links.Count > 0)
            {
                b.BeginStop("links");
                foreach (var c in links) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        // ---- search ----

        private void AddSearch(GraphBuilder b, SearchSectionRenderer search)
        {
            b.BeginStop("search");
            var field = search.InputFieldControl;
            if (field != null && field.gameObject.activeInHierarchy) b.AddItem(CrimsonNodes.Id(field), _search.Vtable(field));

            // The bar's icon button next to the field runs the search, as Enter in the field does.
            var bar = field != null ? field.transform.parent : null;
            if (bar != null)
                foreach (var c in bar.GetComponentsInChildren<ButtonControl>(false))
                    if (c.IsCurrentlySelectable())
                        b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, () => Loc.T("Search"))); // icon only; the game has no word for it

            var found = FoundContact(search);
            if (found != null) AddContact(b, found);
            if (ResultMessage(search) is string message)
                b.AddLabel(ControlId.Structural(CrimsonNodes.PathKey(search.m_MessageRenderer.transform, null)), () => message);
        }

        // The contact a search found, if shown. The result renderer outlives its Unity object: after the game
        // destroys it the field still holds a wrapper, which `?.` lets through and the native call then throws
        // on (every frame while the window was open). Unity's own null check sees the destroyed object.
        private static ContactEntryRenderer FoundContact(SearchSectionRenderer search)
        {
            try
            {
                var r = search.m_ContactEntryRenderer;
                if (r == null || !r.gameObject.activeInHierarchy) return null;
                return r.GetComponentInChildren<ContactEntryRenderer>(false);
            }
            catch { return null; }
        }

        // The game's message under the field after a search ("no such player" and the like), if shown.
        private static string ResultMessage(SearchSectionRenderer search)
        {
            var m = search.m_MessageRenderer;
            if (m == null || !m.gameObject.activeInHierarchy) return null;
            var texts = CrimsonNodes.Texts(m);
            return texts.Count > 0 ? string.Join(". ", texts) : null;
        }

        /// <summary>While the caret is in the search field, the game gets the editing keys (see <see cref="EditBox"/>).</summary>
        public override bool LeavesToGame(KeyCode key) => _search.LeavesToGame(key);

        /// <summary>Landing anywhere but the search field takes the caret out of it.</summary>
        public override void OnCursorMoved(GraphNode node)
        {
            base.OnCursorMoved(node);
            _search.OnCursorMoved(node);
        }

        /// <summary>Echo the search field as an edit box (<see cref="EditBox.Tick"/>), and speak the search
        /// result (a contact found, or the game's message) when it appears.</summary>
        public override void Tick()
        {
            var w = Window();
            var search = w?.GetComponentInChildren<SearchSectionRenderer>(false);
            if (search == null) return;

            _search.Tick();

            var found = FoundContact(search);
            string result = found != null ? ContactLabel(found) : ResultMessage(search);
            if (result != null && result != _result) Speech.Say(result, false);
            _result = result;
        }

        // ---- sections and contacts ----

        // A section is a group mirroring the game's foldout. An empty section's header can't be clicked (the
        // game greys it out); it still reads its title and count, as a line.
        private static void AddSection(GraphBuilder b, ContactsSectionRenderer s)
        {
            var header = s.SectionButton;
            if (header == null || !header.gameObject.activeInHierarchy) return;
            if (!header.IsCurrentlySelectable())
            {
                b.AddLabel(ControlId.Structural(CrimsonNodes.PathKey(header.transform, null)), () => HeaderLabel(header));
                return;
            }
            b.BeginGroup(CrimsonNodes.Id(header), Foldout(header, s.Cast<FoldoutSectionComponent>(), () => HeaderLabel(header)),
                expanded: s.IsOpened);
            var foldout = s.Foldout;
            if (foldout != null)
                foreach (var e in foldout.GetComponentsInChildren<ContactEntryRenderer>(false)) AddContact(b, e);
            b.EndGroup();
        }

        private static string HeaderLabel(AControl header) => string.Join(", ", CrimsonNodes.Texts(header));

        private static void AddContact(GraphBuilder b, ContactEntryRenderer e)
        {
            var header = e.SectionButton;
            if (header == null || !header.gameObject.activeInHierarchy) return;
            b.BeginGroup(CrimsonNodes.Id(header), Foldout(header, e.Cast<FoldoutSectionComponent>(), () => ContactLabel(e)),
                expanded: e.IsOpened);
            var buttons = e.m_ActionButtons;
            if (buttons != null)
                for (int i = 0; i < buttons.Count; i++)
                {
                    var c = buttons[i]?.ActionButtonControl;
                    if (c == null || !c.gameObject.activeInHierarchy || !c.IsCurrentlySelectable()) continue;
                    var texts = CrimsonNodes.Texts(c);
                    b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, texts.Count > 0 ? () => string.Join(", ", CrimsonNodes.Texts(c)) : null));
                }
            b.EndGroup();
        }

        // A contact's header: the name and status line as drawn.
        private static string ContactLabel(ContactEntryRenderer e)
        {
            var texts = e.SectionButton != null ? CrimsonNodes.Texts(e.SectionButton) : null;
            if (texts != null && texts.Count > 0) return string.Join(", ", texts);
            try { return e.Contact?.Name; } catch { return null; }
        }

        /// <summary>A foldout header: Enter and Right/Left click it through the game, which opens or closes the
        /// foldout with its animation and sound; the group's expanded state is read back from the game.</summary>
        internal static NodeVtable Foldout(AControl header, FoldoutSectionComponent foldout, System.Func<string> label)
        {
            // "button" before the kernel's expanded/collapsed, whatever control the header is drawn with.
            var v = CrimsonNodes.WithRole(CrimsonNodes.Button(header, label), Loc.T("button"));
            void Toggle(bool open)
            {
                bool isOpen;
                try { isOpen = foldout.IsOpened; } catch { return; }
                if (isOpen != open) Navigator.ClickControl(header);
            }
            v.OnExpand = () => Toggle(true);
            v.OnCollapse = () => Toggle(false);
            return v;
        }
    }
}
