using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace GwentAccess
{
    /// <summary>
    /// The permanent HOST, loaded by BepInEx. It owns only what can't hot-reload: this entry point, the per-frame
    /// tick (<see cref="MainThread"/>), the dev server and the <see cref="ModuleLoader"/>. Every feature lives in
    /// the byte-loaded module (Module\GwentAccess.Module.dll), which a rebuilt copy replaces through POST /reload
    /// without restarting the game. Changing host code needs a restart, so keep it small.
    /// The split follows WrathAccess (the Pathfinder: Wrath of the Righteous mod).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "gwentaccess";
        public const string Name = "Gwent Access";
        public const string Version = "0.27.0";

        private static ManualLogSource _log;
        public static void LogInfo(string msg) => _log?.LogInfo(msg);
        public static void LogError(string msg) => _log?.LogError(msg);

        /// <summary>BepInEx\plugins\GwentAccess: nvdaControllerClient.dll, the module, devserver.enable.</summary>
        public static string Dir { get; private set; }

        public override void Load()
        {
            _log = Log;
            Dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            Log.LogInfo($"{Name} {Version} host starting from {Dir}");

#if DEBUG
            Dev.DevServer.Start(Dir);
#endif
            try { new HarmonyLib.Harmony(Guid + ".host").CreateClassProcessor(typeof(FrameTickPatch)).Patch(); }
            catch (System.Exception e) { Log.LogError("Frame tick patch failed, the mod can't run: " + e); }

            ModuleLoader.LoadInitial(Path.Combine(Dir, "Module", "GwentAccess.Module.dll"));
        }
    }
}
