using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace GwentAccess
{
    /// <summary>
    /// The mod's options, in BepInEx\config\&lt;plugin id&gt;.cfg (gwentaccess.cfg, thronebreakeraccess.cfg):
    /// BepInEx's own config file, written with every option and its description on first load, edited by hand
    /// (or a BepInEx config manager). The module rereads it when the file changes, so an edit applies with the
    /// game running.
    /// </summary>
    internal static class ModConfig
    {
        private static ConfigFile _file;
        private static ConfigEntry<bool> _readSubtitles;
        private static DateTime _stamp;
        private static float _nextCheck;

        /// <summary>Speak cutscene subtitles and dialogue lines as they appear (off: the game's voice acting
        /// speaks them; a dialogue line is still read on request).</summary>
        public static bool ReadSubtitles => _readSubtitles?.Value ?? true;

        public static void Load()
        {
            try
            {
                string path = Path.Combine(Paths.ConfigPath, Plugin.Guid + ".cfg");
                _file = new ConfigFile(path, true);
                _readSubtitles = _file.Bind("Speech", "ReadSubtitles", true,
                    "Speak cutscene subtitles and dialogue lines as they appear. Off: the game's voice acting speaks them; a dialogue line is still read when you move to it or press Space.");
                _stamp = File.GetLastWriteTimeUtc(path);
                Plugin.LogInfo("[config] " + path + ": ReadSubtitles=" + ReadSubtitles);
            }
            catch (Exception e) { Plugin.LogError("[config] load failed, using defaults: " + e.Message); }
        }

        /// <summary>Per frame: reread the file once a second if it changed.</summary>
        public static void Tick()
        {
            if (_file == null || UnityEngine.Time.realtimeSinceStartup < _nextCheck) return;
            _nextCheck = UnityEngine.Time.realtimeSinceStartup + 1f;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(_file.ConfigFilePath);
                if (stamp == _stamp) return;
                _stamp = stamp;
                _file.Reload();
                Plugin.LogInfo("[config] reloaded: ReadSubtitles=" + ReadSubtitles);
            }
            catch (Exception e) { Plugin.LogError("[config] reload failed: " + e.Message); }
        }
    }
}
