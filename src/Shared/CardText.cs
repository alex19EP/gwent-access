using System;
using System.Collections.Generic;
using UnityEngine;
using CrimsonUI;
using GwentGameplay;
using Localization;

namespace GwentAccess
{
    /// <summary>The card behind a UI control, and a one-line form of a card for a control the dedicated screens
    /// don't read (<see cref="UiText.DescribeSelection"/>). <see cref="CardReader"/> is the full reader.</summary>
    internal static class CardText
    {
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
        public static string Describe(Card card)
        {
            if (card == null) return "";
            var parts = new List<string>();
            string name = Safe(() => LocalizationHelper.GetCardName(card));
            int templateId = -1;
            try { templateId = card.Template.Id; } catch { }
            if (name.StartsWith("[MISSING]") || templateId == 0)
                return Loc.T("Hidden card. You can't see this card yet.");
            parts.Add(name.Length > 0 ? name : Loc.T("Unknown card"));

            bool leader = false, special = false;
            try { leader = card.IsLeader; } catch { }
            try { special = card.IsSpecial; } catch { }
            if (leader) parts.Add(Loc.T("leader"));
            else if (special) parts.Add(Loc.T("special card"));
            else
            {
                try { parts.Add(Loc.T("power {0}", card.Power)); } catch { }
            }
            try { if (card.Armor > 0) parts.Add(Loc.T("armor {0}", card.Armor)); } catch { }
            try
            {
                var t = card.Template;
                if (t != null && t.Provision > 0) parts.Add(Loc.P(t.Provision, "{0} provision", "{0} provisions"));
            }
            catch { }

            string text = string.Join(", ", parts) + ".";
            string desc = Safe(() => LocalizationHelper.GetTooltipDescription(card));
            if (desc.Length > 0) text += " " + desc;

            return text;
        }
    }
}
