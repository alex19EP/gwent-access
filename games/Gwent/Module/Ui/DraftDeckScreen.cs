using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentGameplay;
using GwentVisuals;
using GwentVisuals.ArenaV2;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Draft's deck view: the deck picker the lobby's Show deck opens beside the wheel, listing the run's deck
    /// (leader, stratagem, then the cards). Read as the deck builder reads its deck: the cards one row, Left/Right
    /// through them, each read as a card with its copies and no role, Up/Down through its lines
    /// (<see cref="CardKeys"/>). Backspace closes it through the picker's own close button, back to the lobby's
    /// Show deck. The generic panel reader read it before: the cards said "button", and each carried the
    /// description panel's text, which still showed the card hovered before.
    /// </summary>
    internal sealed class DraftDeckScreen : Screen
    {
        public override string Key => "draft-deck";
        public override int Layer => 2;
        public override bool Overlays => true;
        public override string ScreenName => Loc.T("Deck");

        public override bool IsActive() => Picker(out _) != null;

        // The open picker, and the panel it belongs to.
        private static UIDeckPickerContainer Picker(out UIArenaPanel panel)
        {
            panel = CrimsonNodes.ActivePanel<UIArenaPanel>();
            try
            {
                if (panel == null || !panel.m_IsDeckPickerOpen) return null;
                var picker = panel.UIArenaDeckPickerInstance;
                return CrimsonNodes.Shown(picker) ? picker : null;
            }
            catch { return null; }
        }

        public override void Build(GraphBuilder b)
        {
            var picker = Picker(out var panel);
            if (picker == null) return;
            var rows = new List<AControl>();
            foreach (var row in picker.GetComponentsInChildren<UIDeckCardButtonControl>(false))
                if (row.IsCurrentlySelectable()) rows.Add(row);
            if (rows.Count > 0)
            {
                b.BeginStop("draft-deck:cards");
                b.StartRow();
                foreach (var c in rows)
                {
                    var control = c;
                    b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.WithRole(CrimsonNodes.Button(c, () => DeckBuilderScreen.DeckRow(control)), null));
                }
                b.EndRow();
            }
            var close = panel.m_CloseDeckPicker;
            if (CrimsonNodes.Shown(close))
            {
                b.BeginStop("draft-deck:close");
                b.AddItem(CrimsonNodes.Id(close), CrimsonNodes.Button(close, () => GameText.Get("close", "Close")));
            }
        }

        // The game moves its selection onto the picker's own controls as it opens; the cursor starts on the
        // leader, as the list is read top to bottom.
        public override AControl GameSelection() => null;

        /// <summary>Backspace closes the deck, as its close button does.</summary>
        public override bool Back()
        {
            Picker(out var panel);
            var close = panel?.m_CloseDeckPicker;
            if (!CrimsonNodes.Shown(close)) return false;
            Navigator.MouseClick(close);
            return true;
        }

        private CardKeys _cardKeys;
        private List<ScreenKey> _keys;

        public override void OnCursorMoved(GraphNode node) => _cardKeys?.Reset();

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

        private static Card FocusedCard() =>
            GraphNavigator.CurrentNode?.Id.Reference is AControl c ? c.TryCast<UIDeckCardButtonControl>()?.Card?.Card : null;
    }
}
