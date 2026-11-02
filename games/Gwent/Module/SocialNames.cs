using System;
using System.Collections.Generic;
using HarmonyLib;
using GwentUnity;

namespace GwentAccess
{
    /// <summary>Remembers each social button's icon so we can name it (friends, chat, ...).</summary>
    [HarmonyPatch(typeof(SocialButton), "SetIcons")]
    internal static class SocialIconsPatch
    {
        public static readonly Dictionary<IntPtr, string> Names = new Dictionary<IntPtr, string>();

        static void Postfix(SocialButton __instance, string activeIcon)
        {
            try
            {
                string n = (activeIcon ?? "").ToLowerInvariant();
                Plugin.LogInfo("Social button icon: " + activeIcon);
                string name;
                if (n.Contains("friend")) name = "Friends";
                else if (n.Contains("chat") || n.Contains("message")) name = "Chat";
                else if (n.Contains("history")) name = "Match history";
                else if (n.Contains("invite")) name = "Invites";
                else if (n.Contains("notif")) name = "Notifications";
                else if (n.Contains("setting")) name = "Social settings";
                else if (n.Contains("quest")) name = "Quests";
                else if (n.Contains("journey")) name = "Journey";
                else if (n.Contains("ouroboros")) name = "Season cycle";
                else name = UiText.Prettify(activeIcon.Replace("icon_", "").Replace("social_", "").Replace("_", " "));
                Names[__instance.Pointer] = name;
            }
            catch { }
        }
    }
}
