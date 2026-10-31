using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using CrimsonUI;
using Rewired;

namespace GwentAccess
{
    /// <summary>
    /// Reads the on-screen key hints ("Continue", "Exit shop", ...) and says which keyboard key does each.
    /// Gwent maps keyboard keys to controller buttons through Rewired, so we ask Rewired.
    /// </summary>
    internal static class KeyHints
    {
        private static Dictionary<string, List<string>> _keysByAction;

        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        /// <summary>Rewired action name for a Gwent EInputKey name.</summary>
        private static string ActionName(string inputKey)
        {
            switch (inputKey)
            {
                case "LT": return "LT_Button";
                case "RT": return "RT_Button";
                case "LeftStick_Click": return "LeftStick";
                case "RightStick_Click": return "RightStick";
                default: return inputKey;
            }
        }

        /// <summary>Keyboard key(s) bound to a controller button, asked from Rewired.</summary>
        public static string KeyFor(string inputKey)
        {
            if (Cache.TryGetValue(inputKey, out var cached)) return cached;
            string action = ActionName(inputKey);
            var keys = new List<string>();
            try
            {
                var players = new List<Player>();
                try { players.Add(ReInput.players.GetPlayer(0)); } catch { }
                try { players.Add(ReInput.players.GetSystemPlayer()); } catch { }
                foreach (var p in players)
                {
                    if (p == null) continue;
                    var results = new Il2CppSystem.Collections.Generic.List<ActionElementMap>();
                    p.controllers.maps.GetButtonMapsWithAction(ControllerType.Keyboard, action, false, results);
                    for (int i = 0; i < results.Count; i++)
                    {
                        var aem = results[i];
                        if (aem == null) continue;
                        string k = aem.elementIdentifierName;
                        if (string.IsNullOrEmpty(k)) k = aem.keyCode.ToString();
                        if (!keys.Contains(k)) keys.Add(k);
                    }
                }
            }
            catch (Exception e) { Plugin.LogInfo("KeyFor(" + inputKey + ") failed: " + e.Message); }
            string r = keys.Count > 0 ? string.Join(" or ", keys) : null;
            Plugin.LogInfo("Keyboard key for " + inputKey + " (" + action + "): " + (r ?? "none"));
            Cache[inputKey] = r;
            return r;
        }

        /// <summary>Text for a key-hint glyph: its own label, or the button it's paired with.</summary>
        private static string HintLabel(GlyphLabelControl g)
        {
            string t = "";
            try { t = UiText.Clean(g.Label?.Text?.text); } catch { }
            if (t.Length > 0) return t;
            try
            {
                var parent = g.transform.parent;
                int idx = g.transform.GetSiblingIndex();
                foreach (int d in new[] { -1, 1, -2, 2 })
                {
                    int j = idx + d;
                    if (j < 0 || j >= parent.childCount) continue;
                    var sib = parent.GetChild(j);
                    if (!sib.gameObject.activeInHierarchy) continue;
                    if (sib.GetComponent<GlyphLabelControl>() != null) continue;
                    var tmp = sib.GetComponentInChildren<TMPro.TMP_Text>(false);
                    if (tmp != null && UiText.Clean(tmp.text).Length > 0) return UiText.Clean(tmp.text);
                }
            }
            catch { }
            return "";
        }

        /// <summary>"Label: Key" for every active key hint under root (or the whole scene if root is null).</summary>
        public static List<string> HintLines(Component root)
        {
            var lines = new List<string>();
            var seen = new HashSet<string>();
            try
            {
                IEnumerable<GlyphLabelControl> glyphs;
                if (root != null) glyphs = root.GetComponentsInChildren<GlyphLabelControl>(false);
                else glyphs = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<GlyphLabelControl>()).Select(o => o.TryCast<GlyphLabelControl>());
                foreach (var g in glyphs)
                {
                    if (g == null || !g.gameObject.activeInHierarchy) continue;
                    string label = HintLabel(g);
                    string inputKey = g.InputKey.ToString();
                    string key = g.InputKey == RedInput.EInputKey.Invalid ? null : KeyFor(inputKey);
                    Plugin.LogInfo("Hint: '" + label + "' inputKey=" + inputKey + " key=" + key + " @ " + UiText.DebugPath(g));
                    if (label.Length == 0) label = "Unlabeled action";
                    string line = label + ": " + (key ?? (inputKey + " button"));
                    if (seen.Add(line)) lines.Add(line);
                }
            }
            catch (Exception e) { Plugin.LogInfo("HintLines failed: " + e); }
            return lines;
        }

        /// <summary>F6: list what the current screen lets you do, with keys.</summary>
        public static void ReadHints()
        {
            var lines = HintLines(null).Select(l => l.Replace(": ESC", ": Escape").Replace(": Return", ": Enter")).ToList();
            Speech.Say(lines.Count > 0 ? string.Join(". ", lines) : "No key hints on this screen.", true);
        }
    }
}
