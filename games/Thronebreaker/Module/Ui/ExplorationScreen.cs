using System.Collections.Generic;
using Access.Core.Graph;
using GwentAccess.Exploration;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Walking the world (spec §7.7: an exploration subsystem beside the HUD). Active while the game has the
    /// player exploring under input (CampaignManager.InsideExplorationModeWithInput: no dialogue, cutscene or
    /// panel on top). The graph is the exploration HUD, read as <see cref="PanelScreen"/> reads any panel (the
    /// quest, morale, resources, the menu button), so the arrows and Tab still walk it; the world itself is the
    /// <see cref="Scanner"/>'s keys, walking is WASD (<see cref="Walk"/>), and new things in view are heard
    /// (<see cref="World"/>).
    ///
    /// Enter, Backspace and Tab are left to the game: Enter is its interact button (held for loot), and Backspace
    /// and Tab are its own exploration keys.
    /// </summary>
    internal sealed class ExplorationScreen : Screen
    {
        public override string Key => "exploration";
        public override int Layer => 1;

        public override bool IsActive() => World.Exploring;

        public override void Build(GraphBuilder b) => PanelScreen.BuildPanels(b);

        public override IReadOnlyList<ScreenKey> ExtraKeys => Scanner.Keys;

        public override bool LeavesToGame(KeyCode key) =>
            key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Backspace || key == KeyCode.Tab;
    }
}
