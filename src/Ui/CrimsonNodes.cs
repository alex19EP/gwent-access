using System;
using System.Collections.Generic;
using System.Text;
using Access.Core.Graph;
using CrimsonUI;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Graph nodes over Gwent's own UI controls (CrimsonUI's AControl tree): finding the controls a sighted
    /// player can use right now, giving each a structural id that survives the game rebuilding the object, and
    /// a vtable that reads the control's text and clicks it through the game's own handler.
    /// </summary>
    internal static class CrimsonNodes
    {
        /// <summary>Every usable leaf control under <paramref name="root"/>, in the game's child order: active
        /// in the scene and selectable right now. Containers are walked, not returned. A control can sit in two
        /// containers (the main menu's event button is also in a ProxyEventContainer); it is returned once, and
        /// passing the same <paramref name="seen"/> to several calls keeps it once across roots too.</summary>
        public static List<AControl> Leaves(AControl root, HashSet<IntPtr> seen = null)
        {
            var found = new List<AControl>();
            Walk(root, found, seen ?? new HashSet<IntPtr>(), 0);
            return found;
        }

        private static void Walk(AControl c, List<AControl> found, HashSet<IntPtr> seen, int depth)
        {
            if (c == null || depth > 16 || !c.gameObject.activeInHierarchy) return;
            var container = c.TryCast<AContainer>();
            if (container == null)
            {
                if (c.IsCurrentlySelectable() && seen.Add(c.Pointer)) found.Add(c);
                return;
            }
            var children = container.GetChildren();
            if (children == null) return;
            for (int i = 0; i < children.Count; i++) Walk(children[i], found, seen, depth + 1);
        }

        /// <summary>The node id for a control: its full scene path (<see cref="PathKey"/>), backed by the control.</summary>
        public static ControlId Id(AControl c) => ControlId.Referenced(c, PathKey(c.transform, null));

        /// <summary>A stable id for a control: its GameObject path below <paramref name="root"/>, with sibling
        /// indexes so look-alike clones ("SocialFeatureButton(Clone)") stay distinct. Survives the game
        /// destroying and recreating the same control.</summary>
        public static string PathKey(Transform t, Transform root)
        {
            var sb = new StringBuilder();
            for (; t != null && (root == null || t.Pointer != root.Pointer); t = t.parent)
                sb.Insert(0, "/" + t.name + "#" + t.GetSiblingIndex());
            return sb.ToString();
        }

        /// <summary>The control's distinct visible texts (TextMeshPro and legacy UI text), rich-text stripped.</summary>
        public static List<string> Texts(Component c)
        {
            var parts = new List<string>();
            try
            {
                foreach (var t in c.GetComponentsInChildren<TMPro.TMP_Text>(false)) AddText(parts, t.text);
                foreach (var t in c.GetComponentsInChildren<UnityEngine.UI.Text>(false)) AddText(parts, t.text);
            }
            catch { }
            return parts;
        }

        private static void AddText(List<string> parts, string raw)
        {
            string s = UiText.Clean(raw);
            if (s.Length > 0 && !s.StartsWith("[MISSING]") && !UiText.IsPlaceholder(s) && !parts.Contains(s)) parts.Add(s);
        }

        /// <summary>
        /// A button-like control: label = its first text (the title; icon-only controls fall back to the legacy
        /// describer's names), Space = all its texts, Enter = the game's click. <paramref name="label"/>
        /// overrides the label when the recipe knows better.
        /// </summary>
        public static NodeVtable Button(AControl c, Func<string> label = null)
        {
            return new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(label ?? (() => Label(c)), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => SafeEnabled(c) ? null : "unavailable", live: true, kind: AnnouncementKinds.Enabled),
                },
                OnActivate = () => Navigator.ClickControl(c),
                OnTooltip = () => Speech.Say(Details(c), true),
            };
        }

        public static string Label(AControl c)
        {
            try
            {
                var parts = Texts(c);
                if (parts.Count > 1) return parts[0];
                if (parts.Count == 0 && IconName(c) is string icon) return icon;
                return UiText.DescribeSelection(c);
            }
            catch (Exception e) { return "unreadable control (" + e.GetType().Name + ")"; }
        }

        // An icon-only action button (the back arrow) names itself by its action icon: "Back".
        private static string IconName(AControl c)
        {
            var ic = c.GetComponent<ActionIconController>();
            if (ic == null) return null;
            var name = ic.m_IconType.ToString();
            return name == "None" ? null : UiText.FriendlyName(name);
        }

        public static string Details(AControl c)
        {
            var parts = Texts(c);
            return parts.Count > 0 ? string.Join(". ", parts) : "No details.";
        }

        private static bool SafeEnabled(AControl c)
        {
            try { return c.IsEnabled; } catch { return true; }
        }
    }
}
