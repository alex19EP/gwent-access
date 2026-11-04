using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity.UI.PlayerProfile;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The profile's showcase picker (<see cref="UIShowcasePicker"/>): choosing a contract to show in a
    /// showcase slot of the profile banner, opened from the slot. The window draws its title, a category
    /// dropdown, the list of contracts that can be shown (and a "clear the slot" entry), and its close and
    /// back. No screen read it. Tab stops, as drawn:
    ///
    /// - The category dropdown's controls.
    /// - The contracts: each drawn entry, read as its card shows it (title, description, progress); the one
    ///   chosen for the slot says "selected". Enter clicks it, the mouse's way. The list draws only the entries
    ///   in view; the game scrolls it as its selection moves, which the cursor writes back as it moves.
    /// - Close and the bar.
    ///
    /// Above the profile (<see cref="ProfileScreen"/>), whose window it opens over.
    /// </summary>
    internal sealed class ShowcasePickerScreen : Screen
    {
        public override string Key => "showcase-picker";
        public override int Layer => 2;

        public override string ScreenName
        {
            get
            {
                var p = Picker();
                string t = p != null ? CrimsonNodes.Text(p.m_Title) : "";
                return t.Length > 0 ? t : "Showcase";
            }
        }

        public override bool IsActive() => Picker() != null;

        private static UIPlayerProfilePanel _panel;
        private static float _nextSearch;

        private static UIShowcasePicker Picker()
        {
            try
            {
                if (_panel == null || _panel.WasCollected || !_panel.gameObject.activeInHierarchy)
                {
                    _panel = null;
                    // FindObjectOfType walks the scene: at most once a second while there is no profile.
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (now < _nextSearch) return null;
                    _nextSearch = now + 1f;
                    _panel = UnityEngine.Object.FindObjectOfType<UIPlayerProfilePanel>();
                }
                var picker = _panel?.m_ShowCasePicker;
                if (!CrimsonNodes.Shown(picker)) return null;
                var v = picker.Visibility;
                return v == CrimsonUIBehaviour.EVisibility.Shown || v == CrimsonUIBehaviour.EVisibility.Showing ? picker : null;
            }
            catch { _panel = null; return null; }
        }

        public override bool Back()
        {
            var p = Picker();
            if (p != null && CrimsonNodes.Shown(p.m_CloseButton)) { Navigator.ClickControl(p.m_CloseButton); return true; }
            return PressBack();
        }

        public override void Build(GraphBuilder b)
        {
            var p = Picker();
            if (p == null) return;
            var used = new HashSet<IntPtr>();

            var dropdown = p.m_ShowcaseAchievementDropdown;
            if (CrimsonNodes.Shown(dropdown))
            {
                var controls = CrimsonNodes.Leaves(dropdown, used);
                if (controls.Count > 0)
                {
                    b.BeginStop("showcase:filter");
                    foreach (var c in controls) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
                }
            }

            var list = p.m_ShowcaseAchievementList;
            if (CrimsonNodes.Shown(list))
            {
                var items = new List<UIShowcaseAchievementListItem>(list.GetComponentsInChildren<UIShowcaseAchievementListItem>(false));
                // Drawn in list order top to bottom; the pool's child order is not the list's.
                items.Sort((x, y) => y.transform.position.y.CompareTo(x.transform.position.y));
                if (items.Count > 0) b.BeginStop("showcase:list");
                foreach (var it in items)
                {
                    var item = it;
                    var button = item.m_ButtonControl;
                    if (!CrimsonNodes.Shown(button) || !used.Add(button.Pointer)) continue;
                    var v = CrimsonNodes.Button(button, () => Entry(item));
                    v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                    {
                        new NodeAnnouncement(() => CrimsonNodes.Shown(item.m_SelectedButton) ? "selected" : null, live: true, kind: AnnouncementKinds.Selected),
                    };
                    v.OnActivate = () => Navigator.MouseClick(button);
                    b.AddItem(CrimsonNodes.Id(button), v);
                }
            }

            var rest = new List<AControl>();
            if (CrimsonNodes.Shown(p.m_CloseButton) && used.Add(p.m_CloseButton.Pointer)) rest.Add(p.m_CloseButton);
            rest.AddRange(CrimsonNodes.BarButtons(p, used));
            foreach (var c in CrimsonNodes.Leaves(p, used))
                if (c.GetComponentInParent<UIShowcaseAchievementList>() == null) rest.Add(c);
            if (rest.Count > 0)
            {
                b.BeginStop("showcase:bar");
                foreach (var c in rest)
                {
                    var control = c;
                    b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, control.Pointer == p.m_CloseButton?.Pointer
                        ? () => CrimsonNodes.Texts(control) is var t && t.Count > 0 ? t[0] : GameText.Get("close", "Close")
                        : null));
                }
            }
        }

        // An entry as its card draws it: the contract's texts, or the clear entry's words.
        private static string Entry(UIShowcaseAchievementListItem item)
        {
            var texts = new List<string>();
            try
            {
                if (CrimsonNodes.Shown(item.m_ClearAchievement)) texts.AddRange(CrimsonNodes.Texts(item.m_ClearAchievement));
                if (CrimsonNodes.Shown(item.m_SimpleAchievement)) texts.AddRange(CrimsonNodes.Texts(item.m_SimpleAchievement));
                if (texts.Count == 0) texts.AddRange(CrimsonNodes.Texts(item));
            }
            catch { }
            return texts.Count > 0 ? UiText.Join(texts.ToArray()) : "Contract";
        }
    }
}
