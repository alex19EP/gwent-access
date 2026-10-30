using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GwentAccess
{
    /// <summary>Sends text to NVDA through the NVDA controller client.</summary>
    internal static class Speech
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate int SpeakTextFn([MarshalAs(UnmanagedType.LPWStr)] string text);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int NoArgFn();

        private static SpeakTextFn _speak;
        private static NoArgFn _cancel;
        private static NoArgFn _test;
        private static readonly object Lock = new object();

        public static bool Init(string pluginDir, Action<string> log)
        {
            try
            {
                string path = Path.Combine(pluginDir, "nvdaControllerClient.dll");
                IntPtr lib = NativeLibrary.Load(path);
                _speak = Marshal.GetDelegateForFunctionPointer<SpeakTextFn>(NativeLibrary.GetExport(lib, "nvdaController_speakText"));
                _cancel = Marshal.GetDelegateForFunctionPointer<NoArgFn>(NativeLibrary.GetExport(lib, "nvdaController_cancelSpeech"));
                _test = Marshal.GetDelegateForFunctionPointer<NoArgFn>(NativeLibrary.GetExport(lib, "nvdaController_testIfRunning"));
                log("NVDA controller loaded. NVDA running: " + IsNvdaRunning);
                return true;
            }
            catch (Exception e)
            {
                log("Could not load NVDA controller: " + e);
                return false;
            }
        }

        public static bool IsNvdaRunning
        {
            get { try { return _test != null && _test() == 0; } catch { return false; } }
        }

        /// <summary>Speak text. interrupt=true stops whatever NVDA is saying first.</summary>
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
                if (count == 0) { Say("No messages yet.", true); return; }
                bool continuing = (DateTime.UtcNow - _lastMsgKey).TotalSeconds < 5 && _msgIndex >= 0;
                _msgIndex = continuing ? Math.Max(0, _msgIndex - 1) : count - 1;
                pos = count - _msgIndex;
                text = _messages[_msgIndex];
            }
            _lastMsgKey = DateTime.UtcNow;
            Say((pos > 1 ? "Message " + pos + " back. " : "") + text, true);
        }

#if DEBUG
        /// <summary>Sees every line before it goes to NVDA (the dev server's /speech feed).</summary>
        public static Action<string> Observer;
#endif

        public static void Say(string text, bool interrupt = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
#if DEBUG
            Observer?.Invoke(text);
#endif
            if (_speak == null) return;
            _last = text;
            lock (Lock)
            {
                try
                {
                    if (interrupt) _cancel();
                    _speak(text);
                }
                catch { }
            }
            Plugin.LogInfo("Spoke: " + text);
        }
    }
}
