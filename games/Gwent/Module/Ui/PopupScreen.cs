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
    /// </summary>
    internal sealed class PopupScreen : Screen
    {
        public override string Key => "popup";
        public override int Layer => 3;

        public override bool IsActive() => Container() != null;

        private static UIPopupContainer Container()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var c = attached[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                var p = c.TryCast<UIPopupContainer>();
                if (p != null) return p;
            }
            return null;
        }

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

            string title, message;
            if (popup.TryCast<PopupControl>() != null)
            {
                title = Joined(popup.transform.Find("SafeArea/SizeFitter/VerticalLayout/Header"), null);
                message = Joined(popup.transform.Find("SafeArea/SizeFitter/VerticalLayout/ContentRoot"), buttonTexts);
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
            foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            if (title.Length > 0) b.PopContext();
        }

        // A part's distinct visible texts, joined; texts that are a button's caption are left to the button.
        private static string Joined(UnityEngine.Transform t, HashSet<string> skip)
        {
            if (t == null) return "";
            var parts = new List<string>();
            foreach (var s in CrimsonNodes.Texts(t))
                if (skip == null || !skip.Contains(s)) parts.Add(s);
            return string.Join(" ", parts);
        }

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
            (key == UnityEngine.KeyCode.Return || key == UnityEngine.KeyCode.KeypadEnter)
            && GraphNavigator.CurrentNode is GraphNode n && !(n.Id.Reference is AControl);

        public override AControl GameSelection() => null;
    }
}
