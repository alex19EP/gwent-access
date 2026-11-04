using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The game's generic popup (CrimsonUI.PopupPanel: confirmations, errors, notices, the starter-pick "are you
    /// sure?"). Every kind shares one skeleton under the popup container: a header title, a content root (the
    /// message, plus whatever the kind adds: an input field, an image, a timer), and a bar of captioned buttons
    /// (ОК / Отмена, Да / Нет…). It takes the Popup input layer, so it is active exactly while the container is
    /// attached to the layer that has the input; the screen under it stands down meanwhile.
    ///
    /// One stop under the title: the message (Space re-reads it), the content's own controls, then the
    /// buttons, clicked through their container's callback as the game's keys do. Escape stays the game's
    /// Back, which cancels where the popup allows it. The message node is keyed by its text, so a popup that
    /// is replaced by another lands afresh and is read.
    ///
    /// A popup with a text field (renaming a deck, naming a new one) offers the field as an edit box
    /// (<see cref="EditBox"/>): Enter puts the caret in it, and what is typed or deleted is echoed. The field's
    /// own texts (its placeholder, what is typed) are the edit box's, not part of the message, so typing doesn't
    /// re-key the message and re-read the popup.
    /// </summary>
    internal sealed class PopupScreen : Screen
    {
        public override string Key => "popup";
        public override int Layer => 3;

        public override bool IsActive() => Container() != null;

        private static UIPopupContainer Container() => CrimsonNodes.ActivePanel<UIPopupContainer>();

        // The shown popup: the container's active PopupControl child, else its active child of another kind
        // (the event announcement, UIImagePopupWithText: a picture, a title, its text and buttons).
        private static AControl Popup(UIPopupContainer container)
        {
            foreach (var p in container.GetComponentsInChildren<PopupControl>(false))
                if (p.gameObject.activeInHierarchy) return p;
            for (int i = 0; i < container.transform.childCount; i++)
            {
                var child = container.transform.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;
                var c = child.GetComponent<AControl>();
                if (c != null) return c;
            }
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var container = Container();
            var popup = container != null ? Popup(container) : null;
            if (popup == null) return;

            var buttons = Buttons(popup);
            var buttonTexts = new HashSet<string>();
            foreach (var c in buttons) foreach (var s in CrimsonNodes.Texts(c)) buttonTexts.Add(s);
            var field = Field(popup);

            string title, message;
            if (popup.TryCast<PopupControl>() != null)
            {
                title = Joined(popup.transform.Find("SafeArea/SizeFitter/VerticalLayout/Header"), null);
                message = Joined(popup.transform.Find("SafeArea/SizeFitter/VerticalLayout/ContentRoot"), buttonTexts, field);
            }
            else
            {
                // Another kind has no fixed skeleton: its first text is the title ("Saovine Event"), the rest
                // the message (the dates, how to earn the event's currency).
                var texts = new List<string>();
                foreach (var t in CrimsonNodes.Texts(popup.transform)) if (!buttonTexts.Contains(t)) texts.Add(t);
                title = texts.Count > 1 ? texts[0] : "";
                message = string.Join(" ", texts.Count > 1 ? texts.GetRange(1, texts.Count - 1) : texts);
            }

            b.BeginStop("popup");
            if (title.Length > 0) b.PushContext(title);
            if (message.Length > 0)
            {
                var id = ControlId.Structural("popup:message:" + title + "|" + message);
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => message, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(message, true),
                });
                b.SetStart(id);
            }
            // The text field even when the game doesn't report it selectable: the rename popup opens with its
            // field already typing (seen in the deck builder), and it was left out, nothing to type into.
            if (field != null && !buttons.Exists(c => c.Pointer == field.Pointer))
                b.AddItem(CrimsonNodes.Id(field), _field.Vtable(field));
            foreach (var c in buttons)
            {
                if (field != null && c.Pointer == field.Pointer) b.AddItem(CrimsonNodes.Id(c), _field.Vtable(field));
                else b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
            if (title.Length > 0) b.PopContext();
        }

        // A part's distinct visible texts (TextMeshPro and legacy UI text), joined; texts that are a button's
        // caption are left to the button, and those inside the text field (its placeholder, what is typed) to the
        // edit box.
        private static string Joined(UnityEngine.Transform t, HashSet<string> skip, InputFieldControl field = null)
        {
            if (t == null) return "";
            if (field == null)
            {
                var all = new List<string>();
                foreach (var s in CrimsonNodes.Texts(t))
                    if (skip == null || !skip.Contains(s)) all.Add(s);
                return string.Join(" ", all);
            }
            var parts = new List<string>();
            void Add(UnityEngine.Component c, string raw)
            {
                if (c.transform.IsChildOf(field.transform)) return;
                string s = UiText.Clean(raw);
                if (s.Length == 0 || s.StartsWith("[MISSING]") || UiText.IsPlaceholder(s) || parts.Contains(s)) return;
                if (skip == null || !skip.Contains(s)) parts.Add(s);
            }
            foreach (var tmp in t.GetComponentsInChildren<TMPro.TMP_Text>(false)) Add(tmp, tmp.text);
            foreach (var txt in t.GetComponentsInChildren<UnityEngine.UI.Text>(false)) Add(txt, txt.text);
            return string.Join(" ", parts);
        }

        // ---- the text field ----

        private static readonly EditBox _field = new EditBox(CurrentField, "Name");

        // The popup's shown text field, if it has one.
        private static InputFieldControl Field(AControl popup)
        {
            var f = popup?.GetComponentInChildren<InputFieldControl>(false);
            return f != null && f.gameObject.activeInHierarchy ? f : null;
        }

        private static InputFieldControl CurrentField()
        {
            var container = Container();
            return container != null ? Field(Popup(container)) : null;
        }

        public override void OnCursorMoved(GraphNode node)
        {
            _field.OnCursorMoved(node);
            base.OnCursorMoved(node);
        }

        public override void Tick() => _field.Tick();

        public override void OnPop() => _field.Reset();

        // The content's own selectable controls (an input field…), then the bar's captioned buttons: those are
        // never selectable (the game reaches them by key) and run their container's callback when clicked.
        private static List<AControl> Buttons(AControl popup)
        {
            var list = CrimsonNodes.Leaves(popup);
            if (popup.TryCast<PopupControl>() == null)
            {
                // Another kind's buttons ("Go to the Shop") are captioned but never selectable; its picture is a
                // selectable button with no words, which nothing a player reads names.
                list.RemoveAll(c => CrimsonNodes.Texts(c).Count == 0);
                foreach (var c in popup.GetComponentsInChildren<ButtonControl>(false))
                    if (c.GetComponentInParent<InputButtonContainer>() == null && CrimsonNodes.Texts(c).Count > 0 && !list.Exists(x => x.Pointer == c.Pointer))
                        list.Add(c);
            }
            {
                var seen = new HashSet<System.IntPtr>();
                foreach (var c in list) seen.Add(c.Pointer);
                list.AddRange(CrimsonNodes.BarButtons(popup, seen));
            }
            return list;
        }

        /// <summary>On the message, Enter is the game's own: the popup's default button (ОК, Да…) as its key glyph
        /// says. On a button it clicks that button, so Отмена stays reachable by Enter too.</summary>
        public override bool LeavesToGame(UnityEngine.KeyCode key) =>
            _field.LeavesToGame(key)
            || (key == UnityEngine.KeyCode.Return || key == UnityEngine.KeyCode.KeypadEnter)
            && GraphNavigator.CurrentNode is GraphNode n && !(n.Id.Reference is AControl);

        public override AControl GameSelection() => null;
    }
}
