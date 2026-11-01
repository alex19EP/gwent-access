using System;
using System.Collections.Generic;
using GwentAccess.Ui;
using GwentVisuals.Campaign;
using UnityEngine;
using UnityEngine.AI;

namespace GwentAccess.Exploration
{
    /// <summary>
    /// The exploration scanner (RTAccess's layout): a list of the things the player knows of
    /// (<see cref="World.Known"/>), by kind, reachable ones first and then nearest first, measured from the
    /// character each time a key is pressed. Browsing never moves the character.
    ///
    /// PageDown / PageUp: the next / previous thing in the kind. Ctrl+PageDown / Ctrl+PageUp: the next / previous
    /// kind (empty ones skipped). N / Shift+N: people, nearest first. O: the selection again. Backslash: walk to the
    /// selection, along the game's own path (PlayerLocomotionController.SetWaypoint, what a mouse click does);
    /// WASD takes over at any moment. X: where am I. Shift+X: the current quest.
    ///
    /// A line reads "treasure chest, 14 metres, north-east, 9 north 11 east, 2 of 5", with "no path" before the
    /// position when the game's navmesh has no way there (a locked section, the far side of a river).
    /// </summary>
    internal static class Scanner
    {
        private static Category _category = Category.Quests;
        private static int _selectedId;

        public static readonly ScreenKey[] Keys =
        {
            new ScreenKey { Key = KeyCode.PageDown, Run = () => Step(+1), Hint = "PageDown, next thing" },
            new ScreenKey { Key = KeyCode.PageUp, Run = () => Step(-1), Hint = "PageUp, previous thing" },
            new ScreenKey { Key = KeyCode.PageDown, Ctrl = true, Run = () => StepCategory(+1), Hint = "Control PageDown, next kind" },
            new ScreenKey { Key = KeyCode.PageUp, Ctrl = true, Run = () => StepCategory(-1), Hint = "Control PageUp, previous kind" },
            new ScreenKey { Key = KeyCode.N, Run = () => Cycle(Category.People, +1), Hint = "N, nearest people" },
            new ScreenKey { Key = KeyCode.N, Shift = true, Run = () => Cycle(Category.People, -1), Hint = "Shift N, people, farthest first" },
            new ScreenKey { Key = KeyCode.O, Run = Repeat, Hint = "O, the selection again" },
            new ScreenKey { Key = KeyCode.Backslash, Run = WalkToSelection, Hint = "Backslash, walk to the selection" },
            new ScreenKey { Key = KeyCode.X, Run = WhereAmI, Hint = "X, where am I" },
            new ScreenKey { Key = KeyCode.X, Shift = true, Run = Quest, Hint = "Shift X, current quest" },
        };

        // ---- lists ----

        private sealed class Entry
        {
            public WorldItem Item;
            public float Distance;
            public bool Reachable;
        }

        private static List<Entry> List(Category c)
        {
            var from = World.PlayerPosition;
            var list = new List<Entry>();
            foreach (var it in World.Known())
            {
                if (it.Category != c) continue;
                var d = it.Position - from;
                list.Add(new Entry { Item = it, Distance = new Vector2(d.x, d.z).magnitude, Reachable = Reachable(it.Position, out _) });
            }
            list.Sort((a, b) => a.Reachable != b.Reachable ? (a.Reachable ? -1 : 1) : a.Distance.CompareTo(b.Distance));
            return list;
        }

        private static void Step(int dir)
        {
            var list = List(_category);
            if (list.Count == 0)
            {
                // Nothing of this kind: move on to the next kind that has something.
                StepCategory(dir);
                return;
            }
            int i = list.FindIndex(e => e.Item.Id == _selectedId);
            i = i < 0 ? (dir > 0 ? 0 : list.Count - 1) : (i + dir + list.Count) % list.Count;
            Select(list, i, null);
        }

        private static void StepCategory(int dir)
        {
            int n = Enum.GetValues(typeof(Category)).Length;
            for (int k = 1; k <= n; k++)
            {
                var c = (Category)(((int)_category + dir * k % n + n) % n);
                var list = List(c);
                if (list.Count == 0) continue;
                _category = c;
                Select(list, 0, WorldText.CategoryName(c) + ", " + list.Count);
                return;
            }
            Speech.Say("Nothing known nearby", true);
        }

        private static void Cycle(Category c, int dir)
        {
            if (_category != c)
            {
                _category = c;
                _selectedId = 0;
            }
            var list = List(c);
            if (list.Count == 0) { Speech.Say("No " + WorldText.CategoryName(c) + " known", true); return; }
            Step(dir);
        }

        private static void Select(List<Entry> list, int i, string prefix)
        {
            var e = list[i];
            _selectedId = e.Item.Id;
            string line = Describe(e.Item, e.Reachable) + ", " + (i + 1) + " of " + list.Count;
            Speech.Say(prefix != null ? prefix + ". " + line : line, true);
            World.PlayAt(e.Item, Sounds.Stem(e.Item.Type), 0.6f);
        }

        private static string Describe(WorldItem it, bool reachable)
        {
            string s = it.Label;
            if (!reachable) s += ", no path";
            return s + ", " + WorldText.Where(World.PlayerPosition, it.Position);
        }

        private static WorldItem Selected()
        {
            if (_selectedId == 0) return null;
            foreach (var it in World.Known()) if (it.Id == _selectedId) return it;
            return null;
        }

        private static void Repeat()
        {
            var it = Selected();
            if (it == null) { Speech.Say("Nothing selected", true); return; }
            Speech.Say(Describe(it, Reachable(it.Position, out _)), true);
            World.PlayAt(it, Sounds.Stem(it.Type), 0.6f);
        }

        // ---- reachability ----

        private static NavMeshPath _path;

