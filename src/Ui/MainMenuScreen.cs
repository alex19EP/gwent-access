using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
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
    /// </summary>
    internal sealed class MainMenuScreen : Screen
    {
        public override string Key => "main-menu";
        public override string ScreenName => "Main menu";

        // Nearest ancestor (by GameObject name) → the stop a control belongs to. Order = Tab order.
        private static readonly (string Root, string Stop, string Context)[] Regions =
        {
            ("Carousel", "cards", null),
            ("CardsSpawnRoot", "modes", "Game modes"),
            ("MainView", "deck", "Deck"),
            ("UIVanityInventoryAndPreview(Clone)", "cosmetics", "Cosmetics"),
            ("ControlsSection", "menubar", "Menu bar"),
            ("UIPlayerInfoOverlayPrefab(Clone)", "profile", "Profile and currencies"),
            ("UINotificationsPrefab(Clone)", "notifications", "Notifications"),
            ("UIMenusSocialPrefab(Clone)", "social", "Social"),
        };

        // Skipped subtrees: off-screen staging the game keeps active (the friend-invite card waiting to be dealt
        // in), and a play card's scrollable description, selectable only so a gamepad can scroll it — Space on
        // the card reads that text.
        private static readonly HashSet<string> Hidden = new HashSet<string> { "HidenContainer", "DescriptionPanelRoot" };

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

        public override void Build(GraphBuilder b)
        {
            var layer = CrimsonInputManager.Instance.ActiveLayer;
            var byStop = new List<AControl>[Regions.Length + 1];
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
                foreach (var c in CrimsonNodes.Leaves(attached[i]))
                {
                    int r = RegionOf(c);
                    if (r < 0) continue;
                    (byStop[r] ??= new List<AControl>()).Add(c);
                }

            for (int r = 0; r < byStop.Length; r++)
            {
                if (byStop[r] == null) continue;
                bool known = r < Regions.Length;
                b.BeginStop(known ? Regions[r].Stop : "other");
                string context = known ? Regions[r].Context : null;
                if (context != null) b.PushContext(context);
                AddControls(b, byStop[r]);
                if (context != null) b.PopContext();
            }
        }

        // One item per control, except a deck in the deck list: its button and its faction-info button share a
        // row (Up/Down walks the decks, Right reaches the faction info), deck first.
        private static void AddControls(GraphBuilder b, List<AControl> controls)
        {
            for (int i = 0; i < controls.Count;)
            {
                var deck = controls[i].Parent?.TryCast<GwentVisuals.VirtualDeckContainer>();
                int end = i + 1;
                if (deck != null)
                    while (end < controls.Count && controls[end].Parent?.Pointer == deck.Pointer) end++;
                if (end - i == 1)
                {
                    b.AddItem(CrimsonNodes.Id(controls[i]), Vtable(controls[i]));
                    i++;
                    continue;
                }
                var row = controls.GetRange(i, end - i);
                row.Sort((x, y) => IsDeckButton(y).CompareTo(IsDeckButton(x)));
                b.StartRow();
                foreach (var c in row) b.AddItem(CrimsonNodes.Id(c), Vtable(c));
                b.EndRow();
                i = end;
            }
        }

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

        private static NodeVtable Vtable(AControl c)
        {
            if (IsDeckButton(c))
                return CrimsonNodes.Button(c, () => string.Join(", ", CrimsonNodes.Texts(c)));
            return CrimsonNodes.Button(c);
        }
    }
}
