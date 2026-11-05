using System;
using System.Collections.Generic;
using GwentGameplay;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The card-reading keys every screen showing cards shares (Accessible Arena's and Hearthstone Access's
    /// model: Left/Right move through the cards, Up/Down read the focused one): Up/Down step through the focused
    /// card's lines (the summary spoken on landing is line 0; "Beginning of card" / "End of card" past the ends),
    /// I explains its keywords — just the current line's once Up/Down is on an ability that has any — and K reads
    /// its kind and base power. On screens of cards only (the board, the mulligan, choices, piles) Up/Down always
    /// read and Home/End go to the ends of the current row; where cards sit among other items (rewards) Up/Down
    /// read only while a card is focused, and move otherwise, and Home/End keep their list meaning.
    /// </summary>
    internal sealed class CardKeys
    {
        private readonly Func<Card> _focused;
        private readonly Func<PlayerManager> _players;
        private Card _lineCard;
        private int _line;

        private readonly bool _always;
        private readonly Action _noCard;
        private readonly bool _preview;

        /// <param name="always">Up/Down always read (no card focused: <paramref name="noCard"/>, by default "No
        /// card."); false: they read only while a card is focused, and move otherwise.</param>
        /// <param name="preview">The cards sit in a panel of cards (a choice, a pile): hidden is what the panel
        /// shows face down (<see cref="CardReader.Hidden"/>).</param>
        public CardKeys(Func<Card> focused, Func<PlayerManager> players, bool always = false, Action noCard = null, bool preview = false)
        {
            _focused = focused;
            _players = players;
            _always = always;
            _noCard = noCard;
            _preview = preview;
        }

        /// <summary>The keys. On screens of cards only (<c>always</c>), Home/End also stay in the row: the first or
        /// last card of the hand or board row the cursor is in, not the top or bottom of the whole screen.</summary>
        public IEnumerable<ScreenKey> Keys
        {
            get
            {
                var keys = new List<ScreenKey>
                {
                    new ScreenKey { Key = KeyCode.DownArrow, Run = () => StepLine(1), When = Reads, Hint = Loc.T("Down, next line of the card") },
                    new ScreenKey { Key = KeyCode.UpArrow, Run = () => StepLine(-1), When = Reads, Hint = Loc.T("Up, previous line of the card") },
                    new ScreenKey { Key = KeyCode.I, Run = SayKeywords, Hint = Loc.T("I, what the card's keywords mean") },
                    new ScreenKey { Key = KeyCode.K, Run = SayStats, Hint = Loc.T("K, card kind and base power") },
                };
                if (_always)
                {
                    // Only on a card, or on a spot among the cards that has none (the board's empty rows and
                    // placement gaps, the screen that says where such a spot is): elsewhere (the mulligan's
                    // buttons) Home/End keep their list meaning. One hint for the pair: the help lists each once.
                    string rowEnds = Loc.T("Home, End: first and last card in the row");
                    Func<bool> inRow = () => _noCard != null || _focused() != null;
                    keys.Add(new ScreenKey { Key = KeyCode.Home, Run = () => GraphNavigator.RowEdge(first: true), When = inRow, Hint = rowEnds });
                    keys.Add(new ScreenKey { Key = KeyCode.End, Run = () => GraphNavigator.RowEdge(first: false), When = inRow, Hint = rowEnds });
                }
                return keys;
            }
        }

        /// <summary>The cursor moved: the next Up/Down starts from the new card's summary.</summary>
        public void Reset() => _lineCard = null;

        private bool Reads() => _always || _focused() != null;

        private void StepLine(int dir)
        {
            var card = _focused();
            if (card == null)
            {
                if (_noCard != null) _noCard(); else Speech.Say(Loc.T("No card."), true);
                return;
            }
            if (_lineCard == null || _lineCard.Id != card.Id) { _lineCard = card; _line = 0; }
            var lines = CardReader.Lines(card, _players(), _preview);
            int next = _line + dir;
            // Past either end says so (Accessible Arena's "End of card"), instead of silence.
            if (next < 0) { Speech.Say(Loc.T("Beginning of card"), true); return; }
            if (next >= lines.Count) { Speech.Say(Loc.T("End of card"), true); return; }
            _line = next;
            Speech.Say(lines[next].Text, true);
        }

        private void SayKeywords()
        {
            var card = _focused();
            if (card == null) return;
            IReadOnlyList<string> only = null;
            if (_lineCard != null && _lineCard.Id == card.Id)
            {
                var lines = CardReader.Lines(card, _players(), _preview);
                if (_line < lines.Count && lines[_line].Keywords.Count > 0) only = lines[_line].Keywords;
            }
            var explained = CardReader.Explanations(card, _players(), only, _preview);
            Speech.Say(explained.Count > 0 ? string.Join(" ", explained) : Loc.T("No keywords."), true);
        }

        private void SayStats()
        {
            var card = _focused();
            if (card == null) return;
            Speech.Say(CardReader.Hidden(card, _players(), _preview) ? Loc.T("hidden card") : CardReader.Stats(card), true);
        }
    }
}
