using System;
using System.IO;
using System.Reflection;

namespace GwentAccess
{
    /// <summary>
    /// Loads and hot-swaps the feature module (Module\GwentAccess.Module.dll in the plugin folder). The dll and its
    /// pdb are loaded FROM BYTES, so the files are never locked: rebuilding and copying them while the game runs is
    /// safe, and <see cref="Reload"/> swaps generations without a restart. Ported from WrathAccess's ModuleLoader.
    ///
    /// Each byte load gets its own load context on .NET 6; its references (this host, the IL2CPP interop
    /// assemblies, Harmony) resolve to the copies already loaded, so every generation talks to the same game.
    /// The module's assembly name is timestamped per build: it tells generations apart in the dev evaluator and
    /// lets <see cref="Reload"/> refuse a file that wasn't rebuilt. Old generations stay in memory until the game
    /// exits (Harmony and IL2CPP keep references, so they can't be unloaded) — a few MB per reload.
    /// </summary>
    public static class ModuleLoader
    {
        public static Assembly CurrentAssembly { get; private set; }

        /// <summary>The current generation's raw image, so the dev evaluator can reference a file-less assembly.</summary>
        public static byte[] CurrentImage { get; private set; }

        public static int Generation { get; private set; }

        private static IModModule _module;
        private static string _path;

        /// <summary>Load the first generation. False (logged) when the dll is missing or fails: the host then idles.</summary>
        public static bool LoadInitial(string modulePath)
        {
            _path = modulePath;
            return LoadGeneration();
        }

        /// <summary>Main thread. Dispose the current generation and load the file again. Refused when the file
        /// still carries the current assembly name: reloading an unrebuilt module would only re-run Load.</summary>
        public static string Reload()
        {
            string current = CurrentAssembly?.GetName().Name;
            string candidate = null;
            try { candidate = AssemblyName.GetAssemblyName(_path).Name; } catch { }
            if (current != null && candidate == current)
                return "module: gen " + Generation + " unchanged (" + current + "), rebuild it before reloading\n";
            DisposeCurrent();

            // If Dispose left patches behind (an unpatch that failed on IL2CPP), loading another generation would
            // stack hooks and double every spoken line. Refuse instead; the old patches keep running the old code.
            string leftover = LeftoverPatches();
            if (leftover != null)
            {
                Plugin.LogError("[module] reload aborted, patches still active after dispose: " + leftover);
                return "[reload aborted] patches from the old generation are still active, restart the game (just restart):\n" + leftover;
            }
            return LoadGeneration() ? Describe() : "[reload failed] see LogOutput.log\n";
        }

        /// <summary>Methods still patched by any module generation (Harmony ids "gwentaccess.genN"), or null.</summary>
        private static string LeftoverPatches()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                foreach (var m in HarmonyLib.Harmony.GetAllPatchedMethods())
                {
                    var info = HarmonyLib.Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    foreach (var owner in info.Owners)
                        if (owner.StartsWith(Plugin.Guid + ".gen", StringComparison.Ordinal))
                            sb.Append("  ").Append(m.DeclaringType?.Name).Append('.').Append(m.Name).Append(" (").Append(owner).Append(")\n");
                }
            }
            catch (Exception e) { return "  (could not list patches: " + e.Message + ")\n"; }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        public static void Tick()
        {
            try { _module?.Tick(); }
            catch (Exception e) { Plugin.LogError("[module tick] " + e); }
        }

        public static string Describe()
        {
            if (_module == null) return "module: none loaded\n";
            return "module: gen " + Generation
                + "\nassembly: " + CurrentAssembly?.GetName().Name
                + "\nfile: " + _path + " (written " + File.GetLastWriteTime(_path).ToString("yyyy-MM-dd HH:mm:ss") + ")\n";
        }

        private static void DisposeCurrent()
        {
            if (_module == null) return;
            try { _module.Dispose(); }
            catch (Exception e) { Plugin.LogError("[module dispose] " + e); }
            _module = null;
        }

        private static bool LoadGeneration()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    Plugin.LogError("[module] not found: " + _path);
                    return false;
                }
                byte[] image = File.ReadAllBytes(_path);
                string pdbPath = Path.ChangeExtension(_path, ".pdb");
                byte[] pdb = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;
                var asm = pdb != null ? Assembly.Load(image, pdb) : Assembly.Load(image);

                Type impl = null;
                foreach (var t in asm.GetTypes())
                    if (typeof(IModModule).IsAssignableFrom(t) && !t.IsAbstract) { impl = t; break; }
                if (impl == null)
                {
                    Plugin.LogError("[module] no IModModule implementor in " + asm.GetName().Name);
                    return false;
                }

                var module = (IModModule)Activator.CreateInstance(impl);
                Generation++;
                CurrentAssembly = asm;
                CurrentImage = image;
                _module = module;
                module.Load();
                Plugin.LogInfo("[module] gen " + Generation + " loaded: " + asm.GetName().Name);
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogError("[module load] " + e);
                return false;
            }
        }
    }
}
