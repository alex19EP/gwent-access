using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentVisuals;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The game's tutorial panels: the image popup every tutorial reuses (the shop's first visit, the flowchart's
    /// "show image popup" steps), the tutorial video player, the new-player intro panel (start or skip the
    /// tutorial), and the round popups of the tutorial battles. They sit on the Tutorial input layer above
    /// everything and hold the input until continued; Escape doesn't close them.
    ///
    /// One stop: the panel's text first — Enter continues through the panel's own handler, Space re-reads it —
    /// then the panel's buttons (skip, settings, continue), clicked through the game's own controls. The text
    /// node is keyed by what it says, so a panel that pages to its next text lands afresh and is spoken.
    /// </summary>
    internal sealed class TutorialPanelScreen : Screen
    {
        public override string Key => "tutorial";
        public override int Layer => 2;

        public override bool IsActive() => Panel() != null;

        // The tutorial panel attached to the input layer that has the input.
        private static AGwentPanel Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var c = attached[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                AGwentPanel p = c.TryCast<ATutorialPanel>();
                p ??= c.TryCast<UITutorialIntroPanel>();
                p ??= c.TryCast<UIRoundTutorialPopupPanel>();
                if (p != null) return p;
            }
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var panel = Panel();
            if (panel == null) return;

            var buttons = Buttons(panel);
            var buttonTexts = new HashSet<string>();
            foreach (var c in buttons) foreach (var s in CrimsonNodes.Texts(c)) buttonTexts.Add(s);

            string text = Text(panel, buttonTexts);
            b.BeginStop("tutorial");
            if (text.Length > 0)
            {
                // Keyed by the shown string (the image popup's loc key when it has one), not the panel.
                string key = panel.TryCast<UITutorialImagePopup>()?.CurrentString;
                var id = ControlId.Structural("tutorial:text:" + (string.IsNullOrEmpty(key) ? text : key));
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => text, kind: AnnouncementKinds.Label) },
                    OnActivate = () => Continue(panel),
                    OnTooltip = () => Speech.Say(text, true),
                });
                b.SetStart(id);
            }
            foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        // The panel's own text: every visible string that isn't a button's caption.
        private static string Text(AGwentPanel panel, HashSet<string> buttonTexts)
        {
            var parts = new List<string>();
            foreach (var s in CrimsonNodes.Texts(panel))
                if (!buttonTexts.Contains(s)) parts.Add(s);
            return string.Join(" ", parts);
        }

        // Selectable controls inside the panel, plus its glyph-key buttons (continue, skip): those are never
        // selectable — the game reaches them by key — but clicking goes through the container's own callback.
        private static List<AControl> Buttons(AGwentPanel panel)
        {
            var buttons = CrimsonNodes.Leaves(panel);
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
            return buttons;
        }

        // Enter on the text: the panel's own continue handler, as its continue key does.
        private static void Continue(AGwentPanel panel)
        {
            try
            {
                var image = panel.TryCast<UITutorialImagePopup>();
                if (image != null) { image.HandleContinue(); return; }
                var intro = panel.TryCast<UITutorialIntroPanel>();
                if (intro != null) { intro.HandleContinue(); return; }
                var round = panel.TryCast<UIRoundTutorialPopupPanel>();
                if (round != null) { round.HandleContinue(); return; }
                Speech.Say("No continue here.", true);
            }
            catch (System.Exception e) { Plugin.LogInfo("[tutorial] continue failed: " + e); Speech.Say("Couldn't continue.", true); }
        }

        public override AControl GameSelection() => null;
    }
}
