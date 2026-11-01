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
    /// read; where cards sit among other items (rewards) they read only while a card is focused, and move
    /// otherwise.
    /// </summary>
    internal sealed class CardKeys
    {
        private readonly Func<Card> _focused;
        private readonly Func<PlayerManager> _players;
        private Card _lineCard;
        private int _line;

        private readonly bool _always;
        private readonly Action _noCard;

        /// <param name="always">Up/Down always read (no card focused: <paramref name="noCard"/>, by default "No
        /// card."); false: they read only while a card is focused, and move otherwise.</param>
        public CardKeys(Func<Card> focused, Func<PlayerManager> players, bool always = false, Action noCard = null)
        {
            _focused = focused;
            _players = players;
            _always = always;
            _noCard = noCard;
        }

        public IEnumerable<ScreenKey> Keys => new[]
        {
            new ScreenKey { Key = KeyCode.DownArrow, Run = () => StepLine(1), When = Reads, Hint = "Down, next line of the card" },
            new ScreenKey { Key = KeyCode.UpArrow, Run = () => StepLine(-1), When = Reads, Hint = "Up, previous line of the card" },
            new ScreenKey { Key = KeyCode.I, Run = SayKeywords, Hint = "I, what the card's keywords mean" },
            new ScreenKey { Key = KeyCode.K, Run = SayStats, Hint = "K, card kind and base power" },
        };

        /// <summary>The cursor moved: the next Ctrl+Up/Down starts from the new card's summary.</summary>
        public void Reset() => _lineCard = null;

        private bool Reads() => _always || _focused() != null;

        private void StepLine(int dir)
        {
            var card = _focused();
            if (card == null)
            {
                if (_noCard != null) _noCard(); else Speech.Say("No card.", true);
                return;
            }
            if (_lineCard == null || _lineCard.Id != card.Id) { _lineCard = card; _line = 0; }
            var lines = CardReader.Lines(card, _players());
            int next = _line + dir;
            // Past either end says so (Accessible Arena's "End of card"), instead of silence.
            if (next < 0) { Speech.Say("Beginning of card", true); return; }
            if (next >= lines.Count) { Speech.Say("End of card", true); return; }
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
                var lines = CardReader.Lines(card, _players());
                if (_line < lines.Count && lines[_line].Keywords.Count > 0) only = lines[_line].Keywords;
            }
            var explained = CardReader.Explanations(card, _players(), only);
            Speech.Say(explained.Count > 0 ? string.Join(" ", explained) : "No keywords.", true);
        }

        private void SayStats()
        {
            var card = _focused();
            if (card == null) return;
            Speech.Say(CardReader.Hidden(card, _players()) ? "hidden card" : CardReader.Stats(card), true);
        }
    }
}
