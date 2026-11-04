using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentGameplay;
using GwentUnity;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Opening a keg in the shop (spec §9): the game's prompt ("Reveal four cards", then the pick, then the
    /// summary) as the context, the keg's cards as one row in the order drawn, and the bar's actions.
    ///
    /// The four outer cards lie face down until revealed: a face-down card says so and nothing more (parity:
    /// its face isn't on screen yet), Enter reveals it through the game's own click. The three cards of the
    /// pick wait stacked in the middle, offered as one "stack" (Enter on it reveals all four at once, as the
    /// game does for a click on it), and join the row once the game offers them; Enter picks one. Revealed
    /// cards read like everywhere else (the summary on landing, Space for the whole card, Ctrl+Up/Down, I, K),
    /// with their rarity, which the card's frame shows.
    ///
    /// The shop screen stands down while a keg is open (<see cref="Keg"/>), so this screen owns the keyboard.
    /// </summary>
    internal sealed class KegOpeningScreen : Screen
    {
        public override string Key => "keg";
        public override int Layer => 1;

        public override bool IsActive() => Keg() != null;

        /// <summary>The keg being opened: the shop's opening page shows its keg view with a keg's data.</summary>
        public static UIKegOpeningView Keg()
        {
            var keg = CrimsonNodes.ActivePanel<UIShopMenuPanel>()?.GetComponentInChildren<UIProductOpeningView>(false)?.m_KegOpening;
            return keg != null && keg.gameObject.activeInHierarchy && keg.m_Data != null ? keg : null;
        }

        private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();

        public override void Build(GraphBuilder b)
        {
            var keg = Keg();
            if (keg == null) return;
            _cards.Clear();
            string title = UiText.Clean(keg.m_KegOpeningTitle?.text);
            bool titled = !string.IsNullOrEmpty(title) && keg.m_KegOpeningTitle.gameObject.activeInHierarchy;
            if (titled) b.PushContext(title);

            // One row: the cards, then the bar's actions, so "Continue" is a step past the last card revealed.
            var cards = Cards(keg);
            // The pick began: land on its first card (the four revealed ones just left navigation).
            bool picking = Picking(keg);
            if (picking && !_picking && cards.Count > 0) GraphNavigator.Suggest(CrimsonNodes.Id(cards[0].Control));
            _picking = picking;
            b.BeginStop("cards");
            b.StartRow();
            foreach (var (c, index) in cards)
            {
                var control = c;
                int i = index;
                var id = CrimsonNodes.Id(control);
                if (Revealed(keg, i) && control.CardInstance != null) _cards[(string)id.StructuralKey] = control.CardInstance;
                b.AddItem(id, new NodeVtable
                {
                    // Live: revealing the focused card speaks its face as soon as it turns over.
                    Announcements = new[] { new NodeAnnouncement(() => CardLabel(i, control), live: true, kind: AnnouncementKinds.Label) },
                    OnActivate = () => Navigator.ClickControl(control),
                    OnTooltip = () => Speech.Say(Revealed(Keg(), i) ? CardReader.Whole(control.CardInstance) : CardLabel(i, control), true),
                });
            }
            AddBar(b, keg);
            b.EndRow();
            if (titled) b.PopContext();
        }

        // The cards on show, left to right: the outer four always, the pick's three once the game lets them
        // be chosen (until then they wait stacked in the middle, backs up), and after the pick only the one
        // chosen.
        private static List<(CardViewUIControl Control, int Index)> Cards(UIKegOpeningView keg)
        {
            var found = new List<(CardViewUIControl, int)>();
            var controls = keg.m_CardsControlsOpeningView?.m_CardViewUIControls;
            if (controls == null) return found;
            for (int i = 0; i < controls.Count; i++)
            {
                var c = controls[i];
                if (c == null || !c.gameObject.activeInHierarchy || !CrimsonNodes.Selectable(c)) continue;
                // During the pick the four revealed cards are greyed out and leave the game's navigation; while
                // the pick's cards slide in they still answer as selectable for a moment.
                if (Outer(keg, i) && Picking(keg)) continue;
                // Before the pick its three cards lie stacked in the middle: offered once, as the stack.
                if (!Outer(keg, i) && keg.m_NonChoosableState && !keg.ShowingRevealedMode && i != StackIndex) continue;
                // After the pick the two left behind burn away; they stay in the scene (and selectable),
                // tucked behind the others.
                if (!Outer(keg, i) && keg.m_ChosenCardIdx >= 0 && keg.m_ChosenCardIdx != i) continue;
                found.Add((c, i));
            }
            found.Sort((x, y) => x.Item1.transform.position.x.CompareTo(y.Item1.transform.position.x));
            return found;
        }

        private static bool Outer(UIKegOpeningView keg, int index)
        {
            foreach (var i in keg.OUTER_CARD_INDICES) if (i == index) return true;
            return false;
        }

        /// <summary>The middle card of the pick, which stands for the face-down stack until the pick. The keg's
        /// own click handler (UIKegOpeningView.OnCardClicked, read in GameAssembly) treats a click on it before
        /// the reveals as "reveal all four", as a mouse player clicking the stack gets.</summary>
        private const int StackIndex = 3;

        /// <summary>The pick is on: the three cards are dealt and none chosen yet.</summary>
        private static bool Picking(UIKegOpeningView keg) => !keg.m_NonChoosableState && keg.m_ChosenCardIdx < 0;

        private bool _picking;

        private static bool Stacked(UIKegOpeningView keg, int index) =>
            !Outer(keg, index) && keg.m_NonChoosableState && keg.m_ChosenCardIdx < 0;

        // An outer card shows its face once revealed (one by one, or all at once); the pick's cards are dealt
        // face up.
        private static bool Revealed(UIKegOpeningView keg, int index)
        {
            if (keg == null || Stacked(keg, index)) return false;
            if (!Outer(keg, index) || keg.m_AllRevealed) return true;
            var revealed = keg.m_RevealedIndexes;
            return revealed != null && revealed.Contains(index);
        }

        private string CardLabel(int index, CardViewUIControl c)
        {
            var keg = Keg();
            var card = c.CardInstance;
            if (keg == null || card == null) return "";
            if (Stacked(keg, index)) return Loc.T("stack of 3 face-down cards");
            if (!Revealed(keg, index)) return Loc.T("face-down card");
            string rarity = Rarity(c);
            string summary = CardReader.Summary(card);
            return rarity == null ? summary : summary + ", " + rarity;
        }

        private static string Rarity(CardViewUIControl c)
        {
            try
            {
                switch (c.GetRarity())
                {
                    case ERarity.Common: return GameText.Get("card_rarity_common", "common");
                    case ERarity.Rare: return GameText.Get("card_rarity_rare", "rare");
                    case ERarity.Epic: return GameText.Get("card_rarity_epic", "epic");
                    case ERarity.Legendary: return GameText.Get("card_rarity_legendary", "legendary");
                }
            }
            catch { }
            return null;
        }


        // The opening page's bar: buttons run their stored callbacks (Navigator.ClickControl); glyph labels
        // are key prompts the game never makes selectable, offered (when their text shows) and run through their
        // own callbacks. "Select" repeats Enter on a card and is left out.
        private static void AddBar(GraphBuilder b, UIKegOpeningView keg)
        {
            var view = keg.GetComponentInParent<UIProductOpeningView>();
            var bar = view?.Buttons;
            if (bar == null || !bar.gameObject.activeInHierarchy) return;

            var children = bar.GetChildren();
            for (int i = 0; children != null && i < children.Count; i++)
            {
                var c = children[i];
                if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                if (CrimsonNodes.Texts(c).Count == 0) continue;
                var v = CrimsonNodes.Button(c);
                if (view.m_ContinueBindingPair?.Button?.Pointer == c.Pointer) v.OnActivate = () => Continue(view, c);
                b.AddItem(CrimsonNodes.Id(c), v);
            }
            var labels = bar.m_Labels;
            for (int i = 0; labels != null && i < labels.Count; i++)
            {
                var l = labels[i];
                // A pair binding's label hides its text where its button twin is shown (mouse and keyboard).
                if (l == null || !l.gameObject.activeInHierarchy || l.Callback == null || CrimsonNodes.Texts(l).Count == 0) continue;
                string key = l.LocalizationKey;
                if (key == UIProductOpeningView.KEY_SELECT) continue;
                var label = l;
                b.AddItem(CrimsonNodes.Id(l), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => GameText.Get(label.LocalizationKey, label.LocalizationKey), kind: AnnouncementKinds.Label) },
                    OnActivate = () =>
                    {
                        Plugin.LogInfo("[keg] bar label: " + label.LocalizationKey);
                        label.Callback?.Invoke();
                    },
                });
            }
        }

        // "Continue" after the four reveals. The button's callback (UIProductOpeningView.KegContinueToPick)
        // returns at once while the game navigates by selection (keyboard, gamepad): there, the keg's click
        // handler takes a click on any card once all four are revealed as "continue" (both read in
        // GameAssembly). So after the button, if the keg still waits, click a card as such a player would;
        // that also raises the click event the opening's sounds listen to.
        private static void Continue(UIProductOpeningView view, AControl button)
        {
            Navigator.ClickControl(button);
            var keg = view.m_KegOpening;
            if (keg == null || !keg.m_AllRevealed || !keg.m_NonChoosableState) return;
            var controls = keg.m_CardsControlsOpeningView?.m_CardViewUIControls;
            if (controls == null) return;
            foreach (var i in keg.OUTER_CARD_INDICES)
            {
                if (i >= controls.Count || controls[i] == null) continue;
                Plugin.LogInfo("[keg] continue through a card click (selection navigation)");
                Navigator.ClickControl(controls[i]);
                return;
            }
        }

        // ---- keys ----

        private CardKeys _cardKeys;
        private List<ScreenKey> _keys;

        public override IReadOnlyList<ScreenKey> ExtraKeys
        {
            get
            {
                if (_keys != null) return _keys;
                _cardKeys = new CardKeys(FocusedCard, () => null);
                _keys = new List<ScreenKey>(_cardKeys.Keys);
                return _keys;
            }
        }

        public override void OnCursorMoved(GraphNode node)
        {
            _cardKeys?.Reset();
            // Glyph labels aren't selectable in the game; landing on one leaves the game's selection alone.
            if (node?.Id.Reference is GlyphLabelControl) return;
            base.OnCursorMoved(node);
        }

        private Card FocusedCard()
        {
            var node = GraphNavigator.CurrentNode;
            return node?.Id.StructuralKey is string key && _cards.TryGetValue(key, out var card) ? card : null;
        }
    }
}
