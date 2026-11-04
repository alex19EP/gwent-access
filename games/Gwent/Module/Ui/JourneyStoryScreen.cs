using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentVisuals;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The Journey's story (<see cref="UIJourneyStory"/>, the Journey panel in its Story state), opened by the
    /// Journey's "Journey Story" button. The fallback read it as loose texts: the story's name twice (the title
    /// and the selected story's tab say the same "Geralt's Story"), the whole chapter as one item, and nothing
    /// after Next. Tab stops, as drawn:
    ///
    /// - The stories: the two story tabs ("Dandelion's Story", "Geralt's Story"), the shown one "selected";
    ///   Enter switches to the other.
    /// - The page: the title (when it says more than the selected tab), then the chapter, a paragraph per item.
    /// - Paging: Previous, the page counter ("1 of 12"), Next. After Previous or Next, the new page is read
    ///   when it arrives (its counter, then its text), as a sighted player's eyes go to it.
    /// - The panel's bar (Close).
    /// </summary>
    internal sealed class JourneyStoryScreen : Screen
    {
        public override string Key => "journey-story";
        public override int Layer => 1;
        public override bool Overlays => true;

        public override string ScreenName => GameText.Get("ui_journey_main_panel_story", "Journey Story");

        public override bool IsActive() => Story() != null;

        internal static UIJourneyStory Story()
        {
            var p = CrimsonNodes.ActivePanel<UIJourneyPanel>();
            if (p == null) return null;
            try
            {
                if (p.m_CurrentState != JourneyViewState.Story) return null;
                var s = p.m_Story;
                return CrimsonNodes.Shown(s) ? s : null;
            }
            catch { return null; }
        }

        // The game parks its selection on a paging button; the cursor starts on the page.
        public override AControl GameSelection() => null;

        public override void OnPop() => _turnedFrom = null;

        public override void Build(GraphBuilder b)
        {
            var s = Story();
            if (s == null) return;
            var used = new HashSet<IntPtr>();

            // ---- the stories ----
            var sel = s.m_StorySelection;
            var tabs = new List<UITextButton>();
            if (CrimsonNodes.Shown(sel))
                foreach (var t in new[] { sel.m_StoryLeftButton, sel.m_StoryRightButton })
                    if (CrimsonNodes.Shown(t)) tabs.Add(t);
            string selected = null;
            if (tabs.Count > 0)
            {
                b.BeginStop("story:stories");
                foreach (var t in tabs)
                {
                    used.Add(t.Pointer);
                    var tab = t;
                    if (CrimsonNodes.Toggled(tab)) selected = CrimsonNodes.Texts(tab) is var tt && tt.Count > 0 ? tt[0] : null;
                    // The two stories' buttons switch the page below them: tabs.
                    var v = CrimsonNodes.WithRole(CrimsonNodes.Button(tab), Loc.T("tab"));
                    v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                    {
                        new NodeAnnouncement(() => CrimsonNodes.Toggled(tab) ? Loc.T("selected") : null, live: true, kind: AnnouncementKinds.Selected),
                    };
                    b.AddItem(CrimsonNodes.Id(tab), v);
                }
            }

            // ---- the page ----
            b.BeginStop("story:page");
            string title = CrimsonNodes.Text(s.m_Title);
            if (title.Length > 0 && !string.Equals(title, selected, StringComparison.OrdinalIgnoreCase))
                b.AddLabel(ControlId.Structural("story:title"), () => CrimsonNodes.Text(s.m_Title));
            var paragraphs = Paragraphs(s);
            ControlId start = null;
            for (int i = 0; i < paragraphs.Count; i++)
            {
                string text = paragraphs[i];
                // Keyed by the page and place: a new page lands afresh.
                var id = ControlId.Structural("story:p:" + Page(s) + ":" + i);
                start ??= id;
                b.AddLabel(id, () => text);
            }
            if (start != null) b.SetStart(start);

            // ---- paging ----
            var paging = Paging(s);
            if (paging != null)
            {
                b.BeginStop("story:paging");
                AddTurn(b, paging.m_PrevButton, used);
                string counter = CrimsonNodes.Text(paging.m_PagingLabel);
                if (counter.Length > 0) b.AddLabel(ControlId.Structural("story:counter"), () => CrimsonNodes.Text(paging.m_PagingLabel));
                AddTurn(b, paging.m_NextButton, used);
            }

            // ---- the bar ----
            var bar = CrimsonNodes.BarButtons(s, used);
            if (bar.Count > 0)
            {
                b.BeginStop("story:bar");
                foreach (var c in bar) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        // The paging the story shows: the full-image chapters have their own.
        private static UIJourneyStoryPaging Paging(UIJourneyStory s)
        {
            if (CrimsonNodes.Shown(s.m_PagingFullImage)) return s.m_PagingFullImage;
            return CrimsonNodes.Shown(s.m_Paging) ? s.m_Paging : null;
        }

        private static string Page(UIJourneyStory s)
        {
            var p = Paging(s);
            return p != null ? CrimsonNodes.Text(p.m_PagingLabel) : "";
        }

        /// <summary>The chapter as drawn, a paragraph per item: the game writes its line breaks into the text
        /// (some as the two characters '\' 'n').</summary>
        private static List<string> Paragraphs(UIJourneyStory s)
        {
            var list = new List<string>();
            try
            {
                var label = s.m_ChapterLabel;
                if (!CrimsonNodes.Shown(label)) return list;
                string raw = label.Text?.text ?? "";
                foreach (var part in raw.Replace("\\n", "\n").Split('\n'))
                {
                    string p = UiText.Clean(part);
                    if (p.Length > 0) list.Add(p);
                }
            }
            catch (Exception e) { Plugin.LogInfo("[story] chapter read failed: " + e.Message); }
            return list;
        }

        // Previous / Next: the mouse's click, then the new page is read when it arrives.
        private void AddTurn(GraphBuilder b, UITextButton button, HashSet<IntPtr> used)
        {
            if (!CrimsonNodes.Shown(button) || !used.Add(button.Pointer)) return;
            var v = CrimsonNodes.Button(button);
            v.OnActivate = () =>
            {
                if (!button.IsEnabled) { Speech.Say(Loc.T("Not available right now."), true); return; }
                var s = Story();
                _turnedFrom = s != null ? string.Join(" ", Paragraphs(s)) + "|" + Page(s) : null;
                Navigator.MouseClick(button);
            };
            b.AddItem(CrimsonNodes.Id(button), v);
        }

        // The page the player turned from, until the new one is read.
        private string _turnedFrom;

        public override void Tick()
        {
            if (_turnedFrom == null) return;
            var s = Story();
            if (s == null) { _turnedFrom = null; return; }
            var paragraphs = Paragraphs(s);
            string now = string.Join(" ", paragraphs) + "|" + Page(s);
            if (now == _turnedFrom || paragraphs.Count == 0) return;
            _turnedFrom = null;
            Speech.Say(UiText.Join(Page(s), string.Join(" ", paragraphs)), true);
        }
    }
}
