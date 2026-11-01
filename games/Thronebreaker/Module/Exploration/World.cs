using System;
using System.Collections.Generic;
using GwentAccess.Audio;
using GwentVisuals.Campaign;
using UnityEngine;

namespace GwentAccess.Exploration
{
    /// <summary>The scanner's kinds of things, in the order Ctrl+PageUp/PageDown steps through them.</summary>
    internal enum Category { Quests, People, Loot, Places }

    /// <summary>One thing in the world the player can go to: an interaction zone (AInteractionZone) the game
    /// shows a marker or prompt for.</summary>
    internal sealed class WorldItem
    {
        public AInteractionZone Zone;
        public int Id;            // the zone's instance id: identity across lists and frames
        public Category Category;
        public AInteractionZone.EInteractionType Type;
        public Vector3 Position;
        public bool Revealed;     // on the world map
        public string Label => WorldText.Label(this);
    }

    /// <summary>
    /// What a sighted player knows of the world, kept frame by frame. Thronebreaker loads every zone of the
    /// episode at once (some 240 the player can use), and has no fog of war: the camera, which follows the
    /// character and can't be moved, shows a window about 27 by 26 metres around them, and the world map marks
    /// what the story, message boards and scouts have revealed. So a thing is known once it has been on screen
    /// (remembered for the rest of the episode, as a sighted player remembers the chest they walked past) or
    /// when it is revealed on the map. Nothing else is listed.
    ///
    /// Usable things are the zones with an interaction type that haven't fired yet: loot not yet taken, people
    /// not yet talked to, quest places and battles not yet started. Invisible story triggers (type None) never
    /// count.
    ///
    /// When a usable thing comes into view, a short sound plays from its side (<see cref="Spatializer"/>,
    /// compass-fixed: up the screen is north), one stem per kind (<see cref="Sounds"/>), so new things are heard
    /// rather than spoken (<see cref="ModConfig.ScreenCues"/>). Walking into a thing's zone, where the game shows
    /// its interact prompt, plays a soft cue; leaving it, the lower twin.
    /// </summary>
    internal static class World
    {
        private static readonly List<WorldItem> _items = new List<WorldItem>();
        private static readonly HashSet<int> _seen = new HashSet<int>();
        private static readonly HashSet<int> _onScreen = new HashSet<int>();
        private static float _nextScan;
        private static int _episode = int.MinValue;
        private static bool _primed;
        private static IntPtr _currentZone;

        // Cues for things that came into view together, played a little apart so each is heard.
        private static readonly Queue<WorldItem> _cueQueue = new Queue<WorldItem>();
        private static float _nextCue;
        private const float CueGap = 0.15f;

        /// <summary>The player character, or null outside exploration.</summary>
        public static PlayerController Player => CampaignManager.Instance?.PlayerController;

        public static Vector3 PlayerPosition => Player?.CachedTransform.position ?? Vector3.zero;

        public static bool Exploring
        {
            get
            {
                var cm = CampaignManager.Instance;
                return cm != null && cm.InsideExplorationModeWithInput && cm.PlayerController != null;
            }
        }

        /// <summary>Every usable thing the player knows of (seen or revealed), unsorted.</summary>
        public static List<WorldItem> Known()
        {
            var list = new List<WorldItem>();
            foreach (var it in _items)
                if (Usable(it.Zone) && (_seen.Contains(it.Id) || it.Revealed)) list.Add(it);
            return list;
        }

        public static bool IsOnScreen(WorldItem it) => _onScreen.Contains(it.Id);

        public static void Tick()
        {
            var cm = CampaignManager.Instance;
            if (cm == null || cm.PlayerController == null) { Clear(); return; }
            if (cm.EpisodeIndex != _episode) { Clear(); _episode = cm.EpisodeIndex; }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                Rescan();
            }
            if (!cm.InsideExplorationModeWithInput) return;
            UpdateView();
            UpdateCurrentZone();
            PlayQueuedCue();
        }

        public static void Clear()
        {
            _items.Clear();
            _seen.Clear();
            _onScreen.Clear();
            _cueQueue.Clear();
            _primed = false;
            _currentZone = IntPtr.Zero;
            _episode = int.MinValue;
        }

        // The zone list, refreshed once a second: zones come and go with the story.
        private static void Rescan()
        {
            var byId = new Dictionary<int, WorldItem>();
            foreach (var it in _items) byId[it.Id] = it;
            _items.Clear();
            foreach (var z in UnityEngine.Object.FindObjectsOfType<AInteractionZone>())
            {
                if (z == null || z.InteractionType == AInteractionZone.EInteractionType.None) continue;
                int id = z.GetInstanceID();
                if (!byId.TryGetValue(id, out var it))
                    it = new WorldItem { Id = id, Type = z.InteractionType, Category = CategoryOf(z.InteractionType) };
                it.Zone = z;
                it.Position = z.transform.position;
                it.Revealed = z.IsRevealed;
                _items.Add(it);
            }
        }

