using System;
using RipNRage.Game.Content.Level;
using RipNRage.Game.Content.Vfx;

namespace RipNRage.Game.Content
{
    /// <summary>Where the game is: on the title screen or in a level.</summary>
    public enum GameState { None, MainMenu, Playing }

    /// <summary>
    /// Owns the game flow: main menu ↔ level, level (re)loading, respawning after death and quitting.
    /// Every transition resets the scene, clears the singletons the ported scripts use, rebuilds the
    /// level and the session tools (pause menu, debug overlay). Transitions requested while the scene
    /// is running (from an Update, a coroutine or a UI draw) are deferred to the start of the next
    /// frame so no behaviour is torn down mid-callback.
    /// </summary>
    public static class GameSession
    {
        /// <summary>Level currently played (or shown behind the main menu). Null for the built-in arena.</summary>
        public static string LevelPath { get; private set; }
        /// <summary>Level "New game" starts (set by Program from the command line).</summary>
        public static string DefaultLevelPath;
        public static GameState State { get; private set; }
        /// <summary>True when RESTART after death will load the checkpoint rather than the level start: this run
        /// passed a checkpoint or was loaded from a save (<c>Flow.CheckpointReached</c>, reset with the scene), and the
        /// checkpoint file belongs to this level — a run that never reached one restarts the level even if an older
        /// checkpoint file for it exists on disk.</summary>
        public static bool CanRespawnAtCheckpoint => RipNRage.Game.Ecs.Flows.Current.CheckpointReached && GameSaveSystem.CheckpointAvailable(LevelPath);
        /// <summary>Called with the session tools object after a level was built (the game adds its debug overlay / demo scripts).</summary>
        public static Action<GameObject> OnSceneBuilt;

        // ------------------------------------------------------------------ public transitions

        /// <summary>Builds a level and spawns the player (immediately: used at start-up and by the deferred transitions).</summary>
        public static void StartLevel(string levelPath)
        {
            LevelPath = levelPath;
            GameSaveSystem.CurrentLevelPath = levelPath;
            State = GameState.Playing;
            InputSystem.LockCursorInGameplay = true; // the gameplay scheme locks the mouse; UI screens release it
            TestArea.Build(levelPath);
            UiScreens.Add(new PauseMenu());
            var tools = CreateSessionObject();
            tools.AddComponent<VfxRuntime>();
            OnSceneBuilt?.Invoke(tools);
        }

        /// <summary>Shows the title screen over an attract-mode view of <paramref name="backdropLevelPath"/> (default: the current / default level).</summary>
        public static void ShowMainMenu(string backdropLevelPath = null)
        {
            string level = backdropLevelPath ?? LevelPath ?? DefaultLevelPath;
            Defer(() => DoShowMainMenu(level));
        }

        /// <summary>Tears the scene down and builds the level again; <paramref name="afterBuild"/> runs on the new scene.</summary>
        public static void RestartLevel(string levelPath, Action afterBuild = null)
        {
            Defer(() => DoRestart(levelPath, afterBuild));
        }

        /// <summary>Starts the default level from the beginning.</summary>
        public static void NewGame() => RestartLevel(DefaultLevelPath);

        /// <summary>Loads the most recent save (checkpoint, else quick save). Returns false when there is none.</summary>
        public static bool Continue()
        {
            string slot = GameSaveSystem.ContinueSlot();
            return slot != null && GameSaveSystem.Load(slot);
        }

        /// <summary>
        /// Respawns after death: from the last checkpoint when this run reached one (and the checkpoint
        /// save belongs to the current level), otherwise from the start of the level.
        /// </summary>
        public static void Respawn()
        {
            if (CanRespawnAtCheckpoint && GameSaveSystem.Load("checkpoint")) return;
            RestartLevel(LevelPath);
        }

        public static void Quit() => Application.Quit();

        // ------------------------------------------------------------------ internals

        // ------------------------------------------------------------------ frame-start work (8.4b: no SessionBehaviour)

        private static Action _pending;
        private static System.Collections.IEnumerator _afterBuild;

        /// <summary>Runs <paramref name="action"/> at the start of the next frame when a scene is live (a transition
        /// requested from a screen, a system or a UI draw must not tear the scene down mid-callback), otherwise right away.</summary>
        private static void Defer(Action action)
        {
            if (State == GameState.None) { action(); return; }
            _pending = action;
            UiScreens.BeforeFrame = RunFrameStart;
        }

        /// <summary>Steps <paramref name="routine"/> once per frame, at frame start, until it ends or the scene is reset
        /// (the save's "apply once the player exists"; it yields only <c>null</c>).</summary>
        public static void RunAfterBuild(System.Collections.IEnumerator routine)
        {
            _afterBuild = routine;
            UiScreens.BeforeFrame = RunFrameStart;
        }

        private static void RunFrameStart()
        {
            if (_pending != null) { var r = _pending; _pending = null; r(); }
            if (_afterBuild != null && !_afterBuild.MoveNext()) _afterBuild = null;
        }

        private static void DoRestart(string levelPath, Action afterBuild)
        {
            ResetWorld();
            StartLevel(levelPath);
            afterBuild?.Invoke();
        }

        private static void DoShowMainMenu(string backdropLevelPath)
        {
            ResetWorld();
            LevelPath = backdropLevelPath;
            GameSaveSystem.CurrentLevelPath = backdropLevelPath;
            State = GameState.MainMenu;
            InputSystem.LockCursorInGameplay = false;
            TestArea.Build(backdropLevelPath, spawnPlayer: false);
            var tools = CreateSessionObject();
            tools.AddComponent<MainMenuScreen>();
            tools.AddComponent<VfxRuntime>();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Destroys everything and clears the per-level singletons / legacy save cache.</summary>
        private static void ResetWorld()
        {
            Application.Current.ResetScene();
            EnragedInjector<CombatController>.Reset();
            EnragedInjector<PlayerInputActions>.Reset();
            // The ported PlayerState / Inventory read their starting values from SaveData.Current; a
            // fresh level must not inherit the health or ammo of the previous run (the save system's
            // Load() fills this in again, after the build, when restoring a game).
            SaveData.Current = new SaveData();
            RenderSettings.drawSky = true; // EnterWaterCollision may have hidden it when the old scene died under water
            _afterBuild = null; // a load still waiting for the old scene's player is void
        }

        /// <summary>The holder of the session's remaining GameObject tools (the VFX runtime, the debug overlay and the
        /// automation scripts <see cref="OnSceneBuilt"/> adds, the title screen) — no behaviour of its own.</summary>
        private static GameObject CreateSessionObject() => new GameObject("GameSession");
    }
}
