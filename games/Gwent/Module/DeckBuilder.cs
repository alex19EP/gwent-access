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

        private static CollectionDeck SelectedDeck()
        {
            try
            {
                var c = SelectedControlPatch.Current;
                if (c == null) return null;
                var b = c.TryCast<UIDeckButtonControl>() ?? c.GetComponentInParent<UIDeckButtonControl>();
                if (b != null) return b.Deck;
                // A deck's dropdown option: find the deck button next to it.
                var root = c.transform;
                for (int i = 0; i < 6 && root != null; i++, root = root.parent)
                {
                    var bb = root.GetComponentInChildren<UIDeckButtonControl>();
                    if (bb != null) return bb.Deck;
                }
            }
            catch { }
            return null;
        }


        /// <summary>True while the player has deliberately moved to the search box (Ctrl+F or F2/F3 onto it).</summary>
        public static bool SearchWanted;
        private static float _noSaveArmedUntil = -1;

        /// <summary>Ctrl+Q in the deck builder: save and leave the editor, or leave the deck builder.</summary>
        public static bool Quit()
        {
            try
            {
                if (Edit != null && Battle.Controller() == null)
                {
                    bool starter = false;
                    try { var d = Deck(); starter = d != null && d.IsStarterDeck; } catch { }
                    float now = Time.realtimeSinceStartup;
                    if (starter || now < _noSaveArmedUntil)
                    {
                        _noSaveArmedUntil = -1;
                        Speech.Say(starter ? "Leaving the starter deck." : "Leaving without saving.", true);
                        Edit.QuitWithoutSaving();
                        return true;
                    }
                    Speech.Say("Saving and leaving the deck.", true);
                    var me = Edit;
                    DeactivateSearch();
                    try { Edit.HandleQuitSaveRequest(); Plugin.LogInfo("Ctrl+Q: HandleQuitSaveRequest called"); } catch (Exception e) { Plugin.LogInfo("HandleQuitSaveRequest failed: " + e.Message); }
                    MainThread.Later(2.5f, () =>
                    {
                        if (Edit == null || Edit.Pointer != me.Pointer) return;
                        // Second route: the same path as choosing "Save and exit" in the game's own save popup.
                        try { Plugin.LogInfo("Ctrl+Q: trying save-popup accept path"); Edit.HandleCloseEditingPopup(PopupData.EResult.Accept); } catch (Exception e) { Plugin.LogInfo("Save-popup path failed: " + e.Message); }
                        MainThread.Later(3f, () =>
                        {
                            if (Edit != null && Edit.Pointer == me.Pointer)
                            {
                                _noSaveArmedUntil = Time.realtimeSinceStartup + 8f;
                                Speech.Say("Still in the deck editor. Another way to save: press F3 to the SHOP button in the top bar, F4, and the game asks to save; press Enter. Or press Control Q again to leave without saving.", false);
                            }
                        });
                    });
                    return true;
                }
                if (List != null && Battle.Controller() == null)
                {
                    Speech.Say("Leaving the deck builder.", true);
                    List.HandleQuitRequest();
                    return true;
                }
                if (Home != null && Battle.Controller() == null)
                {
                    Speech.Say("Leaving the deck builder.", true);
                    Home.HandleQuitRequest();
                    return true;
                }
            }
            catch (Exception e) { Plugin.LogInfo("Deck builder quit failed: " + e); }
            return false;
        }

        /// <summary>Takes the cursor out of the card search box so Escape and other keys reach the game again.</summary>
        public static void DeactivateSearch()
        {
            try
            {
                foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<InputFieldControl>()))
                {
                    var f = o.TryCast<InputFieldControl>();
                    if (f == null || f.gameObject.name != "TextSearch" || !f.gameObject.activeInHierarchy) continue;
                    f.DeactivateInputField();
                }
            }
            catch (Exception e) { Plugin.LogInfo("Deactivate search failed: " + e.Message); }
        }


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
