using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentGameplay;
using GwentUnity.ArenaV2;
using GwentVisuals.ArenaV2;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Draft (Arena v2) while a deck is being drafted: UIArenaPanel with its ArenaDraftingContainer shown.
    /// The fallback panel screen read it as "Arena Picked Item Control" ten times and three "Ability Icon"s:
    /// the choices are pictures whose words the game shows on hover. Three stops:
    ///
    /// - The choices, under the step's title ("Choose ability"), each read as the hover shows it: an ability's
    ///   name and text ("Blood Money, Order: Damage an enemy unit by 6…"), a card set's name and its cards, a
    ///   card as the collection reads cards. Enter is the choice's own Choose button.
    /// - The draft's steps, in order: what each asks for ("Choose stratagem"), and its state (done, with what
    ///   was picked; current; to come).
    /// - The panel's buttons (Show deck, Abandon, Options, Quit, EXIT).
    ///
    /// The ticket before a draft, the run between games and its end are <see cref="DraftLobbyScreen"/>'s.
    /// </summary>
    internal sealed class DraftScreen : Screen
    {
        public override string Key => "draft";
        public override int Layer => 1;
        public override string ScreenName => GameText.Get("arenav2_mainmenu_title", "Draft");

        public override bool IsActive() => Drafting(out _) != null;

        // The drafting container, while the arena panel is on the input layer that has the input.
        private static ArenaDraftingContainer Drafting(out UIArenaPanel panel)
        {
            panel = CrimsonNodes.ActivePanel<UIArenaPanel>();
            if (panel == null) return null;
            var d = panel.GetComponentInChildren<ArenaDraftingContainer>(false);
            return d != null && d.gameObject.activeInHierarchy ? d : null;
        }

        public override void Build(GraphBuilder b)
        {
            var drafting = Drafting(out var panel);
            if (drafting == null) return;
            var used = new HashSet<IntPtr>();

            // ---- the choices ----
            var items = new List<ArenaChoiceItem>(drafting.GetComponentsInChildren<ArenaChoiceItem>(false));
            items.Sort((x, y) => x.m_Id.CompareTo(y.m_Id));
            string title = null;
            foreach (var it in items) { title ??= StepTitle(it.m_CurrentStep); }
            bool any = false;
            foreach (var it in items)
            {
                var button = it.m_ArenaChoiceButtonInstance;
                if (button == null || !button.gameObject.activeInHierarchy) continue;
                if (!any) { b.BeginStop("draft:choices"); if (title != null) b.PushContext(title); any = true; }
                var item = it;
                foreach (var c in it.GetComponentsInChildren<AControl>(true)) used.Add(c.Pointer);
                // Keyed by the step too: the same three buttons offer each step's choices, and a new step must
                // land afresh and be read.
                uint stepId = 0;
                try { stepId = it.m_CurrentStep?.StepId ?? 0; } catch { }
                b.AddItem(ControlId.Referenced(button, "draft:choice:" + stepId + ":" + it.m_Id), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(() => ChoiceText(item, false), kind: AnnouncementKinds.Label) },
                    OnActivate = () =>
                    {
                        if (item.m_IsClickable) Navigator.ClickControl(item.m_ArenaChoiceButtonInstance);
                        else Speech.Say("Not available right now.", true);
                    },
                    OnTooltip = () => Speech.Say(ChoiceText(item, true), true),
                });
            }
            if (any && title != null) b.PopContext();

            // ---- the steps ----
            var slots = new List<ArenaPickedItemControl>();
            foreach (var s in drafting.GetComponentsInChildren<ArenaPickedItemControl>(false))
            {
                foreach (var c in s.GetComponentsInChildren<AControl>(true)) used.Add(c.Pointer);
                if (s.m_IsAction || s.DraftingStep == null) continue; // the empty "actions after games" row
                slots.Add(s);
            }
            if (slots.Count > 0)
            {
                // Steps lie left to right across their groups (picked, current, to come).
                slots.Sort((x, y) => x.transform.position.x.CompareTo(y.transform.position.x));
                b.BeginStop("draft:steps");
                b.PushContext(GameText.Get("arenav2_draft_steps", "Draft steps"));
                int n = 0;
                foreach (var s in slots)
                {
                    var slot = s;
                    b.AddItem(ControlId.Structural("draft:step:" + (n++) + ":" + SafeStepId(s)), new NodeVtable
                    {
                        Announcements = new[] { new NodeAnnouncement(() => StepText(slot), kind: AnnouncementKinds.Label) },
                        OnTooltip = () => Speech.Say(StepText(slot), true),
                    });
                }
                b.PopContext();
            }

            // ---- the panel's buttons ----
            var rest = new List<AControl>();
            foreach (var c in CrimsonNodes.Leaves(panel, used)) if (CrimsonNodes.Texts(c).Count > 0 || CrimsonNodes.Label(c) != null) rest.Add(c);
            var seen = new HashSet<IntPtr>(used);
            foreach (var c in rest) seen.Add(c.Pointer);
            rest.AddRange(CrimsonNodes.BarButtons(panel, seen));
            if (rest.Count > 0)
            {
                b.BeginStop("draft:buttons");
                foreach (var c in rest) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        private static string StepTitle(IDraftingStep step)
        {
            try { return step?.TitleKey is string key && key.Length > 0 ? GameText.Get(key, null) : null; }
            catch { return null; }
        }

        private static string SafeStepId(ArenaPickedItemControl s)
        {
            try { return s.DraftingStep.StepId.ToString(); } catch { return "?"; }
        }

        // A choice as its hover shows it. Full: a card set's every card, rather than how many.
        private static string ChoiceText(ArenaChoiceItem item, bool full)
        {
            var parts = new List<string>();
            try
            {
                var view = item.m_CurrentArenaChoiceViewInstance;
                var ability = view?.TryCast<ArenaChoiceAbilityView>();
                var set = view?.TryCast<ArenaChoiceCardSetView>();
                var card = view?.TryCast<ArenaChoiceCardView>();
                if (ability != null)
                {
                    Add(parts, ability.GetName());
                    Add(parts, ability.GetDescription());
                }
                else if (set != null)
                {
                    Add(parts, set.m_CardSetName?.Text?.text);
                    var cards = set.m_CardsInSet;
                    if (cards != null && cards.Count > 0)
                    {
                        if (full)
                            for (int i = 0; i < cards.Count; i++) Add(parts, CardReader.Summary(cards[i]));
                        else
                        {
                            var names = new List<string>();
                            for (int i = 0; i < cards.Count; i++) { var nm = cards[i] == null ? "" : UiText.Clean(CardReader.Name(cards[i], null) ?? ""); if (nm.Length > 0) names.Add(nm); }
                            parts.Add(names.Count + " cards: " + string.Join(", ", names));
                        }
                    }
                }
                else if (card != null)
                {
                    var c = CardText.CardOf(card.GetCardButton());
                    if (c != null) parts.Add(CardReader.Summary(c));
                }
                if (parts.Count == 0) parts.AddRange(CrimsonNodes.Texts(item));
            }
            catch (Exception e) { Plugin.LogInfo("[draft] choice: " + e.Message); }
            return parts.Count > 0 ? string.Join(", ", parts) : "Choice";
        }

        private static void Add(List<string> parts, string s)
        {
            s = UiText.Clean(s ?? "");
            if (s.Length > 0 && !parts.Contains(s)) parts.Add(s);
        }

        // "Choose stratagem, to come"; "Choose ability, Blood Money, done".
        private static string StepText(ArenaPickedItemControl s)
        {
            var parts = new List<string>();
            try
            {
                var step = s.DraftingStep;
                Add(parts, StepTitle(step));
                Add(parts, s.m_PickedItemData?.Name);
                string state = step.State switch
                {
                    EStepState.Active => "current",
                    EStepState.Finished => "done",
                    EStepState.Skipped => "skipped",
                    EStepState.Removed => "removed",
                    _ => "to come",
                };
                parts.Add(state);
            }
            catch { }
            return parts.Count > 0 ? string.Join(", ", parts) : "Step";
        }
    }
}
