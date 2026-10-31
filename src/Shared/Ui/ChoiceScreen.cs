using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentGameplay;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// A card choice (create, pick from the deck, discard…): the offered cards as one row, read like on the
    /// board (Ctrl+Up/Down, I, K, Space). Enter picks the focused card — or toggles it when several may be
    /// picked — E finishes a multi-pick, Backspace cancels when the game allows it. Every press goes through
    /// the game's own choice panel handler, as its buttons and card clicks do.
    /// </summary>
    internal sealed class ChoiceScreen : Screen
    {
        public override string Key => "choice";
        public override int Layer => 1;

        private GameController _gc;
        private RequestCardChoicesAction _req;

        public override bool IsActive()
        {
            _gc = Battle.Controller();
            if (_gc == null) return false;
            _req = Plays.Read(_gc).Choice;
            return _req != null;
        }

        public override string ScreenName
        {
            get
            {
                if (_req == null) return "Choose a card";
                string what = Title(_req.ChoiceType);
                Card requester = null;
                try { requester = _gc.CardManager.GetCard(_req.RequesterId); } catch { }
                if (requester != null) what += ", for " + CardReader.Name(requester, _gc.PlayerManager);
                int max = 1;
                try { max = _req.MaxChoices; } catch { }
                return what + (max > 1 ? ". Pick up to " + max + ", E when done." : ".");
            }
        }

        private static string Title(ERequestChoiceType t) => t switch
        {
            ERequestChoiceType.CardToPlay => "Choose a card to play",
            ERequestChoiceType.CardToMove => "Choose a card to move",
            ERequestChoiceType.CardToDiscard => "Choose a card to discard",
            ERequestChoiceType.CardToPutBackInDeck => "Choose a card to put back in your deck",
            ERequestChoiceType.CardToDraw => "Choose a card to draw",
            ERequestChoiceType.CardToSpawn => "Choose a card to spawn",
            ERequestChoiceType.CardToCopy => "Choose a card to copy",
            ERequestChoiceType.CardToReveal => "Choose a card to reveal",
            _ => "Choose a card",
        };

        private static SelectChoicesHandlerComponent Handler()
        {
            try { return UnityEngine.Object.FindObjectOfType<SelectChoicesHandlerComponent>(); } catch { return null; }
        }

        private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();

        public override void Build(GraphBuilder b)
        {
            var req = _req;
            if (req == null || _gc == null) return;
            // The offered cards are created for the choice and sit nowhere the visibility rules cover (the game
            // reports them hidden from you), yet the choice panel shows them face up: read them without the
            // match's visibility check.
            PlayerManager pm = null;
            _cards.Clear();
            var offered = new List<(Card Card, bool Enabled)>();
            try
            {
                for (int i = 0; i < req.ValidChoices.Count; i++) offered.Add((req.ValidChoices[i], true));
                for (int i = 0; i < req.DisabledChoices.Count; i++) offered.Add((req.DisabledChoices[i], false));
            }
            catch (Exception e) { Plugin.LogInfo("[choice] read failed: " + e.Message); }
            if (offered.Count == 0) return;

            b.BeginStop("choices");
            b.PushContext("Choices");
            b.StartRow();
            foreach (var (card, enabled) in offered)
            {
                var id = ControlId.Structural("choice:card:" + card.Id);
                _cards[(string)id.StructuralKey] = card;
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => CardReader.WithAbilities(card, pm), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => enabled ? (Chosen(card) ? "chosen" : null) : "unavailable", live: true, kind: AnnouncementKinds.Selected),
                    },
                    OnActivate = () => Pick(card, enabled),
                    OnTooltip = () => Speech.Say(CardReader.Whole(card, pm), true),
                });
            }
            b.EndRow();
            b.PopContext();
        }

        private bool Chosen(Card card)
        {
            try { return _req.SelectedChoices.Contains(card.Id); } catch { return false; }
        }

        private void Pick(Card card, bool enabled)
        {
            if (!enabled) { Speech.Say("Unavailable.", true); return; }
            var h = Handler();
            if (h == null) { Speech.Say("Couldn't pick.", true); return; }
            try
            {
                int max = _req.MaxChoices;
                if (max > 1) h.ToggleChoice(card);
                else h.SelectChoice(card);
                Plugin.LogInfo("[choice] pick " + card.Id);
            }
            catch (Exception e) { Plugin.LogInfo("[choice] pick failed: " + e); Speech.Say("Couldn't pick.", true); }
        }

        private void Finish()
        {
            var h = Handler();
            if (h == null) return;
            try { h.HandleFinishBtnClicked(); Plugin.LogInfo("[choice] finish"); }
            catch (Exception e) { Plugin.LogInfo("[choice] finish failed: " + e); Speech.Say("Couldn't finish.", true); }
        }

        private void Cancel()
        {
            bool can = false;
            try { can = _req.CanCancel; } catch { }
            if (!can) { Speech.Say("This can't be cancelled.", true); return; }
            var h = Handler();
            try { h?.HandleCancelBtnClicked(); Speech.Say("Cancelled.", true); }
            catch (Exception e) { Plugin.LogInfo("[choice] cancel failed: " + e); }
        }

        // ---- keys ----

        public override AControl GameSelection() => null;
        public override void OnCursorMoved(GraphNode node) => _cardKeys?.Reset();

        private CardKeys _cardKeys;
        private List<ScreenKey> _keys;

        public override IReadOnlyList<ScreenKey> ExtraKeys
        {
            get
            {
                if (_keys != null) return _keys;
                _cardKeys = new CardKeys(FocusedCard, () => null); // face up, as in Build
                _keys = new List<ScreenKey>(_cardKeys.Keys)
                {
                    new ScreenKey { Key = KeyCode.E, Run = Finish, Hint = "E, done choosing" },
                    new ScreenKey { Key = KeyCode.Backspace, Run = Cancel, Hint = "Backspace, cancel" },
                    new ScreenKey { Key = KeyCode.A, Run = () => { if (_gc != null) Speech.Say(Battle.StatusText(_gc), true); }, Hint = "A, score" },
                };
                return _keys;
            }
        }

        private Card FocusedCard()
        {
            var node = GraphNavigator.CurrentNode;
            return node?.Id.StructuralKey is string key && _cards.TryGetValue(key, out var card) ? card : null;
        }
    }
}
