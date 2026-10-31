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

        /// <summary>Ctrl+U on the deck list: duplicate the selected deck.</summary>
        public static void Duplicate()
        {
            if (List == null) { Speech.Say("Go to your deck list first, and select a deck.", true); return; }
            var d = SelectedDeck();
            if (d == null) { Speech.Say("Select a deck first.", true); return; }
            string name = ""; try { name = UiText.Clean(d.LocalizedName ?? d.Name); } catch { }
            try { List.HandleDuplicateDeckRequest(d); Speech.Say("Duplicating " + name + ". The copy appears in your deck list.", true); }
            catch (Exception e) { Plugin.LogInfo("Duplicate failed: " + e); Speech.Say("Couldn't duplicate that deck.", true); }
        }

        /// <summary>Empties the text box of a rename popup so typing replaces the old name.</summary>
        private static void ClearRenameBox()
        {
            MainThread.Later(0.6f, () =>
            {
                try
                {
                    foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<InputFieldControl>()))
                    {
                        var f = o.TryCast<InputFieldControl>();
                        if (f == null || !f.gameObject.activeInHierarchy) continue;
                        if (f.GetComponentInParent<PopupControl>() == null) continue;
                        f.Text = "";
                        Plugin.LogInfo("Cleared rename box");
                    }
                }
                catch (Exception e) { Plugin.LogInfo("Clear rename box failed: " + e.Message); }
            });
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

        /// <summary>Ctrl+N: rename the deck being edited.</summary>
        public static void Rename()
        {
            if (Edit == null && List != null)
            {
                var d = SelectedDeck();
                if (d == null) { Speech.Say("Select a deck first.", true); return; }
                try { List.HandleDeckRenameRequest(d); ClearRenameBox(); Speech.Say("Rename deck. Type the new name, then press Enter. Letters, numbers, spaces, dashes and underscores only.", true); }
                catch (Exception e) { Plugin.LogInfo("Rename failed: " + e); Speech.Say("Couldn't open rename.", true); }
                return;
            }
            if (Edit == null) { Speech.Say("Open a deck first.", true); return; }
            try { SearchWanted = false; Edit.HandleDeckRenameRequest(); ClearRenameBox(); Speech.Say("Rename deck. Type the new name, then press Enter. Letters, numbers, spaces, dashes and underscores only.", true); }
            catch (Exception e) { Plugin.LogInfo("Rename failed: " + e); Speech.Say("Couldn't open rename.", true); }
        }

        /// <summary>Ctrl+F: jump to the card search box.</summary>
        public static void Search()
        {
            try
            {
                foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<AControl>()))
                {
                    var c = o.TryCast<AControl>();
                    if (c == null || c.gameObject.name != "TextSearch" || !c.gameObject.activeInHierarchy) continue;
                    SearchWanted = true;
                    c.ParentLayer?.RequestControlSelection(c);
                    Speech.Say("Search box. Type part of a card name. F3 or Control Q to leave it.", true);
                    return;
                }
            }
            catch (Exception e) { Plugin.LogInfo("Search focus failed: " + e.Message); }
            Speech.Say("No search box here.", true);
        }
        public static float EnteredAt = -100;
        private static float _quietUntil = -1;

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

        public static void SayStats()
        {
            var d = Deck();
            if (d == null) { Speech.Say("Open a deck in the deck builder first.", true); return; }
            string name = "";
            try { name = UiText.Clean(d.LocalizedName ?? d.Name ?? ""); } catch { }
            string valid = "";
            try { var v = d.GetValidation(); if (v != null) valid = v.IsDeckValid ? " Deck is valid." : " Deck is not valid yet."; } catch { }
            Speech.Say((name.Length > 0 ? name + ". " : "") + Stats() + "." + valid, true);
        }

        public static void SayList()
        {
            var d = Deck();
            if (d == null) { Speech.Say("Open a deck in the deck builder first.", true); return; }
            var lines = new List<string>();
            try
            {
                var leader = d.LeaderAbilityCard;
                if (leader != null) lines.Add("Leader: " + Name(leader));
                var cards = d.GetUniqueCardList(false, false);
                foreach (var cc in cards)
                {
                    int n = 1; try { n = d.GetCardCount(cc); } catch { }
                    int prov = 0; try { prov = cc.Template.Provision; } catch { }
                    lines.Add(Name(cc) + (n > 1 ? " times " + n : "") + (prov > 0 ? ", " + prov : ""));
                }
            }
            catch (Exception e) { Plugin.LogInfo("Deck list failed: " + e); }
            Speech.Say(lines.Count + " entries. " + string.Join(". ", lines) + ". " + Stats(), true);
        }

        public static bool TypingInBox()
        {
            try { var c = SelectedControlPatch.Current; return c != null && c.TryCast<InputFieldControl>() != null; } catch { return false; }
        }

        public static void Add()
        {
            if (TypingInBox()) return;
            var d = Deck();
            if (d == null) { Speech.Say("Open a deck in the deck builder first.", true); return; }
            var cc = CollectionCardOf(SelectedControlPatch.Current, out bool inList);
            if (cc == null) { Speech.Say("Select a card from your collection first.", true); return; }
            try
            {
                if (!d.CanAddCardToDeck(cc, out string why))
                {
                    Speech.Say("Can't add " + Name(cc) + ". " + Reason(why), true);
                    return;
                }
            }
            catch { }
            try { Edit.AddCardToDeck(cc); Plugin.LogInfo("Deck add via state: " + Name(cc)); }
            catch (Exception e) { Plugin.LogInfo("State add failed (" + e.Message + "), adding directly"); try { d.AddCard(cc, true); } catch (Exception e2) { Plugin.LogInfo("Add failed: " + e2); Speech.Say("Couldn't add that card.", true); } }
        }

        private static string Reason(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            string text = null;
            try { text = UiText.Clean(Localization.LocalizationHelper.GetLocalizedMarkupString(key, null)); } catch { }
            if (!string.IsNullOrEmpty(text) && text != key && !text.Contains("MISSING")) return text;
            if (key.Contains("owned_limit")) return "You don't own any more copies of this card.";
            if (key.Contains("copy") || key.Contains("copies")) return "That's the most copies allowed in a deck.";
            if (key.Contains("provision")) return "Not enough provisions left.";
            if (key.Contains("faction")) return "It's from a different faction.";
            return key.Replace("popup_", "").Replace("_", " ");
        }

        public static void Remove()
        {
            var d = Deck();
            if (d == null) { Speech.Say("Open a deck in the deck builder first.", true); return; }
            var cc = CollectionCardOf(SelectedControlPatch.Current, out bool inList);
            if (cc == null) { Speech.Say("Select a card in your deck or collection first.", true); return; }
            try
            {
                if (d.StratagemCard != null && d.StratagemCard.Equals(cc)) { Speech.Say("That's your stratagem. It can't be removed, but you can press Enter on it to swap it for another.", true); return; }
                if (d.LeaderAbilityCard != null && d.LeaderAbilityCard.Equals(cc)) { Speech.Say("That's your leader ability. Use Change Ability to pick a different one.", true); return; }
            }
            catch { }
            bool has = false;
            try { has = d.HasCard(cc); } catch { }
            if (!has) { Speech.Say(Name(cc) + " isn't in this deck.", true); return; }
            try { Edit.RemoveCardFromDeck(cc); Plugin.LogInfo("Deck remove via state: " + Name(cc)); }
            catch (Exception e) { Plugin.LogInfo("State remove failed (" + e.Message + "), removing directly"); try { d.RemoveCard(cc, false, true); } catch (Exception e2) { Plugin.LogInfo("Remove failed: " + e2); Speech.Say("Couldn't remove that card.", true); } }
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
            MainThread.Later(1.5f, () => Speech.Say("Editing deck. " + DeckBuilder.Stats() + ". Control A adds the selected card, Control R removes it, Control D deck stats, Control H lists the deck. Shift F3 jumps between your deck and the collection. Control N renames, Control F searches, Control Q saves and exits.", false));
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
