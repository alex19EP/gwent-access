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

        // The search field's text and the search result as last spoken, for the echo in Tick.
        private string _typed;
        private string _result;

        // The window has no title of its own; the game names it in its friends section.
        public override string ScreenName => GameText.Get("contacts_section_friends", "Friends");

        public override bool IsActive() => Window() != null;

        /// <summary>The open Contacts window of a social panel attached to the input layer that has the input.</summary>
        private static ContactsWindow Window()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var panel = attached[i]?.TryCast<SocialPanel>();
                if (panel == null || !panel.gameObject.activeInHierarchy) continue;
                try
                {
                    if (!panel.IsWindowOpen || panel.CurrentSection != SocialPanel.SocialPanelSection.Contacts) continue;
                }
                catch { continue; }
                var w = panel.GetComponentInChildren<ContactsWindow>(false);
                if (w != null) return w;
            }
            return null;
        }

        public override void OnPop()
        {
            _typed = null;
            _result = null;
            _caret = -1;
            _editing = false;
            _leaving = false;
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

        private static void AddSearch(GraphBuilder b, SearchSectionRenderer search)
        {
            b.BeginStop("search");
            var field = search.InputFieldControl;
            if (field != null && field.gameObject.activeInHierarchy) b.AddItem(CrimsonNodes.Id(field), Field(field));

            // The bar's icon button next to the field runs the search, as Enter in the field does.
            var bar = field != null ? field.transform.parent : null;
            if (bar != null)
                foreach (var c in bar.GetComponentsInChildren<ButtonControl>(false))
                    if (c.IsCurrentlySelectable())
                        b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, () => "Search")); // icon only; the game has no word for it

            var found = search.m_ContactEntryRenderer?.GetComponentInChildren<ContactEntryRenderer>(false);
            if (found != null) AddContact(b, found);
            if (ResultMessage(search) is string message)
                b.AddLabel(ControlId.Structural(CrimsonNodes.PathKey(search.m_MessageRenderer.transform, null)), () => message);
        }

        // The field reads its placeholder ("Найти друга…") as its name, then what is typed, as a sighted player
        // sees one or the other in the box. Enter puts the caret in it.
        private static NodeVtable Field(InputFieldControl field)
        {
            return new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Placeholder(field) ?? "Find a friend", kind: AnnouncementKinds.Label),
                    NodeAnnouncement.Static("edit"),
                    new NodeAnnouncement(() => Typed(field) is string t && t.Length > 0 ? t : null, kind: AnnouncementKinds.Value),
                },
                OnActivate = () =>
                {
                    try { field.ActivateInputField(); }
                    catch (System.Exception e) { Plugin.LogInfo("[contacts] activate field failed: " + e.Message); }
                },
                OnTooltip = () => Speech.Say((Placeholder(field) ?? "Find a friend") + ": " + (Typed(field) is string t && t.Length > 0 ? t : "blank"), true),
            };
        }

        private static string Placeholder(InputFieldControl field)
        {
            try
            {
                var p = field.InputField?.m_PlaceholderText;
                return p != null ? UiText.Clean(p.text) is string s && s.Length > 0 ? s : null : null;
            }
            catch { return null; }
        }

        private static string Typed(InputFieldControl field)
        {
            try { return field.Text ?? ""; } catch { return ""; }
        }

        private static bool Editing(InputFieldControl field)
        {
            try { return field.InputField != null && field.InputField.isFocused; } catch { return false; }
        }

        // The game's message under the field after a search ("no such player" and the like), if shown.
        private static string ResultMessage(SearchSectionRenderer search)
        {
            var m = search.m_MessageRenderer;
            if (m == null || !m.gameObject.activeInHierarchy) return null;
            var texts = CrimsonNodes.Texts(m);
            return texts.Count > 0 ? string.Join(". ", texts) : null;
        }

        private static InputFieldControl EditedField()
        {
            var w = Window();
            var field = w?.GetComponentInChildren<SearchSectionRenderer>(false)?.InputFieldControl;
            return field != null && Editing(field) ? field : null;
        }

        /// <summary>While the caret is in the search field, the game gets the editing keys (Left/Right, Home/End,
        /// Backspace, Space, Enter to search); Tab and Up/Down still move off the one-line field.</summary>
        public override bool LeavesToGame(KeyCode key) =>
            key != KeyCode.Tab && key != KeyCode.UpArrow && key != KeyCode.DownArrow && EditedField() != null;

        /// <summary>Landing anywhere but the search field takes the caret out of it, so the keys are the
        /// navigator's again. (Landing on the field only reads it; Enter starts editing.)</summary>
        public override void OnCursorMoved(GraphNode node)
        {
            base.OnCursorMoved(node);
            var field = Window()?.GetComponentInChildren<SearchSectionRenderer>(false)?.InputFieldControl;
            if (field == null) return;
            bool onField = node?.Id.Reference is AControl c && c.Pointer == field.Pointer;
            if (onField || !Editing(field)) return;
            _leaving = true; // the landing speaks for itself; no "not editing" after it
            try { field.DeactivateInputField(); }
            catch (System.Exception e) { Plugin.LogInfo("[contacts] field deactivate failed: " + e.Message); }
        }

        // The field's caret as last seen, whether it was being edited, and whether the cursor just left it.
        private int _caret = -1;
        private bool _editing;
        private bool _leaving;

        /// <summary>
        /// Echo the search field as a screen reader echoes an edit box, and speak the search result.
        /// Entering and leaving edit mode say so; a typed character is spoken, a deleted one too; moving the
        /// caret by one (Left/Right) speaks the character it now sits on, a longer jump (Home/End, Ctrl+arrows)
        /// the word there; "blank" past the end. A result (a contact found, or the game's message) is spoken
        /// when it appears.
        /// </summary>
        public override void Tick()
        {
            var w = Window();
            var search = w?.GetComponentInChildren<SearchSectionRenderer>(false);
            if (search == null) return;

            var field = search.InputFieldControl;
            string now = field != null ? Typed(field) : null;
            bool editing = field != null && Editing(field);
            int caret = editing ? Caret(field) : -1;
            if (editing != _editing)
            {
                if (editing) Speech.Say("editing", true);
                else if (!_leaving) Speech.Say("not editing", true);
                _leaving = false;
            }
            else if (editing && _typed != null && now != null && now != _typed) Speech.Say(Change(_typed, now), true);
            else if (editing && caret >= 0 && _caret >= 0 && caret != _caret && now != null)
                Speech.Say(System.Math.Abs(caret - _caret) == 1 ? CharAt(now, caret) : WordAt(now, caret), true);
            _editing = editing;
            _typed = now;
            _caret = caret;

            var found = search.m_ContactEntryRenderer?.GetComponentInChildren<ContactEntryRenderer>(false);
            string result = found != null ? ContactLabel(found) : ResultMessage(search);
            if (result != null && result != _result) Speech.Say(result, false);
            _result = result;
        }

        private static int Caret(InputFieldControl field)
        {
            try { return field.InputField.caretPositionInternal; } catch { return -1; }
        }

        // What an edit did: the characters inserted, else those deleted (wherever in the text), as typed.
        private static string Change(string before, string after)
        {
            int p = 0;
            while (p < before.Length && p < after.Length && before[p] == after[p]) p++;
            int s = 0;
            while (s < before.Length - p && s < after.Length - p && before[before.Length - 1 - s] == after[after.Length - 1 - s]) s++;
            string added = after.Substring(p, after.Length - p - s);
            string removed = before.Substring(p, before.Length - p - s);
            return Spoken(added.Length > 0 ? added : removed);
        }

        // The character the caret sits before; "blank" at the end.
        private static string CharAt(string text, int caret) =>
            caret >= 0 && caret < text.Length ? Spoken(text[caret].ToString()) : "blank";

        // The word the caret is in or before; "blank" at the end.
        private static string WordAt(string text, int caret)
        {
            if (caret < 0 || caret >= text.Length) return "blank";
            int start = caret, end = caret;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            return end > start ? text.Substring(start, end - start) : Spoken(text[caret].ToString());
        }

        private static string Spoken(string s) => s == " " ? "space" : s;

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
        private static NodeVtable Foldout(AControl header, FoldoutSectionComponent foldout, System.Func<string> label)
        {
            var v = CrimsonNodes.Button(header, label);
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
