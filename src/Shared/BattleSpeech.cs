using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using GwentGameplay;
using GwentVisuals;
using GwentUnity;

namespace GwentAccess
{
    /// <summary>Reads the battle board: what you select, the score, rows, hand, and what the opponent does.</summary>
    internal static class Battle
    {
        private static BattleHandler _handler;

        public static GameController Controller()
        {
            try
            {
                if (_handler == null || _handler.Pointer == IntPtr.Zero || !_handler.gameObject.activeInHierarchy)
                {
                    _handler = null;
                    var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<BattleHandler>());
                    foreach (var o in found) { _handler = o.TryCast<BattleHandler>(); if (_handler != null) break; }
                }
                var gc = _handler?.GameController;
                if (gc == null || !gc.IsSet) return null;
                return gc;
            }
            catch { return null; }
        }

        public static EPlayerId Me(GameController gc) => gc.PlayerManager.LocalUserId;
        public static EPlayerId Them(GameController gc) => Me(gc) == EPlayerId.P1 ? EPlayerId.P2 : EPlayerId.P1;

        /// <summary>Number of cards in a hand, including the opponent's face-down ones.</summary>
        public static int HandCount(GameController gc, EPlayerId who)
        {
            try { var loc = gc.BoardManager.GetLocation(who, ELocation.Hand); if (loc != null) return loc.NumCards; } catch { }
            try { return gc.BoardManager.GetCardsInLocationCount(who, ELocation.Hand, (ECardType)0xFF, (ECardTier)0xFF, (ECardToken)0, (ECardToken)0, null, false); } catch { }
            return Cards(gc, who, ELocation.Hand).Count;
        }

        public static List<Card> Cards(GameController gc, EPlayerId who, ELocation where)
        {
            var result = new List<Card>();
            try
            {
                var list = gc.BoardManager.GetCardsInLocation(who, where, (ECardType)31, (ECardTier)15, (ECardToken)0, (ECardToken)0, null, false);
                if (list != null) for (int i = 0; i < list.Count; i++) result.Add(list[i]);
            }
            catch (Exception e) { Plugin.LogInfo("Cards(" + who + "," + where + ") failed: " + e.Message); }
            return result;
        }

        /// <summary>The leader character standing at a player's end of the board ("Эмгыр вар Эмрейс") — the
        /// cosmetic leader, not the leader ability card — in the game's words, or null before it has loaded.</summary>
        public static string LeaderCharacter(EPlayerId who)
        {
            try
            {
                var leader = UnityEngine.Object.FindObjectOfType<BoardView>()?.LeaderLoader?.GetLeader(who)?.m_LeaderSetupRequest?.ValidLeader;
                if (leader == null) return null;
                string name = GameText.Get(leader.LeaderNameLocalizationKey, leader.DebugName);
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch { return null; }
        }

        public static string WhereName(ELocation loc)
        {
            switch (loc)
            {
                case ELocation.Melee: return Loc.T("melee row");
                case ELocation.Ranged: return Loc.T("ranged row");
                case ELocation.Hand: return Loc.T("hand");
                case ELocation.Deck: return Loc.T("deck");
                case ELocation.Graveyard: return Loc.T("graveyard");
                case ELocation.Leader: return Loc.T("leader");
                default: return loc.ToString();
            }
        }

        /// <summary>"Your melee row" / "Opponent's ranged row" etc. for a card's position.</summary>
        public static string Place(GameController gc, CardPosition pos)
        {
            string where = WhereName(pos.Location);
            return pos.PlayerId == Me(gc) ? Loc.T("your {0}", where) : Loc.T("opponent's {0}", where);
        }

        public static string StatusText(GameController gc)
        {
            var pm = gc.PlayerManager;
            var me = pm.LocalUser;
            var opp = me.OpponentPlayer;
            var parts = new List<string>();
            try { parts.Add(Loc.T("Round {0}", gc.RoundManager.CurrentRoundIndex + 1)); } catch { }
            try { parts.Add(Loc.T("Score: you {0}, opponent {1}", me.CurrentScore, opp.CurrentScore)); } catch { }
            try { parts.Add(Loc.T("Rounds won: you {0}, opponent {1}", me.Crowns, opp.Crowns)); } catch { }
            try { parts.Add(pm.CurrentPlayerId == Me(gc) ? Loc.T("Your turn") : Loc.T("Opponent's turn")); } catch { }
            try { if (me.HasPassed) parts.Add(Loc.T("You have passed")); } catch { }
            try { if (opp.HasPassed) parts.Add(Loc.T("Opponent has passed")); } catch { }
            try
            {
                var mull = GameObject.Find("UIMulliganPrefab(Clone)");
                if (mull != null && mull.activeInHierarchy) parts.Add(Loc.T("Swaps left: {0}", me.MulligansLeft));
            }
            catch { }
            try { parts.Add(Loc.T("Cards in hand: you {0}, opponent {1}", HandCount(gc, Me(gc)), HandCount(gc, Them(gc)))); } catch { }
            // The counters on both players' deck and graveyard stacks beside the board.
            try { parts.Add(Loc.T("Deck: you {0}, opponent {1}", PileCount(gc, Me(gc), ELocation.Deck), PileCount(gc, Them(gc), ELocation.Deck))); } catch { }
            try { parts.Add(Loc.T("Graveyard: you {0}, opponent {1}", PileCount(gc, Me(gc), ELocation.Graveyard), PileCount(gc, Them(gc), ELocation.Graveyard))); } catch { }
            return string.Join(". ", parts) + ".";
        }

        /// <summary>Cards in a player's deck or graveyard: the number on the stack (the opponent's deck counts
        /// its face-down cards too).</summary>
        public static int PileCount(GameController gc, EPlayerId who, ELocation where)
        {
            try { var loc = gc.BoardManager.GetLocation(who, where); if (loc != null) return loc.NumCards; } catch { }
            return Cards(gc, who, where).Count;
        }
    }
}
