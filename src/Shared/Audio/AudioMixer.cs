using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GwentAccess.Audio
{
    /// <summary>
    /// The mod's own sound output: one stereo <see cref="MixingSampleProvider"/> feeding one
    /// <see cref="WaveOutEvent"/>, every sound a voice on it. Ported from RTAccess's AudioMixer (itself WrathAccess's
    /// NAudioEngine, zlib; THIRD-PARTY-NOTICES.md), trimmed to what the mod plays: one-shots, plain-panned or
    /// placed in space (<see cref="Spatializer"/>). The device opens on the first sound and closes on
    /// <see cref="Shutdown"/> (module dispose), so a reloaded generation opens its own.
    ///
    /// Sounds are WAVs under &lt;plugin&gt;\audio (<see cref="AudioAssets"/>), decoded once to 44.1 kHz interleaved
    /// stereo float and cached by path. The mod's sounds play beside the game's, not through its Wwise mix:
    /// <see cref="ModConfig.SoundVolume"/> sets their level.
    /// </summary>
    internal static class AudioMixer
    {
        public const int Rate = 44100;

        private static MixingSampleProvider _mixer;
        private static IWavePlayer _out;
        private static readonly object _gate = new object();
        // Decoded WAVs by path; a null entry means "tried and failed", so a missing file is logged once.
        private static readonly Dictionary<string, float[]> _cache = new Dictionary<string, float[]>();

        private static void EnsureStarted()
        {
            if (_out != null) return;
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
            // A 100 ms buffer rides through GC and frame hitches without underrunning.
            _out = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 4 };
            _out.Init(_mixer);
            _out.Play();
            Plugin.LogInfo("[audio] output started");
        }

        /// <summary>Play a sound file once, centred or panned (-1 left .. +1 right).</summary>
        public static void Play(string path, float volume, float pan = 0f)
        {
            var buf = Load(path);
            if (buf == null) return;
            Add(() => new OneShot(buf, volume * ModConfig.SoundVolume, pan));
        }

        /// <summary>Play a sound file once, placed by <paramref name="cue"/>: pan, interaural delay, head shadow and
        /// the darker timbre of a sound behind.</summary>
        public static void PlayAt(string path, SpatialCue cue, float volume)
        {
            var buf = Load(path);
            if (buf == null) return;
            Add(() =>
            {
                var v = new PositionalEmitter(buf);
                v.SetPlacement(cue, volume * ModConfig.SoundVolume);
                return v;
            });
        }

        private static void Add(Func<ISampleProvider> make)
        {
            if (ModConfig.SoundVolume <= 0f) return;
            try
            {
                lock (_gate)
                {
                    EnsureStarted();
                    _mixer.AddMixerInput(make());
                }
            }
            catch (Exception e) { Plugin.LogError("[audio] play failed: " + e.Message); }
        }

        /// <summary>A WAV decoded to the mix format, cached; null (logged once) when missing or unreadable.</summary>
        public static float[] Load(string path)
        {
            lock (_gate) { if (_cache.TryGetValue(path, out var hit)) return hit; }
            float[] buf = null;
            try { buf = Decode(path); }
            catch (Exception e) { Plugin.LogError("[audio] decode " + path + ": " + e.Message); }
            lock (_gate) { _cache[path] = buf; }
            return buf;
        }

        private static float[] Decode(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("missing", path);
            using (var reader = new WaveFileReader(path))
            {
                ISampleProvider sp = reader.ToSampleProvider();
                if (sp.WaveFormat.SampleRate != Rate) sp = new WdlResamplingSampleProvider(sp, Rate);
                if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
                var all = new List<float>(Rate);
                var tmp = new float[Rate * 2];
                int n;
                while ((n = sp.Read(tmp, 0, tmp.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add(tmp[i]);
                return all.ToArray();
            }
        }

        /// <summary>Close the device (module dispose). The cache goes too: the next generation decodes afresh.</summary>
        public static void Shutdown()
        {
            lock (_gate)
            {
                try { _out?.Stop(); _out?.Dispose(); } catch { }
                _out = null;
                _mixer = null;
                _cache.Clear();
            }
        }

        // A cached buffer played once with a constant-power pan; returns short at the end so the mixer drops it.
        private sealed class OneShot : ISampleProvider
        {
            private readonly float[] _buf;
            private readonly float _gainL, _gainR;
            private int _pos;

            public OneShot(float[] buf, float vol, float pan)
            {
                _buf = buf;
                float t = (Math.Clamp(pan, -1f, 1f) + 1f) * 0.5f * (float)(Math.PI / 2.0);
                _gainL = vol * (float)Math.Cos(t);
                _gainR = vol * (float)Math.Sin(t);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int n = Math.Min(count, _buf.Length - _pos);
                for (int i = 0; i < n; i++)
                    buffer[offset + i] = _buf[_pos + i] * (((_pos + i) & 1) == 0 ? _gainL : _gainR);
                _pos += n;
                return n;
            }
        }

        // RBJ high-shelf biquad (slope 1); at 0 dB the coefficients are exactly the identity.
        private struct HighShelf
        {
            private float _b0, _b1, _b2, _a1, _a2, _z1, _z2;

            public void Set(float cornerHz, float dbGain)
            {
                double A = Math.Pow(10.0, dbGain / 40.0);
                double w0 = 2.0 * Math.PI * cornerHz / Rate;
                double cosw = Math.Cos(w0);
                double alpha = Math.Sin(w0) / 2.0 * Math.Sqrt(2.0);
                double k = 2.0 * Math.Sqrt(A) * alpha;
                double a0 = (A + 1) - (A - 1) * cosw + k;
                _b0 = (float)(A * ((A + 1) + (A - 1) * cosw + k) / a0);
                _b1 = (float)(-2.0 * A * ((A - 1) + (A + 1) * cosw) / a0);
                _b2 = (float)(A * ((A + 1) + (A - 1) * cosw - k) / a0);
                _a1 = (float)(2.0 * ((A - 1) - (A + 1) * cosw) / a0);
                _a2 = (float)(((A + 1) - (A - 1) * cosw - k) / a0);
            }

            public float Transform(float x)
            {
                float y = _b0 * x + _z1;
                _z1 = _b1 * x - _a1 * y + _z2;
                _z2 = _b2 * x - _a2 * y;
                return y;
            }
        }

        // A one-shot placed in space, from WrathAccess's PositionalEmitter: the (mono) source darkened by a rear
        // high-shelf when behind, split L/R by capped-ILD gains, the far ear delayed (ITD) and head-shadowed.
        // The placement is fixed at fire time: these are short cues.
        private sealed class PositionalEmitter : ISampleProvider
        {
            private const int RingSize = 64, RingMask = RingSize - 1, TailFrames = RingSize;
            private readonly float[] _buf;
            private readonly int _srcFrames;
            private readonly float[] _ring = new float[RingSize];
            private HighShelf _rear, _shadow;
            private float _gainL, _gainR, _itd;
            private int _frame;

            public PositionalEmitter(float[] buf)
            {
                _buf = buf;
                _srcFrames = buf.Length / 2;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
            }

            public WaveFormat WaveFormat { get; }

            public void SetPlacement(SpatialCue cue, float volume)
            {
                Spatializer.PanGains(cue.Pan, out float gl, out float gr);
                _gainL = volume * gl;
                _gainR = volume * gr;
                _itd = cue.ItdSamples;
                _rear.Set(Spatializer.RearCornerHz, cue.RearShelfDb);
                _shadow.Set(Spatializer.ShadowCornerHz, cue.FarShadowDb);
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2, produced = 0;
                float itdMag = Math.Abs(_itd);
                int itdInt = Math.Min((int)itdMag, RingSize - 2);
                float frac = itdMag - (int)itdMag;
                bool delayLeft = _itd >= 0f; // source to the right: the left ear hears it later
                for (int f = 0; f < frames && _frame < _srcFrames + TailFrames; f++, _frame++)
                {
                    float m = _rear.Transform(_frame < _srcFrames ? _buf[_frame * 2] : 0f);
                    _ring[_frame & RingMask] = m;
                    int d0 = _frame - itdInt, d1 = d0 - 1;
                    float s0 = d0 >= 0 ? _ring[d0 & RingMask] : 0f;
                    float s1 = d1 >= 0 ? _ring[d1 & RingMask] : 0f;
                    float far = _shadow.Transform(s0 + (s1 - s0) * frac);
                    buffer[offset + produced++] = (delayLeft ? far : m) * _gainL;
                    buffer[offset + produced++] = (delayLeft ? m : far) * _gainR;
                }
                return produced;
            }
        }
    }
}
