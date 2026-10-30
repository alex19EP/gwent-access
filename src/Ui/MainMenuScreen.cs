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
    /// the profile and currency bar, notifications and the social panel — one Tab stop each, in that order.
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

        // Skipped subtrees: off-screen staging the game keeps active (the friend-invite card waiting to be dealt
        // in), and a play card's scrollable description, selectable only so a gamepad can scroll it — Space on
        // the card reads that text.
        private static readonly HashSet<string> Hidden = new HashSet<string> { "HidenContainer", "DescriptionPanelRoot" };

        // How long a landing the screen chose holds the game's selection against the game's own moves.
        private const float SteerSeconds = 1.5f;

        // The deck selector as of the last build: whether its list was open (null = not seen yet, so the
        // first sighting is no transition); the landing being held (true = the active deck in the list,
        // false = the deck button, null = none) until a deadline or a key; and this frame's control for it.
        private bool? _listOpen;
        private bool? _steerToList;
        private float _steerUntil;
        private AControl _steer;

        public override bool IsActive()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null || layer.Priority != CrimsonInputLayer.EPriority.Base) return false;
            return ContentPanel(layer) != null;
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
            var byStop = new List<AControl>[Regions.Length + 1];
            var attached = layer.GetAttachedControls();
            var seen = new HashSet<System.IntPtr>();
            for (int i = 0; i < attached.Count; i++)
                foreach (var c in CrimsonNodes.Leaves(attached[i], seen))
                {
                    int r = RegionOf(c);
                    if (r < 0) continue;
                    (byStop[r] ??= new List<AControl>()).Add(c);
                }

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

            for (int r = 0; r < byStop.Length; r++)
            {
                if (byStop[r] == null) continue;
                bool known = r < Regions.Length;
                b.BeginStop(known ? Regions[r].Stop : "other");
                string context = r == DeckListRegion
                    ? GameText.Get(DeckSelector.SELECT_DECK_TEXT_LOCALIZATION_KEY, "Change deck")
                    : known ? Regions[r].Context : null;
                if (context != null) b.PushContext(context);
                AddControls(b, byStop[r], activeDeck);
                if (context != null) b.PopContext();
            }

            if (_steer != null) GraphNavigator.Suggest(CrimsonNodes.Id(_steer));
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
            return v;
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
