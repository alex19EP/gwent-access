using System;
using System.Collections.Generic;
using GwentGameplay;

namespace GwentAccess
{
    /// <summary>
    /// What the game is waiting on from the local player this moment, read from its open requests: the turn
    /// itself, a picked-up card to place, card targets, a row, a choice — with the valid options each one
    /// offers (what a sighted player sees glow). Read-only: the moves themselves go through the game's own
    /// input handlers (<see cref="Ui.BattleInput"/>), so its animations, sounds and warnings play as usual.
    ///
    /// The game's flow, as seen in practice: picking up a unit opens a play request with the rows it may go to;
    /// a special that hits a row opens a row request straight away (the play request waits underneath, already
    /// past placing); an order opens a card-target request.
    /// </summary>
    internal sealed class Plays
    {
        public GameController Game;
        public RequestPlayerTurnAction Turn;
        public RequestPlayCardAction Placing;   // picked up and not placed yet
        /// <summary>A special picked up: it has no spot to choose, so the game holds it up and waits for A to
        /// confirm the play (or B to put it back). Only counts with nothing else asked — the same pending play
        /// also waits underneath a special's row or target request.</summary>
        public RequestPlayCardAction Confirming;
        public RequestCardTargetsAction Targets;
        public RequestLocationTargetsAction Rows;
        public RequestCardChoicesAction Choice;

        /// <summary>The request answered next: a choice before targets before a row before placing.</summary>
        public ARequestAction Pending => (ARequestAction)Choice ?? (ARequestAction)Targets ?? (ARequestAction)Rows ?? (ARequestAction)Placing ?? Confirming;

        public bool MyTurn
        {
            get
            {
                try { return Turn != null && Game.PlayerManager.CurrentPlayerId == Game.PlayerManager.LocalUserId; }
                catch { return false; }
            }
        }

        public static Plays Read(GameController gc)
        {
            var p = new Plays { Game = gc };
            if (gc == null) return p;
            try
            {
                var me = gc.PlayerManager.LocalUserId;
                var reqs = gc.RequestManager.Requests;
                for (int i = 0; i < reqs.Count; i++)
                {
                    var r = reqs[i];
                    if (r == null || r.PlayerId != me) continue;
                    switch (r.GetRequestType())
                    {
                        case ERequestType.PlayerTurn:
                            p.Turn = r.TryCast<RequestPlayerTurnAction>();
                            break;
                        case ERequestType.PlayCard:
                            var play = r.TryCast<RequestPlayCardAction>();
                            if (play != null && play.PlayCardResult == EPlayCardResult.Invalid) p.Placing = play;
                            else if (play != null && play.PlayCardResult == EPlayCardResult.PendingPlace && Ui.BattleInput.CanConfirmSpecial()) p.Confirming = play;
                            break;
                        case ERequestType.TargetCards:
                            var cards = r.TryCast<RequestCardTargetsAction>();
                            if (cards != null && !cards.PlayerFinishedTargeting) p.Targets = cards;
                            break;
                        case ERequestType.TargetLocations:
                            var rows = r.TryCast<RequestLocationTargetsAction>();
                            if (rows != null && !rows.PlayerFinishedTargeting) p.Rows = rows;
                            break;
                        case ERequestType.Choice:
                            var choice = r.TryCast<RequestCardChoicesAction>();
                            if (choice != null && !choice.PlayerFinishedTargeting) p.Choice = choice;
                            break;
                    }
                }
            }
            catch (Exception e) { Plugin.LogInfo("[plays] read failed: " + e.Message); }
            return p;
        }

        private static bool Try(Func<bool> f)
        {
            try { return f(); } catch { return false; }
        }

        // ---- your turn ----

