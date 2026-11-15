using RipNRage.Engine.Ecs;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs
{
    /// <summary>Where a level run is. The title screen is not a phase: it is its own scene build (GameSession).</summary>
    public enum FlowPhase
    {
        /// <summary>The player is alive and the world runs.</summary>
        Playing,
        /// <summary>The pause menu is up and the world is frozen (time scale 0).</summary>
        Paused,
        /// <summary>The player died: the death sequence plays, the death screen fades in, no choice is offered yet.</summary>
        Dying,
        /// <summary>The death screen offers RESTART / MAIN MENU; the run ends when the player picks one.</summary>
        Dead,
    }

    /// <summary>
    /// The level run's state (8.2), a singleton: replaces <c>PauseMenu.IsPaused</c>, the menu's own "can I pause"
    /// test, the death screen's clock and <c>GameSession.CheckpointReached</c>. The scene reset destroys every entity
    /// and puts the carrier back at its default — a fresh level is Playing, at full speed, with no checkpoint — so
    /// every default here is the start-of-level value. Only <see cref="FlowSystem"/> moves <see cref="Phase"/>.
    /// </summary>
    public struct Flow : IComponent
    {
        public FlowPhase Phase;
        /// <summary>Real seconds since the player died (Dying and Dead): the death screen's timing, the death camera's
        /// fall, the slow motion, and Dying → Dead.</summary>
        public float DeathClock;
        /// <summary>This run passed a checkpoint or was loaded from a save: dying respawns at the checkpoint.</summary>
        public bool CheckpointReached;
    }

    /// <summary>Asks to pause (true) or resume (false). Posted by the pause menu; <see cref="FlowSystem"/> decides.</summary>
    public struct PauseRequest : IPostProcessEvent
    {
        public bool Pause;
    }

    /// <summary>The flow's rules as pure functions, and the scene-side access to the singleton.</summary>
    public static class Flows
    {
        /// <summary>Real seconds from the death to the death screen's choice (the old <c>DeathScreen.menuDelay</c>).</summary>
        public const float DeathMenuDelay = 2.4f;

        /// <summary>
        /// A pause request: pause only from Playing, resume only from Paused. Dying and Dead ignore it — there is
        /// nothing to pause once the player is dead (the old menu's <c>!player.IsDead</c> test, now a rule).
        /// </summary>
        public static Flow Request(in Flow f, bool pause)
        {
            var n = f;
            if (pause && f.Phase == FlowPhase.Playing) n.Phase = FlowPhase.Paused;
            else if (!pause && f.Phase == FlowPhase.Paused) n.Phase = FlowPhase.Playing;
            return n;
        }

        /// <summary>The player died. From Playing or Paused (a blow already in flight when the pause landed) the run
        /// starts dying, with the clock at zero; a second death changes nothing.</summary>
        public static Flow PlayerDied(in Flow f)
        {
            if (f.Phase == FlowPhase.Dying || f.Phase == FlowPhase.Dead) return f;
            var n = f;
            n.Phase = FlowPhase.Dying;
            n.DeathClock = 0f;
            return n;
        }

        /// <summary>One tick of real time: the death clock runs through Dying and Dead; at the delay the choice opens.</summary>
        public static Flow Step(in Flow f, float unscaledDt)
        {
            if (f.Phase != FlowPhase.Dying && f.Phase != FlowPhase.Dead) return f;
            var n = f;
            n.DeathClock += unscaledDt;
            if (n.Phase == FlowPhase.Dying && n.DeathClock >= DeathMenuDelay) n.Phase = FlowPhase.Dead;
            return n;
        }

        /// <summary>The engine time scale the flow asks for: frozen while paused; after the death, slowing from 1 to
        /// <see cref="DeathCams.SlowMotion"/> over the camera's fall (8.3: a function of the clock — nothing writes it);
        /// else 1.</summary>
        public static float TimeScale(in Flow f) => f.Phase switch
        {
            FlowPhase.Paused => 0f,
            FlowPhase.Dying or FlowPhase.Dead => Mathf.Lerp(1f, DeathCams.SlowMotion, DeathCams.Progress(f.DeathClock)),
            _ => 1f,
        };

        /// <summary>The player has the controls only while Playing: paused, dying and dead block the input.</summary>
        public static bool InputBlocked(in Flow f) => f.Phase != FlowPhase.Playing;

        private static World W => Application.Current?.World;

        /// <summary>The current flow, for the scene side (menus, the death screen, the session). Default outside a level.</summary>
        public static Flow Current => W != null ? W.GetSingleton<Flow>() : default;

        /// <summary>The run may act: the scene-side input paths (the automation scripts' presses, weapon switching)
        /// check this, as the ECS ones are gated by <see cref="InputBlocked"/>.</summary>
        public static bool Playing => Current.Phase == FlowPhase.Playing;

        /// <summary>The save system's checkpoint flag, written between ticks — never the phase, which only
        /// <see cref="FlowSystem"/> moves.</summary>
        public static void SetCheckpointReached() { if (W != null) W.Singleton<Flow>().CheckpointReached = true; }

        /// <summary>The pause menu asks; the answer is in <see cref="Current"/> from the next frame on.</summary>
        public static void RequestPause(bool pause) => W?.Post(new PauseRequest { Pause = pause });
    }

    /// <summary>
    /// Moves the run through its phases and owns the two engine gates the phase decides: the time scale and the
    /// input block (<c>InputOverride.Blocked</c>). Consumes the player's <see cref="DeathEvent"/> (→ Dying) and
    /// <see cref="PauseRequest"/>s; ticks the death clock on real time (Dying → Dead at
    /// <see cref="Flows.DeathMenuDelay"/>). The gates are applied on every change, in that frame — the frame's delta
    /// was taken before the world ran, so a pause freezes from the next frame — and every tick (the slow motion
    /// follows the clock).
    /// </summary>
    [Exclusive] // Time.timeScale, Time.unscaledDeltaTime and InputOverride.Blocked: engine statics, not components
    public sealed class FlowSystem : SystemBase, ITick, IConsumes<DeathEvent>, IConsumes<PauseRequest>
    {
        public override void OnCreate() => World.SingletonEntity<Flow>(); // claimed outside a phase

        public void Tick(SystemContext ctx)
        {
            ref var f = ref ctx.World.Singleton<Flow>();
            f = Flows.Step(in f, Time.unscaledDeltaTime);
            Apply(in f);
        }

        public void Consume(SystemContext ctx, System.ReadOnlySpan<DeathEvent> events)
        {
            var w = ctx.World;
            ref var f = ref w.Singleton<Flow>();
            foreach (ref readonly var e in events)
                if (w.IsAlive(e.Entity) && w.Has<PlayerTag>(e.Entity)) f = Flows.PlayerDied(in f);
            Apply(in f);
        }

        public void Consume(SystemContext ctx, System.ReadOnlySpan<PauseRequest> events)
        {
            ref var f = ref ctx.World.Singleton<Flow>();
            foreach (ref readonly var e in events) f = Flows.Request(in f, e.Pause);
            Apply(in f);
        }

        private static void Apply(in Flow f)
        {
            Time.timeScale = Flows.TimeScale(in f);
            RipNRage.Engine.Ecs.Input.InputOverride.Blocked = Flows.InputBlocked(in f);
        }
    }
}
