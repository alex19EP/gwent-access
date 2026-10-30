using System;
using System.Collections.Generic;
using GwentGameplay;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The card-reading keys every screen showing cards shares: Ctrl+Up/Down step through the focused card's
    /// lines (the summary spoken on landing is line 0; silent at either end), I explains its keywords — just the
    /// current line's once Ctrl+Up/Down is on an ability that has any — and K reads its kind and base power.
    /// </summary>
    internal sealed class CardKeys
    {
        private readonly Func<Card> _focused;
        private readonly Func<PlayerManager> _players;
        private Card _lineCard;
        private int _line;

        public CardKeys(Func<Card> focused, Func<PlayerManager> players)
        {
            _focused = focused;
            _players = players;
        }

        public IEnumerable<ScreenKey> Keys => new[]
        {
            new ScreenKey { Key = KeyCode.DownArrow, Ctrl = true, Run = () => StepLine(1), Hint = "Control Down, next line of the card" },
            new ScreenKey { Key = KeyCode.UpArrow, Ctrl = true, Run = () => StepLine(-1), Hint = "Control Up, previous line of the card" },
            new ScreenKey { Key = KeyCode.I, Run = SayKeywords, Hint = "I, what the card's keywords mean" },
            new ScreenKey { Key = KeyCode.K, Run = SayStats, Hint = "K, card kind and base power" },
        };

        /// <summary>The cursor moved: the next Ctrl+Up/Down starts from the new card's summary.</summary>
        public void Reset() => _lineCard = null;

        private void StepLine(int dir)
        {
            var card = _focused();
            if (card == null) return;
            if (_lineCard == null || _lineCard.Id != card.Id) { _lineCard = card; _line = 0; }
            var lines = CardReader.Lines(card, _players());
            int next = _line + dir;
            if (next < 0 || next >= lines.Count) return;
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
