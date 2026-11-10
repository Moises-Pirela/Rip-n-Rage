using System;
// This namespace is called Input, so the bare name `Input` resolves to it and hides the device class
// RipNRage.Engine.Input (the same trap as Ecs.Physics vs Engine.Physics). Reach the devices through this alias.
using Devices = RipNRage.Engine.Input;

namespace RipNRage.Engine.Ecs.Input
{
    /// <summary>
    /// The frame's gameplay input as a <see cref="World.Singleton{T}"/>: one snapshot of the "gameplay" action map
    /// that every system reads instead of polling devices. <see cref="EcsInputSystem"/> writes it first in Tick, so
    /// a system's view of the input never changes mid-frame.
    ///
    /// <para><see cref="Move"/> is the raw stick / WASD vector (x = strafe, y = forward, clamped to the unit disc) and
    /// <see cref="Look"/> the mouse delta in pixels plus the right stick scaled to the same units — the look system
    /// multiplies by sensitivity. The button fields follow the action map: <c>Pressed</c> is the rising edge of this
    /// frame, <c>Held</c> the level.</para>
    ///
    /// <para>While a menu, the death screen or a settings page owns the input the map is not <c>Enabled</c> and every
    /// field here reads as neutral, so gameplay goes quiet without each system having to ask.</para>
    /// </summary>
    public struct InputState : IComponent
    {
        public Vector2 Move, Look;
        public bool JumpPressed, JumpHeld;
        /// <summary>
        /// A jump press waiting to be acted on. Set on the rising edge and <b>kept across frames</b> until a fixed
        /// step consumes it (<see cref="ConsumeJump"/>) — rendering runs faster than the fixed clock, so a press that
        /// only lived for the frame it happened in would be dropped most of the time. This is the old controller's
        /// <c>IsJumping</c> / <c>CanJump</c> latch.
        /// </summary>
        public bool JumpQueued;
        public bool SlidePressed, SlideHeld;
        public bool DashPressed;
        public bool FirePressed, FireHeld;
        public bool AltFirePressed;
        /// <summary>False while a UI screen owns the input: the other fields are neutral.</summary>
        public bool Live;

        /// <summary>Takes the queued jump, if any, and clears it. Call from the fixed step that acts on it.</summary>
        public bool ConsumeJump()
        {
            if (!JumpQueued) return false;
            JumpQueued = false;
            return true;
        }
    }

    /// <summary>
    /// Overrides that replace the real device input for a frame (the scripted automation runs: <c>--script walk</c>,
    /// <c>strafe</c>, ...). Set from outside the ECS; <see cref="EcsInputSystem"/> applies whichever are non-null.
    /// </summary>
    public static class InputOverride
    {
        public static Vector2? Move, Look;
        public static bool? Fire, Jump, Slide, Dash;

        /// <summary>
        /// Hard gate: while set, <see cref="InputState"/> reads as neutral and not <c>Live</c> whatever the devices
        /// or the overrides say (a cutscene / a script taking the controls away from the player).
        /// </summary>
        public static bool Blocked;

        private static bool _jumpOnce;
        /// <summary>Queues a single jump press (the scripts' old <c>OnJump()</c>).</summary>
        public static void JumpOnce() => _jumpOnce = true;
        internal static bool TakeJumpOnce() { bool j = _jumpOnce; _jumpOnce = false; return j; }

        public static void Clear() { Move = Look = null; Fire = Jump = Slide = Dash = null; _jumpOnce = false; }
    }

    /// <summary>Samples the "gameplay" action map into the <see cref="InputState"/> singleton (first in Tick).</summary>
    public sealed class EcsInputSystem : SystemBase, ITick
    {
        /// <summary>After (re)enabling, fire must be released once: the click that closed a menu must not fire the gun.</summary>
        private bool _waitForFireRelease = true;
        private bool _wasLive;

        /// <summary>Claims the singleton outside a phase: creating its carrier entity is a structural change.</summary>
        public override void OnCreate() => World.SingletonEntity<InputState>();

        public void Tick(SystemContext ctx)
        {
            var G = InputSystem.Gameplay;
            bool live = G.Enabled && !InputOverride.Blocked;
            if (live && !_wasLive) _waitForFireRelease = true;
            _wasLive = live;

            ref var s = ref World.Singleton<InputState>();
            bool queuedJump = s.JumpQueued && !InputOverride.Blocked; // survives until a fixed step consumes it
            s = default;
            s.JumpQueued = queuedJump;
            s.Live = live;
            if (InputOverride.Blocked) return;

            if (live)
            {
                Vector2 move = new Vector2(G["move_right"].Value - G["move_left"].Value, G["move_forward"].Value - G["move_back"].Value);
                if (move.sqrMagnitude > 1f) move = move.normalized;
                s.Move = move;

                Vector2 look = Devices.mouseDelta * InputSystem.mouseSensitivity;
                if (InputSystem.invertMouseY) look.y = -look.y;
                Vector2 stick = InputSystem.RightStick();
                if (stick.sqrMagnitude > 0f)
                {
                    Vector2 g = stick * (InputSystem.gamepadLookSpeed * Time.deltaTime * 10f);
                    if (InputSystem.invertGamepadY) g.y = -g.y;
                    look += g;
                }
                s.Look = look;

                bool fireHeld = G["fire"].Held;
                if (_waitForFireRelease && !fireHeld) _waitForFireRelease = false;
                if (!_waitForFireRelease) { s.FireHeld = fireHeld; s.FirePressed = G["fire"].Pressed; }
                s.AltFirePressed = G["alt_fire"].Pressed;

                s.JumpPressed = G["jump"].Pressed; s.JumpHeld = G["jump"].Held;
                if (s.JumpPressed) s.JumpQueued = true;
                s.SlidePressed = G["slide"].Pressed; s.SlideHeld = G["slide"].Held;
                s.DashPressed = G["dash"].Pressed;
            }

            // ---- automation overrides (they drive the game with no devices, so they ignore Live) ----
            if (InputOverride.Move is Vector2 m) { s.Move = m; s.Live = true; }
            if (InputOverride.Look is Vector2 l) { s.Look = l; s.Live = true; }
            if (InputOverride.Fire is bool f) { s.FireHeld = f; s.FirePressed = f; s.Live = true; }
            if (InputOverride.Jump is bool j) { s.JumpPressed = j; s.JumpHeld = j; if (j) s.JumpQueued = true; s.Live = true; }
            if (InputOverride.TakeJumpOnce()) { s.JumpPressed = true; s.JumpQueued = true; s.Live = true; }
            if (InputOverride.Slide is bool sl) { s.SlidePressed = sl; s.SlideHeld = sl; s.Live = true; }
            if (InputOverride.Dash is bool d) { s.DashPressed = d; s.Live = true; }
        }
    }
}
