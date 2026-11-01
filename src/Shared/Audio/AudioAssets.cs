using System.IO;

namespace GwentAccess.Audio
{
    /// <summary>Where the sound stems are deployed: &lt;plugin&gt;\audio (build/Module.props, from the repo's
    /// assets/audio): cue earcons at the root, object stems under interactables\.</summary>
    internal static class AudioAssets
    {
        public static string Dir => Path.Combine(Plugin.Dir ?? "", "audio");

        /// <summary>An object stem, interactables\&lt;stem&gt;.wav.</summary>
        public static string Interactable(string stem) => Path.Combine(Dir, "interactables", stem + ".wav");

        /// <summary>A cue earcon, &lt;name&gt;.wav.</summary>
        public static string Cue(string name) => Path.Combine(Dir, name + ".wav");
    }
}
