namespace GwentAccess
{
    /// <summary>
    /// Who the shared host (src/Host, src/Dev) is in this game: the BepInEx plugin's id, name and version, the
    /// module file it loads from its plugin folder, and the dev server's port and environment prefix. One per
    /// game (games/&lt;Game&gt;/Host/GameInfo.cs); nothing else in the host names a game.
    /// </summary>
    public static class GameInfo
    {
        public const string Guid = "gwentaccess";
        public const string Name = "Gwent Access";
        public const string Version = "0.27.0";

        /// <summary>Under the plugin folder's Module\ (stable name; the assembly inside is timestamped).</summary>
        public const string ModuleFile = "GwentAccess.Module.dll";

        /// <summary>WrathAccess 8771, RTAccess 8772, Gwent 8773, Thronebreaker 8774.</summary>
        public const int DevPort = 8773;

        /// <summary>Environment overrides: GWENTACCESS_DEV=1 starts the dev server, GWENTACCESS_DEV_PORT moves it.</summary>
        public const string EnvPrefix = "GWENTACCESS";
    }
}
