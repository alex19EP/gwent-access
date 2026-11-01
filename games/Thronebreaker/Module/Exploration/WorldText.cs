using System;
using GwentVisuals.Campaign;
using UnityEngine;

namespace GwentAccess.Exploration
{
    /// <summary>
    /// What the scanner says about a thing: its name (the game's map tooltip for it when it has one, else its
    /// kind), and where it is from the character: distance in metres, the compass direction (north is up the
    /// screen; the camera never turns), and for a diagonal the north/south and east/west parts, because an
    /// eighth of the compass is too vague to walk (RTAccess tester feedback).
    /// </summary>
    internal static class WorldText
    {
        public static string Label(WorldItem it)
        {
            try
            {
                string key = it.Zone != null ? it.Zone.MapTooltipLocKey : null;
                if (!string.IsNullOrEmpty(key))
                {
                    string s = GameText.Get(key, null);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return KindName(it.Type);
        }

        // The game has no on-screen name for these (a sighted player sees the model and an icon); English until the
        // game's own words for them are found.
        public static string KindName(AInteractionZone.EInteractionType t)
        {
            switch (t)
            {
                case AInteractionZone.EInteractionType.GeneralLoot: return "loot";
                case AInteractionZone.EInteractionType.TreasureChest: return "treasure chest";
                case AInteractionZone.EInteractionType.HiddenTreasure: return "hidden treasure";
                case AInteractionZone.EInteractionType.Shrine: return "shrine";
                case AInteractionZone.EInteractionType.Recruits: return "recruits";
                case AInteractionZone.EInteractionType.MainQuest: return "main quest";
                case AInteractionZone.EInteractionType.SideQuest: return "side quest";
                case AInteractionZone.EInteractionType.MinorQuest: return "point of interest";
                case AInteractionZone.EInteractionType.Battle: return "battle";
                case AInteractionZone.EInteractionType.PuzzleBattle: return "puzzle";
                case AInteractionZone.EInteractionType.Dialogue: return "person";
                case AInteractionZone.EInteractionType.MessageBoard: return "message board";
                case AInteractionZone.EInteractionType.EasterEgg: return "curiosity";
                case AInteractionZone.EInteractionType.FastTravel: return "signpost";
                default: return t.ToString();
            }
        }

        public static string CategoryName(Category c)
        {
            switch (c)
            {
                case Category.Quests: return "quests";
                case Category.People: return "people";
                case Category.Loot: return "loot";
                default: return "places";
            }
        }

        private static readonly string[] Compass =
            { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

        /// <summary>"12 metres, north-east, 8 north 9 east"; "here" when within a metre.</summary>
        public static string Where(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist < 1f) return "here";
            int m = Mathf.RoundToInt(dist);
            string s = m + (m == 1 ? " metre, " : " metres, ") + Direction(dx, dz);
            int n = Mathf.RoundToInt(Math.Abs(dz)), e = Mathf.RoundToInt(Math.Abs(dx));
            if (n >= 1 && e >= 1 && !IsCardinal(dx, dz))
                s += ", " + n + (dz >= 0 ? " north " : " south ") + e + (dx >= 0 ? " east" : " west");
            return s;
        }

        public static string Direction(float dx, float dz)
        {
            float deg = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg; // 0 = north, 90 = east
            if (deg < 0) deg += 360f;
            return Compass[(int)Mathf.Round(deg / 45f) % 8];
        }

        private static bool IsCardinal(float dx, float dz)
        {
            float deg = Mathf.Atan2(Math.Abs(dx), Math.Abs(dz)) * Mathf.Rad2Deg;
            return deg < 10f || deg > 80f;
        }
    }
}
