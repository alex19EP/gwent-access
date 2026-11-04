using System;

namespace GwentAccess
{
    /// <summary>Sends text to the screen reader through the host's <see cref="Prism"/> context, which the host
    /// initialises once per process so it outlives module reloads.</summary>
    internal static class Speech
    {
        private static readonly object Lock = new object();

        /// <summary>True when the host has a Prism backend to speak through (logged by the host when it has none).</summary>
        public static bool Init() => Prism.Ready;

        private static string _last = "";
        public static void Repeat() { if (_last.Length > 0) Say(_last, true); }

        // Popups, tutorials and screen messages, newest last. Shift+F7 walks back through them.
        private static readonly System.Collections.Generic.List<string> _messages = new System.Collections.Generic.List<string>();
        private static int _msgIndex = -1;
        private static DateTime _lastMsgKey = DateTime.MinValue;
        public static void Remember(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_messages)
            {
                if (_messages.Count > 0 && _messages[_messages.Count - 1] == text) return;
                _messages.Add(text);
                if (_messages.Count > 15) _messages.RemoveAt(0);
                _msgIndex = -1;
            }
        }
        /// <summary>Shift+F7: read the last popup or tutorial message; press again (within 5 seconds) for older ones.</summary>
        public static void RepeatMessage()
        {
            string text;
            int pos, count;
            lock (_messages)
            {
                count = _messages.Count;
                if (count == 0) { Say(Loc.T("No messages yet."), true); return; }
                bool continuing = (DateTime.UtcNow - _lastMsgKey).TotalSeconds < 5 && _msgIndex >= 0;
                _msgIndex = continuing ? Math.Max(0, _msgIndex - 1) : count - 1;
                pos = count - _msgIndex;
                text = _messages[_msgIndex];
            }
            _lastMsgKey = DateTime.UtcNow;
            Say(pos > 1 ? Loc.T("Message {0} back. {1}", pos, text) : text, true);
        }

#if DEBUG
        /// <summary>Sees every line before it goes to the screen reader (the dev server's /speech feed).</summary>
        public static Action<string> Observer;
#endif

        /// <summary>The mod is switched off (Gwent's F8): nothing is spoken.</summary>
        public static bool Muted = false;

        /// <summary>Speak text. interrupt=true stops whatever is being said first.</summary>
        public static void Say(string text, bool interrupt = false)
        {
            if (string.IsNullOrWhiteSpace(text) || Muted) return;
#if DEBUG
            Observer?.Invoke(text);
#endif
            if (!Prism.Ready) return;
            _last = text;
            // Callers include the hotkey thread as well as the game thread.
            lock (Lock)
            {
                try { Prism.Speak(text, interrupt); }
                catch { }
            }
            Plugin.LogInfo("Spoke: " + text);
        }
    }
}
