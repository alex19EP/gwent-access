using System;

namespace GwentAccess
{
    /// <summary>
    /// The reloadable feature module. The module references the host directly (Plugin.LogInfo, MainThread,
    /// Plugin.Dir); the host knows the module only through this interface. One implementor per module assembly:
    /// the loader creates it, calls <see cref="Load"/>, then <see cref="Tick"/> every frame on the main thread.
    ///
    /// <see cref="IDisposable.Dispose"/> must undo every hook Load put into the game or the host: Harmony patches
    /// (per-generation id), threads, the speech observer, dev routes. Statics of the old generation just become
    /// garbage; only hooks outlive it. A missed one shows up as doubled speech after a reload. The loader disposes
    /// the old generation before loading the new one.
    /// </summary>
    public interface IModModule : IDisposable
    {
        void Load();
        void Tick();
    }
}
