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
    /// (the journey track, the leaderboards, "What's new!", the season cycle…). It replaces the older F2/F3
    /// object navigator and selection narrator. What the panel draws is listed in reading order, top to bottom:
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
            ["UIWelcomeScreenPanel(Clone)"] = ("welcome_screen_title", "What's new!"),
            ["UIOuroborosProgressionPanelPrefab(Clone)"] = ("ouroboros_main_menu_card_title_autumn", "Season cycle"),
            ["UIMainMenuLeaderboardPrefab(Clone)"] = ("leaderboards_title", "Leaderboards"),
        };

        public override string ScreenName
        {
            get
            {
                var p = Panel();
                if (p == null) return null;
                if (Titles.TryGetValue(p.gameObject.name, out var t)) return GameText.Get(t.Key, t.Fallback);
                return UiText.FriendlyName(p.gameObject.name.Replace("(Clone)", "").Replace("Prefab", "").Replace("UI", ""));
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

        // Panels another screen or the game's furniture owns: never the fallback's.
        private static readonly string[] Skip =
        {
            "UIMainMenuContentPanel", "UINotificationsPanel", "UITooltipPanel", "UIPlayerInfoOverlay", "UIBattlePanel",
            "UIBattleDialoguePanel", "UIBattleCardPresentationPanel", "SocialPanel", "UIMainMenuBGPanel", "PopupPanel",
        };

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
                if (Array.IndexOf(Skip, p.GetIl2CppType().Name) >= 0) continue;
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
            public bool Bar;         // a bottom bar button
        }

        public override void Build(GraphBuilder b)
        {
            var panel = Panel();
            if (panel == null) return;

            var controls = new List<AControl>();
            var captions = new HashSet<IntPtr>();
            foreach (var c in CrimsonNodes.Leaves(panel)) controls.Add(c);
            var bar = new List<AControl>();
            foreach (var container in panel.GetComponentsInChildren<InputButtonContainer>(false))
            {
                var children = container.GetChildren();
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                    if (CrimsonNodes.Texts(c).Count == 0) continue;
                    if (!controls.Exists(x => x.Pointer == c.Pointer) && !bar.Exists(x => x.Pointer == c.Pointer)) bar.Add(c);
                }
            }
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
            b.BeginStop("panel:content");
            int n = 0;
            foreach (var g in sorted)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var e in g) { lo = Math.Min(lo, e.Pos.y); hi = Math.Max(hi, e.Pos.y); }
                bool row = g.Count > 1 && hi - lo <= RowSlack;
                g.Sort((x, y) => row || Math.Abs(x.Pos.y - y.Pos.y) <= LineSlack ? x.Pos.x.CompareTo(y.Pos.x) : y.Pos.y.CompareTo(x.Pos.y));
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
                var step = c.GetComponentInParent<GwentVisuals.UIJourneyStepReward>();
                var v = step != null ? CrimsonNodes.Button(c, () => JourneyStep(step)) : CrimsonNodes.Button(c);
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

        // A journey step's reward, which the game draws as pictures only: "Step 3, 100 Ore, claimed" (the
        // premium track's slot says so). Named the way the reward popup names rewards.
        private static string JourneyStep(GwentVisuals.UIJourneyStepReward step)
        {
            var parts = new List<string>();
            try
            {
                int n = step.m_StepData?.StepNumber ?? 0;
                if (n > 0) parts.Add("Step " + n + (step.m_IsExtendedSlot ? ", premium" : ""));
                var data = step.RewardData;
                var rewards = data?.Rewards;
                for (int i = 0; rewards != null && i < rewards.Count; i++)
                {
                    string r = UiText.Clean(GwentUnity.UI.PlayerProfile.RewardTextHelper.GetText(rewards[i]));
                    if (r.Length > 0) parts.Add(r);
                }
                if (data != null) parts.Add(data.RewardState.ToString().ToLowerInvariant());
            }
            catch (Exception e) { Plugin.LogInfo("[panel] journey step: " + e.Message); }
            return parts.Count > 0 ? string.Join(", ", parts) : "Reward";
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
