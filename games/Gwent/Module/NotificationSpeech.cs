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
        private static readonly System.Text.RegularExpressions.Regex Holes = new System.Text.RegularExpressions.Regex(@"\{+[^{}]*\}+");

        internal static void Speak(UINotificationsPanel panel, string message, bool isKey, string iconLabel,
            UINotificationData.EPriority priority, bool forceShow = false, string iconSprite = null)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (priority == UINotificationData.EPriority.FriendMatchIncoming) return; // InviteScreen
            if (!forceShow && !Allowed(panel, priority)) return;
            string text = isKey ? GameText.Get(message, null) : message;
            if (text == null) return;
            // A translation with its parameters still in it ("Player {0} sent you a gift!") is drawn with them
            // filled from data the call doesn't carry: say it without the holes rather than with braces.
            if (isKey) text = Holes.Replace(text, "");
            if (text.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) >= 0) Plugin.LogInfo("[notification] raw: " + text);
            // The GG reward ("... AN ADDITIONAL REWARD! {1} x {2}") draws its currency as an icon: named in place,
            // "5 Ore", rather than dropped with the other tags ("5 x").
            text = Ui.Currency.Inline(text);
            string cased = SentenceCase(text);
            bool shouted = cased != text;
            string clean = UiText.Clean(cased);
            // An amount whose icon is the notification's own picture rather than a sprite in the text.
            var bare = BareAmount.Match(clean);
            if (bare.Success && !string.IsNullOrEmpty(iconSprite) && Ui.Currency.OfIcon(iconSprite) is string currency)
                clean = clean.Substring(0, bare.Index) + bare.Groups[1].Value + " " + currency;
            text = UiText.Join(shouted ? Capitalize(clean) : clean, UiText.Clean(iconLabel ?? ""));
            if (text.Length == 0) return;
            if (text == _last && (DateTime.UtcNow - _lastAt).TotalSeconds < 2) return;
            _last = text;
            _lastAt = DateTime.UtcNow;
            Plugin.LogInfo("[notification] " + priority + ": " + text);
            Speech.Say(text, false);
        }

        // "5 x" at the end: an amount drawn before an icon the text doesn't carry.
        private static readonly System.Text.RegularExpressions.Regex BareAmount = new System.Text.RegularExpressions.Regex(@"(\d+)\s*x$");
        private static readonly System.Text.RegularExpressions.Regex Tags = new System.Text.RegularExpressions.Regex("(<[^>]*>)");
        private static readonly System.Text.RegularExpressions.Regex Word = new System.Text.RegularExpressions.Regex(@"(?<![\p{L}\d_])[\p{L}']{2,}(?![\p{L}\d_])");
        private static readonly System.Text.RegularExpressions.Regex SentenceStart = new System.Text.RegularExpressions.Regex(@"(^|[.!?]\s+)(\p{Ll})");

        /// <summary>
        /// Some notifications are written in capitals in the translation itself ("GOOD GAME! THANKS TO YOUR
        /// OPPONENT {0} ..."), as the banner draws them; a screen reader shouts those or spells short words. Text
        /// that is mostly capitals has its all-capital words put in lower case (the inserted player name, mixed
        /// case, is kept, as are the tags and "GG"); <see cref="Capitalize"/> then starts each sentence with a
        /// capital. The words stay the game's.
        /// </summary>
        private static string SentenceCase(string raw)
        {
            string plain = UiText.Clean(raw);
            int letters = 0, upper = 0;
            foreach (char ch in plain)
                if (char.IsLetter(ch)) { letters++; if (char.IsUpper(ch)) upper++; }
            if (letters < 8 || upper * 10 < letters * 7) return raw;
            var parts = Tags.Split(raw);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("<")) continue;
                parts[i] = Word.Replace(parts[i], m =>
                {
                    string w = m.Value;
                    if (w == "GG") return w;
                    foreach (char ch in w) if (char.IsLetter(ch) && !char.IsUpper(ch)) return w;
                    return w.ToLowerInvariant();
                });
            }
            return string.Concat(parts);
        }

        // Each sentence starts with a capital ("good game! thanks" → "Good game! Thanks").
        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : SentenceStart.Replace(s, m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());

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
                NotificationSpeech.Speak(__instance, data.message, data.messageIsLocKey, data.iconLabel, data.priority, data.ForceShow,
                    data.IconSpriteName);
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
