using System;
using System.Collections.Generic;
using UnityEngine;
using CrimsonUI;
using GwentGameplay;
using Localization;

namespace GwentAccess
{
    /// <summary>Turns a Gwent card into speech: name, power, armor, provisions, and what it does.</summary>
    internal static class CardText
    {
        public static Card LastCard;

        /// <summary>Find the card behind a UI control, if it shows one.</summary>
        public static Card CardOf(Component c)
        {
            try
            {
                var dc = c.TryCast<GwentVisuals.UIDeckCardButtonControl>() ?? c.GetComponentInParent<GwentVisuals.UIDeckCardButtonControl>();
                if (dc != null) { try { return dc.Card?.Card; } catch { } }
                var cbc = c.TryCast<CardButtonControl>();
                if (cbc == null) cbc = c.GetComponentInParent<CardButtonControl>();
                if (cbc != null)
                {
                    Card card = null;
                    try { card = cbc.Card; } catch { }
                    if (card == null) { try { card = cbc.CollectionCard?.Card; } catch { } }
                    return card;
                }
            }
            catch { }
            return null;
        }

        private static string Safe(Func<string> f)
        {
            try { return UiText.Clean(f()); } catch { return ""; }
        }

        /// <summary>Short form for moving around: "Name, power 5, armor 2, 7 provisions. What it does."</summary>
        public static string Describe(Card card, bool full = false)
        {
            if (card == null) return "";
            LastCard = card;
            var parts = new List<string>();
            string name = Safe(() => LocalizationHelper.GetCardName(card));
            int templateId = -1;
            try { templateId = card.Template.Id; } catch { }
            if (name.StartsWith("[MISSING]") || templateId == 0)
                return "Hidden card. You can't see this card yet.";
            parts.Add(name.Length > 0 ? name : "Unknown card");

            bool leader = false, special = false;
            try { leader = card.IsLeader; } catch { }
            try { special = card.IsSpecial; } catch { }
            if (leader) parts.Add("leader");
            else if (special) parts.Add("special card");
            else
            {
                try { parts.Add("power " + card.Power); } catch { }
            }
            try { if (card.Armor > 0) parts.Add("armor " + card.Armor); } catch { }
            try
            {
                var t = card.Template;
                if (t != null && t.Provision > 0) parts.Add(t.Provision + " provisions");
            }
            catch { }

            string text = string.Join(", ", parts) + ".";
            string desc = Safe(() => LocalizationHelper.GetTooltipDescription(card));
            if (desc.Length > 0) text += " " + desc;

            if (full)
            {
                string cats = Safe(() => LocalizationHelper.LocalizeCategories(card));
                if (cats.Length > 0) text += " Categories: " + cats + ".";
                string faction = "";
                try { faction = UiText.Clean(LocalizationHelper.GetFactionName(card.FactionId)); } catch { }
                if (faction.Length > 0) text += " Faction: " + faction + ".";
                try
                {
                    var t = card.Template;
                    if (t != null) text += " Rarity: " + t.Rarity + ".";
                }
                catch { }
                string kw = Safe(() => LocalizationHelper.GetKeywordsDescription(card));
                if (kw.Length > 0) text += " Keywords: " + kw;
                string flavor = Safe(() => LocalizationHelper.GetCardFlavor(card));
                if (flavor.Length > 0 && !flavor.StartsWith("[MISSING]")) text += " Flavor: " + flavor;
            }
            return text;
        }

        /// <summary>F1: full details of the last card you landed on.</summary>
        public static void ReadDetails()
        {
            var c = LastCard;
            if (c == null)
            {
                var sel = SelectedControlPatch.Current;
                if (sel != null) c = CardOf(sel);
            }
            if (c == null) { Speech.Say("No card selected.", true); return; }
            string t = Describe(c, true);
            Plugin.LogInfo("Card details: " + t);
            Speech.Say(t, true);
        }
    }
}
