using System.Collections.Generic;
using System;
using System.Collections.Concurrent;
using HarmonyLib;
using CrimsonUI;

namespace GwentAccess
{
    /// <summary>
    /// Lets the hotkey thread ask for work to be done on Unity's main thread.
    /// We piggyback on a game method that runs every frame (the input layers' Update, see FrameTickPatch)
    /// instead of adding our own Unity component. Host-side, so the dev server can use it
    /// and the module's per-frame work runs through <see cref="ModuleLoader.Tick"/>.
    /// </summary>
    public static class MainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        public static void Post(Action a) => Queue.Enqueue(a);

        private static readonly List<(float at, Action a)> Timed = new List<(float, Action)>();
        /// <summary>Run on the main thread after a delay (call from the main thread).</summary>
        public static void Later(float seconds, Action a) { lock (Timed) Timed.Add((UnityEngine.Time.realtimeSinceStartup + seconds, a)); }

        public static void Drain()
        {
            if (Timed.Count > 0)
            {
                var now = UnityEngine.Time.realtimeSinceStartup;
                List<Action> due = null;
                lock (Timed)
                {
                    for (int i = Timed.Count - 1; i >= 0; i--)
                        if (Timed[i].at <= now) { (due ??= new List<Action>()).Add(Timed[i].a); Timed.RemoveAt(i); }
                }
                if (due != null) foreach (var a in due) { try { a(); } catch (Exception e) { Plugin.LogInfo("Timed action failed: " + e); } }
            }
            while (Queue.TryDequeue(out var a))
            {
                try { a(); } catch (Exception e) { Plugin.LogInfo("Main-thread action failed: " + e); }
            }
        }
    }

    /// <summary>
    /// The per-frame tick, after an input layer's Update. Every live layer updates each frame, so the tick runs
    /// once per frame (the first layer to update), whatever the input mode. It used to follow
    /// UpdateSelectionNavigation, which Update calls only in keyboard/pad mode: once the mouse moved, the game
    /// switched to UpdateCursorNavigation and the whole mod (speech, keys, eval, reload) stopped.
    /// Applied by the host under its own Harmony id (not by the module's PatchAll), so it survives reloads.
    /// </summary>
    [HarmonyPatch(typeof(CrimsonInputLayer), "Update")]
    internal static class FrameTickPatch
    {
        private static int _frame = -1;

        static void Postfix()
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame == _frame) return;
            _frame = frame;
            MainThread.Drain(); Prism.Tick(); ModuleLoader.Tick();
        }
    }
}
