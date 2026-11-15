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
        /// <summary>The death's slow motion (the time scale the death sequence ramps to); 0 = none, full speed.</summary>
        public float SlowMotion;
        /// <summary>Real seconds since the player died (Dying and Dead): the death screen's timing, and Dying → Dead.</summary>
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

        /// <summary>The engine time scale the flow asks for: frozen while paused, else the death's slow motion, else 1.</summary>
        public static float TimeScale(in Flow f) =>
            f.Phase == FlowPhase.Paused ? 0f : f.SlowMotion > 0f ? f.SlowMotion : 1f;

        private static World W => Application.Current?.World;

        /// <summary>The current flow, for the scene side (menus, the death screen, the session). Default outside a level.</summary>
        public static Flow Current => W != null ? W.GetSingleton<Flow>() : default;

        /// <summary>Scene-side writes between ticks (the death sequence's slow motion, the save system's checkpoint
        /// flag) — never the phase, which only <see cref="FlowSystem"/> moves.</summary>
        public static void SetSlowMotion(float scale) { if (W != null) W.Singleton<Flow>().SlowMotion = scale; }
        public static void SetCheckpointReached() { if (W != null) W.Singleton<Flow>().CheckpointReached = true; }

        /// <summary>The pause menu asks; the answer is in <see cref="Current"/> from the next frame on.</summary>
        public static void RequestPause(bool pause) => W?.Post(new PauseRequest { Pause = pause });
    }

    /// <summary>
    /// Moves the run through its phases and is the only writer of the engine time scale. Consumes the player's
    /// <see cref="DeathEvent"/> (→ Dying) and <see cref="PauseRequest"/>s; ticks the death clock on real time
    /// (Dying → Dead at <see cref="Flows.DeathMenuDelay"/>). The time scale is applied on every change, in the
    /// same frame — the frame's delta was taken before the world ran, so a pause freezes from the next frame, as
    /// the menu's own write did — and every tick, which carries the death sequence's slow motion.
    /// </summary>
    [UpdateAfter(typeof(CombatBridgeSystem))] // the death sequence starts inside the bridge's dispatch (a coroutine's first step runs at once) and sets the first slow-motion value there: the scale is applied after it
    [Exclusive] // Time.timeScale and Time.unscaledDeltaTime: engine statics, not components
    public sealed class FlowSystem : SystemBase, ITick, IConsumes<DeathEvent>, IConsumes<PauseRequest>
    {
        public override void OnCreate() => World.SingletonEntity<Flow>(); // claimed outside a phase

        public void Tick(SystemContext ctx)
        {
            ref var f = ref ctx.World.Singleton<Flow>();
            f = Flows.Step(in f, Time.unscaledDeltaTime);
            Time.timeScale = Flows.TimeScale(in f);
        }

        public void Consume(SystemContext ctx, System.ReadOnlySpan<DeathEvent> events)
        {
            var w = ctx.World;
            ref var f = ref w.Singleton<Flow>();
            foreach (ref readonly var e in events)
                if (w.IsAlive(e.Entity) && w.Has<PlayerTag>(e.Entity)) f = Flows.PlayerDied(in f);
            Time.timeScale = Flows.TimeScale(in f);
        }

        public void Consume(SystemContext ctx, System.ReadOnlySpan<PauseRequest> events)
        {
            ref var f = ref ctx.World.Singleton<Flow>();
            foreach (ref readonly var e in events) f = Flows.Request(in f, e.Pause);
            Time.timeScale = Flows.TimeScale(in f);
        }
    }
}
