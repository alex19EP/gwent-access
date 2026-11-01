using System.Collections.Generic;
using System.Linq;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentUnity.MainMenu;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The main menu: the play-mode cards, the deck chosen for them, the menu bar (rewards, settings, quit…),
    /// the profile and currency bar, notifications and the social panel — one list in that order, each part
    /// spoken as context on entry (Hearthstone Access's single menu list). The modes are one row (Left/Right
    /// change the mode), Left/Right on the deck button change the deck, Tab moves like Down, and a letter jumps
    /// to an item (opening it when it is the only one with that letter).
    ///
    /// Built from the controls the Base input layer has attached, so whatever the game shows (the top-level
    /// cards, or the play stack with its mode cards and deck selector; new-player gating that hides most of
    /// the menu) is what the graph shows. Each control lands in the stop of its nearest known ancestor.
    /// Active while the Base layer has the input and the main menu content panel is on screen; any popup or
    /// full-screen panel takes the input layer away and the screen steps aside.
    ///
    /// The deck selector is a dropdown: Enter on the deck button opens the game's deck list, which takes the
    /// deck button's place at the top of the deck stop (decks top to bottom as drawn, the active one marked),
    /// above the bar of list actions on one row. Opening lands on the active deck; picking a deck, or
    /// closing the list, lands back on the deck button, which then reads the deck now chosen. The game
    /// moves its own selection elsewhere around both (the guides button on open; the shop and the mode
    /// card after a pick), so the screen holds the game's selection on the landing for a moment.
    /// </summary>
    internal sealed class MainMenuScreen : Screen
    {
        public override string Key => "main-menu";
        public override string ScreenName => "Main menu";

        private const string DeckListRoot = "DeckSelectorListContainerRoot";

        // Nearest ancestor (by GameObject name) → the stop a control belongs to. Order = Tab order. Two
        // regions may share a stop (the open deck list sits above the deck stop's own controls); a region's
        // Context is the level it speaks on entry, DeckListRoot's being the game's own "Change deck".
        private static readonly (string Root, string Stop, string Context)[] Regions =
        {
            (FriendStackRoot, "friend-match", null),
            ("Carousel", "cards", null),
            ("CardsSpawnRoot", "modes", "Game modes"),
            (DeckListRoot, "deck", null),
            ("MainView", "deck", "Deck"),
            ("UIVanityInventoryAndPreview(Clone)", "cosmetics", "Cosmetics"),
            ("ControlsSection", "menubar", "Menu bar"),
            ("UIPlayerInfoOverlayPrefab(Clone)", "profile", "Profile and currencies"),
            ("UINotificationsPrefab(Clone)", "notifications", "Notifications"),
            ("UIMenusSocialPrefab(Clone)", "social", "Social"),
        };

        private static readonly int DeckListRegion = System.Array.FindIndex(Regions, r => r.Root == DeckListRoot);
        private static readonly int FriendRegion = System.Array.FindIndex(Regions, r => r.Root == FriendStackRoot);

        // Where the game stages the friend-match card stack, and whether a friend match is being set up (an
        // invite accepted, or ours accepted): read once per build, from the content panel.
        private const string FriendStackRoot = "HidenContainer";
        private static bool _friendSetup;
        private static readonly int ProfileRegion = System.Array.FindIndex(Regions, r => r.Stop == "profile");

        // Skipped subtree: a play card's scrollable description, selectable only so a gamepad can scroll it —
        // Space on the card reads that text. (The friend-match staging is skipped too, unless a friend match is
        // being set up: see RegionOf.)
        private static readonly HashSet<string> Hidden = new HashSet<string> { "DescriptionPanelRoot" };

        // How long a landing the screen chose holds the game's selection against the game's own moves.
        private const float SteerSeconds = 1.5f;

        // The deck selector as of the last build: whether its list was open (null = not seen yet, so the
        // first sighting is no transition); the landing being held (true = the active deck in the list,
        // false = the deck button, null = none) until a deadline or a key; and this frame's control for it.
        private bool? _listOpen;
        private bool? _steerToList;
        private float _steerUntil;
        private AControl _steer;

        // ---- keys ----

        private List<ScreenKey> _keys;

        /// <summary>Tab and Shift+Tab move like Down and Up (one list); a letter jumps to the next item whose name
        /// starts with it and, when it is the only one, opens it (Hearthstone Access's main-menu letters).</summary>
        public override IReadOnlyList<ScreenKey> ExtraKeys
        {
            get
            {
                if (_keys != null) return _keys;
                _keys = new List<ScreenKey>
                {
                    new ScreenKey { Key = KeyCode.Tab, Run = () => GraphNavigator.Step(GraphDir.Down), Hint = "Tab, next item, as Down" },
                    new ScreenKey { Key = KeyCode.Tab, Shift = true, Run = () => GraphNavigator.Step(GraphDir.Up), Hint = "Shift Tab, previous item, as Up" },
                };
                bool first = true;
                for (var k = KeyCode.A; k <= KeyCode.Z; k++)
                {
                    char letter = (char)('a' + (k - KeyCode.A));
                    _keys.Add(new ScreenKey
                    {
                        Key = k,
                        Run = () => Letter(letter),
                        Hint = first ? "A letter, jump to the next item starting with it; when it is the only one, open it" : null,
                    });
                    first = false;
                }
                return _keys;
            }
        }

        // Items a letter never opens, only reaches: leaving the game.
        private static bool NeverOpenByLetter(string label) =>
            label.StartsWith("Quit", System.StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Exit", System.StringComparison.OrdinalIgnoreCase)
            || label.StartsWith(GameText.Get("main_menu_quit", "Quit"), System.StringComparison.OrdinalIgnoreCase);

        private static void Letter(char letter)
        {
            var nodes = GraphNavigator.Nodes();
            if (nodes.Count == 0) return;
            var matches = new List<GraphNode>();
            foreach (var n in nodes)
            {
                if (!n.Focusable) continue;
                string l = GraphNavigator.LabelOf(n)?.Trim();
                if (!string.IsNullOrEmpty(l) && char.ToLowerInvariant(l[0]) == letter) matches.Add(n);
            }
            if (matches.Count == 0) { Speech.Say("Nothing starting with " + char.ToUpperInvariant(letter) + ".", true); return; }
            // The next match after the cursor, wrapping, so pressing the letter again walks them.
            var cur = GraphNavigator.CurrentNode;
            int curAt = -1;
            for (int i = 0; i < matches.Count; i++) if (cur != null && matches[i].Id.Equals(cur.Id)) curAt = i;
            var target = matches[(curAt + 1) % matches.Count];
            if (cur == null || !target.Id.Equals(cur.Id)) GraphNavigator.FocusNode(target.Id);
            if (matches.Count == 1 && !NeverOpenByLetter(GraphNavigator.LabelOf(target) ?? "")) GraphNavigator.ActivateCurrent();
        }

        // The game modes and the deck can hold one entry (a new player's PLAY, the starter deck): "1 of 1" says so.
        public override bool PositionOfOne => true;

        public override bool IsActive()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null || layer.Priority != CrimsonInputLayer.EPriority.Base || ContentPanel(layer) == null)
            {
                _shownSince = -1;
                return false;
            }
            if (_shownSince < 0) _shownSince = UnityEngine.Time.realtimeSinceStartup;
            return !PassingThrough();
        }

        // When the menu first showed this time; -1 while it isn't up.
        private float _shownSince = -1;
        private static GwentVisuals.GwentApp _app;

        /// <summary>
        /// The new-player flow only passes through the menu: at start-up and between tutorial battles the game
        /// shows it while its current FTUX step gets going — opening the intro panel, a cinematic, the next
        /// tutorial battle — and reading it ("Profile and currencies, CARDSMITH, unavailable") announced a place
        /// the player never gets to use. Quiet while the step hasn't run its opening actions, a transition is
        /// under way, or the step launches a battle or cinematic. Bounded: after 15 seconds of menu the menu is
        /// read regardless, so a stalled flow can't leave the player in silence.
        /// </summary>
        private bool PassingThrough()
        {
            if (UnityEngine.Time.realtimeSinceStartup - _shownSince > 15f) return false;
            try
            {
                if (_app == null || _app.Pointer == System.IntPtr.Zero || _app.WasCollected) _app = UnityEngine.Object.FindObjectOfType<GwentVisuals.GwentApp>();
                var ux = _app?.UX;
                if (ux == null || !ux.IsInitialized) return true;
                if (!ux.IsInUXFlow) return false;
                var fs = ux.FlowState;
                var step = fs?.CurrentStep;
                if (step == null || fs.m_InProgressTransitionState != null) return true;
                if (step.m_CurrentActionIdToTrigger < step.m_IntroActions.Count) return true;
                var intro = step.UXStep?.IntroActions;
                for (int i = 0; intro != null && i < intro.Count; i++)
                {
                    var a = intro[i]?.Action;
                    if (a == GwentVisuals.UX.EUXActionType.LaunchBattle || a == GwentVisuals.UX.EUXActionType.PlayCinematic) return true;
                }
            }
            catch { }
            return false;
        }

        private static UIMainMenuContentPanel ContentPanel(CrimsonInputLayer layer)
        {
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var p = attached[i]?.TryCast<UIMainMenuContentPanel>();
                if (p != null && p.gameObject.activeInHierarchy) return p;
            }
            return null;
        }

        public override void OnPop()
        {
            _listOpen = null;
            _steerToList = null;
            _steer = null;
        }

        public override void Build(GraphBuilder b)
        {
            var layer = CrimsonInputManager.Instance.ActiveLayer;
            try { _friendSetup = ContentPanel(layer)?.IsFriendMatchSetupInProgress == true; } catch { _friendSetup = false; }
            var byStop = new List<AControl>[Regions.Length + 1];
            var attached = layer.GetAttachedControls();
            var seen = new HashSet<System.IntPtr>();
            for (int i = 0; i < attached.Count; i++)
                foreach (var c in CrimsonNodes.Leaves(attached[i], seen))
                {
                    if (BehindStandIn(c)) continue;
                    int r = RegionOf(c);
                    if (r < 0) continue;
                    (byStop[r] ??= new List<AControl>()).Add(c);
                }
            UIPlayerInfoOverlay overlay = null;
            for (int i = 0; i < attached.Count && overlay == null; i++) overlay = attached[i]?.TryCast<UIPlayerInfoOverlay>();
            if (overlay != null) AddCurrencies(byStop, overlay);

            // The deck list's child order is its scroll pool's order, not the order on screen.
            if (byStop[DeckListRegion] != null)
                byStop[DeckListRegion] = byStop[DeckListRegion].OrderByDescending(c => RowOf(c).position.y).ToList();

            var selector = FindSelector(byStop);
            GwentVisuals.CollectionDeck active = null;
            try { active = selector?.GetSelectedDeck(); } catch { }
            ulong activeDeck = active?.Id ?? 0;

            // A held landing whose control is still being rebuilt: declare nothing, so the cursor waits on it
            // silently instead of falling to a neighbour (spec §9: no content yet).
            if (!TrackSelector(selector, byStop, active)) return;

            // One list, top to bottom (Hearthstone Access's main menu, Accessible Arena's): Up/Down walk every
            // part of the menu; the parts are spoken as context on entering them, not separate Tab stops.
            b.BeginStop("main-menu");
            for (int r = 0; r < byStop.Length; r++)
            {
                if (byStop[r] == null) continue;
                bool known = r < Regions.Length;
                string context = r == DeckListRegion
                    ? GameText.Get(DeckSelector.SELECT_DECK_TEXT_LOCALIZATION_KEY, "Change deck")
                    : r == FriendRegion ? GameText.Get("friend_match_setup_title", "Friend match setup")
                    : known ? Regions[r].Context : null;
                if (context != null) b.PushContext(context);
                if (r == ProfileRegion && overlay != null) AddProfile(b, overlay);
                if (known && Regions[r].Stop == "modes" && byStop[r].Count > 1)
                {
                    // The game modes lie side by side, as the game draws them: Left/Right change the mode (the
                    // game's selection follows the cursor), Up/Down leave the row.
                    b.StartRow("modes");
                    foreach (var c in byStop[r]) b.AddItem(CrimsonNodes.Id(c), Vtable(c, activeDeck));
                    b.EndRow();
                }
                else AddControls(b, byStop[r], activeDeck);
                if (context != null) b.PopContext();
            }

            AddNotes(b, byStop[System.Array.FindIndex(Regions, x => x.Stop == "notifications")]);

            if (_steer != null) GraphNavigator.Suggest(CrimsonNodes.Id(_steer));
        }

        /// <summary>
        /// The notifications pinned on the menu (a new player's "Finish the 2nd game out of 3 in Standard mode",
        /// with its "1/3" progress): drawn for everyone but not selectable when they carry no button, so they
        /// would be missing from the list. Each shown one with no control already listed becomes a read-only item
        /// with its words ("1/3" read as "1 of 3").
        /// </summary>
        private static void AddNotes(GraphBuilder b, List<AControl> listed)
        {
            UINotificationControl[] notes;
            try { notes = UnityEngine.Object.FindObjectsOfType<UINotificationControl>(); } catch { return; }
            bool pushed = false;
            foreach (var note in notes)
            {
                if (note == null || !note.gameObject.activeInHierarchy) continue;
                if (listed != null && listed.Exists(c => c.transform.IsChildOf(note.transform))) continue;
                var texts = CrimsonNodes.Texts(note);
                if (texts.Count == 0) continue;
                for (int i = 0; i < texts.Count; i++)
                    if (System.Text.RegularExpressions.Regex.IsMatch(texts[i], @"^\d+/\d+$")) texts[i] = "progress " + texts[i].Replace("/", " of ");
                string text = string.Join(", ", texts);
                if (!pushed) { b.PushContext("Notifications"); pushed = true; }
                b.AddItem(ControlId.Structural("menu:note:" + CrimsonNodes.PathKey(note.transform, null)), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => text, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(text, true),
                });
            }
            if (pushed) b.PopContext();
        }

        /// <summary>
        /// The currency counters beside the shop button (event currency, reward points, ore, scraps, powder,
        /// kegs). The game never makes them selectable — a gamepad skips them — but they are on screen and a
        /// mouse click opens the shop where that currency is spent (ore: the kegs), so they join the profile stop before the shop, in the
        /// order drawn, each read by its tooltip and amount (<see cref="CrimsonNodes.Label"/>). Hidden ones
        /// (no event running, guest mode) stay out.
        /// </summary>
        private static void AddCurrencies(List<AControl>[] byStop, UIPlayerInfoOverlay overlay)
        {
            var list = byStop[ProfileRegion] ??= new List<AControl>();
            var shop = overlay.m_ShopButton;
            int at = shop == null ? -1 : list.FindIndex(c => c.Pointer == shop.Pointer);
            if (at < 0) at = list.Count;
            var counters = new AControl[]
            {
                overlay.m_EventButton, overlay.m_RewardButton, overlay.m_GoldButton,
                overlay.m_ScrapsButton, overlay.m_PowderButton, overlay.m_KegsButton,
            };
            foreach (var c in counters)
                if (c != null && c.gameObject.activeInHierarchy && !list.Exists(x => x.Pointer == c.Pointer))
                    list.Insert(at++, c);
        }

        /// <summary>
        /// The profile banner at the top left, ahead of the currencies: the player's name and title (Enter
        /// opens the profile, as clicking the banner does), then the badges beside them: level, rank, journey
        /// level, season-cycle level. The game gives the keyboard none of this (its profile button isn't
        /// selectable, and the badges aren't in its control tree), but all of it is on screen. Each badge
        /// reads as the profile panel names it, then its number; the badges are read-only.
        /// </summary>
        private static void AddProfile(GraphBuilder b, UIPlayerInfoOverlay overlay)
        {
            var info = overlay.m_BasePlayerInfoInstance;
            var button = overlay.m_ProfileButtonController?.m_ProfileButton;
            if (info == null || !info.gameObject.activeInHierarchy) return;

            var who = new List<string>();
            foreach (var label in new Component[] { info.m_NameLabel, info.m_TitleLabel })
                if (label != null && label.gameObject.activeInHierarchy) who.AddRange(CrimsonNodes.Texts(label));
            if (button != null && who.Count > 0)
                b.AddItem(CrimsonNodes.Id(button), CrimsonNodes.Button(button, () => string.Join(", ", who)));

            Badge(b, info.m_LevelInfoInstance, GameText.Get("player_profile_level", "Level"));
            Badge(b, info.m_RankedInfoInstance, GameText.Get("interface_profile_current_rank", "Rank"));
            Badge(b, info.m_JourneyLevelInstance, GameText.Get("ui_journey_level_tooltip", "Journey level"));
            var cycle = info.m_OuroborosLevelInstance;
            Badge(b, cycle, GameText.Get("interface_profile_ouroboros_level_" + CycleSeason(cycle), "Season cycle level"));
        }

        private static void Badge(GraphBuilder b, Component badge, string name)
        {
            if (badge == null || !badge.gameObject.activeInHierarchy) return;
            var texts = CrimsonNodes.Texts(badge);
            if (texts.Count == 0) return;
            b.AddItem(ControlId.Structural(CrimsonNodes.PathKey(badge.transform, null)), new NodeVtable
            {
                Announcements = new[] { new NodeAnnouncement(() => name + ", " + string.Join(", ", texts), kind: AnnouncementKinds.Label) },
            });
        }

        // The season of the cycle a season-cycle badge shows, as its translation keys spell it ("autumn"): the
        // badge has no field for it, only its shield picture, which is drawn per season.
        internal static string CycleSeason(Component cycle)
        {
            if (cycle == null) return null;
            foreach (var img in cycle.GetComponentsInChildren<UnityEngine.UI.Image>(false))
            {
                string sprite = img.sprite?.name ?? "";
                foreach (var season in new[] { "winter", "spring", "summer", "autumn" })
                    if (sprite.Contains(season)) return season;
            }
            return null;
        }

        // ---- the deck selector ----

        private static DeckSelector FindSelector(List<AControl>[] byStop)
        {
            foreach (var list in byStop)
                if (list != null)
                    foreach (var c in list)
                        if (IsDeckButton(c)) return c.GetComponentInParent<DeckSelector>();
            return null;
        }

        /// <summary>
        /// The list opening lands on the active deck; it closing (a pick, Close, or the deck button again)
        /// lands on the deck button. The game rebuilds both around the transition (the deck button reads its
        /// prefab's "NEW DECK" for a frame or two, and can be swapped again), so the landing is a role,
        /// re-found every build and suggested once it shows the active deck's name; it holds until
        /// <see cref="SteerSeconds"/> pass or a key is pressed. False while it's still waiting for its control.
        /// </summary>
        private bool TrackSelector(DeckSelector selector, List<AControl>[] byStop, GwentVisuals.CollectionDeck active)
        {
            _steer = null;
            if (_steerToList != null && (Keys.AnyDown() || Time.realtimeSinceStartup > _steerUntil)) _steerToList = null;
            if (selector == null)
            {
                _listOpen = null;
                _steerToList = null;
                return true;
            }
            bool open = selector.m_Expanded;
            if (_listOpen.HasValue && _listOpen.Value != open)
            {
                _steerToList = open;
                _steerUntil = Time.realtimeSinceStartup + SteerSeconds;
            }
            _listOpen = open;
            if (_steerToList == null) return true;

            AControl target;
            if (_steerToList.Value)
                target = byStop[DeckListRegion]?.FirstOrDefault(c => IsDeckButton(c) && DeckOf(c) == (active?.Id ?? 0));
            else
            {
                target = selector.m_Button?.TryCast<GwentVisuals.VirtualDeckContainer>()?.m_MainButton;
                if (target != null && !target.gameObject.activeInHierarchy) target = null;
            }
            if (target == null || !ShowsDeck(target, active)) return false;
            _steer = target;
            return true;
        }

        // Whether a deck button already reads the deck's name. Preset decks are named by a translation key
        // ("$(skellige_preset_deck_name)"), player decks by their own text.
        private static bool ShowsDeck(AControl c, GwentVisuals.CollectionDeck deck)
        {
            string name = deck?.Name;
            if (string.IsNullOrEmpty(name)) return true;
            if (name.StartsWith("$(") && name.EndsWith(")")) name = GameText.Get(name.Substring(2, name.Length - 3), null);
            return name == null || DeckLabel(c).Contains(UiText.Clean(name));
        }

        /// <summary>While a landing is held (<see cref="TrackSelector"/>), the game's selection is put back on
        /// it each frame and reported as staying there, so the navigator doesn't follow the game's own jumps
        /// (to the guides button on open; to the shop, then the mode card, after a pick).</summary>
        public override AControl GameSelection()
        {
            var real = base.GameSelection();
            if (_steer == null) return real;
            if (real == null || real.Pointer != _steer.Pointer) _steer.ParentLayer?.RequestControlSelection(_steer);
            return _steer;
        }

        // The next or previous deck in the selector's own list, chosen through the list's click handler.
        private static void CycleDeck(AControl button, int delta)
        {
            try
            {
                var selector = button.GetComponentInParent<DeckSelector>();
                var decks = selector?.GetDeckList();
                if (decks == null || decks.Count == 0) { Speech.Say("No other decks.", true); return; }
                if (decks.Count == 1) { Speech.Say("Only one deck.", true); return; }
                ulong current = selector.GetSelectedDeck()?.Id ?? 0;
                int at = 0;
                for (int i = 0; i < decks.Count; i++) if (decks[i]?.Id == current) { at = i; break; }
                int next = ((at + (delta >= 0 ? 1 : -1)) % decks.Count + decks.Count) % decks.Count;
                selector.OnDeckButtonClicked(decks[next]);
                Plugin.LogInfo("[menu] deck " + at + " -> " + next);
            }
            catch (System.Exception e) { Plugin.LogInfo("[menu] deck change failed: " + e.Message); Speech.Say("Couldn't change the deck.", true); }
        }

        private static ulong DeckOf(AControl c)
        {
            try { return c.TryCast<GwentVisuals.UIDeckButtonControl>()?.Deck?.Id ?? 0; } catch { return 0; }
        }

        // ---- layout ----

        // One item per control, except two kinds of rows: a deck's button and its faction-info button (Up/Down
        // walks the decks, Right reaches the faction info, deck first), and a bar of buttons the game lays out
        // side by side (the deck list's library / new deck / close).
        private static void AddControls(GraphBuilder b, List<AControl> controls, ulong activeDeck)
        {
            for (int i = 0; i < controls.Count;)
            {
                var row = RowKey(controls[i]);
                int end = i + 1;
                if (row != null)
                    while (end < controls.Count && RowKey(controls[end])?.Pointer == row.Pointer) end++;
                if (end - i == 1)
                {
                    b.AddItem(CrimsonNodes.Id(controls[i]), Vtable(controls[i], activeDeck));
                    i++;
                    continue;
                }
                var items = controls.GetRange(i, end - i);
                if (row.TryCast<GwentVisuals.VirtualDeckContainer>() != null)
                    items.Sort((x, y) => IsDeckButton(y).CompareTo(IsDeckButton(x)));
                b.StartRow();
                foreach (var c in items) b.AddItem(CrimsonNodes.Id(c), Vtable(c, activeDeck));
                b.EndRow();
                i = end;
            }
        }

        // The component whose controls share a row: a deck's container, or a button bar. Null = its own row.
        private static Component RowKey(AControl c)
        {
            var deck = c.Parent?.TryCast<GwentVisuals.VirtualDeckContainer>();
            if (deck != null) return deck;
            return c.GetComponentInParent<InputButtonContainer>();
        }

        // What a control is positioned by on screen: a deck's whole row, else the control itself.
        private static Transform RowOf(AControl c) =>
            c.Parent?.TryCast<GwentVisuals.VirtualDeckContainer>()?.transform ?? c.transform;

        private static bool IsDeckButton(AControl c) => c.TryCast<GwentVisuals.UIDeckButtonControl>() != null;

        // The index into Regions of the control's nearest known ancestor, Regions.Length for "other", or -1
        // to skip it.
        private static int RegionOf(AControl c)
        {
            // The friend-match stack is staged in the hidden container and only moved on screen and enabled
            // while a friend match is being set up: then all of it is the friend-match stop, else none of it.
            for (var t = c.transform; t != null; t = t.parent)
                if (t.name == FriendStackRoot) return _friendSetup ? FriendRegion : -1;
            for (var t = c.transform; t != null; t = t.parent)
            {
                string name = t.name;
                if (Hidden.Contains(name)) return -1;
                for (int i = 0; i < Regions.Length; i++)
                    if (Regions[i].Root == name) return i;
            }
            return Regions.Length;
        }

        // A deck button reads its deck name and leader ability. In the open list the active deck says so; the
        // deck button outside it says it opens a list ("collapsed"), as a sighted player sees the dropdown
        // arrow.
        private static NodeVtable Vtable(AControl c, ulong activeDeck)
        {
            if (c.TryCast<SocialButton>() is SocialButton social) return SocialToggle(c, social);
            if (CardOf(c) is UIMainMenuCardItem card) return CardVtable(c, card);
            if (!IsDeckButton(c)) return CrimsonNodes.Button(c);
            var v = CrimsonNodes.Button(c, () => DeckLabel(c));
            bool inList = RegionOf(c) == DeckListRegion;
            var anns = new List<NodeAnnouncement>(v.Announcements)
            {
                inList
                    ? new NodeAnnouncement(() => activeDeck != 0 && DeckOf(c) == activeDeck ? "selected" : null,
                        kind: AnnouncementKinds.Selected)
                    : new NodeAnnouncement(() => GraphAnnouncer.ExpandedStateText?.Invoke(false) ?? "collapsed",
                        kind: AnnouncementKinds.Value),
            };
            v.Announcements = anns;
            // Left/Right on the deck button change the deck, as picking the next one in its list does.
            if (!inList) v.OnAdjust = (delta, large) => CycleDeck(c, delta);
            return v;
        }

        /// <summary>
        /// A social-bar button (journey, season cycle, quests, friends): not a menu card's twin but a toggle
        /// opening the side panel on its section, one at a time (the game's toggle group). The open one reads
        /// "pressed", as a sighted player sees it lit; pressing it again closes the panel.
        /// </summary>
        private static NodeVtable SocialToggle(AControl c, SocialButton toggle)
        {
            var v = CrimsonNodes.Button(c);
            v.Announcements = new List<NodeAnnouncement>(v.Announcements)
            {
                new NodeAnnouncement(() => toggle.IsToggled ? "pressed" : null, live: true, kind: AnnouncementKinds.Selected),
            };
            return v;
        }

        // ---- menu cards ----

        /// <summary>The menu card a control is the face of: its button, or the stand-in below. Null for anything
        /// else, such as the opponent toggles on a card.</summary>
        private static UIMainMenuCardItem CardOf(AControl c)
        {
            var card = c.GetComponentInParent<UIMainMenuCardItem>();
            if (card == null) return null;
            return c.Pointer == card.m_Button?.Pointer || IsStandIn(c, card) ? card : null;
        }

        // A disabled card (one locked by level, a mode outside its dates, the Balance Council before voting
        // opens; or, looking disabled, a mode with no valid deck) gets a textless button over it, its
        // m_LookDisabledButtonControl, which takes the selection and the press in the card's place.
        private static bool IsStandIn(AControl c, UIMainMenuCardItem card) =>
            c.Pointer == card.m_LookDisabledButtonControl?.Pointer;

        /// <summary>A card's own button while its stand-in is up: the two are one card to a sighted player, so
        /// the graph keeps only the stand-in, which is where the game's selection lands.</summary>
        private static bool BehindStandIn(AControl c)
        {
            var card = CardOf(c);
            var standIn = card?.m_LookDisabledButtonControl;
            return card != null && !IsStandIn(c, card) && standIn != null && standIn.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// A card reads as its title, then why it is locked (the game's own words, the ones its popup shows) or
        /// else any countdown on it. A disabled card with no reason to give says it is unavailable. Enter on a
        /// stand-in is the mouse's press and release: the popup explaining the lock opens on the press alone
        /// (<c>HandleControlStatesChanged</c>), which the click a keyboard submit sends never reaches.
        /// </summary>
        private static NodeVtable CardVtable(AControl c, UIMainMenuCardItem card)
        {
            var v = CrimsonNodes.Button(c, () => CardLabel(c, card));
            // The description drawn on a play-mode card ("Standard Mode. Play against worthy opponents…", a new
            // player's next goal) is read after the card's name, as a sighted player reads it on the card.
            v.Announcements = new List<NodeAnnouncement>(v.Announcements)
            {
                new NodeAnnouncement(() => DrawnDescription(card), kind: AnnouncementKinds.Tooltip),
            };
            if (!IsStandIn(c, card)) return v;
            var button = c.TryCast<ButtonControl>();
            v.Announcements = new List<NodeAnnouncement>(v.Announcements)
            {
                new NodeAnnouncement(() => LockedReason(card) == null ? "unavailable" : null, live: true, kind: AnnouncementKinds.Enabled),
            };
            v.OnActivate = () =>
            {
                BattleInput.SendButton(button, CursorButtonEventArgs.EState.ButtonDown);
                BattleInput.ReleaseButton(button);
            };
            v.OnTooltip = () => Speech.Say(CardDetails(c, card), true);
            return v;
        }

        private static string CardLabel(AControl c, UIMainMenuCardItem card)
        {
            CardTexts(card, out string title, out var timer);
            title ??= KeyTitle(card) ?? CrimsonNodes.Label(c);
            string detail = LockedReason(card) ?? (timer.Count > 0 ? string.Join(" ", timer) : null);
            return detail == null ? title : title + ", " + detail;
        }

        // A card whose title isn't drawn yet (the menu still building, or a locked card's stand-in up) is named
        // from its object, "Item(mainmenu_practice_title)": the game's own string for that key. The stand-in's
        // own name ("Look Disabled Highlight Control") was spoken otherwise.
        private static string KeyTitle(UIMainMenuCardItem card)
        {
            try
            {
                string n = card.gameObject.name;
                int open = n.IndexOf('('), close = n.LastIndexOf(')');
                if (open < 0 || close <= open + 1) return null;
                string key = n.Substring(open + 1, close - open - 1);
                if (key == "Clone") return null;
                string t = UiText.Clean(GameText.Get(key, ""));
                return t.Length > 0 ? t : null;
            }
            catch { return null; }
        }

        // The description panel's words as drawn on the card (null when it shows none).
        private static string DrawnDescription(UIMainMenuCardItem card)
        {
            try
            {
                var parts = new List<string>();
                foreach (var d in card.GetComponentsInChildren<UIPlayMenuCardDescription>(false))
                    foreach (var t in CrimsonNodes.Texts(d)) { var line = Narrator.Sentence(t); if (!parts.Contains(line)) parts.Add(line); }
                return parts.Count > 0 ? string.Join(" ", parts) : null;
            }
            catch { return null; }
        }

        // Space on a stand-in, which holds no text of its own: the card's label, then what the mode is.
        private static string CardDetails(AControl c, UIMainMenuCardItem card)
        {
            string details = CardLabel(c, card);
            string description = null;
            try { description = UiText.Clean(card.GetDescription()); } catch { } // not every kind of card has one
            return string.IsNullOrEmpty(description) ? details : details + ". " + description;
        }

        // The card's title and its countdown's texts, as drawn; its description panel is left to Space.
        private static void CardTexts(UIMainMenuCardItem card, out string title, out List<string> timer)
        {
            title = null;
            timer = new List<string>();
            foreach (var t in card.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (t.GetComponentInParent<UIPlayMenuCardDescription>() != null) continue;
                string s = UiText.Clean(t.text);
                if (s.Length == 0 || UiText.IsPlaceholder(s)) continue;
                if (t.GetComponentInParent<TimerCountdownRenderer>() != null) timer.Add(s);
                else title ??= s;
            }
        }

        // Why a disabled card is locked, or null: the game has no reason for some kinds of card.
        private static string LockedReason(UIMainMenuCardItem card)
        {
            try
            {
                if (!card.IsDisabled) return null;
                string reason = UiText.Clean(card.GetLockedDescription());
                return string.IsNullOrEmpty(reason) ? null : reason;
            }
            catch { return null; }
        }

        // The button's texts without its deck-stats panel (card count and provisions), which the game shows
        // on the button while it animates; Space still reads everything.
        private static string DeckLabel(AControl c)
        {
            var parts = new List<string>();
            foreach (var t in c.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (t.GetComponentInParent<GwentVisuals.UIDeckStatsWidget>() != null) continue;
                string s = UiText.Clean(t.text);
                if (s.Length > 0 && !UiText.IsPlaceholder(s) && !parts.Contains(s)) parts.Add(s);
            }
            return string.Join(", ", parts);
        }
    }
}
