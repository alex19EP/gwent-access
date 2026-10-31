using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace GwentAccess
{
    /// <summary>
    /// The permanent HOST, loaded by BepInEx. It owns only what can't hot-reload: this entry point, the per-frame
    /// tick (<see cref="MainThread"/>), the <see cref="Prism"/> speech context, the dev server and the <see cref="ModuleLoader"/>. Every feature lives in
    /// the byte-loaded module (Module\&lt;ModName&gt;.Module.dll), which a rebuilt copy replaces through POST /reload
    /// without restarting the game. Changing host code needs a restart, so keep it small.
    /// The split follows WrathAccess (the Pathfinder: Wrath of the Righteous mod).
    ///
    /// Shared by every game's mod; which game this build is for comes from its <see cref="GameInfo"/>.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = GameInfo.Guid;
        public const string Name = GameInfo.Name;
        public const string Version = GameInfo.Version;

        private static ManualLogSource _log;
        public static void LogInfo(string msg) => _log?.LogInfo(msg);
        public static void LogError(string msg) => _log?.LogError(msg);

        /// <summary>BepInEx\plugins\&lt;ModName&gt;: prism.dll, the module, devserver.enable.</summary>
        public static string Dir { get; private set; }

        public override void Load()
        {
            _log = Log;
            Dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            Log.LogInfo($"{Name} {Version} host starting from {Dir}");
            Prism.Init(Dir); // once per process: every module generation speaks through it

#if DEBUG
            Dev.DevServer.Start(Dir);
#endif
            try { new HarmonyLib.Harmony(Guid + ".host").CreateClassProcessor(typeof(FrameTickPatch)).Patch(); }
            catch (System.Exception e) { Log.LogError("Frame tick patch failed, the mod can't run: " + e); }

            ModuleLoader.LoadInitial(Path.Combine(Dir, "Module", GameInfo.ModuleFile));
        }
    }
}
