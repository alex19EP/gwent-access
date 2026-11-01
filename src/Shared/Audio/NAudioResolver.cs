using System;
using System.IO;
using System.Reflection;

namespace GwentAccess.Audio
{
    /// <summary>
    /// Finds NAudio.dll beside the host (BepInEx\plugins\&lt;ModName&gt;, deployed by build/Module.props) when the
    /// module first touches an NAudio type. The module is byte-loaded, so the runtime has no folder to probe for its
    /// dependencies. Registered by ModuleMain.Load before anything reaches <see cref="AudioMixer"/>, removed on
    /// Dispose; the loaded NAudio stays for the process and the next generation reuses it.
    /// This class must not reference NAudio types itself.
    /// </summary>
    internal static class NAudioResolver
    {
        private static Assembly _naudio;

        public static void Register() => AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        public static void Unregister() => AppDomain.CurrentDomain.AssemblyResolve -= Resolve;

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            if (!args.Name.StartsWith("NAudio,", StringComparison.Ordinal)) return null;
            if (_naudio != null) return _naudio;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "NAudio") return _naudio = a;
            string path = Path.Combine(Plugin.Dir ?? "", "NAudio.dll");
            if (!File.Exists(path)) { Plugin.LogError("[audio] NAudio.dll not found at " + path); return null; }
            return _naudio = Assembly.LoadFrom(path);
        }
    }
}