        /// <summary>Still there and not used up: active, and its event not fired (loot taken, talk done).</summary>
        public static bool Usable(AInteractionZone z)
        {
            if (z == null) return false;
            try
            {
                return z.gameObject.activeInHierarchy && z.IsActive
                    && z.EventFiredType == AInteractionZone.EventZoneFiredType.EventNotFired;
            }
            catch { return false; }
        }

        // Which usable things are in the camera's view now; newly visible ones are remembered and cued.
        private static void UpdateView()
        {
            var cam = ExplorationCamera.Instance?.Camera;
            if (cam == null) return;
            var now = new HashSet<int>();
            foreach (var it in _items)
            {
                if (!Usable(it.Zone)) continue;
                var vp = cam.WorldToViewportPoint(it.Position);
                if (vp.z <= 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) continue;
                now.Add(it.Id);
                _seen.Add(it.Id);
                // The first frame after a load or a reload shows a screen full of things at once: no cues for those.
                if (_primed && !_onScreen.Contains(it.Id) && ModConfig.ScreenCues && _cueQueue.Count < 6)
                    _cueQueue.Enqueue(it);
            }
            _onScreen.Clear();
            _onScreen.UnionWith(now);
            _primed = true;
        }

        private static void PlayQueuedCue()
        {
            if (_cueQueue.Count == 0 || Time.unscaledTime < _nextCue) return;
            var it = _cueQueue.Dequeue();
            if (!_onScreen.Contains(it.Id)) return; // gone again before its turn
            _nextCue = Time.unscaledTime + CueGap;
            PlayAt(it, Sounds.Stem(it.Type), 0.8f);
        }

        /// <summary>Play <paramref name="path"/> from where <paramref name="it"/> is, relative to the character.</summary>
        public static void PlayAt(WorldItem it, string path, float volume)
        {
            var d = it.Position - PlayerPosition;
            float dist = new Vector2(d.x, d.z).magnitude;
            AudioMixer.PlayAt(path, Spatializer.Cue(d.x, d.z), volume * Spatializer.VolumeFor(dist, 12f, 0.35f));
        }

        // The zone whose interact prompt the game shows (PlayerController.CurrentEventZone).
        private static void UpdateCurrentZone()
        {
            var z = Player?.CurrentEventZone;
            var ptr = z == null ? IntPtr.Zero : z.Pointer;
            if (ptr == _currentZone) return;
            bool entered = ptr != IntPtr.Zero;
            _currentZone = ptr;
            AudioMixer.Play(AudioAssets.Cue(entered ? "object_enter" : "object_exit"), 0.6f);
        }

        /// <summary>The item for the zone whose interact prompt is up, or null.</summary>
        public static WorldItem Current()
        {
            var z = Player?.CurrentEventZone;
            if (z == null) return null;
            int id = z.GetInstanceID();
            foreach (var it in _items) if (it.Id == id) return it;
            return null;
        }

        public static Category CategoryOf(AInteractionZone.EInteractionType t)
        {
            switch (t)
            {
                case AInteractionZone.EInteractionType.Dialogue:
                    return Category.People;
                case AInteractionZone.EInteractionType.GeneralLoot:
                case AInteractionZone.EInteractionType.TreasureChest:
                case AInteractionZone.EInteractionType.HiddenTreasure:
                case AInteractionZone.EInteractionType.Shrine:
                case AInteractionZone.EInteractionType.Recruits:
                    return Category.Loot;
                case AInteractionZone.EInteractionType.MessageBoard:
                case AInteractionZone.EInteractionType.FastTravel:
                    return Category.Places;
                default: // main, side and minor quests, battles, puzzles, easter eggs
                    return Category.Quests;
            }
        }
    }

    /// <summary>The sound stem for each kind of thing (WrathAccess's recordings, assets/audio/interactables).</summary>
    internal static class Sounds
    {
        public static string Stem(AInteractionZone.EInteractionType t)
        {
            string s;
            switch (t)
            {
                case AInteractionZone.EInteractionType.GeneralLoot: s = "loot-generic"; break;
                case AInteractionZone.EInteractionType.TreasureChest:
                case AInteractionZone.EInteractionType.HiddenTreasure: s = "loot-chest"; break;
                case AInteractionZone.EInteractionType.Shrine: s = "buff-zone"; break;
                case AInteractionZone.EInteractionType.Recruits: s = "units-ally"; break;
                case AInteractionZone.EInteractionType.Dialogue: s = "units-neutral"; break;
                case AInteractionZone.EInteractionType.Battle:
                case AInteractionZone.EInteractionType.PuzzleBattle: s = "units-enemy"; break;
                case AInteractionZone.EInteractionType.FastTravel: s = "transition"; break;
                case AInteractionZone.EInteractionType.MessageBoard: s = "mechanism"; break;
                default: s = "unknown"; break; // quests and points of interest
            }
            return AudioAssets.Interactable(s);
        }
    }
}