        /// <summary>Is there a complete navmesh path from the character to (near) <paramref name="target"/>?
        /// <paramref name="end"/> is the walkable point it leads to.</summary>
        private static bool Reachable(Vector3 target, out Vector3 end)
        {
            end = target;
            try
            {
                if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 4f, NavMesh.AllAreas)) return false;
                end = hit.position;
                _path ??= new NavMeshPath();
                return NavMesh.CalculatePath(World.PlayerPosition, end, NavMesh.AllAreas, _path)
                    && _path.status == NavMeshPathStatus.PathComplete;
            }
            catch (Exception e)
            {
                Plugin.LogError("[scanner] path: " + e.Message);
                return true; // don't hide a thing over a failed query
            }
        }

        // ---- walking to the selection ----

        private static WorldItem _walkTarget;
        private static Vector3 _lastPos;
        private static float _stillSince;

        /// <summary>A walk the scanner started is under way (and WASD hasn't taken over).</summary>
        public static bool AutoWalking => _walkTarget != null && !Walk.Moving;

        /// <summary>
        /// Keeps the game in mouse navigation while the scanner walks the character. A click-walk (SetWaypoint)
        /// is only followed in that mode: PlayerLocomotionController.UpdateInput steers by the stick otherwise
        /// (GetControllerInputVector), which drops the click and heads for the character's own feet. The game
        /// leaves mouse mode for the stick when the stick moves and sometimes resets it (PlayerInputController
        /// Reset: layer changes), so it is set again before each of its input updates; with no button held and
        /// the stick at rest the update keeps it, and the walk goes on as after a mouse click.
        /// </summary>
        [HarmonyLib.HarmonyPatch(typeof(PlayerInputController), nameof(PlayerInputController.HandleUpdate))]
        private static class KeepMouseNavigation
        {
            private static void Prefix(PlayerInputController __instance)
            {
                if (AutoWalking) __instance.UseMouseNavigation = true;
            }
        }

        private static void WalkToSelection()
        {
            var it = Selected();
            if (it == null) { Speech.Say("Nothing selected", true); return; }
            if (!Reachable(it.Position, out var end)) { Speech.Say("No path to " + it.Label, true); return; }
            var player = World.Player;
            var loco = player?.Locomotion;
            if (loco == null) return;
            player.Input.UseMouseNavigation = true;
            loco.SetWaypoint(end);
            _walkTarget = it;
            _lastPos = World.PlayerPosition;
            _stillSince = Time.unscaledTime;
            Speech.Say("Walking to " + it.Label, true);
            Plugin.LogInfo("[scanner] walk to " + it.Label + " " + end);
        }

        /// <summary>Per frame: report the end of a walk the scanner started. Arrival is the game showing the
        /// target's interact prompt, or standing at it; a walk that stopped short (WASD, a blocked path) is
        /// reported with where the target is now.</summary>
        public static void Tick()
        {
            if (_walkTarget == null) return;
            if (!World.Exploring || Walk.Moving) { _walkTarget = null; return; } // a dialogue opened; WASD took over
            var pos = World.PlayerPosition;
            var target = _walkTarget;
            var cur = World.Current();
            float dist = new Vector2(target.Position.x - pos.x, target.Position.z - pos.z).magnitude;
            if ((cur != null && cur.Id == target.Id) || dist < 1.5f)
            {
                _walkTarget = null;
                Speech.Say("At " + target.Label);
                return;
            }
            if ((pos - _lastPos).sqrMagnitude > 0.01f)
            {
                _lastPos = pos;
                _stillSince = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - _stillSince > 0.6f)
            {
                _walkTarget = null;
                Speech.Say("Stopped. " + target.Label + ", " + WorldText.Where(pos, target.Position));
            }
        }

        public static void Reset()
        {
            _walkTarget = null;
            _selectedId = 0;
            _path = null;
        }

        // ---- orientation ----

        private static void WhereAmI()
        {
            var parts = new List<string>();
            string place = PlaceName();
            if (place.Length > 0) parts.Add(place);
            var cur = World.Current();
            if (cur != null) parts.Add("at " + cur.Label);
            if (parts.Count == 0) parts.Add("Unknown place");
            Speech.Say(string.Join(", ", parts), true);
        }

        /// <summary>The name of the area the character is in: the location the game names on screen when it is
        /// entered (LocationNameZone), else the map section's name.</summary>
        private static string PlaceName()
        {
            try
            {
                var loc = LocationNameZone.ActiveLocationNameZone;
                if (loc != null && !string.IsNullOrEmpty(loc.LocationKey))
                {
                    string s = GameText.Get(loc.LocationKey, null);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                var def = GwentUnity.Exploration.MapDefinition.s_MapDefinition;
                int sec = World.Player?.SectionID ?? -1;
                if (def?.Sections != null && sec >= 0 && sec < def.Sections.Count)
                {
                    string key = def.Sections[sec].NameLoc;
                    if (!string.IsNullOrEmpty(key))
                    {
                        string s = GameText.Get(key, null);
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
            }
            catch (Exception e) { Plugin.LogError("[scanner] place: " + e.Message); }
            return "";
        }

        /// <summary>The quest the HUD shows (its title and current objective).</summary>
        private static void Quest()
        {
            var view = UnityEngine.Object.FindObjectOfType<GwentUnity.Exploration.QuestJournalView>();
            var parts = new List<string>();
            if (view != null)
                foreach (var t in view.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (PanelScreen.Faded(t)) continue;
                    string s = UiText.Clean(t.text);
                    if (s.Length > 0 && !parts.Contains(s)) parts.Add(s);
                }
            Speech.Say(parts.Count > 0 ? string.Join(", ", parts) : "No quest shown", true);
        }
    }
}
