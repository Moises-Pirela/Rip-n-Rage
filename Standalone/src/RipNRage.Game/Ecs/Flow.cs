using RipNRage.Engine.Ecs;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs
{
    /// <summary>Where the game is. The title screen is its own scene build (GameSession), whose flow is Title.</summary>
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
        /// <summary>The title screen (no player). Set by the title build before its first tick — the one phase the
        /// build writes rather than <see cref="FlowSystem"/>: it says what kind of scene this is.</summary>
        Title,
    }

    /// <summary>
    /// What the flow can be asked for (8.8b). Pause / Resume move the phase; every other action is a <b>move</b> — a
    /// scene change the flow accepts into <see cref="Flow.Pending"/> and <c>GameSession</c> carries out at the start
    /// of the next frame (a scene change destroys the world, so no system can perform one).
    /// </summary>
    public enum FlowAction : byte
    {
        None,
        Pause, Resume,
        /// <summary>Title: the default level from the beginning.</summary>
        NewGame,
        /// <summary>Title: the most recent save (checkpoint, else quick save).</summary>
        Continue,
        /// <summary>The current level from the beginning.</summary>
        Restart,
        /// <summary>Dead: from the checkpoint when this run reached one, else from the level start.</summary>
        Respawn,
        LoadCheckpoint, LoadQuick,
        MainMenu, Quit,
    }

    /// <summary>
    /// The level run's state (8.2), a singleton: replaces <c>PauseMenu.IsPaused</c>, the menu's own "can I pause"
    /// test, the death screen's clock and <c>GameSession.CheckpointReached</c>. The scene reset destroys every entity
    /// and puts the carrier back at its default — a fresh level is Playing, at full speed, with no checkpoint — so
    /// every default here is the start-of-level value. Only <see cref="FlowSystem"/> moves <see cref="Phase"/>, save the
    /// title build's <see cref="Flows.BeginTitle"/>; only the session takes <see cref="Pending"/> (8.8b).
    /// </summary>
    public struct Flow : IComponent
    {
        public FlowPhase Phase;
        /// <summary>Real seconds since the player died (Dying and Dead): the death screen's timing, the death camera's
        /// fall, the slow motion, and Dying → Dead.</summary>
        public float DeathClock;
        /// <summary>This run passed a checkpoint or was loaded from a save: dying respawns at the checkpoint.</summary>
        public bool CheckpointReached;
        /// <summary>The accepted move, waiting for the next frame start (None: nothing pending). While one is pending
        /// every further request is refused — the first choice wins, and the menus go quiet.</summary>
        public FlowAction Pending;
    }

    /// <summary>
    /// Asks the flow for an action (8.8b; replaces <c>PauseRequest</c>). Posted by the menus, the developer keys
    /// (<see cref="Debug"/>) and, later, by systems; <see cref="FlowSystem"/> decides against <see cref="Flows.Allows"/>.
    /// </summary>
    public struct FlowRequest : IPostProcessEvent
    {
        public FlowAction Action;
        /// <summary>A developer key (F7 / F8 / F9): restart and load are allowed in any phase of a level.</summary>
        public bool Debug;
    }

    /// <summary>The flow's rules as pure functions, and the scene-side access to the singleton.</summary>
    public static class Flows
    {
        /// <summary>Real seconds from the death to the death screen's choice (the old <c>DeathScreen.menuDelay</c>).</summary>
        public const float DeathMenuDelay = 2.4f;

        /// <summary>
        /// The transition table: may <paramref name="a"/> be asked for in <paramref name="phase"/>? Pause only from
        /// Playing, resume only from Paused (nothing to pause once the player is dead); the title offers NEW GAME /
        /// CONTINUE / QUIT, the pause menu RESTART / LOAD CHECKPOINT / MAIN MENU / QUIT, the death screen RESTART
        /// (respawn) / MAIN MENU. A developer key may restart or load in any phase of a level, never on the title.
        /// </summary>
        public static bool Allows(FlowPhase phase, FlowAction a, bool debug = false)
        {
            if (debug)
                return phase != FlowPhase.Title && (a == FlowAction.Restart || a == FlowAction.LoadCheckpoint || a == FlowAction.LoadQuick);
            return phase switch
            {
                FlowPhase.Playing => a == FlowAction.Pause,
                FlowPhase.Paused => a is FlowAction.Resume or FlowAction.Restart or FlowAction.LoadCheckpoint or FlowAction.MainMenu or FlowAction.Quit,
                FlowPhase.Dead => a is FlowAction.Respawn or FlowAction.MainMenu,
                FlowPhase.Title => a is FlowAction.NewGame or FlowAction.Continue or FlowAction.Quit,
                _ => false, // Dying: no choice is offered yet
            };
        }

        /// <summary>
        /// A request against the table. Pause / Resume move the phase at once; a move is accepted into
        /// <see cref="Flow.Pending"/> and the phase stays (a paused world stays frozen until the scene goes). While a
        /// move is pending everything is refused: the first choice wins.
        /// </summary>
        public static Flow Request(in Flow f, FlowAction a, bool debug = false)
        {
            if (f.Pending != FlowAction.None || a == FlowAction.None || !Allows(f.Phase, a, debug)) return f;
            var n = f;
            if (a == FlowAction.Pause) n.Phase = FlowPhase.Paused;
            else if (a == FlowAction.Resume) n.Phase = FlowPhase.Playing;
            else n.Pending = a;
            return n;
        }

        /// <summary>The player died. From Playing or Paused (a blow already in flight when the pause landed) the run
        /// starts dying, with the clock at zero; a second death changes nothing.</summary>
        public static Flow PlayerDied(in Flow f)
        {
            if (f.Phase != FlowPhase.Playing && f.Phase != FlowPhase.Paused) return f;
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

        /// <summary>The player has the controls only while Playing: paused, dying, dead and the title block the input.</summary>
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

        /// <summary>A move is waiting for the next frame start: the menus stop taking choices.</summary>
        public static bool Busy => Current.Pending != FlowAction.None;

        /// <summary>Asks the flow (menus, developer keys); the answer is in <see cref="Current"/> from the next frame on.</summary>
        public static void Ask(FlowAction a, bool debug = false) => W?.Post(new FlowRequest { Action = a, Debug = debug });

        public static void RequestPause(bool pause) => Ask(pause ? FlowAction.Pause : FlowAction.Resume);

        /// <summary>The title build says what kind of scene this is (before its first tick; the documented exception
        /// to "only FlowSystem moves the phase").</summary>
        public static void BeginTitle() { if (W != null) W.Singleton<Flow>().Phase = FlowPhase.Title; }

        /// <summary>The session takes the pending move to carry it out (frame start, between ticks).</summary>
        public static FlowAction TakePending()
        {
            if (W == null || !W.HasSingleton<Flow>()) return FlowAction.None;
            ref var f = ref W.Singleton<Flow>();
            var a = f.Pending;
            f.Pending = FlowAction.None;
            return a;
        }
    }

    /// <summary>
    /// Moves the run through its phases and owns the two engine gates the phase decides: the time scale and the
    /// input block (<c>InputOverride.Blocked</c>). Consumes the player's <see cref="DeathEvent"/> (→ Dying) and
    /// <see cref="FlowRequest"/>s (pause / resume, and moves into <see cref="Flow.Pending"/>); ticks the death clock on real time (Dying → Dead at
    /// <see cref="Flows.DeathMenuDelay"/>). The gates are applied on every change, in that frame — the frame's delta
    /// was taken before the world ran, so a pause freezes from the next frame — and every tick (the slow motion
    /// follows the clock).
    /// </summary>
    [Exclusive] // Time.timeScale, Time.unscaledDeltaTime and InputOverride.Blocked: engine statics, not components
    public sealed class FlowSystem : SystemBase, ITick, IConsumes<DeathEvent>, IConsumes<FlowRequest>
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

        public void Consume(SystemContext ctx, System.ReadOnlySpan<FlowRequest> events)
        {
            ref var f = ref ctx.World.Singleton<Flow>();
            foreach (ref readonly var e in events) f = Flows.Request(in f, e.Action, e.Debug);
            Apply(in f);
        }

        private static void Apply(in Flow f)
        {
            Time.timeScale = Flows.TimeScale(in f);
            RipNRage.Engine.Ecs.Input.InputOverride.Blocked = Flows.InputBlocked(in f);
        }
    }
}
