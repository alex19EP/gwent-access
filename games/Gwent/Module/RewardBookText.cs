using System;
using System.Collections.Generic;
using CrimsonUI;
using GwentVisuals;
using Modules.RewardBook.Entities;

namespace GwentAccess
{
    /// <summary>Names Reward Book tree nodes, including the tutorial battle nodes.</summary>
    internal static class RewardBookText
    {
        public static string Describe(AControl c)
        {
            var node = c.TryCast<RewardSystemTreeElement>();
            if (node == null) { try { node = c.GetComponentInParent<RewardSystemTreeElement>(); } catch { } }
            if (node == null) return null;
            var parts = new List<string>();
            try
            {
                var d = node.ElementData;
                string type = d.NodeType.ToString();
                switch (type)
                {
                    case "Combat": type = "Tutorial battle"; break;
                    case "Story": type = "Story"; break;
                    case "Quest": type = "Quest"; break;
                    case "Mosaic": type = "Mosaic reward"; break;
                    case "Large": type = "Large reward"; break;
                    case "Small": type = "Small reward"; break;
                    case "Starting": type = "Starting node"; break;
                    case "Exchange": type = "Exchange"; break;
                }
                parts.Add(type);
                try { parts.Add(d.Claimed ? "claimed" : (d.Claimable ? "can claim" : "locked")); } catch { }
            }
            catch (Exception e) { Plugin.LogInfo("Reward node read failed: " + e.Message); }
            try
            {
                string cost = UiText.Clean(node.m_CostText?.text);
                if (cost.Length > 0) parts.Add("cost " + cost + " reward points");
            }
            catch { }
            try
            {
                var cards = node.RewardCards;
                if (cards != null)
                    for (int i = 0; i < cards.Count; i++)
                    {
                        string n = UiText.Clean(Localization.LocalizationHelper.GetCardName(cards[i]));
                        if (n.Length > 0) parts.Add("reward: " + n);
                    }
            }
            catch { }
            string text = UiText.Describe(node, 4);
            if (text.Length > 0 && !text.StartsWith("Button") && !text.Contains("Control")) parts.Add(text);
            return string.Join(", ", parts);
        }
    }
}
