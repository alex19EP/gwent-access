using System;
using System.Collections.Generic;
using Rewired;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Input arbitration (spec §8): while a mod screen owns a key, the game must not also act on it. Gwent reads
    /// the keyboard only through Rewired's system player (arrows → DPad actions, Return → A, Escape → B/Start,
    /// Tab, Space and a few letters), so the chokepoint is that player's keyboard map: each mapping
    /// (ActionElementMap) whose key the mod claims is switched off, and switched back on when the claim ends.
    /// Rewired keeps reporting the raw key on its keyboard device, which is what <see cref="Keys"/> reads.
    ///
    /// Only unmodified mappings are touched (Ctrl+Home stays the game's). Only mappings this class switched off
    /// are ever switched back on, and <see cref="Release"/> restores everything — the module calls it on
    /// dispose, so a reload or an exception can't leave the game's keyboard dead.
    /// </summary>
    internal static class GameKeyBlock
    {
        private static readonly Dictionary<IntPtr, ActionElementMap> _off = new Dictionary<IntPtr, ActionElementMap>();
        private static readonly HashSet<KeyCode> _applied = new HashSet<KeyCode>();
        private static float _nextResync;

        /// <summary>Per frame: make the set of blocked game keys equal <paramref name="claimed"/>. Cheap when
        /// nothing changed; re-walks the maps once a second anyway in case the game reloaded them.</summary>
        public static void Apply(HashSet<KeyCode> claimed)
        {
            float now = Time.realtimeSinceStartup;
            if (_applied.SetEquals(claimed) && now < _nextResync) return;
            _nextResync = now + 1f;
            _applied.Clear();
            _applied.UnionWith(claimed);
            try
            {
                foreach (var e in KeyboardElements())
                {
                    bool block = e.modifierKey1 == ModifierKey.None && claimed.Contains(e.keyCode);
                    bool ours = _off.ContainsKey(e.Pointer);
                    if (block && !ours && e.enabled)
                    {
                        e.enabled = false;
                        _off[e.Pointer] = e;
                    }
                    else if (!block && ours)
                    {
                        e.enabled = true;
                        _off.Remove(e.Pointer);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogError("[keys] blocking failed, giving the keyboard back to the game: " + ex.Message);
                Release();
            }
        }

        /// <summary>Switch every mapping this class turned off back on.</summary>
        public static void Release()
        {
            foreach (var e in _off.Values)
                try { e.enabled = true; } catch { }
            _off.Clear();
            _applied.Clear();
        }

        /// <summary>Which game keys are blocked right now, for the dev tools.</summary>
        public static string Describe()
        {
            var keys = new List<string>();
            foreach (var e in _off.Values)
                try { keys.Add(e.keyCode + "→" + e.actionId); } catch { }
            return keys.Count == 0 ? "none" : string.Join(", ", keys);
        }

        private static IEnumerable<ActionElementMap> KeyboardElements()
        {
            var player = ReInput.players.GetSystemPlayer();
            var maps = new Il2CppSystem.Collections.Generic.List<ControllerMap>();
            player.controllers.maps.GetAllMaps(ControllerType.Keyboard, maps);
            for (int i = 0; i < maps.Count; i++)
            {
                var elements = maps[i].GetButtonMaps();
                for (int j = 0; j < elements.Length; j++) yield return elements[j];
            }
        }
    }
}
