using System;

namespace GwentAccess
{
    /// <summary>
    /// The module entry. <see cref="Load"/> boots speech,
    /// the hotkey thread and the Harmony patches; <see cref="Tick"/> is the per-frame work; <see cref="Dispose"/>
    /// undoes every hook so the next generation can take over (see <see cref="IModModule"/>).
    /// </summary>
    public sealed class ModuleMain : IModModule
    {
        private HarmonyLib.Harmony _harmony;

        public void Load()
        {
#if DEBUG
            Speech.Observer = Dev.DevServer.TapSpeech; // before the first Say, so /speech sees it
#endif
            bool first = ModuleLoader.Generation == 1;
            if (Speech.Init())
                Speech.Say(first
                    ? Plugin.Name + " " + Plugin.Version + " loaded. Press F1 for help."
                    : "Gwent Access reloaded.");
            Hotkeys.Start();
            KeyGlyphs.Install();
            ModConfig.Load();

            // Per-generation id: Dispose unpatches by id, and a fixed id would also strip a newer generation's
            // patches if an old Dispose ran late. Each class is patched separately so one failure can't disable the mod.
            _harmony = new HarmonyLib.Harmony(Plugin.Guid + ".gen" + ModuleLoader.Generation);
            int ok = 0, bad = 0;
            foreach (var t in typeof(ModuleMain).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Length == 0) continue;
                try { _harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (Exception pe) { bad++; Plugin.LogError("Patch " + t.Name + " failed: " + pe.Message); }
            }
            Plugin.LogInfo("Patches applied: " + ok + ", failed: " + bad);

            Ui.ScreenManager.Register(new Ui.MainMenuScreen());
            Ui.ScreenManager.Register(new Ui.SettingsScreen());
            Ui.ScreenManager.Register(new Ui.BattleScreen());
            Ui.ScreenManager.Register(new Ui.MulliganScreen());
            Ui.ScreenManager.Register(new Ui.ChoiceScreen());
            Ui.ScreenManager.Register(new Ui.EmoteScreen());
            Ui.ScreenManager.Register(new Ui.PileScreen());
            Ui.ScreenManager.Register(new Ui.LogScreen());
            Ui.ScreenManager.Register(new Ui.RewardBookScreen());
            Ui.ScreenManager.Register(new Ui.ShopScreen());
            Ui.ScreenManager.Register(new Ui.KegOpeningScreen());
            Ui.ScreenManager.Register(new Ui.ProfileScreen());
            Ui.ScreenManager.Register(new Ui.ContactsScreen());
            Ui.ScreenManager.Register(new Ui.QuestsScreen());
            Ui.ScreenManager.Register(new Ui.SideWindowScreen());
            Ui.ScreenManager.Register(new Ui.DailyLoginScreen());
            Ui.ScreenManager.Register(new Ui.InviteScreen());
            Ui.ScreenManager.Register(new Ui.StarterDeckScreen());
            Ui.ScreenManager.Register(new Ui.MillSparesScreen());
            Ui.ScreenManager.Register(new Ui.DeckBuilderScreen());
            Ui.ScreenManager.Register(new Ui.DraftScreen());
            Ui.ScreenManager.Register(new Ui.NewDeckScreen());
            Ui.ScreenManager.Register(new Ui.CycleScreen());
            Ui.ScreenManager.Register(new Ui.JourneyScreen());
            Ui.ScreenManager.Register(new Ui.CardPreviewScreen());
            Ui.ScreenManager.Register(new Ui.TutorialPanelScreen());
            Ui.ScreenManager.Register(new Ui.RewardPopupScreen());
            Ui.ScreenManager.Register(new Ui.PopupScreen());
            Ui.ScreenManager.Register(new Ui.BattleResultsScreen());
            Ui.ScreenManager.Register(new Ui.VersusScreen());
            Ui.ScreenManager.Register(new Ui.HelpScreen());
            Ui.ScreenManager.Register(new Ui.PanelScreen());
        }

        public void Tick()
        {
            if (!ModSwitch.On) return; // F8 switched the mod off: the game runs as without it
            ModConfig.Tick();
            TurnTimer.Tick();
            ControllerMode.Tick();
            Narrator.Tick();
            Ui.ScreenManager.Tick();
            Ui.GraphNavigator.Tick();
        }

        public void Dispose()
        {
            // First: the game's keyboard mappings must come back even if everything after this fails.
            try { Ui.GraphNavigator.Reset(); } catch (Exception e) { Plugin.LogError("[dispose] navigator: " + e); }
            try { Ui.ScreenManager.Reset(); } catch (Exception e) { Plugin.LogError("[dispose] screens: " + e); }
            try { Hotkeys.Stop(); } catch (Exception e) { Plugin.LogError("[dispose] hotkeys: " + e); }
            KeyGlyphs.Uninstall();
            try { _harmony?.UnpatchSelf(); } catch (Exception e) { Plugin.LogError("[dispose] harmony: " + e); }
            _harmony = null;
#if DEBUG
            Speech.Observer = null;
#endif
        }
    }
}
