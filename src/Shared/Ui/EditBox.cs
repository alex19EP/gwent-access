using System;
using Access.Core.Graph;
using CrimsonUI;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// A game text field (<see cref="InputFieldControl"/>) read and edited as a screen reader edit box. One
    /// instance per field a screen offers; the screen forwards <see cref="LeavesToGame"/>,
    /// <see cref="OnCursorMoved"/> and <see cref="Tick"/> to it.
    ///
    /// - The node reads the field's placeholder as its name ("Поиск", "Найти друга…"), "edit", then what is
    ///   typed, as a sighted player sees one or the other in the box. Enter puts the caret in it.
    /// - While the caret is in it, the game gets the editing keys (letters, Left/Right, Home/End, Backspace,
    ///   Space, Enter); Tab and Up/Down still move off the one-line field.
    /// - Landing anywhere else takes the caret out, so the keys are the navigator's again.
    /// - Echo: entering and leaving edit mode say so; a typed or deleted character is spoken; moving the caret
    ///   by one speaks the character it now sits on, a longer jump (Home/End, Ctrl+arrows) the word there;
    ///   "blank" past the end.
    /// </summary>
    internal sealed class EditBox
    {
        private readonly Func<InputFieldControl> _find;
        private readonly string _fallbackName;

        // The caret as last seen, whether the field was being edited, what it held, and whether the cursor just
        // left it (the landing speaks for itself; no "not editing" after it).
        private int _caret = -1;
        private bool _editing;
        private bool _leaving;
        private string _typed;

        /// <param name="find">The field, found fresh from live game state (null while not shown).</param>
        /// <param name="fallbackName">The name when the field has no placeholder text.</param>
        public EditBox(Func<InputFieldControl> find, string fallbackName)
        {
            _find = find;
            _fallbackName = fallbackName;
        }

        /// <summary>Forget the echo state (the screen was reopened).</summary>
        public void Reset()
        {
            _caret = -1; _editing = false; _leaving = false; _typed = null;
        }

        public NodeVtable Vtable(InputFieldControl field) => new NodeVtable
        {
            Announcements = new[]
            {
                new NodeAnnouncement(() => Placeholder(field) ?? _fallbackName, kind: AnnouncementKinds.Label),
                NodeAnnouncement.Static("edit"),
                new NodeAnnouncement(() => Typed(field) is string t && t.Length > 0 ? t : null, kind: AnnouncementKinds.Value),
            },
            OnActivate = () =>
            {
                try { field.ActivateInputField(); }
                catch (Exception e) { Plugin.LogInfo("[edit] activate field failed: " + e.Message); }
            },
            OnTooltip = () => Speech.Say((Placeholder(field) ?? _fallbackName) + ": " + (Typed(field) is string t && t.Length > 0 ? t : "blank"), true),
        };

        /// <summary>True while the caret is in the field.</summary>
        public bool Editing()
        {
            var field = _find();
            return field != null && IsEditing(field);
        }

        public bool LeavesToGame(KeyCode key) =>
            key != KeyCode.Tab && key != KeyCode.UpArrow && key != KeyCode.DownArrow && Editing();

        /// <summary>Landing anywhere but the field takes the caret out of it. (Landing on the field only reads
        /// it; Enter starts editing.)</summary>
        public void OnCursorMoved(GraphNode node)
        {
            var field = _find();
            if (field == null) return;
            bool onField = node?.Id.Reference is AControl c && c.Pointer == field.Pointer;
            if (onField || !IsEditing(field)) return;
            _leaving = true;
            try { field.DeactivateInputField(); }
            catch (Exception e) { Plugin.LogInfo("[edit] field deactivate failed: " + e.Message); }
        }

        /// <summary>The echo; call every frame while the screen is focused.</summary>
        public void Tick()
        {
            var field = _find();
            if (field == null) return;
            string now = Typed(field);
            bool editing = IsEditing(field);
            int caret = editing ? Caret(field) : -1;
            if (editing != _editing)
            {
                if (editing) Speech.Say("editing", true);
                else if (!_leaving) Speech.Say("not editing", true);
                _leaving = false;
            }
            else if (editing && _typed != null && now != null && now != _typed) Speech.Say(Change(_typed, now), true);
            else if (editing && caret >= 0 && _caret >= 0 && caret != _caret && now != null)
                Speech.Say(Math.Abs(caret - _caret) == 1 ? CharAt(now, caret) : WordAt(now, caret), true);
            _editing = editing;
            _typed = now;
            _caret = caret;
        }

        // ---- the field ----

        public static string Placeholder(InputFieldControl field)
        {
            try
            {
                var p = field.InputField?.m_PlaceholderText;
                return p != null ? UiText.Clean(p.text) is string s && s.Length > 0 ? s : null : null;
            }
            catch { return null; }
        }

        public static string Typed(InputFieldControl field)
        {
            try { return field.Text ?? ""; } catch { return ""; }
        }

        private static bool IsEditing(InputFieldControl field)
        {
            try { return field.InputField != null && field.InputField.isFocused; } catch { return false; }
        }

        private static int Caret(InputFieldControl field)
        {
            try { return field.InputField.caretPositionInternal; } catch { return -1; }
        }

        // ---- echo ----

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
    }
}
