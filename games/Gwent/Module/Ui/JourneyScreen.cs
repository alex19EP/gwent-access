using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The Journey (<see cref="UIJourneyPanel"/>, its main view). The fallback panel screen read it as loose
    /// numbers and "x2"s, every step reward twice over, each bar button twice, and only the ten steps on screen.
    /// Tab stops, as the panel is drawn:
    ///
    /// - Progress: the Journey level, the crowns toward the next one ("6 of 24, 18 until next level"), and the
    ///   resource counters beside them.
    /// - Steps: every step of the journey, from the game's own list (it scrolls; ten are drawn at a time), each a
    ///   row as drawn, the Standard reward then the Premium one: "Step 2, 2 Reward Point(s), locked". A special
    ///   step's Fast Travel buttons follow on its row, with their prices. The cursor starts on the current step,
    ///   where the game scrolls the list when the journey opens, and the list scrolls with the cursor. Enter
    ///   clicks the reward: the game previews it beside the list (and claims one that is ready).
    /// - Preview: what the panel beside the list shows of the chosen reward ("Geralt, Flax Shirt, Legendary
    ///   Leader Skin…"), the full-preview button, Unlock Premium and the user agreement link.
    /// - The panel's buttons (EXIT, Journey Story, Journey List, Back), each once: the bar draws a mouse and a
    ///   gamepad copy of each.
    ///
    /// The story is <see cref="JourneyStoryScreen"/>'s; the journey list is left to the fallback panel screen.
    /// </summary>
    internal sealed class JourneyScreen : Screen
    {
        public override string Key => "journey";
        public override int Layer => 1;
        public override bool Overlays => true;
        public override string ScreenName => GameText.Get("EP_5_2_title", "Journey");

        public override bool IsActive() => Content() != null;

        private static UIJourneyMainContent Content()
        {
            var p = CrimsonNodes.ActivePanel<UIJourneyPanel>();
            if (p == null) return null;
            try
            {
                if (p.m_CurrentState != JourneyViewState.Main) return null;
                var mc = p.m_MainContent;
                // Not until its layout is ready: the first frames show the prefab's placeholders ("Journey
                // level 22, 22 Left").
                if (mc == null || !mc.gameObject.activeInHierarchy || !mc.IsLayoutReady()) return null;
                var selection = mc.m_JourneySelectionPanel;
                return selection != null && selection.gameObject.activeInHierarchy ? null : mc;
            }
            catch { return null; }
        }

        // The game parks its selection on EXIT; the cursor starts on the current step.
        public override AControl GameSelection() => null;

        public override void Build(GraphBuilder b)
        {
            var mc = Content();
            if (mc == null) return;
            var used = new HashSet<IntPtr>();
            AddProgress(b, mc);
            AddSteps(b, mc, used);
            AddPreview(b, mc, used);
            AddBar(b, mc, used);
        }

        // ---- progress ----

        private static void AddProgress(GraphBuilder b, UIJourneyMainContent mc)
        {
            var pr = mc.m_Progress;
            if (!CrimsonNodes.Shown(pr)) return;
            b.BeginStop("journey:progress");
            b.AddLabel(ControlId.Structural("journey:level"), () => UiText.Join(Loc.T("Journey level {0}", CrimsonNodes.Text(pr.m_JourneyLevel?.m_Level)), Crowns(pr)));
            foreach (var r in mc.m_ResourceInfoBar?.GetComponentsInChildren<UIResourceInfo>(false) ?? new UIResourceInfo[0])
            {
                var info = r;
                b.AddLabel(ControlId.Structural("journey:resource:" + r.RewardType), () => UiText.Join(ResourceName(info.RewardType), CrimsonNodes.Text(info.m_CountLabel)));
            }
        }

        // "6 of 24, 18 until next level": the bar's crowns and the counter beside it.
        private static string Crowns(UIJourneyProgress pr)
        {
            var parts = new List<string>();
            if (CrimsonNodes.Shown(pr.m_JourneyProgressBar)) parts.AddRange(CrimsonNodes.Texts(pr.m_JourneyProgressBar));
            var counter = pr.m_JourneyCrownCounter;
            if (CrimsonNodes.Shown(counter)) parts.Add(UiText.Join(CrimsonNodes.Text(counter.m_CrownCount), CrimsonNodes.Text(counter.m_Text)).Replace(", ", " "));
            return UiText.Join(parts.ToArray());
        }

        private static string ResourceName(EIRewardSubType type) => type switch
        {
            EIRewardSubType.RewardPoints => Currency.Of(Modules.Common.EVirtualCurrencyType.RewardPoints),
            EIRewardSubType.Scraps => Currency.Of(Modules.Common.EVirtualCurrencyType.Scraps),
            EIRewardSubType.Ore => Currency.Of(Modules.Common.EVirtualCurrencyType.Gold),
            EIRewardSubType.Powder => Currency.Of(Modules.Common.EVirtualCurrencyType.Powder),
            EIRewardSubType.KegOrPack or EIRewardSubType.Keg => Currency.Kegs() ?? Loc.T("Kegs"),
            _ => type.ToString(),
        };

        // ---- the steps ----

        private void AddSteps(GraphBuilder b, UIJourneyMainContent mc, HashSet<IntPtr> used)
        {
            var list = mc.m_Steps?.StepList;
            UIJourneyStepsData data;
            try { data = list?.StepsData; } catch { data = null; }
            if (data?.Steps == null || data.Steps.Count == 0) return;
            foreach (var c in list.GetComponentsInChildren<ButtonControl>(true)) used.Add(c.Pointer);
            var live = LiveItems(list);
            string standard = CrimsonNodes.Text(mc.m_Steps.m_Header?.m_StandardItem?.m_TitleLabel);
            string premium = CrimsonNodes.Text(mc.m_Steps.m_Header?.m_ExtendedItem?.m_TitleLabel);

            b.BeginStop("journey:steps");
            int count = data.Steps.Count;
            for (int i = 0; i < count; i++)
            {
                var step = data.Steps[i];
                if (step == null) continue;
                int number = step.StepNumber;
                b.StartRow("journey:step:" + number);
                var std = ControlId.Structural("journey:step:" + number + ":standard");
                if (step.IsCurrentStep) b.SetStart(std);
                string position = Loc.T("{0} of {1}", i + 1, count);
                b.AddItem(std, Reward(step, false, standard, position));
                b.AddItem(ControlId.Structural("journey:step:" + number + ":premium"), Reward(step, true, premium, position));
                if (live.TryGetValue(number, out var view))
                {
                    AddBuy(b, view.m_BuyRealCurrencyButton, view, null);
                    AddBuy(b, view.m_BuyVirtualCurrencyButton, view, Currency.Of(Modules.Common.EVirtualCurrencyType.Scraps));
                }
                b.EndRow();
            }
        }

        // A step reward: "Step 3, 2 Reward Point(s), locked", the premium one after its header ("Premium, Alchemy
        // Border, locked"). Its place among the steps, not in its row.
        private NodeVtable Reward(UIJourneyStepData step, bool premium, string header, string position)
        {
            Func<string> text = () => RewardLine(step, premium, header);
            return new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(text, kind: AnnouncementKinds.Label),
                    CrimsonNodes.RolePart(Loc.T("button")), // Enter claims it, as the mouse clicks the reward
                    new NodeAnnouncement(() => State(premium ? step.ExtendedRewardData : step.StandardRewardData), live: true, kind: AnnouncementKinds.Value),
                    new NodeAnnouncement(() => premium ? null : position, kind: AnnouncementKinds.Position),
                },
                SpeaksOwnPosition = true,
                OnActivate = () => Click(step.StepNumber, premium),
                OnTooltip = () => Speech.Say(UiText.Join(text(), State(premium ? step.ExtendedRewardData : step.StandardRewardData)), true),
            };
        }

        private static string RewardLine(UIJourneyStepData step, bool premium, string header)
        {
            var data = premium ? step.ExtendedRewardData : step.StandardRewardData;
            string reward = RewardText.Of(data?.Rewards) ?? Loc.T("Reward");
            return premium ? UiText.Join(header.Length > 0 ? header : Loc.T("Premium"), reward) : UiText.Join(Loc.T("Step {0}", step.StepNumber), reward);
        }

        private static string State(UIJourneyRewardData data)
        {
            try
            {
                if (data == null) return null;
                return data.RewardState switch
                {
                    EJourneyRewardState.Locked => Loc.T("locked"),
                    EJourneyRewardState.ReadyToClaim => Loc.T("ready to claim"),
                    EJourneyRewardState.Claimed => Loc.T("claimed"),
                    var other => other.ToString().ToLowerInvariant(),
                };
            }
            catch { return null; }
        }

        // A special step's Fast Travel button, as drawn: "Fast Travel, $4.99"; the scraps one shows its cost as a
        // negative number beside the scraps icon ("-10000").
        private static void AddBuy(GraphBuilder b, UICommonBuyButton button, UIJourneyStepListItem view, string currency)
        {
            if (!CrimsonNodes.Shown(button)) return;
            string title = CrimsonNodes.Text(view.m_FastTravelTitle);
            Func<string> label = () =>
            {
                string price = string.Join(" ", CrimsonNodes.Texts(button)).TrimStart('-');
                return UiText.Join(title, currency != null ? price + " " + currency : price);
            };
            var v = CrimsonNodes.Button(button, label);
            v.SpeaksOwnPosition = true;
            b.AddItem(CrimsonNodes.Id(button), v);
        }

        // The step items the list draws now, by step number.
        private static Dictionary<int, UIJourneyStepListItem> LiveItems(UIJourneyStepList list)
        {
            var items = new Dictionary<int, UIJourneyStepListItem>();
            foreach (var v in list.GetComponentsInChildren<UIJourneyStepListItem>(false))
            {
                try
                {
                    var r = v.m_StandardReward;
                    int n = r?.m_StepData?.StepNumber ?? 0;
                    if (n > 0) items[n] = v;
                }
                catch { }
            }
            return items;
        }

        // The list scrolls to the step under the cursor, as a gamepad player's selection scrolls it.
        public override void OnCursorMoved(GraphNode node)
        {
            if (node?.Id.Reference is AControl) { base.OnCursorMoved(node); return; }
            if (!StepOf(node, out int number, out _)) return;
            try
            {
                var list = Content()?.m_Steps?.StepList;
                var data = list?.StepsData;
                for (int i = 0; data?.Steps != null && i < data.Steps.Count; i++)
                    if (data.Steps[i]?.StepNumber == number) { CrimsonNodes.ScrollTo(list.ScrollList, i); break; }
            }
            catch (Exception e) { Plugin.LogInfo("[journey] scroll failed: " + e.Message); }
        }

        private static bool StepOf(GraphNode node, out int number, out bool premium)
        {
            number = 0; premium = false;
            if (node?.Id.StructuralKey is not string key || !key.StartsWith("journey:step:")) return false;
            var parts = key.Split(':');
            premium = parts.Length > 3 && parts[3] == "premium";
            return parts.Length > 3 && int.TryParse(parts[2], out number);
        }

        // The reward's own button, once the list has scrolled it into view: the mouse's click, which previews the
        // reward beside the list. The preview is read when it shows.
        private void Click(int number, bool premium)
        {
            var mc = Content();
            if (mc != null && LiveItems(mc.m_Steps.StepList).TryGetValue(number, out var view))
            {
                var reward = premium ? view.m_ExtendedReward : view.m_StandardReward;
                if (reward?.m_ButtonControl != null && Navigator.MouseClick(reward.m_ButtonControl))
                {
                    _previewAt = Time.unscaledTime + 0.4f;
                    return;
                }
            }
            Speech.Say(Loc.T("Not available right now."), true);
        }

        private float _previewAt = -1;

        public override void Tick()
        {
            if (_previewAt < 0 || Time.unscaledTime < _previewAt) return;
            _previewAt = -1;
            var mc = Content();
            if (mc != null && Preview(mc) is string s && s.Length > 0) Speech.Say(s, false);
        }

        // ---- the preview ----

        private static string Preview(UIJourneyMainContent mc)
        {
            var d = mc.m_VanitySimpleDescription;
            if (!CrimsonNodes.Shown(d)) return null;
            return UiText.Join(CrimsonNodes.Text(d.m_GroupNameLabel), CrimsonNodes.Text(d.m_NameLabel), CrimsonNodes.Text(d.m_DescriptionLabel));
        }

        private static void AddPreview(GraphBuilder b, UIJourneyMainContent mc, HashSet<IntPtr> used)
        {
            b.BeginStop("journey:preview");
            if (Preview(mc) != null) b.AddLabel(ControlId.Structural("journey:preview"), () => Preview(mc));
            AddButton(b, mc.m_FullPreviewButton, used);
            AddButton(b, mc.m_JourneyPaymentContainer?.m_BuyButton, used);
            var eula = mc.m_EulaLink;
            if (CrimsonNodes.Shown(eula))
                foreach (var c in CrimsonNodes.Leaves(eula, used)) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        // ---- the bar ----

        private static void AddBar(GraphBuilder b, UIJourneyMainContent mc, HashSet<IntPtr> used)
        {
            var buttons = CrimsonNodes.BarButtons(new[] { mc.m_InputButtonContainer }, used);
            if (buttons.Count == 0) return;
            b.BeginStop("journey:bar");
            foreach (var c in buttons) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }

        // ---- helpers ----

        private static void AddButton(GraphBuilder b, AControl c, HashSet<IntPtr> used)
        {
            if (CrimsonNodes.Shown(c) && used.Add(c.Pointer)) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
        }
    }
}
