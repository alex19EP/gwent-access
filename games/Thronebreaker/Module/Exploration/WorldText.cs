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
                case AInteractionZone.EInteractionType.GeneralLoot: return Loc.T("loot");
                case AInteractionZone.EInteractionType.TreasureChest: return Loc.T("treasure chest");
                case AInteractionZone.EInteractionType.HiddenTreasure: return Loc.T("hidden treasure");
                case AInteractionZone.EInteractionType.Shrine: return Loc.T("shrine");
                case AInteractionZone.EInteractionType.Recruits: return Loc.T("recruits");
                case AInteractionZone.EInteractionType.MainQuest: return Loc.T("main quest");
                case AInteractionZone.EInteractionType.SideQuest: return Loc.T("side quest");
                case AInteractionZone.EInteractionType.MinorQuest: return Loc.T("point of interest");
                case AInteractionZone.EInteractionType.Battle: return Loc.T("battle");
                case AInteractionZone.EInteractionType.PuzzleBattle: return Loc.T("puzzle");
                case AInteractionZone.EInteractionType.Dialogue: return Loc.T("person");
                case AInteractionZone.EInteractionType.MessageBoard: return Loc.T("message board");
                case AInteractionZone.EInteractionType.EasterEgg: return Loc.T("curiosity");
                case AInteractionZone.EInteractionType.FastTravel: return Loc.T("signpost");
                default: return t.ToString();
            }
        }

        public static string CategoryName(Category c)
        {
            switch (c)
            {
                case Category.Quests: return Loc.T("quests");
                case Category.People: return Loc.T("people");
                case Category.Loot: return Loc.T("loot");
                default: return Loc.T("places");
            }
        }

        private static readonly string[] Compass =
            { Loc.Mark("north"), Loc.Mark("north-east"), Loc.Mark("east"), Loc.Mark("south-east"), Loc.Mark("south"), Loc.Mark("south-west"), Loc.Mark("west"), Loc.Mark("north-west") };

        /// <summary>"12 metres, north-east, 8 north 9 east"; "here" when within a metre.</summary>
        public static string Where(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist < 1f) return Loc.T("here");
            int m = Mathf.RoundToInt(dist);
            string s = Loc.P(m, "{0} metre, {1}", "{0} metres, {1}", Direction(dx, dz));
            int n = Mathf.RoundToInt(Math.Abs(dz)), e = Mathf.RoundToInt(Math.Abs(dx));
            if (n >= 1 && e >= 1 && !IsCardinal(dx, dz))
                s += ", " + (dz >= 0
                    ? (dx >= 0 ? Loc.T("{0} north {1} east", n, e) : Loc.T("{0} north {1} west", n, e))
                    : (dx >= 0 ? Loc.T("{0} south {1} east", n, e) : Loc.T("{0} south {1} west", n, e)));
            return s;
        }

        public static string Direction(float dx, float dz)
        {
            float deg = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg; // 0 = north, 90 = east
            if (deg < 0) deg += 360f;
            return Loc.T(Compass[(int)Mathf.Round(deg / 45f) % 8]);
        }

        private static bool IsCardinal(float dx, float dz)
        {
            float deg = Mathf.Atan2(Math.Abs(dx), Math.Abs(dz)) * Mathf.Rad2Deg;
            return deg < 10f || deg > 80f;
        }
    }
}
