using System;
using System.Collections.Generic;
using System.Linq;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity.UI.PlayerProfile;
using GwentVisuals;
using Modules.RewardBook.Entities;
using UnityEngine;
using ENodeType = Modules.RewardBook.Entities.RewardSystemTreeElementData.ENodeType;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The reward book. Two views, as on screen:
    ///
    /// The book's page: its tabs, then the trees on the page (each with its progress, as the game prints it
    /// under the tree), then the window's buttons. Enter on a tree opens it through the tree's own button.
    ///
    /// An open tree: its nodes nearest the root first (left to right within a step), each read as its kind,
    /// whether it is claimed, can be claimed or is out of reach, its cost and its rewards in the game's own
    /// words; then the side panel's text and buttons (the tree's title, story, progress, buy), then the
    /// window's buttons. Enter on a node is the game's own click on it — claiming spends reward points, so the
    /// game's own claim flow, hold and confirmation included, decides what happens.
    ///
    /// Active while the reward book panel is attached to the input layer that has the input; the tutorial
    /// panels and popups the book opens take the input and step in above it.
    /// </summary>
    internal sealed class RewardBookScreen : Screen
    {
        public override string Key => "reward-book";
        public override int Layer => 1;

        public override string ScreenName
        {
            get
            {
                var p = Panel();
                string title = p == null ? "" : UiText.Clean(p.m_TitleLabel?.Text?.text);
                return title.Length > 0 ? title : "Reward book";
            }
        }

        public override bool IsActive() => Panel() != null;

        // ---- pressing a reward ----

        // Claiming is the mouse's press-and-hold: the node's sound handler plays the click (or the locked click)
        // on the press, the hold's progress, and the claim or the interruption — all from the button's own state
        // and hold. The game's keyboard submit claims at once and silently, so Enter on a reward is the mouse
        // instead: the press goes down when Enter does and comes up when Enter is let go (or the cursor leaves).
        private RewardSystemTreeElement _pressed;

        private void Press(RewardSystemTreeElement node)
        {
            Release();
            if (SendButton(node, CursorButtonEventArgs.EState.ButtonDown)) _pressed = node;
        }

        private void Release()
        {
            var node = _pressed;
            _pressed = null;
            if (node != null) SendButton(node, CursorButtonEventArgs.EState.ButtonUp);
        }

        private static bool SendButton(RewardSystemTreeElement node, CursorButtonEventArgs.EState state)
        {
            try
            {
                if (node == null || !node.gameObject.activeInHierarchy) return false;
                var args = new CursorButtonEventArgs(node.ParentLayer, CursorInputEventArgs.EMouseButton.Left, Camera.main,
                    state, node, node, new Il2CppSystem.Collections.Generic.List<CrimsonUI.EventSystems.RaycastResult>());
                node.HandleCursorButtonEvent(args);
                return true;
            }
            catch (Exception e) { Plugin.LogInfo("[reward-book] " + state + " failed: " + e.Message); return false; }
        }

        public override void Tick()
        {
            if (_pressed != null && !Keys.Held(KeyCode.Return) && !Keys.Held(KeyCode.KeypadEnter)) Release();
        }

        // Landing is the mouse moving onto the control: its OnHover event, which the layer raises from the
        // pointer's raycast, is what the sound handlers hear (the hover sound, locked or not). Selecting the
        // control, setting its Hovered state or its HandleControlHover() don't reach them.
        public override void OnCursorMoved(GraphNode node)
        {
            var c = node?.Id.Reference as AControl;
            if (_pressed != null && !(c != null && c.Pointer == _pressed.Pointer)) Release();
            base.OnCursorMoved(node);
            try { if (c != null && c.gameObject.activeInHierarchy) c.OnHover?.Invoke(c); }
            catch (Exception e) { Plugin.LogInfo("[reward-book] hover failed: " + e.Message); }
        }

        public override void OnUnfocus() => Release();

        private static UIRewardSystemPanel Panel()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var p = attached[i]?.TryCast<UIRewardSystemPanel>();
                if (p != null && p.gameObject.activeInHierarchy) return p;
            }
            return null;
        }

        // An open tree: the tree canvas is shown and has drawn its nodes.
        private static List<RewardSystemTreeElement> OpenTree(UIRewardSystemPanel p)
        {
            var canvas = p.TreeCanvas;
            if (canvas == null || !canvas.gameObject.activeInHierarchy) return null;
            var nodes = new List<RewardSystemTreeElement>();
            var all = canvas.m_TreeElements;
            for (int i = 0; all != null && i < all.Count; i++)
                if (all[i] != null && all[i].gameObject.activeInHierarchy) nodes.Add(all[i]);
            return nodes.Count > 0 ? nodes : null;
        }

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();

            AddHint(b, p);
            // The tab bar stays live behind an open tree; it is the page's, and never one of the leftovers.
            var tabs = Tabs(p, used);
            var nodes = OpenTree(p);
            if (nodes != null) BuildTree(b, p, nodes, used);
            else BuildPage(b, p, tabs, used);

            // Whatever else the window offers (close, the glyph-key buttons), in the game's order.
            var rest = new List<AControl>();
            foreach (var c in CrimsonNodes.Leaves(p)) if (used.Add(c.Pointer)) rest.Add(c);
            foreach (var bar in p.GetComponentsInChildren<InputButtonContainer>(false))
            {
                var children = bar.GetChildren();
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                    if (used.Add(c.Pointer)) rest.Add(c);
                }
            }
            if (rest.Count > 0)
            {
                b.BeginStop("buttons");
                foreach (var c in rest) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        // ---- the book's page ----

        private static List<AControl> Tabs(UIRewardSystemPanel p, HashSet<IntPtr> used)
        {
            var tabs = new List<AControl>();
            foreach (var root in new[] { p.m_TabBarRoot, p.m_TabBarPcRoot })
            {
                if (root == null || !root.gameObject.activeInHierarchy) continue;
                foreach (var c in root.GetComponentsInChildren<AControl>(false))
                    if (c.TryCast<AContainer>() == null && c.IsCurrentlySelectable() && used.Add(c.Pointer)) tabs.Add(c);
            }
            return tabs;
        }

        private static void BuildPage(GraphBuilder b, UIRewardSystemPanel p, List<AControl> tabs, HashSet<IntPtr> used)
        {
            if (tabs.Count > 0)
            {
                b.BeginStop("tabs");
                b.PushContext("Tabs");
                for (int i = 0; i < tabs.Count; i++)
                {
                    int index = i;
                    var v = CrimsonNodes.Button(tabs[i], () => TabName(p, index));
                    v.Announcements = new[]
                    {
                        v.Announcements[0],
                        new NodeAnnouncement(() => SafeOpenTab(p) == index ? "selected" : null, live: true, kind: AnnouncementKinds.Selected),
                        new NodeAnnouncement(() => SafeOpenTab(p) != index && InWalkthrough(p) ? "locked" : null, live: true, kind: AnnouncementKinds.Enabled),
                    };
                    // A locked tab ignores the click; the game's own notice says what unlocks it.
                    var click = v.OnActivate;
                    v.OnActivate = () =>
                    {
                        if (SafeOpenTab(p) != index && InWalkthrough(p))
                            Speech.Say(GameText.Get("rb_ftux_notification_locked_tabs", "To reach the other pages, claim every reward in one tree."), true);
                        else click();
                    };
                    b.AddItem(CrimsonNodes.Id(tabs[i]), v);
                }
                b.PopContext();
            }

            var trees = new List<RewardSystemMosaicElement>();
            var elements = p.ImageCanvas?.m_MosaicElements;
            for (int i = 0; elements != null && i < elements.Count; i++)
            {
                var m = elements[i];
                if (m != null && m.isActiveAndEnabled && m.BtnControl != null) trees.Add(m);
            }
            if (trees.Count == 0) return;
            b.BeginStop("trees");
            b.PushContext("Trees");
            foreach (var m in trees)
            {
                var btn = m.BtnControl;
                used.Add(btn.Pointer);
                var tree = m;
                b.AddItem(CrimsonNodes.Id(btn), new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => TreeName(tree), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => NullIfEmpty(UiText.Clean(tree.m_NodeProgressText?.text)), live: true, kind: AnnouncementKinds.Value),
                    },
                    OnActivate = () => Navigator.ClickControl(btn),
                    OnTooltip = () => Speech.Say(string.Join(". ", CrimsonNodes.Texts(tree)), true),
                });
            }
            b.PopContext();
        }

        // A tree is shown as its art; the game titles it (in its side panel and story) by reward_tree_<name>_title,
        // whose case doesn't always match the tree's name. The starter trees' art is their faction's.
        private static string TreeName(RewardSystemMosaicElement m)
        {
            var d = m.ElementData;
            string name = d?.MosaicElementName;
            if (string.IsNullOrEmpty(name)) return "Tree";
            string title = GameText.Get("reward_tree_" + name + "_title", null)
                ?? GameText.Get("reward_tree_" + name.ToLowerInvariant() + "_title", null);
            if (title != null && title != "N/A") return title;
            try { title = UiText.Clean(Localization.LocalizationHelper.GetFactionName(d.FactionId)); } catch { }
            return string.IsNullOrEmpty(title) ? name : title;
        }

        // ---- the walkthrough ----

        // The new-player walkthrough keeps the book on its starter page: the other tabs ignore clicks until
        // the player has chosen a faction tree.
        private static bool InWalkthrough(UIRewardSystemPanel p)
        {
            try { return p.m_FTUX != null && p.m_FTUX.IsInFTUX; } catch { return false; }
        }

        // The hint last seen over the book (null = none yet since the book opened).
        private static string _hint;

        public override void OnPop()
        {
            Release();
            _hint = null;
        }

        // The walkthrough's floating hint, while one shows over the book: the first thing on the page, read on
        // landing, and queued when it changes while the book is open. The game sets these texts its own ways,
        // not always through the hint's SetHint (which TutorialHintPatch hears).
        private static void AddHint(GraphBuilder b, UIRewardSystemPanel p)
        {
            string text = null;
            try
            {
                foreach (var h in p.GetComponentsInChildren<UITutorialHint>(false))
                {
                    if (h.Visibility != CrimsonUIBehaviour.EVisibility.Shown) continue;
                    text = UiText.Clean(h.HintText?.Text?.text);
                    if (text.Length > 0) break;
                }
            }
            catch { }
            if (_hint != null && text != _hint && !string.IsNullOrEmpty(text))
            {
                Speech.Say(text, false);
                Speech.Remember(text);
            }
            if (!string.IsNullOrEmpty(text) || _hint == null) _hint = text ?? "";
            if (string.IsNullOrEmpty(text)) return;
            b.BeginStop("hint");
            b.AddItem(ControlId.Structural("reward-book:hint:" + text), new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(() => "Hint: " + text, kind: AnnouncementKinds.Label) },
                OnTooltip = () => Speech.Say(text, true),
            });
        }

        // ---- tabs ----

        private static int SafeOpenTab(UIRewardSystemPanel p)
        {
            try { return p.CurrentOpenTab; } catch { return -1; }
        }

        // A tab is an icon; the tab model's entry at the same index names the icon. The game has names for the
        // seasons and the toad year; the other themes are named in English.
        private static string TabName(UIRewardSystemPanel p, int index)
        {
            string icon = null;
            try
            {
                var list = p.m_TabsModel.RewardBookTabList;
                if (index < list.Count) icon = list[index].Cast<RewardBookTab>().IconImage;
            }
            catch { }
            if (string.IsNullOrEmpty(icon)) return "Tab " + (index + 1);
            const string season = "ui_icons_seasons_";
            if (icon.StartsWith(season))
            {
                string s = GameText.Get("season_" + icon.Substring(season.Length) + "_title", null);
                if (s != null) return s;
            }
            if (icon.Contains("year_toad")) return GameText.Get("welcome_screen_year_of_cursed_toad_title", "Year of the Cursed Toad");
            if (icon.Contains("year_boar")) return "Year of the Wild Boar";
            if (icon.Contains("year_rat")) return "Year of the Wererat";
            if (icon.Contains("year_oak")) return "Year of the Great Oak";
            if (icon.Contains("saovine")) return "Events";
            if (icon.Contains("expansion_unmillible")) return "Base set";
            return icon.Replace("ui_icons_", "").Replace("ui_icon_", "").Replace('_', ' ');
        }

        // ---- an open tree ----

        private void BuildTree(GraphBuilder b, UIRewardSystemPanel p, List<RewardSystemTreeElement> nodes, HashSet<IntPtr> used)
        {
            // Along the tree's paths from its start: fewest steps first; within a step, top to bottom as drawn.
            var steps = StepsFromStart(nodes);
            var ordered = nodes
                .Select(n => (Node: n, Steps: steps.TryGetValue(SafeId(n), out int s) ? s : int.MaxValue, Y: n.transform.position.y))
                .OrderBy(t => t.Steps).ThenByDescending(t => t.Y)
                .Select(t => t.Node).ToList();

            b.BeginStop("nodes");
            b.PushContext("Rewards");
            foreach (var n in ordered)
            {
                used.Add(n.Pointer);
                var node = n;
                b.AddItem(CrimsonNodes.Id(node), new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => Kind(node), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => State(node), live: true, kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => Rewards(node), kind: AnnouncementKinds.Tooltip),
                    },
                    OnActivate = () => Press(node),
                    OnTooltip = () => Speech.Say(Kind(node) + ". " + State(node) + ". " + Rewards(node), true),
                });
            }
            b.PopContext();

            // The side panel: the tree's title, progress, story and its buttons.
            var side = p.m_SidePanel;
            if (side == null || !side.gameObject.activeInHierarchy) return;
            var sideButtons = CrimsonNodes.Leaves(side, used);
            var buttonTexts = new HashSet<string>();
            foreach (var c in sideButtons) foreach (var s in CrimsonNodes.Texts(c)) buttonTexts.Add(s);
            var info = CrimsonNodes.Texts(side).Where(s => !buttonTexts.Contains(s)).ToList();
            if (info.Count == 0 && sideButtons.Count == 0) return;
            b.BeginStop("details");
            b.PushContext("Details");
            if (info.Count > 0)
            {
                string text = string.Join(". ", info);
                b.AddItem(ControlId.Structural("reward-book:details:" + text), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => text, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(text, true),
                });
            }
            foreach (var c in sideButtons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            b.PopContext();
        }

        private static uint SafeId(RewardSystemTreeElement n)
        {
            try { return n.ElementData.UniqueID; } catch { return 0; }
        }

        // Each node's distance in links from the tree's starting node. The node's own DistanceToRoot is 0
        // throughout, so the links (each node's ConnectedNodes) are walked breadth-first.
        private static Dictionary<uint, int> StepsFromStart(List<RewardSystemTreeElement> nodes)
        {
            var steps = new Dictionary<uint, int>();
            var queue = new Queue<RewardSystemTreeElementData>();
            foreach (var n in nodes)
            {
                try
                {
                    var d = n.ElementData;
                    if (d.NodeType == ENodeType.Starting && steps.TryAdd(d.UniqueID, 0)) queue.Enqueue(d);
                }
                catch { }
            }
            while (queue.Count > 0)
            {
                var d = queue.Dequeue();
                int next = steps[d.UniqueID] + 1;
                try
                {
                    var links = d.ConnectedNodes;
                    for (int i = 0; links != null && i < links.Count; i++)
                        if (links[i] != null && steps.TryAdd(links[i].UniqueID, next)) queue.Enqueue(links[i]);
                }
                catch { }
            }
            return steps;
        }

        private static string Kind(RewardSystemTreeElement n)
        {
            ENodeType type;
            try { type = n.ElementData.NodeType; } catch { return "Reward"; }
            return type switch
            {
                ENodeType.Starting => "Start",
                ENodeType.Small => "Reward",
                ENodeType.Large => "Large reward",
                ENodeType.Mosaic => "Tree reward",
                ENodeType.Story => "Story",
                ENodeType.Combat => "Battle",
                ENodeType.Quest => "Quest",
                ENodeType.Exchange => "Exchange",
                _ => type.ToString(),
            };
        }

        // Claimed, or what stands between the player and claiming it — the cost as the node prints it.
        private static string State(RewardSystemTreeElement n)
        {
            try
            {
                var d = n.ElementData;
                if (d.Claimed) return "claimed";
                string cost = UiText.Clean(n.m_CostText?.text);
                string costPart = cost.Length > 0 ? ", cost " + cost : "";
                if (!n.MeetCondition) return "out of reach" + costPart;
                if (!n.IsEnoughCurrencyForClaiming()) return "not enough points" + costPart;
                return "can claim" + costPart;
            }
            catch (Exception e) { Plugin.LogInfo("[reward-book] state failed: " + e.Message); return null; }
        }

        // The node's rewards in the game's own words, then the cards it gives by name.
        private static string Rewards(RewardSystemTreeElement n)
        {
            var parts = new List<string>();
            try
            {
                var infos = n.ElementData.m_RewardInfo;
                for (int i = 0; infos != null && i < infos.Count; i++)
                {
                    var rewards = infos[i]?.Rewards;
                    for (int j = 0; rewards != null && j < rewards.Count; j++)
                    {
                        string s = UiText.Clean(RewardTextHelper.GetText(rewards[j]));
                        if (s.Length > 0 && !parts.Contains(s)) parts.Add(s);
                    }
                }
            }
            catch (Exception e) { Plugin.LogInfo("[reward-book] rewards failed: " + e.Message); }
            try
            {
                var cards = n.RewardCards;
                for (int i = 0; cards != null && i < cards.Count; i++)
                {
                    string s = UiText.Clean(Localization.LocalizationHelper.GetCardName(cards[i]));
                    if (s.Length > 0 && !parts.Contains(s)) parts.Add(s);
                }
            }
            catch { }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
