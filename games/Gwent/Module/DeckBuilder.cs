using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using CrimsonUI;
using GwentGameplay;
using GwentVisuals;

namespace GwentAccess
{
    /// <summary>
    /// Deck building: Ctrl+A adds the selected card, Ctrl+R removes it, Ctrl+D reads deck stats,
    /// Ctrl+H reads the whole deck list. Adds and removes are announced however they happen.
    /// </summary>
    internal static class DeckBuilder
    {
        public static DBEditDeckState Edit;
        public static DBCollectionState Home;
        public static DBEditCollectionState List;







        public static float EnteredAt = -100;

        public static bool Active
        {
            get { try { return Edit != null && Battle.Controller() == null; } catch { return false; } }
        }

        public static CollectionDeck Deck()
        {
            try { return Edit?.GetDeck(); } catch { return null; }
        }

        /// <summary>The collection card behind a collection-grid card or a deck-list row.</summary>
        public static CollectionCard CollectionCardOf(Component c, out bool inDeckList)
        {
            inDeckList = false;
            if (c == null) return null;
            try
            {
                var dc = c.TryCast<UIDeckCardButtonControl>() ?? c.GetComponentInParent<UIDeckCardButtonControl>();
                if (dc != null) { inDeckList = true; return dc.Card; }
                var cbc = c.TryCast<CardButtonControl>() ?? c.GetComponentInParent<CardButtonControl>();
                if (cbc != null) return cbc.CollectionCard;
            }
            catch { }
            return null;
        }

        private static string Name(CollectionCard cc)
        {
            try { return UiText.Clean(Localization.LocalizationHelper.GetCardName(cc.Card)); } catch { return "Card"; }
        }

        public static string Stats()
        {
            var d = Deck();
            if (d == null) return "";
            try
            {
                var v = d.GetValidation();
                int maxProv = 150;
                try { var l = d.LeaderAbilityCard; if (l != null) maxProv += l.Template.Provision; } catch { }
                return v.TotalCardCount + " of 25 cards, " + v.UnitCardCount + " of 13 units, provisions " + v.ProvisionUsed + " of " + maxProv + " used";
            }
            catch (Exception e) { Plugin.LogInfo("Deck stats failed: " + e.Message); }
            try { return d.GetTotalCardCount(false, false) + " cards"; } catch { return ""; }
        }







        public static void Announce(string verb, CollectionCard cc)
        {
            if (!Active || Time.realtimeSinceStartup - EnteredAt < 2f) return;
            var d = Deck();
            int n = 0; try { n = d.GetCardCount(cc); } catch { }
            string name = Name(cc);
            // Stats labels refresh a frame later.
            MainThread.Later(0.3f, () => Speech.Say(verb + " " + name + (n > 0 ? ", " + n + " in deck" : "") + ". " + Stats(), true));
        }
    }

    [HarmonyPatch(typeof(DBEditDeckState), "OnEnterState")]
    internal static class DBEditEnterPatch
    {
        static void Postfix(DBEditDeckState __instance)
        {
            DeckBuilder.Edit = __instance; DeckBuilder.EnteredAt = Time.realtimeSinceStartup;
            Plugin.LogInfo("Deck edit entered");
        }
    }

    [HarmonyPatch(typeof(DBEditCollectionState), "OnEnterState")]
    internal static class DBListEnterPatch { static void Postfix(DBEditCollectionState __instance) { DeckBuilder.List = __instance; Plugin.LogInfo("Deck list (edit collection) entered"); } }

    [HarmonyPatch(typeof(ADBEditState), "OnLeaveState")]
    internal static class DBListLeavePatch { static void Postfix(ADBEditState __instance) { if (DeckBuilder.List != null && DeckBuilder.List.Pointer == __instance.Pointer) { DeckBuilder.List = null; Plugin.LogInfo("Deck list (edit collection) left"); } } }

    [HarmonyPatch(typeof(DBCollectionState), "OnEnterState")]
    internal static class DBHomeEnterPatch { static void Postfix(DBCollectionState __instance) { DeckBuilder.Home = __instance; Plugin.LogInfo("Deck list entered"); } }

    [HarmonyPatch(typeof(DBCollectionState), "OnLeaveState")]
    internal static class DBHomeLeavePatch { static void Postfix(DBCollectionState __instance) { if (DeckBuilder.Home != null && DeckBuilder.Home.Pointer == __instance.Pointer) DeckBuilder.Home = null; Plugin.LogInfo("Deck list left"); } }

    [HarmonyPatch(typeof(DBEditDeckState), "HandleCloseEditingPopup")]
    internal static class DBSavePopupLog { static void Prefix(PopupData.EResult result) { Plugin.LogInfo("Save popup result: " + result); } }

    [HarmonyPatch(typeof(DBEditDeckState), "OnLeaveState")]
    internal static class DBEditLeavePatch
    {
        static void Postfix(DBEditDeckState __instance) { if (DeckBuilder.Edit != null && DeckBuilder.Edit.Pointer == __instance.Pointer) DeckBuilder.Edit = null; Plugin.LogInfo("Deck edit left"); }
    }

    [HarmonyPatch(typeof(CollectionDeck), nameof(CollectionDeck.AddCard), new Type[] { typeof(CollectionCard), typeof(bool) })]
    internal static class DeckAddPatch
    {
        static void Postfix(CollectionCard card, bool __result) { try { if (__result) DeckBuilder.Announce("Added", card); } catch { } }
    }

    [HarmonyPatch(typeof(CollectionDeck), nameof(CollectionDeck.RemoveCard), new Type[] { typeof(CollectionCard), typeof(bool), typeof(bool) })]
    internal static class DeckRemovePatch
    {
        static void Postfix(CollectionCard card, bool __result) { try { if (__result) DeckBuilder.Announce("Removed", card); } catch { } }
    }
}
