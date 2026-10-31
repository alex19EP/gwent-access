using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Thronebreaker's fallback screen: whatever panels the active input layer holds (the startup autosave
    /// notice, simple menus and notices no dedicated screen reads yet). Each attached panel that shows text or
    /// has controls becomes one stop: its texts that aren't a button's caption as one message node (read on
    /// landing, Space re-reads it), then its selectable controls and the captioned buttons of its button bars
    /// (InputButtonContainer: never selectable, the game reaches them by key glyph; clicked through the
    /// container's callback). Dedicated screens sit on higher layers and take over where they apply.
    ///
    /// Message nodes are keyed by their text, so a panel whose message changes lands afresh and is read.
    /// </summary>
    internal sealed class PanelScreen : Screen
    {
        public override string Key => "tb-panel";
        public override int Layer => 0;

        public override bool IsActive()
        {
            foreach (var p in Panels()) if (HasContent(p)) return true;
            return false;
        }

        // The active layer's attached, visible root controls, in attach order.
        private static List<AControl> Panels()
        {
            var list = new List<AControl>();
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return list;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var c = attached[i];
                if (c != null && c.gameObject.activeInHierarchy) list.Add(c);
            }
            return list;
        }

        private static bool HasContent(AControl panel) =>
            CrimsonNodes.Texts(panel).Count > 0 || Controls(panel).Count > 0;

        public override void Build(GraphBuilder b)
        {
            foreach (var panel in Panels())
            {
                var controls = Controls(panel);
                var captions = new HashSet<string>();
                foreach (var c in controls) foreach (var s in CrimsonNodes.Texts(c)) captions.Add(s);
                var parts = new List<string>();
                foreach (var s in VisibleTexts(panel)) if (!captions.Contains(s)) parts.Add(s);
                if (parts.Count == 0 && controls.Count == 0) continue;

                // Keyed by the panel's name, not its path: the path's sibling indices shift while a scene loads
                // around the panel, and each shift re-read the unchanged message (the loading tip, six times).
                string key = "tb-panel:" + panel.gameObject.name;
                b.BeginStop(key);
                if (parts.Count > 0)
                {
                    string message = string.Join(" ", parts);
                    var id = ControlId.Structural(key + ":message:" + message);
                    // A notice with a single button (Продолжить): Enter on the message presses it.
                    var only = controls.Count == 1 ? controls[0] : null;
                    b.AddItem(id, new NodeVtable
                    {
                        Announcements = new[] { new NodeAnnouncement(() => message, kind: AnnouncementKinds.Label) },
                        OnTooltip = () => Speech.Say(message, true),
                        OnActivate = only != null ? () => Navigator.ClickControl(only) : null,
                    });
                    b.SetStart(id);
                }
                foreach (var c in controls) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        /// <summary>The panel's texts a sighted player can see: like <see cref="CrimsonNodes.Texts"/>, minus those
        /// faded out (text alpha 0, or a fully transparent CanvasGroup above it), e.g. the autosave notice's
        /// "ЗАГРУЗКА" once loading is done: the game fades it rather than disabling it.</summary>
        private static List<string> VisibleTexts(AControl panel)
        {
            var parts = new List<string>();
            foreach (var t in panel.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (Faded(t)) continue;
                string s = UiText.Clean(t.text);
                if (s.Length > 0 && !s.StartsWith("[MISSING]") && !UiText.IsPlaceholder(s) && !parts.Contains(s)) parts.Add(s);
            }
            return parts;
        }

        internal static bool Faded(TMPro.TMP_Text t)
        {
            if (t.alpha <= 0.01f) return true;
            foreach (var g in t.GetComponentsInParent<UnityEngine.CanvasGroup>())
            {
                if (g.alpha <= 0.01f) return true;
                if (g.ignoreParentGroups) break;
            }
            return false;
        }

        // The panel's selectable leaves, then its button bars' captioned buttons.
        private static List<AControl> Controls(AControl panel)
        {
            var list = CrimsonNodes.Leaves(panel);
            foreach (var bar in panel.GetComponentsInChildren<InputButtonContainer>(false))
            {
                var children = bar.GetChildren();
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                    if (CrimsonNodes.Texts(c).Count == 0) continue;
                    if (!list.Exists(x => x.Pointer == c.Pointer)) list.Add(c);
                }
            }
            return list;
        }
    }
}
