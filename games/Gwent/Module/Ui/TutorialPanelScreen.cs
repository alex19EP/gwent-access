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
    /// then the panel's buttons (settings, continue, skip), clicked through the game's own controls. The text
    /// node is keyed by what it says, so a panel that pages to its next text lands afresh and is spoken.
    ///
    /// The intro panel opens the settings window on its own input layer (Escape or its settings button) and stays
    /// attached underneath; the screen steps aside for it, so <see cref="SettingsScreen"/> gets the keyboard.
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

        // Every button caption seen this push. Kept across renders: opening settings removes the intro panel's
        // Continue from its bar a frame before its caption goes, and a caption read as panel text would change
        // the text node's key and speak the whole text again.
        private readonly HashSet<string> _captions = new HashSet<string>();

        public override void OnPush() { _lastKey = null; _built = false; _captions.Clear(); }

        // The tutorial panel attached to the input layer that has the input; none while the settings window is
        // open on that layer, which the player is then in.
        private static AGwentPanel Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            AGwentPanel found = null;
            for (int i = 0; i < attached.Count; i++)
            {
                var c = attached[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                if (c.TryCast<UISettingsPanel>() != null) return null;
                AGwentPanel p = c.TryCast<ATutorialPanel>();
                p ??= c.TryCast<UITutorialIntroPanel>();
                p ??= c.TryCast<UIRoundTutorialPopupPanel>();
                found ??= p;
            }
            return found;
        }

        public override void Build(GraphBuilder b)
        {
            var panel = Panel();
            if (panel == null) return;

            var buttons = Buttons(panel);
            var intro = panel.TryCast<UITutorialIntroPanel>();
            var skip = intro != null ? SkipControl(intro) : null;
            var skipTexts = skip != null ? CrimsonNodes.Texts(skip) : null;
            if (intro != null)
            {
                // The skip node stands for both of the panel's skip controls, so neither is listed on its own.
                buttons.RemoveAll(x => x.Pointer == intro.m_SkipTutorialButton?.Pointer
                    || x.Pointer == intro.m_SkipTutorialLabel?.Pointer);
            }
            foreach (var c in buttons) foreach (var s in CrimsonNodes.Texts(c)) _captions.Add(s);
            if (skipTexts != null) foreach (var s in skipTexts) _captions.Add(s);

            b.BeginStop("tutorial");
            var round = panel.TryCast<UIRoundTutorialPopupPanel>();
            if (round != null)
            {
                BuildRound(b, round);
                foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
                return;
            }
            // The image popup's loc key, when it has one: it keys the text node, and tells the glyph names which
            // sentence they are in.
            string key = panel.TryCast<UITutorialImagePopup>()?.CurrentString;
            string text = KeyGlyphs.InContext(key, () => Text(panel, _captions));
            var note = KeyGlyphs.KeyNote(key);
            if (note != null && text.Length > 0) text += " " + note;
            if (text.Length > 0)
            {
                // Keyed by the shown string (the image popup's loc key when it has one), not the panel, so a new
                // text lands afresh and is spoken.
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
            if (skipTexts != null && skipTexts.Count > 0) b.AddItem(CrimsonNodes.Id(skip), SkipNode(intro, skip, skipTexts[0]));
        }

        // The intro panel's skip: whichever of its two controls shows words for the input device, the button or
        // the glyph label the game reaches by key. The other stays active with nothing drawn. Null while the
        // panel's input is unbound.
        private static AControl SkipControl(UITutorialIntroPanel intro)
        {
            AControl button = intro.m_SkipTutorialButton;
            if (button != null && button.gameObject.activeInHierarchy && CrimsonNodes.Texts(button).Count > 0) return button;
            AControl label = intro.m_SkipTutorialLabel;
            return label != null && label.gameObject.activeInHierarchy && CrimsonNodes.Texts(label).Count > 0 ? label : null;
        }

        // Skip goes through the panel's own handler, as both controls do. It only asks: the game's confirmation
        // popup takes the input, and nothing is sent unless the player accepts it there.
        private static NodeVtable SkipNode(UITutorialIntroPanel intro, AControl skip, string caption)
        {
            var v = CrimsonNodes.Button(skip, () => caption);
            v.OnActivate = () => intro.HandleSkipTutorial();
            return v;
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
            var seen = new HashSet<System.IntPtr>();
            foreach (var c in buttons) seen.Add(c.Pointer);
            buttons.AddRange(CrimsonNodes.BarButtons(panel, seen));
            // A glyph label or a bar's icon-only variant can be selectable too (the UX flow's gating decides) but
            // shows no words, only the key or icon for the same action: it is no stop of its own. An icon button
            // outside a bar, like the panel's settings, stays; it is named by its icon.
            buttons.RemoveAll(c => CrimsonNodes.Texts(c).Count == 0
                && (c.TryCast<GlyphLabelControl>() != null || c.GetComponentInParent<InputButtonContainer>() != null));
            return buttons;
        }

        // Enter on the text: the panel's own continue handler, as its continue key does.
        private static void Continue(AGwentPanel panel)
        {
            try
            {
                var image = panel.TryCast<UITutorialImagePopup>();
                if (image != null) { image.HandleContinue(); return; }
                // The intro panel's Continue has to be clicked, not just handled: the new-player flow only leaves
                // this step on the button's click event (see Navigator.ClickControl). The binding is gone while
                // the panel's settings are open, as the game's own Continue is.
                var intro = panel.TryCast<UITutorialIntroPanel>();
                if (intro != null)
                {
                    var button = intro.m_ContinueBinding?.Button;
                    if (button != null) Navigator.ClickControl(button);
                    else Speech.Say("No continue here.", true);
                    return;
                }
                var round = panel.TryCast<UIRoundTutorialPopupPanel>();
                if (round != null) { round.HandleContinue(); return; }
                Speech.Say("No continue here.", true);
            }
            catch (System.Exception e) { Plugin.LogInfo("[tutorial] continue failed: " + e); Speech.Say("Couldn't continue.", true); }
        }

        public override AControl GameSelection() => null;
    }
}
