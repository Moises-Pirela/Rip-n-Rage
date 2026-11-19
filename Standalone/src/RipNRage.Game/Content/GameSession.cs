using System;
using RipNRage.Game.Content.Level;
using RipNRage.Game.Content.Vfx;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Carries out the flow's moves (8.8b): the menus, the developer keys and the systems <i>ask</i> the flow
    /// (<c>Flows.Ask</c>); <c>FlowSystem</c> decides against the transition table and records the accepted move in
    /// <c>Flow.Pending</c>; at the start of the next frame this class takes it and changes the scene — a scene change
    /// destroys the world, so no system can perform one. Every scene change resets the scene, clears the singletons
    /// the ported scripts use and rebuilds the level (or the title screen) and the session tools. It also resolves
    /// what needs the disk (which save CONTINUE loads, whether RESTART after death has a checkpoint). Boot builds the
    /// first scene directly: there is no world to ask yet.
    /// </summary>
    public static class GameSession
    {
        /// <summary>Level currently played (or shown behind the main menu). Null for the built-in arena.</summary>
        public static string LevelPath { get; private set; }
        /// <summary>Level "New game" starts (set by Program from the command line).</summary>
        public static string DefaultLevelPath;
        /// <summary>True when RESTART after death will load the checkpoint rather than the level start: this run
        /// passed a checkpoint or was loaded from a save (<c>Flow.CheckpointReached</c>, reset with the scene), and the
        /// checkpoint file belongs to this level — a run that never reached one restarts the level even if an older
        /// checkpoint file for it exists on disk.</summary>
        public static bool CanRespawnAtCheckpoint => RipNRage.Game.Ecs.Flows.Current.CheckpointReached && GameSaveSystem.CheckpointAvailable(LevelPath);
        /// <summary>Called after a scene was built (the game adds its debug overlay and the automation script — screens, 9.0).</summary>
        public static Action OnSceneBuilt;

        // ------------------------------------------------------------------ public transitions

        /// <summary>Builds a level and spawns the player (immediately: used at start-up and by the deferred transitions).</summary>
        public static void StartLevel(string levelPath)
        {
            LevelPath = levelPath;
            GameSaveSystem.CurrentLevelPath = levelPath;
            UiScreens.BeforeFrame = RunFrameStart;
            InputSystem.LockCursorInGameplay = true; // the gameplay scheme locks the mouse; UI screens release it
            LevelBuild.Build(levelPath);
            // 11.10d: the navigation grid is built here, with the level, not inside the first fixed tick where its
            // cost came out of the step budget. The bake flushes the collider registrations it needs itself.
            RipNRage.Game.Ecs.AI.NavBaking.Bake(Application.Current.World, "level start");
            UiScreens.Add(new PauseMenu());
            OnSceneBuilt?.Invoke();
        }

        /// <summary>Builds the title screen over an attract-mode view of <paramref name="backdropLevelPath"/> (default: the
        /// current / default level), now — boot, and the MainMenu move.</summary>
        public static void ShowMainMenu(string backdropLevelPath = null)
        {
            string level = backdropLevelPath ?? LevelPath ?? DefaultLevelPath;
            ResetWorld();
            LevelPath = level;
            GameSaveSystem.CurrentLevelPath = level;
            UiScreens.BeforeFrame = RunFrameStart;
            InputSystem.LockCursorInGameplay = false;
            LevelBuild.Build(level, spawnPlayer: false);
            RipNRage.Game.Ecs.Flows.BeginTitle();
            UiScreens.Add(new MainMenuScreen());
            OnSceneBuilt?.Invoke(); // the debug overlay and the automation scripts drive the title screen too (8.8)
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Tears the scene down and builds <paramref name="levelPath"/> again, now; <paramref name="afterBuild"/>
        /// runs on the new scene (the save's load).</summary>
        public static void Rebuild(string levelPath, Action afterBuild = null)
        {
            ResetWorld();
            StartLevel(levelPath);
            afterBuild?.Invoke();
        }

        // ------------------------------------------------------------------ the moves

        /// <summary>Frame start: the pending move, if any, then the save's apply-when-ready step.</summary>
        private static void RunFrameStart()
        {
            var move = RipNRage.Game.Ecs.Flows.TakePending();
            if (move != RipNRage.Game.Ecs.FlowAction.None) Carry(move);
            if (_afterBuild != null && !_afterBuild.MoveNext()) _afterBuild = null;
            GameVfxReload.Poll(); // vfx_bindings.json hot reload (9.4: was the VfxRuntime MonoBehaviour)
        }

        /// <summary>
        /// Carries out an accepted move. A move that cannot be done (no save, an unreadable one) changes nothing: the
        /// pending move is already taken, the phase is where it was, the menu that asked takes choices again, and the
        /// save system has said why.
        /// </summary>
        private static void Carry(RipNRage.Game.Ecs.FlowAction move)
        {
            switch (move)
            {
                case RipNRage.Game.Ecs.FlowAction.NewGame: Rebuild(DefaultLevelPath); break;
                case RipNRage.Game.Ecs.FlowAction.Continue:
                {
                    string slot = GameSaveSystem.ContinueSlot();
                    if (slot != null) GameSaveSystem.Load(slot);
                    break;
                }
                case RipNRage.Game.Ecs.FlowAction.Restart: Rebuild(LevelPath); break;
                case RipNRage.Game.Ecs.FlowAction.Respawn:
                    // from the checkpoint when this run reached one (and it belongs to this level), else the level start
                    if (!(CanRespawnAtCheckpoint && GameSaveSystem.Load("checkpoint"))) Rebuild(LevelPath);
                    break;
                case RipNRage.Game.Ecs.FlowAction.LoadCheckpoint: GameSaveSystem.Load("checkpoint"); break;
                case RipNRage.Game.Ecs.FlowAction.LoadQuick: GameSaveSystem.Load("quick"); break;
                case RipNRage.Game.Ecs.FlowAction.MainMenu: ShowMainMenu(); break;
                case RipNRage.Game.Ecs.FlowAction.Quit: Application.Quit(); break;
            }
        }

        // ------------------------------------------------------------------ internals

        // ------------------------------------------------------------------ frame-start work (8.4b: no SessionBehaviour)

        private static System.Collections.IEnumerator _afterBuild;

        /// <summary>Steps <paramref name="routine"/> once per frame, at frame start, until it ends or the scene is reset
        /// (the save's "apply once the player exists"; it yields only <c>null</c>).</summary>
        public static void RunAfterBuild(System.Collections.IEnumerator routine)
        {
            _afterBuild = routine;
        }

        /// <summary>Destroys everything and clears the per-level singletons / legacy save cache.</summary>
        private static void ResetWorld()
        {
            Application.Current.ResetScene();
            RipNRage.Game.Ecs.PlayerLink.Clear(); // the handles must not outlive their entities
            RenderSettings.drawSky = true; // EnterWaterCollision may have hidden it when the old scene died under water
            _afterBuild = null; // a load still waiting for the old scene's player is void
        }


    }
}
