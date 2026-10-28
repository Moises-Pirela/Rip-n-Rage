using System;
using RipNRage.Game.Content.Level;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Owns level (re)loading: resets the scene, clears the singletons the ported scripts use, rebuilds
    /// the level and the debug tools. Used by "new game", load game and the checkpoint system.
    /// </summary>
    public static class GameSession
    {
        public static string LevelPath { get; private set; }
        public static Behaviour Behaviour { get; private set; }
        public static Action<GameObject> OnSceneBuilt;

        public static void StartLevel(string levelPath)
        {
            LevelPath = levelPath;
            GameSaveSystem.CurrentLevelPath = levelPath;
            TestArea.Build(levelPath);
            var tools = new GameObject("GameSession");
            Behaviour = tools.AddComponent<SessionBehaviour>();
            OnSceneBuilt?.Invoke(tools);
        }

        /// <summary>Tears the scene down and builds the level again; <paramref name="afterBuild"/> runs on the new scene.</summary>
        public static void RestartLevel(string levelPath, Action afterBuild = null)
        {
            // deferred to the start of the next frame so no behaviour is mid-update
            var b = Behaviour;
            if (b != null && b.isActiveAndEnabled) { ((SessionBehaviour)b).pendingRestart = () => DoRestart(levelPath, afterBuild); }
            else DoRestart(levelPath, afterBuild);
        }

        private static void DoRestart(string levelPath, Action afterBuild)
        {
            var app = Application.Current;
            app.ResetScene();
            EnragedInjector<CombatController>.Reset();
            EnragedInjector<PlayerInputActions>.Reset();
            EnragedInjector<CameraRecoil>.Reset();
            EnragedInjector<HudController>.Reset();
            StartLevel(levelPath);
            afterBuild?.Invoke();
        }

        private class SessionBehaviour : MonoBehaviour
        {
            public Action pendingRestart;
            protected override void Update()
            {
                if (pendingRestart != null) { var r = pendingRestart; pendingRestart = null; r(); }
            }
        }
    }
}
