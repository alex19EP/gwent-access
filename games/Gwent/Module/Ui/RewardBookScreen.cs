using System;
using System.Collections.Generic;
using System.Linq;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity.UI.PlayerProfile;
using GwentVisuals;
using Modules.Common;
using Modules.RewardBook.Entities;
using UnityEngine;
using ENodeType = Modules.RewardBook.Entities.RewardSystemTreeElementData.ENodeType;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The reward book. Two views, as on screen:
    ///
    /// Both views start with the currency balances and the tabs, and end with the window's buttons, exit or back
    /// first. The tab bar stays live over an open tree, and a single-tree page (a running event, which is what
    /// the book opens on then) has no page of trees at all: its tree is the page, Back closes the book, and the
    /// tabs are the only way to the other pages.
    ///
    /// The book's page: the trees on the page (each with its progress, as the game prints it under the tree).
    /// Enter on a tree opens it through the tree's own button.
    ///
    /// An open tree: its nodes nearest the root first (left to right within a step), each read as its kind,
    /// whether it is claimed, can be claimed or is out of reach, its cost and its rewards in the game's own
    /// words; then the side panel's text and buttons (the tree's title, story, progress, buy). Enter on a node
    /// is the game's own click on it — claiming spends the node's currency, so the game's own claim flow, hold
    /// and confirmation included, decides what happens.
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

        // Moving off a pressed reward lets go of it, as dragging the pointer off does; the base raises the hover.
        public override void OnCursorMoved(GraphNode node)
        {
            var c = node?.Id.Reference as AControl;
            if (_pressed != null && !(c != null && c.Pointer == _pressed.Pointer)) Release();
            base.OnCursorMoved(node);
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

            AddPoints(b);
            AddHint(b, p);
            AddTabs(b, p, used);
            _firstContent = null;
            var nodes = OpenTree(p);
            if (nodes != null) BuildTree(b, p, nodes, used);
            else BuildPage(b, p, used);

            // The window's way out leads its buttons, under the label the game gives it for this view (exit on
            // a page or a single-tree page, back in a tree); the icon beside it is the same action again.
            var rest = new List<AControl>();
            var back = p.m_BackButton?.Button;
            if (back != null && back.gameObject.activeInHierarchy && used.Add(back.Pointer)) rest.Add(back);
            if (p.m_IconBackButton != null) used.Add(p.m_IconBackButton.Pointer);

            // Whatever else the window offers (zoom, the glyph-key buttons), in the game's order.
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

            SteerOnViewChange(b, p);
        }

        // ---- moving between views ----

        // What the book is showing: the open tab and the tree open in it. The game sets both the moment it is
        // asked, ahead of the animation that swaps what is drawn.
        private static string _view;

        // The cursor is to land in the new view's content once the animation ends.
        private static bool _landInContent;

        // Where this render's content takes the cursor: the first reward, the first tree, or the tree last open.
        private static ControlId _firstContent;

        // The tree most recently open.
        private static string _lastTree;

        private static readonly ControlId ChangingView = ControlId.Structural("reward-book:changing-view");

        // Opening a tree or going back takes away the node the cursor was on, and the kernel's fallback would then
        // read out whatever precedes it (the last tab); the new view's first tree or reward is where the player
        // is headed. A cursor on a tab or the balances stays put, so a tab can be pressed and pressed again.
        // During the animation there is nothing to land on yet, so a silent node holds the cursor until there is.
        private static void SteerOnViewChange(GraphBuilder b, UIRewardSystemPanel p)
        {
            string tree = p.m_CurrentTreeData?.MosaicElementName;
            if (!string.IsNullOrEmpty(tree)) _lastTree = tree;
            string view = SafeOpenTab(p) + "/" + tree;
            if (_view != null && view != _view)
            {
                var stop = GraphNavigator.CurrentNode?.StopKey as string;
                if (stop != "tabs" && stop != "points") _landInContent = true;
            }
            _view = view;
            if (!_landInContent) return;

            if (p.IsAnimating)
            {
                b.BeginStop("changing-view");
                b.AddItem(ChangingView, new NodeVtable { Announcements = new[] { new NodeAnnouncement(() => null, kind: AnnouncementKinds.Label) } });
                GraphNavigator.Suggest(ChangingView);
                return;
            }
            _landInContent = false;
            if (_firstContent != null) GraphNavigator.Suggest(_firstContent);
        }

        // ---- the tabs ----

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

        private static void AddTabs(GraphBuilder b, UIRewardSystemPanel p, HashSet<IntPtr> used)
        {
            var tabs = Tabs(p, used);
            if (tabs.Count == 0) return;
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

        // ---- the book's page ----

        private static void BuildPage(GraphBuilder b, UIRewardSystemPanel p, HashSet<IntPtr> used)
        {
            var trees = new List<RewardSystemMosaicElement>();
            var elements = p.ImageCanvas?.m_MosaicElements;
            for (int i = 0; elements != null && i < elements.Count; i++)
            {
                var m = elements[i];
                if (m != null && m.isActiveAndEnabled && m.BtnControl != null) trees.Add(m);
            }
            if (trees.Count == 0) return;
            // Back from a tree lands on that tree, not the top of the page.
            var landing = trees.Find(m => m.ElementData?.MosaicElementName == _lastTree) ?? trees[0];
            _firstContent = CrimsonNodes.Id(landing.BtnControl);
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

        // ---- the currency balances ----

        // While the book is open the top bar shows what there is to spend in it: the reward points and, during
        // an event, the event's currency, which is what the event's tree costs. The player sees both throughout,
        // so they are the book's first item, one line so that landing on it says both, and live (they drop as
        // rewards are claimed). The names and counters are found once per opening: this is read every frame.
        private static List<(string Name, TMPro.TMP_Text Count)> _balances;

        private static string Balances()
        {
            try
            {
                if (_balances == null || _balances.Exists(x => x.Count == null || !x.Count.gameObject.activeInHierarchy))
                    _balances = FindBalances();
                var parts = new List<string>();
                foreach (var (name, count) in _balances)
                {
                    string n = UiText.Clean(count.text);
                    if (n.Length > 0) parts.Add(name + ", " + n);
                }
                return parts.Count > 0 ? string.Join(", ", parts) : null;
            }
            catch { _balances = null; return null; }
        }

        private static List<(string, TMPro.TMP_Text)> FindBalances()
        {
            var found = new List<(string, TMPro.TMP_Text)>();
            var bar = UnityEngine.Object.FindObjectOfType<GwentUnity.UIPlayerInfoOverlay>();
            if (bar == null) return found;
            foreach (var (button, type) in new (AControl, EVirtualCurrencyType)[]
            {
                (bar.m_EventButton, EVirtualCurrencyType.EventCurrency),
                (bar.m_RewardButton, EVirtualCurrencyType.RewardPoints),
            })
            {
                if (button == null || !button.gameObject.activeInHierarchy) continue;
                var count = button.GetComponentInChildren<TMPro.TMP_Text>(false);
                string name = Currency.Of(type);
                if (count != null && !string.IsNullOrEmpty(name)) found.Add((name, count));
            }
            return found;
        }

        private static void AddPoints(GraphBuilder b)
        {
            if (Balances() == null) return;
            b.BeginStop("points");
            b.AddItem(ControlId.Structural("reward-book:points"), new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(Balances, live: true, kind: AnnouncementKinds.Label) },
            });
        }

        // The hint last seen over the book (null = none yet since the book opened).
        private static string _hint;

        public override void OnPop()
        {
            Release();
            _hint = null;
            _balances = null;
            _costNames.Clear();
            _view = null;
            _lastTree = null;
            _landInContent = false;
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
            // The tree as drawn: the arrows follow its connector lines the way each one runs on screen, and a
            // landing says which ways the lines go from here. Declared from the start outward, so entering the
            // stop lands on the start.
            var steps = StepsFromStart(nodes);
            var ordered = nodes
                .Select(n => (Node: n, Steps: steps.TryGetValue(SafeId(n), out int s) ? s : int.MaxValue, Y: n.transform.position.y))
                .OrderBy(t => t.Steps).ThenByDescending(t => t.Y)
                .Select(t => t.Node).ToList();
            var exits = Exits(ordered);
            _firstContent = CrimsonNodes.Id(ordered[0]);

            b.BeginStop("nodes");
            b.PushContext("Rewards");
            foreach (var n in ordered)
            {
                used.Add(n.Pointer);
                var node = n;
                exits.TryGetValue(SafeId(node), out var ways);
                b.AddNode(CrimsonNodes.Id(node), new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => Kind(node), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => State(node), live: true, kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => Rewards(node), kind: AnnouncementKinds.Tooltip),
                        new NodeAnnouncement(() => Paths(ways), kind: AnnouncementKinds.Position),
                    },
                    OnActivate = () => Press(node),
                    OnTooltip = () => Speech.Say(Kind(node) + ". " + State(node) + ". " + Rewards(node), true),
                });
            }
            foreach (var n in ordered)
                if (exits.TryGetValue(SafeId(n), out var ways))
                    foreach (var (dir, to) in ways) b.Connect(CrimsonNodes.Id(n), dir, CrimsonNodes.Id(to));
            b.PopContext();

            // The side panel: the tree's title, progress, story and its buttons.
            var side = p.m_SidePanel;
            if (side == null || !side.gameObject.activeInHierarchy) return;
            var sideButtons = CrimsonNodes.Leaves(side, used);
            var buttonTexts = new HashSet<string>();
            foreach (var c in sideButtons) foreach (var s in CrimsonNodes.Texts(c)) buttonTexts.Add(s);
            var locked = LockedStory(side);
            var info = CrimsonNodes.Texts(side).Where(s => !buttonTexts.Contains(s) && !locked.Contains(s)).ToList();
            if (StoryPartLocked(side)) info.Add(GameText.Get("interface_rewards_story_text_unlock_desc", "Claim story nodes to read more."));
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
            var sections = StorySections(side);
            foreach (var c in sideButtons)
            {
                var v = CrimsonNodes.Button(c);
                if (sections.TryGetValue(c.Pointer, out var section))
                {
                    var toggle = c.TryCast<ToggleButtonControl>();
                    v = CrimsonNodes.Button(c, () => section.Label);
                    // One value, and not the Selected kind: that would make the selected tab where the stop is
                    // entered, ahead of the text this stop opens with.
                    v.Announcements = new[]
                    {
                        v.Announcements[0],
                        new NodeAnnouncement(() =>
                        {
                            string selected = toggle != null && toggle.IsToggled ? "selected" : null;
                            return selected != null && section.State != null ? section.State + ", " + selected : selected ?? section.State;
                        }, live: true, kind: AnnouncementKinds.Value),
                    };
                }
                b.AddItem(CrimsonNodes.Id(c), v);
            }
            b.PopContext();
        }

        // The story text a sighted player can't read yet. The game keeps a locked part's whole text in its label,
        // invisible under a mask, until its node is claimed and the reveal has played; speaking it would give away
        // what the screen withholds.
        private static HashSet<string> LockedStory(RewardSystemSidePanel side)
        {
            var locked = new HashSet<string>();
            try
            {
                var parts = side.m_StoryTextes;
                for (int i = 0; parts != null && i < parts.Count; i++) AddIfLocked(locked, parts[i]);
                AddIfLocked(locked, side.m_ChestElement);
            }
            catch (Exception e) { Plugin.LogInfo("[reward-book] locked story failed: " + e.Message); }
            return locked;
        }

        private static void AddIfLocked(HashSet<string> locked, RewardSystemStoryTextElement part)
        {
            if (part == null || !part.gameObject.activeInHierarchy || part.TextIsRevealed) return;
            string text = UiText.Clean(part.m_StoryText?.text);
            if (text.Length > 0) locked.Add(text);
        }

        // Whether a part of the story on show is still locked (a large reward's own text is the tab's "not claimed").
        private static bool StoryPartLocked(RewardSystemSidePanel side)
        {
            try
            {
                var parts = side.m_StoryTextes;
                for (int i = 0; parts != null && i < parts.Count; i++)
                    if (parts[i] != null && parts[i].gameObject.activeInHierarchy && !parts[i].TextIsRevealed) return true;
            }
            catch { }
            return false;
        }

        // The side panel's section tabs, by control. They are bare icons choosing which text the panel shows: the
        // story's parts (several only on a tree that tells more than one story), then one tab per large reward
        // that has a story of its own, its icon gold once that reward is claimed.
        private static Dictionary<IntPtr, (string Label, string State)> StorySections(RewardSystemSidePanel side)
        {
            var found = new Dictionary<IntPtr, (string, string)>();
            try
            {
                string storyName = GameText.Get("interface_rewards_story_node_title", "Story");
                var stories = ActiveTabs(side.m_StoryTabs);
                for (int i = 0; i < stories.Count; i++)
                    found[stories[i].Pointer] = (stories.Count > 1 ? storyName + " " + (i + 1) : storyName, null);

                var chests = ActiveTabs(side.m_ChestTabs);
                var large = side.CurrentData?.ElementTree?.LargeElements;
                for (int i = 0; i < chests.Count; i++)
                {
                    string state = large != null && i < large.Count ? (large[i].Claimed ? "claimed" : "not claimed") : null;
                    found[chests[i].Pointer] = (chests.Count > 1 ? "Large reward " + (i + 1) : "Large reward", state);
                }
            }
            catch (Exception e) { Plugin.LogInfo("[reward-book] story sections failed: " + e.Message); }
            return found;
        }

        // The controls of the tabs on show, in the panel's order.
        private static List<AControl> ActiveTabs(Il2CppSystem.Collections.Generic.List<RewardBookTabButtonView> views)
        {
            var controls = new List<AControl>();
            for (int i = 0; views != null && i < views.Count; i++)
            {
                var view = views[i];
                if (view == null || !view.gameObject.activeInHierarchy) continue;
                var control = view.GetComponentInParent<ToggleButtonControl>();
                if (control != null) controls.Add(control);
            }
            return controls;
        }

        // Each node's links, each given the arrow nearest the way its connector runs on screen. A node has at
        // most one link per arrow: the closest fits are taken first, and a link whose best arrow is taken gets
        // its next best (a fork drawn up-right and down-right becomes Up and Down).
        private static Dictionary<uint, List<(GraphDir Dir, RewardSystemTreeElement To)>> Exits(List<RewardSystemTreeElement> nodes)
        {
            var byId = new Dictionary<uint, RewardSystemTreeElement>();
            foreach (var n in nodes) byId[SafeId(n)] = n;
            var exits = new Dictionary<uint, List<(GraphDir, RewardSystemTreeElement)>>();
            foreach (var n in nodes)
            {
                var from = n.transform.position;
                var fits = new List<(float Off, GraphDir Dir, RewardSystemTreeElement To)>();
                try
                {
                    var links = n.ElementData.ConnectedNodes;
                    for (int i = 0; links != null && i < links.Count; i++)
                    {
                        if (links[i] == null || !byId.TryGetValue(links[i].UniqueID, out var to)) continue;
                        var d = to.transform.position - from;
                        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;   // 0 = right, 90 = up
                        foreach (var (dir, at) in new[] { (GraphDir.Right, 0f), (GraphDir.Up, 90f), (GraphDir.Left, 180f), (GraphDir.Down, -90f) })
                            fits.Add((Mathf.Abs(Mathf.DeltaAngle(angle, at)), dir, to));
                    }
                }
                catch { }
                var ways = new List<(GraphDir, RewardSystemTreeElement)>();
                var taken = new HashSet<GraphDir>();
                var placed = new HashSet<IntPtr>();
                foreach (var f in fits.OrderBy(f => f.Off))
                {
                    if (f.Off >= 135f || taken.Contains(f.Dir) || placed.Contains(f.To.Pointer)) continue;
                    taken.Add(f.Dir);
                    placed.Add(f.To.Pointer);
                    ways.Add((f.Dir, f.To));
                }
                exits[SafeId(n)] = ways;
            }
            return exits;
        }

        // "paths left, up, down": the ways the connector lines leave this node, in the arrows' order.
        private static string Paths(List<(GraphDir Dir, RewardSystemTreeElement To)> ways)
        {
            if (ways == null || ways.Count == 0) return null;
            var order = new[] { GraphDir.Left, GraphDir.Up, GraphDir.Right, GraphDir.Down };
            var words = order.Where(d => ways.Any(w => w.Dir == d)).Select(d => d.ToString().ToLowerInvariant());
            return "paths " + string.Join(", ", words);
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

        // Claimed, or what stands between the player and claiming it. The cost comes from the node's data, which
        // is what the game prints and checks: a single-tree page draws it in a separate label with the currency's
        // icon, and the usual label stays hidden there holding the prefab's placeholder. The icon is said as the
        // currency's name, since one tree can mix currencies (an event's story node costs reward points).
        private static string State(RewardSystemTreeElement n)
        {
            try
            {
                var d = n.ElementData;
                if (d.Claimed) return "claimed";
                var cost = d.ClaimCost;
                int amount = cost == null ? 0 : cost.Amount;
                string name = amount > 0 ? CostName(cost.CostType) : null;
                string costPart = amount <= 0 ? "" : name == null ? ", cost " + amount : ", cost " + amount + " " + name;
                if (!n.MeetCondition) return "out of reach" + costPart;
                if (!n.IsEnoughCurrencyForClaiming()) return NotEnough(cost.CostType, amount, name);
                return "can claim" + costPart;
            }
            catch (Exception e) { Plugin.LogInfo("[reward-book] state failed: " + e.Message); return null; }
        }

        // The game's own words for falling short, which name the currency; its reward point line also says how
        // many are needed.
        private static string NotEnough(EVirtualCurrencyType type, int amount, string name)
        {
            if (type == EVirtualCurrencyType.RewardPoints
                && GameText.Get("interface_rewards_node_not_enough_rp", null) is string points)
                return points.Replace("{0}", amount.ToString());
            string template = GameText.Get("interface_rewards_node_not_enough_currency", "Not enough {0}");
            return template.Replace("{0}", name ?? "").TrimEnd('!', ' ') + ", cost " + amount;
        }

        // Currency names by type, found once per opening: a focused node's state is read every frame.
        private static readonly Dictionary<EVirtualCurrencyType, string> _costNames = new Dictionary<EVirtualCurrencyType, string>();

        private static string CostName(EVirtualCurrencyType type)
        {
            if (!_costNames.TryGetValue(type, out var name)) _costNames[type] = name = Currency.Of(type);
            return name;
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
