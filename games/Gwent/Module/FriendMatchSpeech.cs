using HarmonyLib;
using GwentUnity;
using GwentUnity.PlayWithFriend;

namespace GwentAccess
{
    /// <summary>
    /// Speaks the friend-match notifications as they slide in: the outgoing invite ("Ждем ответа игрока,
    /// получившего приглашение"), and the game's waiting, warning and error notices around a friend match
    /// ("Приглашение уже недействительно", "Дружеский матч был отменен."). A sighted player sees each pop up in
    /// the corner, wherever they are in the menus. The incoming invite is not spoken here: it takes the
    /// keyboard as <see cref="Ui.InviteScreen"/>, whose name is its message.
    ///
    /// The game builds these through a few helpers that all end in the notification panel's
    /// ShowSlideNotification; a depth counter set around the helpers tells their notifications from the
    /// panel's other traffic (reward reminders and the like, which stay as they are).
    /// </summary>
    internal static class FriendMatchSpeech
    {
        internal static int Depth;

        internal static void Speak(string message, bool isKey)
        {
            if (string.IsNullOrEmpty(message)) return;
            string text = isKey ? GameText.Get(message, null) : message;
            if (text == null) return;
            text = UiText.Clean(text);
            if (text.Length == 0) return;
            Plugin.LogInfo("[friend-match] notification: " + text);
            Speech.Say(text, false);
        }
    }

    [HarmonyPatch(typeof(UIStickyNotificationHelper), nameof(UIStickyNotificationHelper.ShowFriendMatchErrorNotification))]
    internal static class FriendMatchErrorNotificationPatch
    {
        static void Prefix() => FriendMatchSpeech.Depth++;
        static void Postfix() => FriendMatchSpeech.Depth--;
    }

    [HarmonyPatch(typeof(UIStickyNotificationHelper), nameof(UIStickyNotificationHelper.ShowFriendMatchWaitingNotification))]
    internal static class FriendMatchWaitingNotificationPatch
    {
        static void Prefix() => FriendMatchSpeech.Depth++;
        static void Postfix() => FriendMatchSpeech.Depth--;
    }

    [HarmonyPatch(typeof(FriendMatchNotificationManager), nameof(FriendMatchNotificationManager.ShowWarningNotification))]
    internal static class FriendMatchWarningNotificationPatch
    {
        static void Prefix() => FriendMatchSpeech.Depth++;
        static void Postfix() => FriendMatchSpeech.Depth--;
    }

    /// <summary>The notification panel's data overload: the outgoing invite (by its priority), and whatever
    /// the friend-match helpers above post through it.</summary>
    [HarmonyPatch(typeof(UINotificationsPanel), nameof(UINotificationsPanel.ShowSlideNotification),
        new[] { typeof(UINotificationData), typeof(bool) })]
    internal static class FriendMatchSlideDataPatch
    {
        static void Postfix(UINotificationData data)
        {
            try
            {
                if (data == null) return;
                if (data.priority != UINotificationData.EPriority.FriendMatchOutgoing && FriendMatchSpeech.Depth <= 0) return;
                FriendMatchSpeech.Speak(data.message, data.messageIsLocKey);
            }
            catch (System.Exception e) { Plugin.LogInfo("[friend-match] slide notification failed: " + e.Message); }
        }
    }

    /// <summary>The panel's icon-and-text overload, which the friend-match helpers may use instead.</summary>
    [HarmonyPatch(typeof(UINotificationsPanel), nameof(UINotificationsPanel.ShowSlideNotification),
        new[] { typeof(UINotificationData.EIcon), typeof(string), typeof(string), typeof(UINotificationData.EPriority), typeof(bool), typeof(bool) })]
    internal static class FriendMatchSlideTextPatch
    {
        static void Postfix(string message, bool messageIsLocKey)
        {
            try
            {
                if (FriendMatchSpeech.Depth > 0) FriendMatchSpeech.Speak(message, messageIsLocKey);
            }
            catch (System.Exception e) { Plugin.LogInfo("[friend-match] slide notification failed: " + e.Message); }
        }
    }
}
