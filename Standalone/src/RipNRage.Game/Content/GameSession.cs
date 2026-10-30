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
        /// <summary>
        /// True once the current run passed a checkpoint (or was loaded from a save). Dying then respawns
        /// at the checkpoint; a run that never reached one restarts the level even if an older
        /// checkpoint file for this level exists on disk.
        /// </summary>
        public static bool CheckpointReached { get; set; }
        /// <summary>True when RESTART after death will load the checkpoint rather than the level start.</summary>
        public static bool CanRespawnAtCheckpoint => CheckpointReached && GameSaveSystem.CheckpointAvailable(LevelPath);
        public static Behaviour Behaviour { get; private set; }
        /// <summary>Called with the session tools object after a level was built (the game adds its debug overlay / demo scripts).</summary>
        public static Action<GameObject> OnSceneBuilt;

        // ------------------------------------------------------------------ public transitions

        /// <summary>Builds a level and spawns the player (immediately: used at start-up and by the deferred transitions).</summary>
        public static void StartLevel(string levelPath)
        {
            LevelPath = levelPath;
            GameSaveSystem.CurrentLevelPath = levelPath;
            State = GameState.Playing;
            CheckpointReached = false;
            InputSystem.LockCursorInGameplay = true; // the gameplay scheme locks the mouse; UI screens release it
            Time.timeScale = 1f;
            TestArea.Build(levelPath);
            var tools = CreateSessionObject();
            tools.AddComponent<PauseMenu>();
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

        /// <summary>Runs <paramref name="action"/> at the start of the next frame when a scene is live, otherwise right away.</summary>
        private static void Defer(Action action)
        {
            var b = Behaviour as SessionBehaviour;
            if (b != null && !b.IsDestroyed && b.isActiveAndEnabled) b.pending = action;
            else action();
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
            EnragedInjector<CameraRecoil>.Reset();
            EnragedInjector<HudController>.Reset();
            // The ported PlayerState / Inventory read their starting values from SaveData.Current; a
            // fresh level must not inherit the health or ammo of the previous run (the save system's
            // Load() fills this in again, after the build, when restoring a game).
            SaveData.Current = new SaveData();
            RenderSettings.drawSky = true; // EnterWaterCollision may have hidden it when the old scene died under water
            Behaviour = null;
        }

        private static GameObject CreateSessionObject()
        {
            var tools = new GameObject("GameSession");
            Behaviour = tools.AddComponent<SessionBehaviour>();
            return tools;
        }

        private class SessionBehaviour : MonoBehaviour
        {
            public Action pending;
            protected override void Update()
            {
                if (pending != null) { var r = pending; pending = null; r(); }
            }
        }
    }
}
