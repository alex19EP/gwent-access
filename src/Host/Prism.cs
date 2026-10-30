using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GwentAccess
{
    /// <summary>
    /// Speech through the native Prism library (https://github.com/ethindp/prism), which speaks through the running
    /// screen reader (NVDA, JAWS, ...) or a Windows voice (OneCore, SAPI) when there is none. Same C ABI as
    /// RTAccess's <c>PrismSpeech</c>.
    ///
    /// It lives in the HOST because a native context belongs to the process, not to a module generation: Prism is
    /// initialised once in <see cref="Plugin.Load"/> and every module generation speaks through it, so a hot reload
    /// neither re-initialises nor leaks a context. The module's <c>Speech</c> is the only caller.
    ///
    /// Prism picks a backend once (a screen reader only counts if it is already running), so <see cref="Tick"/>
    /// keeps the choice current: while a lower-priority backend speaks (typically the Windows voice because NVDA
    /// started after Gwent), it probes the higher-priority ones every few seconds and switches when one comes up;
    /// after a failed speak (the screen reader quit) it picks the best again. Probing creates and initialises the
    /// candidate, which for screen readers is a window lookup or a local RPC ping. It runs on the game thread (the
    /// host frame tick), because some backends (JAWS) create COM objects bound to the creating thread's apartment.
    ///
    /// <c>prism.dll</c> sits in the plugin folder, built from the pinned <c>third_party/prism</c> submodule by
    /// <c>scripts/build-prism.ps1</c> (see <c>build/Prism.targets</c>). The CLR's DllImport probing does not look
    /// in the plugin folder, so it is loaded by full path and its exports bound as function pointers. No
    /// <c>nvdaControllerClient.dll</c> is needed: Prism generates the NVDA RPC stubs itself.
    /// </summary>
    public static unsafe class Prism
    {
        private const int PRISM_OK = 0;
        private const int PRISM_ERROR_NOT_SPEAKING = 10;
        // PRISM_CONFIG_VERSION from prism.h. Safe to hardcode: we ship the prism.dll built from the submodule commit
        // this file is pinned against (v0.18.3). Everything past `version` is optional; a zeroed tail asks for the
        // global backend registry and no availability-polling thread.
        private const byte PRISM_CONFIG_VERSION = 4;

        /// <summary>How often a lower-priority backend looks for a better one.</summary>
        private const long ProbeIntervalMs = 3000;

        /// <summary>Mirror of prism.h's <c>PrismConfig</c> (natural alignment, 56 bytes on x64). The bool is a byte
        /// to keep the struct blittable. Built by hand instead of calling <c>prism_config_init</c>, which returns the
        /// struct by value; zero-init plus the version byte is all that function does.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PrismConfig
        {
            public byte version;
            public IntPtr registry;
            public IntPtr availability_callback;
            public IntPtr availability_userdata;
            public uint availability_poll_interval_ms;
            public uint availability_debounce_samples;
            public uint availability_backoff_max_ms;
            public byte availability_auto_power_manage;
            public IntPtr availability_baseline_callback;
        }

        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr> _createBest;
        private static delegate* unmanaged[Cdecl]<IntPtr, ulong, IntPtr> _create;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> _initialize;
        private static delegate* unmanaged[Cdecl]<IntPtr, void> _free;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr> _name;
        private static delegate* unmanaged[Cdecl]<IntPtr, nuint> _count;
        private static delegate* unmanaged[Cdecl]<IntPtr, nuint, ulong> _idAt;
        private static delegate* unmanaged[Cdecl]<IntPtr, byte*, ulong> _idOf;
        private static delegate* unmanaged[Cdecl]<IntPtr, ulong, int> _priority;
        private static delegate* unmanaged[Cdecl]<IntPtr, byte*, byte, int> _speak;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> _stop;
        private static delegate* unmanaged[Cdecl]<int, IntPtr> _errorString;

        /// <summary>Guards <see cref="_backend"/>: Speak runs on the hotkey thread and the game thread, Tick swaps.</summary>
        private static readonly object Lock = new object();
        private static IntPtr _ctx;
        private static IntPtr _backend;
        private static int _backendPriority;
        /// <summary>Set by a failed speak; the next Tick picks the best backend again.</summary>
        private static volatile bool _repick;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long _nextProbeMs;

        /// <summary>The backend speaking now (e.g. "NVDA", "OneCore"), or null when speech is unavailable.</summary>
        public static string BackendName { get; private set; }

        public static bool Ready => _backend != IntPtr.Zero;

        /// <summary>Load prism.dll from <paramref name="pluginDir"/> and pick the best backend. Logs and returns
        /// false (never throws) when the library is missing or nothing can speak.</summary>
        public static bool Init(string pluginDir)
        {
            if (_ctx != IntPtr.Zero) return Ready;
            try
            {
                string path = Path.Combine(pluginDir, "prism.dll");
                if (!File.Exists(path)) { Plugin.LogError("Prism: not found: " + path); return false; }
                IntPtr lib = NativeLibrary.Load(path);
                IntPtr Export(string name) => NativeLibrary.GetExport(lib, name);

                var init = (delegate* unmanaged[Cdecl]<PrismConfig*, IntPtr>)Export("prism_init");
                var version = (delegate* unmanaged[Cdecl]<IntPtr>)Export("prism_version_string");
                _createBest = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)Export("prism_registry_create_best");
                _create = (delegate* unmanaged[Cdecl]<IntPtr, ulong, IntPtr>)Export("prism_registry_create");
                _initialize = (delegate* unmanaged[Cdecl]<IntPtr, int>)Export("prism_backend_initialize");
                _free = (delegate* unmanaged[Cdecl]<IntPtr, void>)Export("prism_backend_free");
                _name = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)Export("prism_backend_name");
                _count = (delegate* unmanaged[Cdecl]<IntPtr, nuint>)Export("prism_registry_count");
                _idAt = (delegate* unmanaged[Cdecl]<IntPtr, nuint, ulong>)Export("prism_registry_id_at");
                _idOf = (delegate* unmanaged[Cdecl]<IntPtr, byte*, ulong>)Export("prism_registry_id");
                _priority = (delegate* unmanaged[Cdecl]<IntPtr, ulong, int>)Export("prism_registry_priority");
                _speak = (delegate* unmanaged[Cdecl]<IntPtr, byte*, byte, int>)Export("prism_backend_speak");
                _stop = (delegate* unmanaged[Cdecl]<IntPtr, int>)Export("prism_backend_stop");
                _errorString = (delegate* unmanaged[Cdecl]<int, IntPtr>)Export("prism_error_string");

                var cfg = new PrismConfig { version = PRISM_CONFIG_VERSION };
                _ctx = init(&cfg);
                if (_ctx == IntPtr.Zero) { Plugin.LogError("Prism: prism_init returned null"); return false; }
                Plugin.LogInfo("Prism " + Marshal.PtrToStringUTF8(version()) + " loaded");

                // Already initialised by create_best.
                IntPtr best = _createBest(_ctx);
                if (best == IntPtr.Zero) { Plugin.LogError("Prism: no speech backend available"); return false; }
                Use(best);
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogError("Prism: could not load: " + e);
                return false;
            }
        }

        /// <summary>Game thread, every frame (host frame tick). Re-picks after a failed speak, and while a
        /// lower-priority backend speaks, probes the better ones every <see cref="ProbeIntervalMs"/>.</summary>
        public static void Tick()
        {
            if (_ctx == IntPtr.Zero) return;
            try
            {
                if (_repick)
                {
                    _repick = false;
                    IntPtr best = _createBest(_ctx);
                    if (best != IntPtr.Zero) Use(best);
                    return;
                }
                long now = Clock.ElapsedMilliseconds;
                if (now < _nextProbeMs) return;
                _nextProbeMs = now + ProbeIntervalMs;
                IntPtr better = FindBetter();
                if (better != IntPtr.Zero) Use(better);
            }
            catch (Exception e) { Plugin.LogError("Prism: re-pick failed: " + e); }
        }

        /// <summary>The first backend, by priority, that outranks the current one and initialises now; or zero. The
        /// registry is sorted highest priority first, so this stops at the current backend's own rank.</summary>
        private static IntPtr FindBetter()
        {
            int current = Ready ? _backendPriority : int.MinValue;
            nuint n = _count(_ctx);
            for (nuint i = 0; i < n; i++)
            {
                ulong id = _idAt(_ctx, i);
                if (_priority(_ctx, id) <= current) break;
                IntPtr candidate = _create(_ctx, id);
                if (candidate == IntPtr.Zero) continue;
                if (_initialize(candidate) == PRISM_OK) return candidate;
                _free(candidate);
            }
            return IntPtr.Zero;
        }

        /// <summary>Make <paramref name="backend"/> (initialised) the one that speaks, freeing the previous one.</summary>
        private static void Use(IntPtr backend)
        {
            IntPtr namePtr = _name(backend);
            string name = namePtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(namePtr) : "unknown";
            int priority;
            fixed (byte* p = Utf8Z(name)) priority = _priority(_ctx, _idOf(_ctx, p));
            IntPtr old;
            lock (Lock)
            {
                old = _backend;
                _backend = backend;
                _backendPriority = priority;
                BackendName = name;
                if (old != IntPtr.Zero) _free(old);
            }
            Plugin.LogInfo("Prism: speaking through " + name + (old != IntPtr.Zero ? " (switched)" : ""));
        }

        /// <summary>Speak <paramref name="text"/>; <paramref name="interrupt"/> cuts off whatever is being said.</summary>
        public static void Speak(string text, bool interrupt)
        {
            if (string.IsNullOrEmpty(text)) return;
            // Prism takes NUL-terminated UTF-8; the card and menu text is localised, so ANSI would corrupt it.
            byte[] utf8 = Utf8Z(text);
            int err;
            lock (Lock)
            {
                if (_backend == IntPtr.Zero) return;
                fixed (byte* p = utf8) err = _speak(_backend, p, interrupt ? (byte)1 : (byte)0);
            }
            if (err != PRISM_OK)
            {
                Plugin.LogError("Prism: speak failed on " + BackendName + ": " + ErrorString(err));
                _repick = true;
            }
        }

        public static void Stop()
        {
            int err;
            lock (Lock)
            {
                if (_backend == IntPtr.Zero) return;
                err = _stop(_backend);
            }
            if (err != PRISM_OK && err != PRISM_ERROR_NOT_SPEAKING) Plugin.LogError("Prism: stop failed: " + ErrorString(err));
        }

        private static byte[] Utf8Z(string s)
        {
            byte[] z = new byte[Encoding.UTF8.GetByteCount(s) + 1]; // trailing 0 is the terminator
            Encoding.UTF8.GetBytes(s, 0, s.Length, z, 0);
            return z;
        }

        private static string ErrorString(int err)
        {
            IntPtr p = _errorString(err);
            return p != IntPtr.Zero ? Marshal.PtrToStringUTF8(p) : "code " + err;
        }
    }
}
