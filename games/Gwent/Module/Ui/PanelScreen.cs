using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Gwent's fallback screen: any panel on the input layer that has the input which no dedicated screen reads
    /// (the leaderboards, "What's new!", the journey's story…). What the panel draws is listed in reading order,
    /// top to bottom:
    /// every visible text that isn't a control's caption is a read-only item, every selectable control and
    /// captioned bar button is a button (Enter clicks it, the mouse's way). Items that share a line on screen
    /// form a row (a leaderboard entry's rank, rating, level and name): Left/Right move along it, Up/Down
    /// between lines. The panel's bottom button bar is its own Tab stop. Dedicated screens sit on higher layers
    /// and take over wherever they apply; the main menu, the battle and the always-on overlays are left out.
    /// </summary>
    internal sealed class PanelScreen : Screen
    {
        public override string Key => "panel";
        public override int Layer => -1;

        // Panels named by the game's own title for them (the menu card or button that opens them), not their object.
        private static readonly Dictionary<string, (string Key, string Fallback)> Titles = new Dictionary<string, (string, string)>
        {
            ["UIEP_5_2_Prefab(Clone)"] = ("EP_5_2_title", "Journey"),
            ["UIWelcomeScreenPanel(Clone)"] = ("mainmenu_welcomescreen_button_tooltip", "What's new!"),
            ["UIMainMenuLeaderboardPrefab(Clone)"] = ("leaderboards_title", "Leaderboards"),
            ["UIDraftPanel(Clone)"] = ("arenav2_mainmenu_title", "Draft"),
        };

        public override string ScreenName
        {
            get
            {
                var p = Panel();
                if (p == null) return null;
                if (Titles.TryGetValue(p.gameObject.name, out var t)) return GameText.Get(t.Key, t.Fallback);
                if (p.TryCast<UILoadingScreenBase>() != null) return "Loading"; // its tip is read as its content
                if (p.TryCast<IntroPanel>() != null) return "Versus";            // the two players before the coin toss
                return UiText.FriendlyName(p.gameObject.name.Replace("(Clone)", "").Replace("Prefab", "").Replace("UIDB", "").Replace("UI", ""));
            }
        }

        // The tooltip the game shows for the control under the cursor (a reward icon's name, an icon button's
        // purpose), as a sighted player reads it on hover: the landing raises the control's hover.
        private static string HoverTooltip(AControl c)
        {
            try
            {
                if (GraphNavigator.CurrentNode?.Id.Reference is not AControl cur || cur.Pointer != c.Pointer) return null;
                var panel = UnityEngine.Object.FindObjectOfType<GwentUnity.UI.UITooltipPanel>();
                if (panel == null || !panel.gameObject.activeInHierarchy) return null;
                var parts = new List<string>();
                foreach (var t in panel.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (!t.isActiveAndEnabled || Faded(t)) continue;
                    string s = UiText.Clean(t.text);
                    if (s.Length > 0 && !parts.Contains(s)) parts.Add(s);
                }
                return parts.Count > 0 ? string.Join(". ", parts) : null;
            }
            catch { return null; }
        }

        public override bool IsActive() => Panel() != null;

        public override bool NamedOnReturn => Panel()?.TryCast<UILoadingScreenBase>() != null;

        /// <summary>The journey, leaderboards… are windows of the screen that opened them (the main menu keeps its
        /// place under them); a loading screen is not (what follows it replaces the screen before).</summary>
        public override bool Overlays => Panel() is AGwentPanel p && p.TryCast<UILoadingScreenBase>() == null;

        // A panel starts at its top, not wherever the game parked its selection (the leaderboard opened on its
        // tenth item, the journey on EXIT): the game's selection is followed only after the player's first key.
        private bool _fresh;
        public override void OnFocus() => _fresh = true;

        public override AControl GameSelection()
        {
            if (_fresh)
            {
                if (Keys.AnyDown()) _fresh = false;
                return null;
            }
            return base.GameSelection();
        }

        // Panels another screen or the game's furniture owns: never the fallback's.
        private static readonly string[] Skip =
        {
            "UIMainMenuContentPanel", "UINotificationsPanel", "UITooltipPanel", "UIPlayerInfoOverlay", "UIBattlePanel",
            "UIBattleDialoguePanel", "UIBattleCardPresentationPanel", "SocialPanel", "UIMainMenuBGPanel", "PopupPanel",
            "UIResultBannerPanel", // the results screen that follows names itself by the same banner
            "TauntSelectorView", // the emote wheel: EmoteScreen once it has opened
        };

        // Panels a dedicated screen reads once they finish opening: the fallback would read their placeholders
        // meanwhile. The journey's story is still the fallback's.
        private static bool Dedicated(AGwentPanel p)
        {
            if (p.TryCast<GwentVisuals.UIOuroborosProgressionPanel>() != null) return true;
            var journey = p.TryCast<GwentVisuals.UIJourneyPanel>();
            try { return journey != null && journey.m_CurrentState == GwentVisuals.JourneyViewState.Main; } catch { return false; }
        }

        // The first attached, shown panel on the active layer with something to read or press.
        private static AGwentPanel Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var p = attached[i]?.TryCast<AGwentPanel>();
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                if (Array.IndexOf(Skip, p.GetIl2CppType().Name) >= 0 || Dedicated(p)) continue;
                var v = p.Visibility;
                if (v != CrimsonUIBehaviour.EVisibility.Shown && v != CrimsonUIBehaviour.EVisibility.Showing) continue;
                return p;
            }
            return null;
        }

        private sealed class Entry
        {
            public Vector3 Pos;
            public Transform TextTransform;
            public string Text;     // a read-only text…
            public AControl Control; // …or a control
        }

        public override void Build(GraphBuilder b)
        {
            var panel = Panel();
            if (panel == null) return;

            var controls = new List<AControl>();
            var captions = new HashSet<IntPtr>();
            foreach (var c in CrimsonNodes.Leaves(panel)) controls.Add(c);
            var seen = new HashSet<IntPtr>();
            foreach (var c in controls) seen.Add(c.Pointer);
            var bar = CrimsonNodes.BarButtons(panel, seen);
            foreach (var c in controls) foreach (var t in c.GetComponentsInChildren<TMPro.TMP_Text>(false)) captions.Add(t.Pointer);
            foreach (var c in bar) foreach (var t in c.GetComponentsInChildren<TMPro.TMP_Text>(false)) captions.Add(t.Pointer);

            var entries = new List<Entry>();
            foreach (var t in panel.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (captions.Contains(t.Pointer) || !t.isActiveAndEnabled || Faded(t)) continue;
                string s = UiText.Clean(t.text);
                if (s.Length == 0 || s.StartsWith("[MISSING]") || UiText.IsPlaceholder(s)) continue;
                entries.Add(new Entry { Pos = t.transform.position, Text = s, TextTransform = t.transform });
            }
            foreach (var c in controls) entries.Add(new Entry { Pos = c.transform.position, Control = c });
            // A button standing for a whole entry (a leaderboard line's button reads every column) makes that entry's
            // texts redundant: drop texts inside the button's prefab instance that its label already says.
            foreach (var c in controls)
            {
                Transform root = null;
                for (var p = c.transform; p != null && p.Pointer != panel.transform.Pointer; p = p.parent)
                    if (p.name.EndsWith("(Clone)")) { root = p; break; }
                if (root == null) continue;
                string label = CrimsonNodes.Label(c) ?? "";
                entries.RemoveAll(e => e.Control == null && e.TextTransform != null && e.TextTransform.IsChildOf(root)
                    && label.IndexOf(e.Text, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (entries.Count == 0 && bar.Count == 0) return;

            // Reading order by the game's own structure: entries under the same row object (a leaderboard entry,
            // a reward line: the nearest prefab instance or horizontal layout above them) form a group; groups read
            // top to bottom, then left to right; a group whose members share a line is a row (Left/Right through
            // its columns), else its members read top to bottom.
            var groups = new Dictionary<IntPtr, List<Entry>>();
            var order = new List<IntPtr>();
            foreach (var e in entries)
            {
                var key = GroupOf(e.Control != null ? e.Control.transform : e.TextTransform, panel.transform);
                if (!groups.TryGetValue(key, out var list)) { groups[key] = list = new List<Entry>(); order.Add(key); }
                list.Add(e);
            }
            var sorted = new List<List<Entry>>();
            foreach (var k in order)
            {
                // A group with a button that already says its texts (a leaderboard entry's button reads the whole
                // line) keeps the button only.
                var g = groups[k];
                var said = new List<string>();
                foreach (var e in g) if (e.Control != null) said.Add(CrimsonNodes.Label(e.Control) ?? "");
                if (said.Count > 0) g.RemoveAll(e => e.Control == null && said.Exists(l => l.IndexOf(e.Text, StringComparison.OrdinalIgnoreCase) >= 0));
                if (g.Count > 0) sorted.Add(g);
            }
            float Top(List<Entry> g) { float t = float.MinValue; foreach (var e in g) t = Math.Max(t, e.Pos.y); return t; }
            float Left(List<Entry> g) { float l = float.MaxValue; foreach (var e in g) l = Math.Min(l, e.Pos.x); return l; }
            sorted.Sort((x, y) => Math.Abs(Top(x) - Top(y)) > LineSlack ? Top(y).CompareTo(Top(x)) : Left(x).CompareTo(Left(y)));
            // A caption drawn with its number on its own just after ("Wins in current draft", "0"): one item.
            for (int i = 0; i + 1 < sorted.Count; i++)
            {
                var a = sorted[i]; var z = sorted[i + 1];
                if (a.Count == 1 && z.Count == 1 && a[0].Control == null && z[0].Control == null
                    && IsNumber(z[0].Text) && !IsNumber(a[0].Text) && !char.IsDigit(a[0].Text[a[0].Text.Length - 1]))
                {
                    a[0].Text = a[0].Text + ", " + z[0].Text;
                    sorted.RemoveAt(i + 1);
                }
            }
            b.BeginStop("panel:content");
            int n = 0;
            // A notice with nothing to press (the match intro, a loading tip): one message, read whole on landing;
            // it is often up only for seconds. Keyed by its words, so a changed notice lands afresh.
            if (controls.Count == 0 && bar.Count == 0)
            {
                var words = new List<string>();
                foreach (var g in sorted)
                {
                    g.Sort((x, y) => Math.Abs(x.Pos.y - y.Pos.y) <= LineSlack ? x.Pos.x.CompareTo(y.Pos.x) : y.Pos.y.CompareTo(x.Pos.y));
                    foreach (var e in g) words.Add(e.Text);
                }
                string message = string.Join(", ", words);
                // A notice that grows on the same panel (the intro's coin toss result) says only what is new; Space
                // reads it all.
                string key = panel.gameObject.name;
                if (key != _noticePanel) { _noticePanel = key; _noticeWords = new List<string>(); }
                if (message != _noticeMessage)
                {
                    var fresh = words.FindAll(w => !_noticeWords.Contains(w));
                    _noticeSaid = fresh.Count > 0 && fresh.Count < words.Count ? string.Join(", ", fresh) : message;
                    _noticeMessage = message;
                    _noticeWords = words;
                }
                string said = _noticeSaid;
                b.AddItem(ControlId.Structural("panel:message:" + message), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => said, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(message, true),
                });
                return;
            }
            foreach (var g in sorted)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var e in g) { lo = Math.Min(lo, e.Pos.y); hi = Math.Max(hi, e.Pos.y); }
                bool row = g.Count > 1 && hi - lo <= RowSlack;
                g.Sort((x, y) => row || Math.Abs(x.Pos.y - y.Pos.y) <= LineSlack ? x.Pos.x.CompareTo(y.Pos.x) : y.Pos.y.CompareTo(x.Pos.y));
                if (!row) JoinNumbers(g);
                if (row) b.StartRow();
                foreach (var e in g) Add(b, e, ref n);
                if (row) b.EndRow();
            }
            if (bar.Count > 0)
            {
                b.BeginStop("panel:bar");
                foreach (var c in bar) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        private const float LineSlack = 6f, RowSlack = 24f;

        // A caption with its number drawn on its own just after it, within a group: one item.
        private static void JoinNumbers(List<Entry> g)
        {
            for (int i = 0; i + 1 < g.Count; i++)
            {
                var a = g[i]; var z = g[i + 1];
                if (a.Control == null && z.Control == null && IsNumber(z.Text) && !IsNumber(a.Text) && !char.IsDigit(a.Text[a.Text.Length - 1]))
                {
                    a.Text = a.Text + ", " + z.Text;
                    g.RemoveAt(i + 1);
                }
            }
        }

        private static bool IsNumber(string s) => s.Length > 0 && s.Length <= 6 && int.TryParse(s, out _);

        // The notice last built: its panel, words and message, and what its landing says.
        private static string _noticePanel, _noticeMessage, _noticeSaid;
        private static List<string> _noticeWords = new List<string>();

        // The row object an item belongs to: its nearest ancestor below the panel that is a prefab instance
        // ("…(Clone)") or lays its children out side by side; the panel itself when there is none.
        private static IntPtr GroupOf(Transform t, Transform panel)
        {
            for (var p = t.parent; p != null && p.Pointer != panel.Pointer; p = p.parent)
                if (p.name.EndsWith("(Clone)") || p.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() != null) return p.Pointer;
            return panel.Pointer;
        }

        private static void Add(GraphBuilder b, Entry e, ref int n)
        {
            if (e.Control != null)
            {
                var c = e.Control;
                var v = CrimsonNodes.Button(c);
                v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                {
                    new NodeAnnouncement(() => HoverTooltip(c), live: true, kind: AnnouncementKinds.Tooltip),
                };
                b.AddItem(CrimsonNodes.Id(c), v);
                return;
            }
            string text = e.Text;
            b.AddItem(ControlId.Structural("panel:text:" + (n++) + ":" + text), new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(() => text, kind: AnnouncementKinds.Label) },
                OnTooltip = () => Speech.Say(text, true),
            });
        }

        // Drawn invisibly: text alpha 0, or a fully transparent CanvasGroup above it (faded, not disabled).
        private static bool Faded(TMPro.TMP_Text t)
        {
            if (t.alpha <= 0.01f) return true;
            foreach (var g in t.GetComponentsInParent<CanvasGroup>())
            {
                if (g.alpha <= 0.01f) return true;
                if (g.ignoreParentGroups) break;
            }
            return false;
        }
    }
}
