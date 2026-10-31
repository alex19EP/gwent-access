using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;

namespace GwentAccess.Ui
{
    /// <summary>
    /// An incoming friend-match invite: the game's sticky notification in the corner ("{0} приглашает вас на
    /// дружеский матч") with its accept button and countdown, and the close button that declines it. A sighted
    /// player sees it pop up over whatever they are doing and answers it there, so it takes the keyboard while
    /// it is shown: the screen's name is the invite's message, then Accept ("Принять") and Decline, both
    /// clicking the notification's own buttons (accepting goes through the game's deck check and loading
    /// popup; declining is the close button's discard). Space reads the message again with the time left.
    ///
    /// Active while the notification panel shows a friend-match-incoming notification with its accept button;
    /// it steps aside when the invite is answered, withdrawn or times out. Above the game's screens, below
    /// popups (<see cref="PopupScreen"/>), which the answer may open.
    /// </summary>
    internal sealed class InviteScreen : Screen
    {
        public override string Key => "friend-invite";
        public override int Layer => 2;

        public override string ScreenName
        {
            get
            {
                var n = Incoming();
                return n != null ? Message(n) : null;
            }
        }

        public override bool IsActive() => Incoming() != null;

        /// <summary>The incoming friend-match invite the notification panel is showing, if any.</summary>
        private static UINotificationControl Incoming()
        {
            UINotificationsPanel panel;
            try { panel = GwentUIManager.Instance?.m_NotificationManager; } catch { return null; }
            if (panel == null || !panel.gameObject.activeInHierarchy) return null;
            foreach (var c in panel.GetComponentsInChildren<UINotificationControl>(false))
            {
                UINotificationData data;
                try { data = c.Data; } catch { continue; }
                if (data == null || data.priority != UINotificationData.EPriority.FriendMatchIncoming) continue;
                var accept = c.m_ActionWidget?.m_Button;
                if (accept != null && accept.gameObject.activeInHierarchy) return c;
            }
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var n = Incoming();
            if (n == null) return;
            var widget = n.m_ActionWidget;
            b.BeginStop("invite");

            var accept = widget.m_Button;
            b.AddItem(CrimsonNodes.Id(accept), Answer(n, accept, () =>
                Label(widget) ?? GameText.Get("friend_match_incoming_notification_action_play", "Accept")));

            var decline = widget.m_CloseButton;
            if (decline != null && decline.gameObject.activeInHierarchy)
                b.AddItem(CrimsonNodes.Id(decline), Answer(n, decline, () =>
                    GameText.Get("contact_action_decline_challenge_to_duel", "Decline")));
        }

        private static NodeVtable Answer(UINotificationControl n, AControl c, System.Func<string> label)
        {
            var v = CrimsonNodes.Button(c, label);
            v.OnTooltip = () => Speech.Say(Details(n), true);
            return v;
        }

        // The invite's message as drawn ("Zefinae приглашает вас на дружеский матч"), else the data's text.
        private static string Message(UINotificationControl n)
        {
            var parts = n.m_TextLabel != null ? CrimsonNodes.Texts(n.m_TextLabel) : new List<string>();
            if (parts.Count > 0) return string.Join(". ", parts);
            try
            {
                var d = n.Data;
                string m = d?.message;
                if (m == null) return null;
                return UiText.Clean(d.messageIsLocKey ? GameText.Get(m, m) : m);
            }
            catch { return null; }
        }

        // The accept button's caption ("Принять").
        private static string Label(UINotificationActionWidget widget)
        {
            var parts = widget.m_Label != null ? CrimsonNodes.Texts(widget.m_Label) : null;
            return parts != null && parts.Count > 0 ? parts[0] : null;
        }

        // The message, then what else the widget shows (the countdown's seconds).
        private static string Details(UINotificationControl n)
        {
            var parts = new List<string>();
            if (Message(n) is string m) parts.Add(m);
            var cd = n.m_ActionWidget?.m_CountdownController;
            if (cd != null && cd.gameObject.activeInHierarchy)
                foreach (var t in CrimsonNodes.Texts(cd)) if (!parts.Contains(t)) parts.Add(t);
            return string.Join(". ", parts);
        }
    }
}
