using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using CrimsonUI;

namespace GwentAccess
{
    /// <summary>Tracks which UI panels (screens/popups) are open and reads them.</summary>
    internal static class Screens
    {
        private static readonly List<APanel> Open = new List<APanel>();
        public static DateTime LastAnnounce = DateTime.MinValue;

        public static void Opened(APanel p)
        {
            Open.RemoveAll(x => x == null || x.Pointer == p.Pointer);
            Open.Add(p);
            string text = UiText.Describe(p, 40);
            Plugin.LogInfo("Panel opened: " + UiText.DebugPath(p) + " text(" + text.Length + ")='" + text + "'");
            if (text.Length > 0 && text.Length <= 400 && !UiText.LooksLikeNoise(p.gameObject.name))
            {
                Speech.Say(text, false);
                Speech.Remember(text);
                LastAnnounce = DateTime.UtcNow;
            }
        }

        private static IntPtr _lastPanel = IntPtr.Zero;

        /// <summary>Returns a screen name to announce if the selection moved onto a different panel, else null.</summary>
        public static string CheckScreenChange(AControl c)
        {
            APanel p = null;
            try { p = c.GetComponentInParent<APanel>(); } catch { }
            if (p == null) return null;
            if (p.Pointer == _lastPanel) return null;
            _lastPanel = p.Pointer;
            CurrentPanel = p;
            string name = CleanScreenName(UiText.FriendlyName(p.gameObject.name));
            string body = UiText.BodyText(p, 350);
            Plugin.LogInfo("Screen: " + UiText.DebugPath(p) + " name='" + name + "' body='" + body + "'");
            string hint = "";
            string pn = p.gameObject.name;
            if (pn.Contains("DailyLoginReward")) hint = ". Your reward is collected. To continue, press F3 until you hear Click anywhere to continue, then F4";
            else if (pn.StartsWith("UIDBPrefab")) hint = ". Shift F3 jumps between sections. Control Q exits";
            else if (pn.Contains("Settings")) { hint = ". Shift F3 jumps between the categories, the options and the buttons. Left and right arrows change a value"; body = ""; }
            if (!string.IsNullOrEmpty(body)) Speech.Remember((string.IsNullOrEmpty(name) ? "" : name + ". ") + body);
            if (string.IsNullOrEmpty(body)) return name + hint;
            return (string.IsNullOrEmpty(name) ? body : name + ". " + body) + hint;
        }

        public static APanel CurrentPanel;

        private static readonly Dictionary<string, string> ScreenNames = new Dictionary<string, string>
        {
            { "Main Menu Content", "Main menu" },
            { "Player Info Overlay", "Top bar" },
        };

        public static string CleanName(string n) => CleanScreenName(n);
        private static string CleanScreenName(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;
            if (ScreenNames.TryGetValue(n.Trim(), out var nice)) return nice;
            foreach (var w in new[] { "Prefab", "Panel" }) n = n.Replace(w, " ");
            n = n.Trim();
            if (n.StartsWith("UI")) n = n.Substring(2);
            n = System.Text.RegularExpressions.Regex.Replace(n, @"\s+", " ").Trim();
            if (ScreenNames.TryGetValue(n, out var nice2)) return nice2;
            return n.Length == 0 ? null : n;
        }

        public static void Closed(APanel p)
        {
            Open.RemoveAll(x => x == null || x.Pointer == p.Pointer);
            Plugin.LogInfo("Panel closed: " + p.gameObject.name);
        }

        /// <summary>Read the most recently opened panel that is still active.</summary>
        public static void ReadCurrent()
        {
            APanel p = CurrentPanel;
            if (p == null || !p.gameObject.activeInHierarchy)
            {
                var c = SelectedControlPatch.Current;
                try { if (c != null) p = c.GetComponentInParent<APanel>(); } catch { }
            }
            if (p == null) { Speech.Say("No screen found.", true); return; }
            string text = UiText.Describe(p, 80);
            Plugin.LogInfo("Read screen: " + UiText.DebugPath(p) + " => '" + text + "'");
            Speech.Say(text.Length > 0 ? text : "No text found on this screen.", true);
        }
    }

    [HarmonyPatch(typeof(APanel), "set_IsPanelActive")]
    internal static class PanelActivePatch
    {
        static void Postfix(APanel __instance, bool value)
        {
            try
            {
                if (value) Screens.Opened(__instance); else Screens.Closed(__instance);
            }
            catch (Exception e) { Plugin.LogInfo("PanelActivePatch error: " + e); }
        }
    }

    /// <summary>Speak a switcher's new value (e.g. language, resolution) when it changes while selected.</summary>
    [HarmonyPatch(typeof(Switcher), "RefreshLabel")]
    internal static class SwitcherPatch
    {
        static void Postfix(Switcher __instance)
        {
            try
            {
                if (Ui.GraphNavigator.Owns) return; // the graph screen speaks its own value changes
                var sel = SelectedControlPatch.Current;
                if (sel == null || !sel.transform.IsChildOf(__instance.transform)) return;
                string v = UiText.Describe(__instance, 3);
                if (v.Length == 0) v = UiText.Describe(sel, 5);
                string raw = "";
                try { raw = __instance.Value?.Text; } catch { }
                Plugin.LogInfo("Switcher raw value: " + raw);
                Plugin.LogInfo("Switcher value: " + __instance.gameObject.name + " => " + v);
                Speech.Say(v, true);
            }
            catch (Exception e) { Plugin.LogInfo("SwitcherPatch error: " + e); }
        }
    }
}
