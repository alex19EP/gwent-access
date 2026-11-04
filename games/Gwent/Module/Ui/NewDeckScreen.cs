using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentVisuals;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The deck builder's NEW DECK window (<see cref="UIDBCreateDeckWithAbilityPanel"/>): pick a faction, then the
    /// leader ability the deck is built on. The fallback panel screen read it as one flat list with the
    /// gamepad's "SELECT" hint as text and no word of what was chosen. Two stops:
    ///
    /// - Faction: the six factions (toggles; the chosen one says "selected" and, as the panel beside it shows,
    ///   its description). Enter chooses one, and the abilities below change to its own.
    /// - Leader ability: the chosen faction's abilities under the game's headings (Basic, Advanced), each with
    ///   the provisions it adds to the deck's limit and what it does. Enter creates the deck with it: the game's
    ///   click on an ability (SELECT is that click's gamepad hint).
    ///
    /// Backspace is the window's Back (the game's Back key binding).
    /// </summary>
    internal sealed class NewDeckScreen : Screen
    {
        public override string Key => "new-deck";
        public override int Layer => 1;
        public override string ScreenName => GameText.Get("deck_builder_create_deck_title", "New deck");

        public override bool IsActive() => Panel() != null;

        private static UIDBCreateDeckWithAbilityPanel Panel() => CrimsonNodes.ActivePanel<UIDBCreateDeckWithAbilityPanel>();

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;

            // ---- factions ----
            var factions = p.m_FactionList?.GetComponentsInChildren<UIFactionListItem>(false);
            if (factions != null && factions.Length > 0)
            {
                b.BeginStop("new-deck:factions");
                b.PushContext(Loc.T("Faction"));
                foreach (var f in factions)
                {
                    var item = f;
                    var toggle = f.TryCast<ToggleButtonControl>();
                    Func<string> name = () => UiText.Clean(item.m_Title?.Text?.text ?? item.FactionId.ToString());
                    b.AddItem(CrimsonNodes.Id(f), new NodeVtable
                    {
                        Announcements = new[]
                        {
                            new NodeAnnouncement(name, kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => CrimsonNodes.Toggled(toggle) ? Loc.T("selected") : null, live: true, kind: AnnouncementKinds.Selected),
                            new NodeAnnouncement(() => CrimsonNodes.Toggled(toggle) ? FactionDescription(p, name()) : null, kind: AnnouncementKinds.Tooltip),
                        },
                        OnActivate = () => Navigator.ClickControl(item),
                        OnTooltip = () => Speech.Say(FactionDescription(p, name()) ?? name(), true),
                    });
                }
                b.PopContext();
            }

            // ---- leader abilities ----
            var list = p.m_AbilityList;
            var abilities = list?.GetComponentsInChildren<UIAbilityListItem>(false);
            if (abilities != null && abilities.Length > 0)
            {
                b.BeginStop("new-deck:abilities");
                b.PushContext(Loc.T("Leader ability, Enter creates the deck"));
                string group = null;
                foreach (var a in abilities)
                {
                    string heading = Heading(a, list.transform);
                    if (heading != group)
                    {
                        if (group != null) b.PopContext();
                        group = heading;
                        if (group != null) b.PushContext(group);
                    }
                    var item = a;
                    b.AddItem(CrimsonNodes.Id(a), new NodeVtable
                    {
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => AbilityName(item), kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => Provisions(item), kind: AnnouncementKinds.Value),
                            new NodeAnnouncement(() => AbilityText(item), kind: AnnouncementKinds.Tooltip),
                        },
                        OnActivate = () => Navigator.ClickControl(item),
                        OnTooltip = () => Speech.Say(item.LeaderCard?.Card is GwentGameplay.Card c ? CardReader.Whole(c) : AbilityName(item), true),
                    });
                }
                if (group != null) b.PopContext();
                b.PopContext();
            }

            // ---- back ----
            var back = p.m_BackBinding?.Button;
            if (back != null && back.gameObject.activeInHierarchy && CrimsonNodes.Texts(back).Count > 0)
            {
                b.BeginStop("new-deck:bar");
                b.AddItem(CrimsonNodes.Id(back), CrimsonNodes.Button(back));
            }
        }


        // The details panel's description of the chosen faction.
        // Only while the panel shows a faction (its name first): hovering an ability turns it to the ability.
        private static string FactionDescription(UIDBCreateDeckWithAbilityPanel p, string faction)
        {
            try
            {
                var parts = CrimsonNodes.Texts(p.m_AbilityDetails);
                if (parts.Count < 2 || !string.Equals(parts[0], faction, StringComparison.OrdinalIgnoreCase)) return null;
                return string.Join(". ", parts.GetRange(1, parts.Count - 1));
            }
            catch { return null; }
        }

        // The heading drawn over an ability's group ("Basic", "Advanced"): a text child of an ancestor, outside
        // any ability item.
        private static string Heading(UIAbilityListItem item, UnityEngine.Transform list)
        {
            var root = item.transform.parent; // EasyListRoot / DifficultListRoot
            var parent = root?.parent;
            if (root == null || parent == null) return null;
            int at = root.GetSiblingIndex();
            for (int i = at - 1; i >= 0; i--)
            {
                var t = parent.GetChild(i).GetComponent<TMPro.TMP_Text>();
                if (t != null && t.isActiveAndEnabled) return UiText.Clean(t.text);
            }
            return null;
        }

        private static string AbilityName(UIAbilityListItem a)
        {
            try { return UiText.Clean(a.m_Title?.Text?.text ?? ""); } catch { return ""; }
        }

        // The number on the ability's diamond: what it adds to the deck's provision limit.
        private static string Provisions(UIAbilityListItem a)
        {
            try
            {
                string n = UiText.Clean(a.m_CardProvision?.Text?.text ?? "");
                return n.Length > 0 ? Loc.T("adds {0} provisions", n) : null;
            }
            catch { return null; }
        }

        private static string AbilityText(UIAbilityListItem a)
        {
            try
            {
                if (a.LeaderCard?.Card is not GwentGameplay.Card c) return null;
                string text = CardReader.Abilities(c) ?? "";
                // The card's own "This ability adds 16 provisions..." repeats the diamond already said.
                return System.Text.RegularExpressions.Regex.Replace(text, @"\s*This ability adds \d+ provisions[^.]*\.", "").Trim();
            }
            catch { return null; }
        }
    }
}
