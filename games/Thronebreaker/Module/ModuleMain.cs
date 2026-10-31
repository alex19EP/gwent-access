using System;

namespace GwentAccess
{
    /// <summary>
    /// Thronebreaker Access, the module's entry (see the Gwent module's ModuleMain for the contract). Milestone 1:
    /// speech, the graph navigator and the shared screens that already build against Thronebreaker (the settings
    /// screen). The battle screens and narration join as their shared files are ported (the Compile Remove list
    /// in ThronebreakerAccess.Module.csproj); Thronebreaker's own screens (dialogue, exploration, camp, map) are
    /// registered here.
    /// </summary>
    public sealed class ModuleMain : IModModule
    {
        private HarmonyLib.Harmony _harmony;

        public void Load()
        {
#if DEBUG
            Speech.Observer = Dev.DevServer.TapSpeech; // before the first Say, so /speech sees it
#endif
            ModConfig.Load();
            bool first = ModuleLoader.Generation == 1;
            if (Speech.Init())
                Speech.Say(first ? "Thronebreaker Access loaded." : "Thronebreaker Access reloaded.");

            _harmony = new HarmonyLib.Harmony(Plugin.Guid + ".gen" + ModuleLoader.Generation);
            int ok = 0, bad = 0;
            foreach (var t in typeof(ModuleMain).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Length == 0) continue;
                try { _harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (Exception pe) { bad++; Plugin.LogError("Patch " + t.Name + " failed: " + pe.Message); }
            }
            Plugin.LogInfo("Patches applied: " + ok + ", failed: " + bad);

            Ui.ScreenManager.Register(new Ui.PanelScreen());
            Ui.ScreenManager.Register(new Ui.MainMenuScreen());
            Ui.ScreenManager.Register(new Ui.CutsceneScreen());
            Ui.ScreenManager.Register(new Ui.DialogueScreen());
            Ui.ScreenManager.Register(new Ui.PopupScreen());
            Ui.ScreenManager.Register(new Ui.SettingsScreen());
        }

        public void Tick()
        {
            ModConfig.Tick();
            Ui.CutsceneScreen.SpeakSubtitles();
            Ui.ScreenManager.Tick();
            Ui.GraphNavigator.Tick();
        }

        public void Dispose()
        {
            try { Ui.GraphNavigator.Reset(); } catch (Exception e) { Plugin.LogError("[dispose] navigator: " + e); }
            try { Ui.ScreenManager.Reset(); } catch (Exception e) { Plugin.LogError("[dispose] screens: " + e); }
            try { _harmony?.UnpatchSelf(); } catch (Exception e) { Plugin.LogError("[dispose] harmony: " + e); }
            _harmony = null;
#if DEBUG
            Speech.Observer = null;
#endif
        }
    }
}
