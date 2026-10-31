using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentVisuals;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Thronebreaker's popups, every kind the game raises over whatever is on screen:
    /// - the generic popup (CrimsonUI.PopupPanel: notices, accept/cancel, yes/no, modal "please wait", loading,
    ///   the input-field and crafting variants; online play's "Sync failed… launch Gwent?"),
    /// - the autosave notice at start-up (UISaveNotificationPanel),
    /// - the tutorial's picture popup (UITutorialImagePopup).
    /// All share one shape: a title, a message, perhaps the content's own controls (an input field), and a bar
    /// of captioned buttons (InputButtonContainer: never selectable, the game reaches them by key glyph; clicked
    /// through the bar's callback). One stop under the title: the message (read on landing, Space re-reads it),
    /// the content's controls, then the buttons.
    ///
    /// Enter on the message: with a single button it presses it (Продолжить, ОК); with several it is left to
    /// the game, whose key glyph shows which button Enter is. Escape stays the game's Back. The message node is
    /// keyed by its title and text, so a popup replaced by another lands afresh and is read.
    /// Gwent's popup is a different class tree (UIPopupContainer); its screen is the Gwent module's.
    /// </summary>
    internal sealed class PopupScreen : Screen
    {
        public override string Key => "popup";
        public override int Layer => 3;

        private sealed class Popup
        {
            public AControl Root;
            public string Title = "";
            public string Message = "";
            public List<AControl> Controls = new List<AControl>();
        }

        public override bool IsActive() => Find() != null;

        /// <summary>The popup the active input layer has attached, the last attached (topmost) first.</summary>
        private static Popup Find()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = attached.Count - 1; i >= 0; i--)
            {
                var c = attached[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                var p = Read(c);
                if (p != null) return p;
            }
            return null;
        }

        private static Popup Read(AControl c)
        {
            var generic = c.TryCast<PopupPanel>();
            if (generic != null)
            {
                var ctl = generic.m_Control;
                if (ctl == null || !ctl.gameObject.activeInHierarchy || !generic.IsPopupVisible()) return null;
                var p = new Popup { Root = ctl, Title = Joined(ctl.m_TitleLabel), Message = Joined(ctl.m_MessageLabel) };
                var field = ctl.m_InputFieldControl;
                if (field != null && field.gameObject.activeInHierarchy) p.Controls.AddRange(CrimsonNodes.Leaves(field));
                AddBar(p.Controls, ctl.m_ButtonsPanel);
                return p;
            }

            var save = c.TryCast<UISaveNotificationPanel>();
            if (save != null)
            {
                var p = new Popup { Root = save, Title = Joined(save.m_TitleLabel), Message = Joined(save.m_DescriptionLabel) };
                AddBar(p.Controls, save.m_InputButtonContainer);
                return p;
            }

            var picture = c.TryCast<UITutorialImagePopup>();
            if (picture != null)
            {
                var p = new Popup { Root = picture, Message = Joined(picture.m_Text) };
                AddBar(p.Controls, picture.m_ButtonsPanel);
                return p;
            }
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var p = Find();
            if (p == null) return;
            b.BeginStop("popup");
            if (p.Title.Length > 0) b.PushContext(p.Title);
            if (p.Message.Length > 0)
            {
                string message = p.Message;
                var only = p.Controls.Count == 1 ? p.Controls[0] : null;
                // Keyed by the text alone, not the popup's path: the path's sibling indices shift as the scene
                // behind it loads, and each shift re-read the unchanged message (the start-up autosave notice
                // was read three times).
                var id = ControlId.Structural("popup:message:" + p.Title + "|" + message);
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => message, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(message, true),
                    OnActivate = only != null ? () => Navigator.ClickControl(only) : null,
                });
                b.SetStart(id);
            }
            foreach (var c in p.Controls) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            if (p.Title.Length > 0) b.PopContext();
        }

        /// <summary>On the message of a popup with several buttons, Enter is the game's own: its default button,
        /// as the key glyph beside it shows.</summary>
        public override bool LeavesToGame(UnityEngine.KeyCode key)
        {
            if (key != UnityEngine.KeyCode.Return && key != UnityEngine.KeyCode.KeypadEnter) return false;
            if (!(GraphNavigator.CurrentNode is GraphNode n) || n.Id.Reference is AControl) return false;
            var p = Find();
            return p != null && p.Controls.Count != 1;
        }

        public override AControl GameSelection() => null;

        // A label's visible text, or "" when it is hidden.
        private static string Joined(UnityEngine.Component c)
        {
            if (c == null || !c.gameObject.activeInHierarchy) return "";
            return string.Join(" ", CrimsonNodes.Texts(c));
        }

        // A button bar's captioned buttons, in the bar's order.
        private static void AddBar(List<AControl> list, InputButtonContainer bar)
        {
            if (bar == null || !bar.gameObject.activeInHierarchy) return;
            var children = bar.GetChildren();
            for (int i = 0; children != null && i < children.Count; i++)
            {
                var c = children[i];
                if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                if (CrimsonNodes.Texts(c).Count == 0) continue;
                if (!list.Exists(x => x.Pointer == c.Pointer)) list.Add(c);
            }
        }
    }
}
