using System;
using System.Collections.Generic;
using GwentUnity.UI.PlayerProfile;
using Modules.Rewards;
using Modules.Rewards.Entities;

namespace GwentAccess.Ui
{
    /// <summary>
    /// A reward in the game's own words (<see cref="RewardTextHelper"/>: "40 Ore", "Soldier Haircut Avatar"), the
    /// way its reward popup and tooltips name it. The helper throws on a random card (its rarity is unset) and
    /// gives nothing for a picture-only reward; those fall back to the game's item description, else null.
    /// </summary>
    internal static class RewardText
    {
        public static string Of(IReward reward)
        {
            if (reward == null) return null;
            try
            {
                string s = UiText.Clean(RewardTextHelper.GetText(reward));
                if (s.Length > 0) return s;
            }
            catch { }
            try
            {
                if (reward.Type == ERewardType.RandomCardReward)
                    return GameText.Get("item_description_card_standard", "Adds a random card from any set to your collection");
            }
            catch { }
            return null;
        }

        /// <summary>Every reward of a list, each named once, joined with commas; null when none has a name.</summary>
        public static string Of(Il2CppSystem.Collections.Generic.List<IReward> rewards)
        {
            var parts = new List<string>();
            for (int i = 0; rewards != null && i < rewards.Count; i++)
            {
                string s = Of(rewards[i]);
                if (!string.IsNullOrEmpty(s) && !parts.Contains(s)) parts.Add(s);
            }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }
    }
}
