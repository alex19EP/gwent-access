using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The navigator's keyboard, read once per frame on the game thread from Rewired's own keyboard device —
    /// the same source the game reads. Device reads are independent of the game's action mappings, so keys
    /// keep arriving here while <see cref="GameKeyBlock"/> has the game's mapping for them switched off.
    /// Held keys repeat at the user's Windows typematic rate (Rewired reports presses, not repeats).
    /// </summary>
    internal static class Keys
    {
        public static bool Ctrl { get; private set; }
        public static bool Shift { get; private set; }
        public static bool Alt { get; private set; }

        private static Rewired.Keyboard _kb;
        private static KeyCode _held = KeyCode.None;
        private static float _nextRepeat;

        /// <summary>Call once per frame before any query. False when there is no keyboard to read (Rewired not
        /// up yet, or the game window isn't focused); every query then reports nothing.</summary>
        public static bool Update()
        {
            _kb = null;
            if (!Application.isFocused) return false;
            try
            {
                if (!Rewired.ReInput.isReady) return false;
                _kb = Rewired.ReInput.controllers.Keyboard;
            }
            catch { _kb = null; }
            if (_kb == null) return false;
            Ctrl = _kb.GetKey(KeyCode.LeftControl) || _kb.GetKey(KeyCode.RightControl);
            Shift = _kb.GetKey(KeyCode.LeftShift) || _kb.GetKey(KeyCode.RightShift);
            Alt = _kb.GetKey(KeyCode.LeftAlt) || _kb.GetKey(KeyCode.RightAlt);
            if (_held != KeyCode.None && !_kb.GetKey(_held)) _held = KeyCode.None;
            return true;
        }

        /// <summary>Pressed this frame.</summary>
        public static bool Down(KeyCode k) => _kb != null && _kb.GetKeyDown(k);

        /// <summary>Pressed this frame, or held long enough to repeat. One key repeats at a time (the last
        /// one pressed), like the OS. Query each repeatable key once per frame.</summary>
        public static bool DownOrRepeat(KeyCode k)
        {
            if (_kb == null) return false;
            float now = Time.realtimeSinceStartup;
            if (_kb.GetKeyDown(k))
            {
                _held = k;
                _nextRepeat = now + RepeatDelay;
                return true;
            }
            if (_held != k || now < _nextRepeat) return false;
            _nextRepeat = now + RepeatInterval;
            return true;
        }

        // ---- the Windows typematic settings, read once ----

        private static float _delay = -1f, _interval;
        private static float RepeatDelay { get { LoadTypematic(); return _delay; } }
        private static float RepeatInterval { get { LoadTypematic(); return _interval; } }

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);

        private static void LoadTypematic()
        {
            if (_delay >= 0f) return;
            _delay = 0.4f;
            _interval = 0.06f;
            try
            {
                int delay = 0, speed = 0;
                const uint SPI_GETKEYBOARDSPEED = 0x0A, SPI_GETKEYBOARDDELAY = 0x16;
                if (SystemParametersInfo(SPI_GETKEYBOARDDELAY, 0, ref delay, 0)
                    && SystemParametersInfo(SPI_GETKEYBOARDSPEED, 0, ref speed, 0))
                {
                    _delay = (Math.Clamp(delay, 0, 3) + 1) * 0.25f;                 // 0..3 → 250..1000 ms
                    _interval = 1f / (2.5f + Math.Clamp(speed, 0, 31) / 31f * 27.5f); // 0..31 → 2.5..30 per second
                }
            }
            catch { }
        }
    }
}
