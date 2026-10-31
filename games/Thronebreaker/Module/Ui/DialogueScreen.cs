using System.Collections.Generic;
using System.Text.RegularExpressions;
using Access.Core.Graph;
using CrimsonUI;
using GwentVisuals.Campaign;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Thronebreaker's story dialogues: every say dialog the active input layer holds (ASayDialog: the cinematic
    /// scenes with the narrator, the active dialogue with a portrait, text events, the tutorial's dialog…).
    /// One stop: the line, any consequence notice ("Вы выбрали одно из двух зол."), the choices, then the
    /// captioned buttons of the dialog's bar (Продолжить, Выбрать).
    ///
    /// The line is read whole as soon as it starts: the game writes it out letter by letter by moving an
    /// invisible colour tag (&lt;color=#FFFFFF00&gt;) through the full text, and reading the text as it grows
    /// re-read the line at every step. The tags are dropped and the line node is keyed by the full text, so each
    /// line is spoken once, with its speaker ("Рассказчик: …"): the dialog's visible texts that aren't the line,
    /// the notice, a choice or a key prompt. With <see cref="ModConfig.ReadSubtitles"/> off the voice acting
    /// speaks the lines, and the line node keeps one key so a new line isn't announced.
    ///
    /// Enter on the line, when there are no choices, continues: ASayDialog.Continue, which the game runs for its
    /// continue actions (Rewired actions 2 and 28, get_ContinueActions) and a left click. The bar's Продолжить is
    /// only a mouse hint (InputButtonContainer.AddMouseLabel with no callback), so clicking it does nothing, and
    /// the claimed Enter never reaches the game's own binding. When choices appear the cursor lands on the first one, so they are read; Enter on a
    /// choice picks it. Space on the line re-reads it. Escape stays the game's.
    /// </summary>
    internal sealed class DialogueScreen : Screen
    {
        public override string Key => "dialogue";
        public override int Layer => 2;

        public override bool IsActive() => Find() != null;

        private static ASayDialog Find()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = attached.Count - 1; i >= 0; i--)
            {
                var d = attached[i]?.TryCast<ASayDialog>();
                if (d != null && d.gameObject.activeInHierarchy) return d;
            }
            return null;
        }

        // The choice set last seen, so the cursor moves to a new set once and is free after that.
        private string _choicesKey = "";

        public override void Build(GraphBuilder b)
        {
            var d = Find();
            if (d == null) return;

            var choices = Choices(d);
            var bar = BarButtons(d.m_ButtonsPanel);
            string line = Line(d.m_NarrationText);
            string notice = Visible(d.m_ConsequencesText) ? UiText.Clean(d.m_ConsequencesText.text) : "";
            string speaker = line.Length > 0 ? Speaker(d) : "";
            string title = "";
            var textEvent = d.TryCast<UITextEventDialogPanel>();
            if (textEvent != null && Visible(textEvent.m_TitleText)) title = UiText.Clean(textEvent.m_TitleText.text);

            if (line.Length == 0 && notice.Length == 0 && choices.Count == 0 && bar.Count == 0) return;
            b.BeginStop("dialogue");
            if (title.Length > 0) b.PushContext(title);

            // With subtitles off the line node stays while the game clears the text between lines: were it to
            // go, the cursor would drop to another node and land back on the next line, reading it.
            if (line.Length > 0 || !ModConfig.ReadSubtitles)
            {
                string spoken = speaker.Length > 0 ? speaker + ": " + line : line;
                // Keyed by the text, each new line is a new landing and is spoken. With subtitles off the game's
                // voice acting speaks it: one key for every line, so the cursor stays put and nothing is read
                // unless asked (arrow onto it, Space).
                var id = ControlId.Structural(ModConfig.ReadSubtitles ? "dialogue:line:" + spoken : "dialogue:line");
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => spoken, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(spoken, true),
                    OnActivate = choices.Count == 0 ? () => Find()?.Continue() : (System.Action)null,
                });
                b.SetStart(id);
            }
            if (notice.Length > 0)
                b.AddItem(ControlId.Structural("dialogue:notice:" + notice), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => notice, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(notice, true),
                });

            var keys = new List<string>();
            foreach (var c in choices)
            {
                var id = CrimsonNodes.Id(c);
                b.AddItem(id, CrimsonNodes.Button(c));
                keys.Add(id.StructuralKey + "=" + string.Join(" ", CrimsonNodes.Texts(c)));
            }
            string choicesKey = string.Join("|", keys);
            if (choicesKey != _choicesKey)
            {
                _choicesKey = choicesKey;
                if (choices.Count > 0) GraphNavigator.Suggest(CrimsonNodes.Id(choices[0]));
            }

            foreach (var c in bar) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            if (title.Length > 0) b.PopContext();
        }

        public override AControl GameSelection() => null;

        // ---- reading ----

        private static readonly Regex ColorTags = new Regex("</?color[^>]*>", RegexOptions.IgnoreCase);

        /// <summary>The whole line, written or not yet: the writer's colour tags dropped (not turned into spaces,
        /// they sit mid-word).</summary>
        private static string Line(TMPro.TMP_Text t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return "";
            return UiText.Clean(ColorTags.Replace(t.text ?? "", ""));
        }

        private static bool Visible(TMPro.TMP_Text t) =>
            t != null && t.gameObject.activeInHierarchy && !PanelScreen.Faded(t) && UiText.Clean(t.text).Length > 0;

        /// <summary>Who speaks: the portrait's name (the active dialogue), else the dialog's other visible texts
        /// (the cinematic scene's speaker label, "Рассказчик").</summary>
        private static string Speaker(ASayDialog d)
        {
            var active = d.TryCast<UIActiveDialogPanel>();
            if (active != null && Visible(active.m_NameText)) return UiText.Clean(active.m_NameText.text);

            var skip = new HashSet<System.IntPtr>();
            if (d.m_NarrationText != null) skip.Add(d.m_NarrationText.Pointer);
            if (d.m_ConsequencesText != null) skip.Add(d.m_ConsequencesText.Pointer);
            var textEvent = d.TryCast<UITextEventDialogPanel>();
            if (textEvent?.m_TitleText != null) skip.Add(textEvent.m_TitleText.Pointer);
            var parts = new List<string>();
            foreach (var t in d.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (skip.Contains(t.Pointer) || !Visible(t)) continue;
                if (t.GetComponentInParent<GlyphLabelControl>() != null) continue;
                if (t.GetComponentInParent<InputButtonContainer>() != null) continue;
                if (t.GetComponentInParent<ADialogChoicesContainer>() != null) continue;
                string s = UiText.Clean(t.text);
                if (!UiText.IsPlaceholder(s) && !s.StartsWith("[MISSING]") && !parts.Contains(s)) parts.Add(s);
            }
            return string.Join(", ", parts);
        }

        // The choices on offer, in the game's order.
        private static List<AControl> Choices(ASayDialog d)
        {
            var list = new List<AControl>();
            var boxes = new List<ADialogChoicesContainer>(d.GetComponentsInChildren<ADialogChoicesContainer>(false));
            var own = d.DialogChoicesContainer; // spawned under a root that may sit outside the dialog
            if (own != null && own.gameObject.activeInHierarchy) boxes.Add(own);
            foreach (var box in boxes)
                foreach (var c in CrimsonNodes.Leaves(box))
                    if (CrimsonNodes.Texts(c).Count > 0 && !list.Exists(x => x.Pointer == c.Pointer)) list.Add(c);
            return list;
        }

        // A bar's captioned, shown buttons, in its order.
        private static List<AControl> BarButtons(InputButtonContainer bar)
        {
            var list = new List<AControl>();
            if (bar == null || !bar.gameObject.activeInHierarchy) return list;
            var children = bar.GetChildren();
            for (int i = 0; children != null && i < children.Count; i++)
            {
                var c = children[i];
                if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                if (CrimsonNodes.Texts(c).Count == 0) continue;
                list.Add(c);
            }
            return list;
        }
    }
}
