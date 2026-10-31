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

        // The text node's key at the last render, and whether this push has rendered yet: the first render's
        // text is landed on by SetStart, later new texts by a suggestion.
        private string _lastKey;
        private bool _built;

        public override void OnPush() { _lastKey = null; _built = false; }

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

            b.BeginStop("tutorial");
            var round = panel.TryCast<UIRoundTutorialPopupPanel>();
            if (round != null)
            {
                BuildRound(b, round);
                foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
                return;
            }
            string text = Text(panel, buttonTexts);
            if (text.Length > 0)
            {
                // Keyed by the shown string (the image popup's loc key when it has one), not the panel, so a new
                // text lands afresh and is spoken.
                string key = panel.TryCast<UITutorialImagePopup>()?.CurrentString;
                string textKey = string.IsNullOrEmpty(key) ? text : key;
                var id = ControlId.Structural("tutorial:text:" + textKey);
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => text, kind: AnnouncementKinds.Label) },
                    OnActivate = () => Continue(panel),
                    OnTooltip = () => Speech.Say(text, true),
                });
                b.SetStart(id);
                // A panel's text can arrive after the panel (an image popup fading in, the round explainer's next
                // line): the cursor moves onto the new text so it is spoken, not left on the button.
                if (_built && textKey != _lastKey) GraphNavigator.Suggest(id);
                _lastKey = textKey;
            }
            _built = true;
            foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        /// <summary>The round explainer's title ("РАУНД И КАРТЫ В ГВИНТЕ"), spoken as the screen's name.</summary>
        public override string ScreenName
        {
            get
            {
                try
                {
                    var round = Panel()?.TryCast<UIRoundTutorialPopupPanel>();
                    if (round == null) return null;
                    foreach (var t in round.GetComponentsInChildren<TMPro.TMP_Text>(false))
                        if (t.name == "MainTitle") return UiText.Clean(t.text);
                }
                catch { }
                return null;
            }
        }

        // The round explainer's strings already spoken this push, and the text last drawn (held through the
        // fades between lines, when nothing is drawn).
        private readonly HashSet<string> _roundSpoken = new HashSet<string>();
        private string _roundText = "";

        // The round explainer is one fixed text node: its lines cross-fade, so a node keyed by the current line
        // would vanish between lines and drop the cursor onto the button. Each newly drawn line or label is
        // spoken once as it appears, queued, without moving the cursor; Space re-reads all that is shown.
        private void BuildRound(GraphBuilder b, UIRoundTutorialPopupPanel round)
        {
            var shown = RoundText(round);
            if (!_built) { _roundSpoken.Clear(); _roundText = ""; }
            var fresh = new List<string>();
            foreach (var s in shown) if (_roundSpoken.Add(s)) fresh.Add(s);
            if (shown.Count > 0) _roundText = string.Join(" ", shown);
            // The first render's text is spoken by landing on the node; later ones as they appear.
            if (_built && fresh.Count > 0)
            {
                string news = string.Join(" ", fresh);
                Speech.Say(news, false);
                Speech.Remember(news);
            }
            var id = ControlId.Structural("tutorial:round");
            b.AddItem(id, new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(() => _roundText, kind: AnnouncementKinds.Label) },
                OnActivate = () => Continue(round),
                OnTooltip = () => Speech.Say(_roundText, true),
            });
            b.SetStart(id);
            _built = true;
        }

        // The round explainer (UIRoundTutorialPopupPanel) is an animation: its explanation lines fade in one at
        // a time over a diagram of three round columns whose outcome labels ("ВЫ ВЫИГРАЛИ РАУНД", "ПОБЕДА")
        // fade in as it goes. Every label is always there, faded out, so only what is drawn counts: the shown
        // explanation lines and then the shown outcome labels. The title is the screen's name and the column
        // headers ("РАУНД 1") are static.
        private static List<string> RoundText(UIRoundTutorialPopupPanel panel)
        {
            var lines = new List<string>();
            var labels = new List<string>();
            try
            {
                foreach (var t in panel.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (Alpha(t, panel.transform) < 0.5f) continue;
                    string s = UiText.Clean(t.text);
                    if (s.Length == 0) continue;
                    var outcome = t.transform.parent?.parent;
                    if (t.name.Contains("Explanation")) lines.Add(s);
                    else if (outcome != null && outcome.name == "GameplayAbstracts") labels.Add(s);
                }
            }
            catch { }
            lines.AddRange(labels);
            return lines;
        }

        // How opaque a text is drawn: its color's alpha times every CanvasGroup's up to the panel.
        private static float Alpha(TMPro.TMP_Text t, UnityEngine.Transform root)
        {
            float a = t.color.a;
            for (var tr = t.transform; tr != null && tr != root; tr = tr.parent)
            {
                var cg = tr.GetComponent<UnityEngine.CanvasGroup>();
                if (cg != null) a *= cg.alpha;
            }
            return a;
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
                    // The bar keeps an icon-only variant (a checkmark) whose graphics are off: nothing shows.
                    if (CrimsonNodes.Texts(c).Count == 0) continue;
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
