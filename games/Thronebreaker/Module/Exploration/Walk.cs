using GwentVisuals.Campaign;
using HarmonyLib;
using UnityEngine;

namespace GwentAccess.Exploration
{
    /// <summary>
    /// Walking with the keyboard. Thronebreaker walks only with the mouse (click or hold on the ground) or the
    /// gamepad's left stick: its keyboard map binds nothing to the walk axes. WASD is fed in as that stick, through
    /// the one place the game reads it (RedInput.InputWrapper.GetAxis for Rewired actions 14 LeftStickX and 15
    /// LeftStickY, read by PlayerInputController each frame), so everything downstream is the game's own gamepad
    /// walk: the camera-relative heading (W is up the screen, which never rotates: north), the 180° turn, the
    /// footsteps, the zones the character walks into.
    ///
    /// The stick's tilt sets the pace: a half tilt walks, a full one runs. WASD walks, Shift+WASD runs. Only while
    /// the game has the player walking under input (CampaignManager.InsideExplorationModeWithInput: not in a
    /// dialogue, a cutscene or a panel), and only while a key is held: a real stick still works.
    /// </summary>
    internal static class Walk
    {
        /// <summary>Stick tilt for walking; the game's dead zone is 0.2 (CampaignPlayerControllerSettings
        /// m_GamepadWalkThreshold), a full tilt runs.</summary>
        private const float WalkTilt = 0.5f;

        private static Vector2 _stick;
        private static bool _wasMoving;

        /// <summary>Per frame, after <see cref="Ui.Keys.Update"/>.</summary>
        public static void Tick()
        {
            var v = Vector2.zero;
            if (CanWalk())
            {
                if (Ui.Keys.Held(KeyCode.W)) v.y += 1;
                if (Ui.Keys.Held(KeyCode.S)) v.y -= 1;
                if (Ui.Keys.Held(KeyCode.D)) v.x += 1;
                if (Ui.Keys.Held(KeyCode.A)) v.x -= 1;
                if (v != Vector2.zero) v = v.normalized * (Ui.Keys.Shift ? 1f : WalkTilt);
            }
            _stick = v;
            bool moving = v != Vector2.zero;
            if (moving != _wasMoving) Plugin.LogInfo("[walk] " + (moving ? "stick " + v : "stop"));
            _wasMoving = moving;
        }

        public static void Reset() => _stick = Vector2.zero;

        /// <summary>A WASD key is walking the character this frame.</summary>
        public static bool Moving => _stick != Vector2.zero;

        private static bool CanWalk()
        {
            if (Ui.Keys.Ctrl || Ui.Keys.Alt) return false;
            var cm = CampaignManager.Instance;
            return cm != null && cm.InsideExplorationModeWithInput;
        }

        [HarmonyPatch(typeof(RedInput.InputWrapper), nameof(RedInput.InputWrapper.GetAxis))]
        private static class AxisPatch
        {
            private static void Postfix(int actionId, ref float __result)
            {
                if (_stick == Vector2.zero) return;
                if (actionId == 14) __result = _stick.x;
                else if (actionId == 15) __result = _stick.y;
            }
        }
    }
}