        public bool CanPlay(Card c) => Turn != null && Try(() => Turn.CanPlayCard(c));
        public bool CanOrder(Card c) => Turn != null && Try(() => Turn.CanTapCard(c));
        public bool CanUse(Card c) => CanPlay(c) || CanOrder(c) || Turn != null && Try(() => Turn.CanPayFor(c) || Turn.CanRevealAmbush(c));
        public bool CardPlayed => Turn != null && Try(() => Turn.WasCardPlayed);

        /// <summary>Why Enter on this card of yours does nothing, or null when it does something.</summary>
        public string WhyNot(Card c)
        {
            if (!MyTurn) return "Not your turn.";
            if (Pending != null) return "Finish the current play first, or Backspace to cancel.";
            if (CanUse(c)) return null;
            bool inHand = Try(() => c.Position.Location == ELocation.Hand);
            if (inHand) return CardPlayed ? "You've already played a card this turn." : "Can't play that now.";
            return Try(() => c.HasTapAbility()) ? "Order not ready." : "Nothing to use.";
        }

        /// <summary>Your cards with a play or an order available right now, hand first.</summary>
        public List<Card> Usable()
        {
            var list = new List<Card>();
            if (!MyTurn || Pending != null) return list;
            var me = Battle.Me(Game);
            foreach (var where in new[] { ELocation.Hand, ELocation.Melee, ELocation.Ranged, ELocation.Leader })
                foreach (var c in Battle.Cards(Game, me, where))
                    if (CanUse(c)) list.Add(c);
            return list;
        }

        /// <summary>An order still ready on your board or leader (E asks before ending the turn).</summary>
        public bool OrdersReady()
        {
            var me = Battle.Me(Game);
            foreach (var where in new[] { ELocation.Melee, ELocation.Ranged, ELocation.Leader })
                foreach (var c in Battle.Cards(Game, me, where))
                    if (CanOrder(c)) return true;
            return false;
        }

        // ---- the request being answered ----

        /// <summary>The rows the picked-up card may go to.</summary>
        public bool MayPlaceIn(Location l) => Placing != null && Contains(Placing.m_AvailableSpots, l);

        public bool IsTarget(Card c) => Targets != null && Try(() => Targets.ValidTargets.Contains(c.Id));
        public bool IsChosen(Card c) => Targets != null && Try(() => Targets.SelectedTargets.Contains(c.Id));
        public bool IsTargetRow(Location l) => Rows != null && Contains(Rows.ValidTargets, l);
        public bool IsChosenRow(Location l) => Rows != null && Contains(Rows.SelectedTargets, l);

        public static bool Same(Location a, Location b) => a != null && b != null && a.PlayerId == b.PlayerId && a.Loc == b.Loc;

        private static bool Contains(Il2CppSystem.Collections.Generic.List<Location> list, Location l)
        {
            try
            {
                for (int i = 0; i < list.Count; i++)
                    if (Same(list[i], l)) return true;
            }
            catch { }
            return false;
        }

        public ARequestTargetsAction TargetRequest => (ARequestTargetsAction)Targets ?? Rows;

        public int Chosen => TargetRequest is { } r ? SafeInt(() => r.GetNumSelectedTargets()) : 0;
        public int MinTargets => TargetRequest is { } r ? SafeInt(() => r.MinTargets) : 0;
        public int MaxTargets => TargetRequest is { } r ? SafeInt(() => r.MaxTargets) : 0;

        private static int SafeInt(Func<int> f)
        {
            try { return f(); } catch { return 0; }
        }

        /// <summary>The card a request is for: the one being placed, or the one whose ability asks.</summary>
        public Card Requester()
        {
            try
            {
                ushort id = Targets != null ? Targets.RequesterId
                    : Rows != null ? Rows.RequesterId
                    : Choice != null ? Choice.RequesterId
                    : Placing != null ? Placing.CardIdToPlay
                    : Confirming != null ? Confirming.CardIdToPlay : (ushort)0;
                return id == 0 ? null : Game.CardManager.GetCard(id);
            }
            catch { return null; }
        }
    }
}
