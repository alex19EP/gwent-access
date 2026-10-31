using Access.Core.Graph;
using CrimsonUI;
using GwentUnity.CutsceneMoviePlayer;

namespace GwentAccess.Ui
{
    /// <summary>
    /// A playing cutscene (CutscenePlayer: the intro and the story films). Its subtitles are spoken by
    /// <see cref="SpeakSubtitles"/> as each line appears, queued behind whatever is speaking, unless the config turns them
    /// off (<see cref="ModConfig.ReadSubtitles"/>); a line carries its speaker ("Наемник: …") as the game shows it.
    /// The screen itself holds the skip prompt ("Пропуск (зажать)", shown once a key is pressed); Space on it
    /// re-reads the current subtitle.
    ///
    /// Without this screen the fallback panel screen read the subtitle bar as the panel's message, which came
    /// and went with every line, so the reading was tied to the panel flickering in and out.
    /// </summary>
    internal sealed class CutsceneScreen : Screen
    {
        public override string Key => "cutscene";
        public override int Layer => 2;

        public override bool IsActive() => Find() != null;

        private static CutscenePlayer Find()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var p = attached[i]?.TryCast<CutscenePlayer>();
                if (p != null && p.gameObject.activeInHierarchy) return p;
            }
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var player = Find();
            var skip = player?.m_SkipLabel;
            if (skip == null || !skip.gameObject.activeInHierarchy) return;
            string label = string.Join(" ", CrimsonNodes.Texts(skip));
            if (label.Length == 0) return;
            b.BeginStop("cutscene");
            b.AddItem(ControlId.Structural("cutscene:skip"), new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(() => label, kind: AnnouncementKinds.Label) },
                OnTooltip = () => { string s = Subtitle(Find()); if (s.Length > 0) Speech.Say(s, true); },
            });
        }

        public override AControl GameSelection() => null;

        // The subtitle line on screen, "" when the bar is hidden or empty.
        private static string Subtitle(CutscenePlayer player)
        {
            if (player == null) return "";
            var root = player.m_SubtitlesRoot;
            var text = player.m_SubtitlesTarget;
            if (root != null && !root.activeInHierarchy) return "";
            if (text == null || !text.gameObject.activeInHierarchy || text.alpha <= 0.01f) return "";
            return UiText.Clean(text.text);
        }

        private static string _lastLine = "";

        /// <summary>Per frame, whichever screen has the navigator: speak a new subtitle line once.</summary>
        public static void SpeakSubtitles()
        {
            string line;
            try { line = Subtitle(Find()); }
            catch { line = ""; }
            if (line == _lastLine) return;
            _lastLine = line;
            if (line.Length > 0 && ModConfig.ReadSubtitles) Speech.Say(line, false);
        }
    }
}
