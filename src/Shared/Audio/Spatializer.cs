using System;

namespace GwentAccess.Audio
{
    /// <summary>The direction cues for one sound, heard from the listener.</summary>
    internal struct SpatialCue
    {
        public float Pan;         // -1 hard left (west) .. +1 hard right (east), centre-expanded
        public float ItdSamples;  // interaural delay: magnitude in samples, sign + = east (the far ear is delayed)
        public float RearShelfDb; // high-shelf on the whole sound: 0 ahead or beside .. negative behind
        public float FarShadowDb; // high-shelf on the far ear only (head shadow): 0 centred .. negative at the side
    }

    /// <summary>
    /// Places a sound around the listener, ported from RTAccess's Spatializer (WrathAccess's perceptual model,
    /// zlib; THIRD-PARTY-NOTICES.md). Directions are compass-fixed: callers pass world offsets east (+X) and north
    /// (+Z), and a sound to the west is always on the left. Three channels:
    /// east/west is a pan whose level difference is capped at 12 dB, plus an interaural time delay and a mild
    /// head shadow on the far ear (together they put the sound outside the head); north/south is timbre, a sound
    /// behind (south) darker by a high-shelf cut; distance is gain (<see cref="VolumeFor"/>, the caller's).
    /// </summary>
    internal static class Spatializer
    {
        public const float RearCornerHz = 3000f;
        public const float ShadowCornerHz = 1500f;
        private const float RearMaxCutDb = 10f;
        private const float ShadowMaxDb = 8f;
        private const float MaxIldDb = 12f;
        private const float PanExponent = 0.82f;               // steeper near the centre: a small offset is heard
        private const float MaxItdSamples = 0.00066f * AudioMixer.Rate; // head width / speed of sound
        private static readonly float WoodworthMax = (float)(Math.PI / 2 + 1);

        /// <summary>Direction cues for a sound <paramref name="dxEast"/>, <paramref name="dzNorth"/> metres from the
        /// listener. Within <paramref name="panWidth"/> metres the pan opens toward the side; past it, it follows
        /// the bearing.</summary>
        public static SpatialCue Cue(float dxEast, float dzNorth, float panWidth = 3f)
        {
            float dist = (float)Math.Sqrt(dxEast * dxEast + dzNorth * dzNorth);
            float lat = dist > 1e-4f ? Math.Clamp(dxEast / Math.Max(dist, panWidth), -1f, 1f) : 0f;
            if (lat != 0f) lat = Math.Sign(lat) * (float)Math.Pow(Math.Abs(lat), PanExponent);
            var cue = new SpatialCue { Pan = lat };
            float s = Math.Abs(lat);
            float theta = (float)Math.Asin(Math.Clamp(s, 0f, 1f));
            cue.ItdSamples = MaxItdSamples * ((theta + s) / WoodworthMax) * Math.Sign(lat);
            cue.FarShadowDb = -ShadowMaxDb * s;
            if (dist > 1e-4f)
            {
                float north = Math.Clamp(dzNorth / dist, -1f, 1f);
                if (north < 0f) cue.RearShelfDb = RearMaxCutDb * north;
            }
            return cue;
        }

        /// <summary>Stereo gains for a pan: constant power, the level difference capped so the far ear keeps signal.</summary>
        public static void PanGains(float pan, out float gainL, out float gainR)
        {
            float mag = Math.Clamp(Math.Abs(pan), 0f, 1f);
            float far = (float)Math.Pow(10f, -MaxIldDb * mag / 20f);
            float norm = 1f / (float)Math.Sqrt(1f + far * far);
            float near = norm;
            far *= norm;
            if (pan >= 0f) { gainR = near; gainL = far; }
            else { gainL = near; gainR = far; }
        }

        /// <summary>Distance to gain: 1 at the listener, refDist / (refDist + dist), floored so a far sound stays audible.</summary>
        public static float VolumeFor(float dist, float refDist, float minVol = 0.15f)
            => Math.Clamp(refDist / (refDist + Math.Max(0f, dist)), minVol, 1f);
    }
}
