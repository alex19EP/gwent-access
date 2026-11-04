using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentUnity.Social;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The quests window: the social panel's Quests section, opened from the quests button in the social bar.
    /// Its Tab stops, as the window is drawn top to bottom:
    ///
    /// <list type="bullet">
    /// <item><b>summary</b>: the countdown to the new day (read when landed on, not ticking; Space reads it
    /// again) and the daily login button, a picture the game gives no words, named by the panel it opens.</item>
    /// <item><b>sections</b>: the game's foldouts (daily quests, the season's quests, tracked contracts, special
    /// events), each a group reading its title and counter ("Ежедневные задания, 3/3"). Enter and Right/Left
    /// click the game's own header, as in <see cref="ContactsScreen"/>.</item>
    /// <item><b>other</b>: whatever else the window shows that a player can press (a running challenge's button,
    /// the skip-training button), so nothing the game offers is out of reach.</item>
    /// </list>
    ///
    /// A daily quest is a row as it is drawn: the quest (its text, its progress, its reward), then to its right
    /// the reroll button, which reads the button's tooltip ("Сменить задание (1/1)"; once none are left, when
    /// the next one comes). The game asks no confirmation: one click abandons the quest on the server and uses
    /// up the day's reroll. So the quest itself does nothing on Enter and the button takes a deliberate move
    /// to the right, and Up/Down from it stays on the other quests' buttons. The new quest is spoken when it
    /// arrives, since the cursor stays on the button.
    ///
    /// The nodes of a quest row are keyed by slot, not by path: the game renames a quest's object after the
    /// quest, so a rerolled quest would otherwise lose the cursor.
    ///
    /// The window shares the main menu's input layer, so this screen sits above <see cref="MainMenuScreen"/>
    /// while it is open and steps aside when it closes (Escape is the game's Back).
    /// </summary>
    internal sealed class QuestsScreen : Screen
    {
        public override string Key => "quests";
        public override int Layer => 1;
        public override string ScreenName => GameText.Get("quests_title", "Quests");

        public override bool IsActive() => Window() != null;

        private static QuestsWindow Window() => ContactsScreen.OpenWindow<QuestsWindow>(SocialPanel.SocialPanelSection.Quests);

        // The game leaves its selection on a section header when the window opens and moves it around a
        // reroll; following it would pull the cursor off the button just pressed.
        public override AControl GameSelection() => null;

        public override void OnPop() => _rerolledSlot = -1;

        private AControl _opener;
        public override void OnFocus() => _opener = ContactsScreen.SocialOpener();

        /// <summary>Backspace closes the window as pressing its social bar button again does.</summary>
        public override bool Back() => ContactsScreen.CloseSocialWindow(_opener, "quest");

        // Whether this build has named where the cursor starts.
        private bool _startSet;

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;
            var used = new HashSet<System.IntPtr>();
            _startSet = false;

            AddSummary(b, w, used);

            b.BeginStop("sections");
            var daily = w.GetComponentInChildren<DailyQuestSectionRenderer>(false);
            if (daily != null) AddDaily(b, daily, used);
            var season = w.GetComponentInChildren<SeasonSectionRenderer>(false);
            if (season != null) AddSeason(b, season, used);
            foreach (var s in w.GetComponentsInChildren<AAchievementSectionRenderer>(false)) AddContracts(b, s, used);

            var rest = CrimsonNodes.Leaves(w, used);
            if (rest.Count > 0)
            {
                b.BeginStop("other");
                foreach (var c in rest) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        // ---- the top of the window ----

        private static void AddSummary(GraphBuilder b, QuestsWindow w, HashSet<System.IntPtr> used)
        {
            b.BeginStop("summary");
            var timer = w.m_NewDayTimer;
            if (CrimsonNodes.Shown(timer))
            {
                string Text() => string.Join(", ", CrimsonNodes.Texts(timer));
                b.AddItem(ControlId.Structural("quests:new-day"), new NodeVtable
                {
                    Announcements = new[] { new NodeAnnouncement(Text, kind: AnnouncementKinds.Label) },
                    OnTooltip = () => Speech.Say(Text(), true),
                });
            }
            var login = w.m_DailyLoginButton;
            if (CrimsonNodes.Shown(login) && login.IsCurrentlySelectable() && used.Add(login.Pointer))
                b.AddItem(CrimsonNodes.Id(login), CrimsonNodes.Button(login, () => GameText.Get("daily_login_title", "Daily reward")));
        }

        // ---- daily quests ----

        private void AddDaily(GraphBuilder b, DailyQuestSectionRenderer section, HashSet<System.IntPtr> used)
        {
            var slots = Slots(section);
            // Marked even while the foldout is closed, when the rows below add nothing.
            foreach (var slot in slots)
            {
                var q = Quest(slot);
                if (q == null) continue;
                if (q.m_MouseButtonControl != null) used.Add(q.m_MouseButtonControl.Pointer);
                if (q.m_DisableButtonControl != null) used.Add(q.m_DisableButtonControl.Pointer);
                if (q.m_ConsoleButtonControl != null) used.Add(q.m_ConsoleButtonControl.Pointer);
                if (q.SectionButton != null) used.Add(q.SectionButton.Pointer);
            }
            if (!BeginSection(b, section, section.SectionButton, used)) return;
            int shown = slots.FindAll(s => CrimsonNodes.Shown(s)).Count, at = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (!CrimsonNodes.Shown(slot)) continue;
                at++;
                var q = Quest(slot);
                if (q == null)
                {
                    // An empty slot shows when its next quest comes.
                    var wait = slot.m_TimerCountdownRenderer;
                    if (CrimsonNodes.Shown(wait))
                        b.AddLabel(ControlId.Structural("quests:slot:" + i), () => string.Join(", ", CrimsonNodes.Texts(wait)));
                    continue;
                }
                AddQuest(b, i, q, at + " of " + shown);
            }
            b.EndGroup();
        }

        private static List<DailyQuestSlotRenderer> Slots(DailyQuestSectionRenderer section)
        {
            var slots = new List<DailyQuestSlotRenderer>();
            var list = section.GetComponentInChildren<DailyQuestList>(false)?.m_SlotRenderers;
            for (int i = 0; list != null && i < list.Count; i++)
                if (list[i] != null) slots.Add(list[i]);
            return slots;
        }

        /// <summary>The quest a slot shows; null while it holds none.</summary>
        private static DailyQuestRenderer Quest(DailyQuestSlotRenderer slot)
        {
            try
            {
                var q = slot.m_DailyQuestRenderer;
                return CrimsonNodes.Shown(q) && q.Quest != null ? q : null;
            }
            catch { return null; }
        }

        // A quest says where it is among the quests, not in its row: the row's "1 of 2" would be the same on
        // every quest. The button beside it adds no position of its own.
        private void AddQuest(GraphBuilder b, int slot, DailyQuestRenderer q, string position)
        {
            b.StartRow("quests:daily");
            var id = ControlId.Structural("quests:quest:" + slot);
            // The window opens on the quests, the reason it was opened; the timer is a Tab away.
            if (!_startSet) { b.SetStart(id); _startSet = true; }
            b.AddItem(id, new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(() => UiText.Clean(q.Quest?.Description), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Progress(q), live: true, kind: AnnouncementKinds.Value),
                    new NodeAnnouncement(() => Reward(q), kind: AnnouncementKinds.Value),
                    new NodeAnnouncement(() => position, kind: AnnouncementKinds.Position),
                },
                OnTooltip = () => Speech.Say(Line(q), true),
            });

            var reroll = q.m_MouseButtonControl;
            if (CrimsonNodes.Shown(q.m_RerollButtonContainer) && reroll != null)
            {
                var v = CrimsonNodes.Button(reroll, () => CrimsonNodes.TooltipText(reroll) ?? GameText.Get("daily_quest_action_abandon", "Change"));
                v.SpeaksOwnPosition = true;
                v.OnActivate = () => Reroll(slot, q);
                v.OnTooltip = () => Speech.Say(CrimsonNodes.TooltipText(reroll) ?? GameText.Get("daily_quest_action_abandon", "Change"), true);
                b.AddItem(ControlId.Referenced(reroll, "quests:reroll:" + slot), v);
            }
            b.EndRow();
        }

        // The quest's text, progress and reward in one line.
        private static string Line(DailyQuestRenderer q)
        {
            var parts = new List<string> { UiText.Clean(q.Quest?.Description), Progress(q), Reward(q) };
            parts.RemoveAll(string.IsNullOrEmpty);
            return string.Join(", ", parts);
        }

        private static string Progress(DailyQuestRenderer q)
        {
            var m = q.Quest;
            if (m == null) return null;
            return m.IsCompleted ? GameText.Get("daily_quest_completed", "Completed") : "progress " + m.CurrentValue + " of " + m.CompleteValue; // drawn "1/3"
        }

        /// <summary>
        /// The reward as drawn beside the progress bar: the amount, then what its icon shows, named as the top
        /// bar names that currency. The game draws no number for a reward of one, and hides the reward
        /// altogether during the new-player flow.
        /// </summary>
        private static string Reward(DailyQuestRenderer q)
        {
            var m = q.Quest;
            if (m == null || !CrimsonNodes.Shown(q.m_RewardIcon)) return null;
            string icon = m.RewardIconName switch
            {
                "symbol_stones" => Currency.Ore(),
                "symbol_icon_RP" => Currency.RewardPoints(),
                _ => null,
            };
            var parts = new List<string> { GameText.Get("achievements_reward", "Reward:") };
            if (m.Reward >= 2) parts.Add(m.Reward.ToString());
            if (icon != null) parts.Add(icon);
            return parts.Count > 1 ? string.Join(" ", parts) : null;
        }

        // ---- rerolling ----

        // The slot whose quest was just rerolled and the quest it held: Tick speaks the new one when it arrives.
        private int _rerolledSlot = -1;
        private string _rerolledFrom;

        // The mouse's click, so the button's press plays; the game then asks the server for a new quest.
        private void Reroll(int slot, DailyQuestRenderer q)
        {
            var c = q.m_MouseButtonControl;
            if (c == null || !c.IsEnabled) return;
            _rerolledSlot = slot;
            _rerolledFrom = q.Quest?.Description;
            Navigator.MouseClick(c);
        }

        public override void Tick()
        {
            if (_rerolledSlot < 0) return;
            var section = Window()?.GetComponentInChildren<DailyQuestSectionRenderer>(false);
            if (section == null) return;
            var slots = Slots(section);
            if (_rerolledSlot >= slots.Count) { _rerolledSlot = -1; return; }
            var q = Quest(slots[_rerolledSlot]);
            if (q == null || q.Quest?.Description == _rerolledFrom) return;
            _rerolledSlot = -1;
            Speech.Say(Line(q), false);
        }

        // ---- the other sections ----

        // The season's quests are drawn as text only; no control to press.
        private static void AddSeason(GraphBuilder b, SeasonSectionRenderer section, HashSet<System.IntPtr> used)
        {
            if (!BeginSection(b, section, section.SectionButton, used)) return;
            foreach (var q in section.GetComponentsInChildren<SeasonQuestRenderer>(false))
            {
                var quest = q;
                b.AddLabel(ControlId.Structural(CrimsonNodes.PathKey(q.transform, null)), () => string.Join(", ", CrimsonNodes.Texts(quest)));
            }
            b.EndGroup();
        }

        // Tracked contracts and special events: each contract a button reading all it draws.
        private static void AddContracts(GraphBuilder b, AAchievementSectionRenderer section, HashSet<System.IntPtr> used)
        {
            var contracts = section.GetComponentsInChildren<AAchievementRenderer>(false);
            foreach (var c in contracts) used.Add(c.Pointer);
            if (!BeginSection(b, section, section.SectionButton, used)) return;
            foreach (var c in contracts)
            {
                var contract = c;
                b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, () => string.Join(", ", CrimsonNodes.Texts(contract))));
            }
            b.EndGroup();
        }

        /// <summary>Opens a section's group, mirroring the game's foldout. False when there is nothing to put
        /// under it: no header, or one the game greys out, which then reads as a line.</summary>
        internal static bool BeginSection(GraphBuilder b, FoldoutSectionComponent section, ButtonControl header, HashSet<System.IntPtr> used)
        {
            if (!CrimsonNodes.Shown(header)) return false;
            used.Add(header.Pointer);
            string Label() => string.Join(", ", CrimsonNodes.Texts(header));
            if (!header.IsCurrentlySelectable())
            {
                b.AddLabel(ControlId.Structural(CrimsonNodes.PathKey(header.transform, null)), Label);
                return false;
            }
            b.BeginGroup(CrimsonNodes.Id(header), ContactsScreen.Foldout(header, section, Label), expanded: section.IsOpened);
            return true;
        }
    }
}
