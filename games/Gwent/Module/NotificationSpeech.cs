using System;
using HarmonyLib;
using GwentUnity;

namespace GwentAccess
{
    /// <summary>
    /// Speaks the notifications that slide in at the corner of the screen, wherever the player is: a contract
    /// completed, a gift received, server maintenance, an admin notice, a GG received, the friend-match notices
    /// ("Waiting for the invited player to respond", "The invite is no longer valid"), and the like. A sighted
    /// player sees each pop up and fade; they take no input, so they are spoken (queued) as they are posted, in
    /// the game's words, with the label drawn beside their icon. The incoming friend-match invite is not spoken
    /// here: it takes the keyboard as <see cref="Ui.InviteScreen"/>, whose name is its message.
    ///
    /// Every notification goes through one of the notification panel's ShowSlideNotification overloads. One may
    /// build its data and call another, so a message posted twice within a moment is said once. A notification
    /// the panel holds back right now (its allowed priorities exclude it, as during a match) is not said, as it
    /// is not shown.
    /// </summary>
    internal static class NotificationSpeech
    {
        private static string _last;
        private static DateTime _lastAt;

        internal static void Speak(UINotificationsPanel panel, string message, bool isKey, string iconLabel,
            UINotificationData.EPriority priority, bool forceShow = false)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (priority == UINotificationData.EPriority.FriendMatchIncoming) return; // InviteScreen
            if (!forceShow && !Allowed(panel, priority)) return;
            string text = isKey ? GameText.Get(message, null) : message;
            if (text == null) return;
            text = UiText.Join(UiText.Clean(text), UiText.Clean(iconLabel ?? ""));
            if (text.Length == 0) return;
            if (text == _last && (DateTime.UtcNow - _lastAt).TotalSeconds < 2) return;
            _last = text;
            _lastAt = DateTime.UtcNow;
            Plugin.LogInfo("[notification] " + priority + ": " + text);
            Speech.Say(text, false);
        }

        private static bool Allowed(UINotificationsPanel panel, UINotificationData.EPriority priority)
        {
            try
            {
                if (panel == null || priority == UINotificationData.EPriority.None) return true;
                var allowed = panel.AllowedPriorities;
                return allowed == UINotificationData.EPriority.All || (allowed & priority) != 0;
            }
            catch { return true; }
        }

        internal static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e) { Plugin.LogInfo("[notification] speech failed: " + e.Message); }
        }
    }

    /// <summary>The data overload: most notifications, built by the game's helpers.</summary>
    [HarmonyPatch(typeof(UINotificationsPanel), nameof(UINotificationsPanel.ShowSlideNotification),
        new[] { typeof(UINotificationData), typeof(bool) })]
    internal static class SlideNotificationDataPatch
    {
        static void Postfix(UINotificationsPanel __instance, UINotificationData data) => NotificationSpeech.Guard(() =>
        {
            if (data != null)
                NotificationSpeech.Speak(__instance, data.message, data.messageIsLocKey, data.iconLabel, data.priority, data.ForceShow);
        });
    }

    /// <summary>The icon-and-text overload.</summary>
    [HarmonyPatch(typeof(UINotificationsPanel), nameof(UINotificationsPanel.ShowSlideNotification),
        new[] { typeof(UINotificationData.EIcon), typeof(string), typeof(string), typeof(UINotificationData.EPriority), typeof(bool), typeof(bool) })]
    internal static class SlideNotificationTextPatch
    {
        static void Postfix(UINotificationsPanel __instance, string message, string iconLabel, UINotificationData.EPriority priority, bool messageIsLocKey) =>
            NotificationSpeech.Guard(() => NotificationSpeech.Speak(__instance, message, messageIsLocKey, iconLabel, priority));
    }

    /// <summary>The icon-and-text overload with an action button.</summary>
    [HarmonyPatch(typeof(UINotificationsPanel), nameof(UINotificationsPanel.ShowSlideNotification),
        new[] { typeof(UINotificationData.EIcon), typeof(string), typeof(int), typeof(CrimsonUI.CrimsonInputLayer.EPriority), typeof(string),
            typeof(UINotificationData.EPriority), typeof(bool), typeof(bool) })]
    internal static class SlideNotificationActionPatch
    {
        static void Postfix(UINotificationsPanel __instance, string message, UINotificationData.EPriority priority, bool messageIsLocKey) =>
            NotificationSpeech.Guard(() => NotificationSpeech.Speak(__instance, message, messageIsLocKey, null, priority));
    }

    /// <summary>The icon-and-text overload shown for a set time.</summary>
    [HarmonyPatch(typeof(UINotificationsPanel), nameof(UINotificationsPanel.ShowSlideNotificationWithCustomDuration))]
    internal static class SlideNotificationTimedPatch
    {
        static void Postfix(UINotificationsPanel __instance, string message, string iconLabel, UINotificationData.EPriority priority, bool messageIsLocKey) =>
            NotificationSpeech.Guard(() => NotificationSpeech.Speak(__instance, message, messageIsLocKey, iconLabel, priority));
    }
}
