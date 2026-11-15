using System.Collections.Generic;

namespace RipNRage.Engine.Graphics
{
    /// <summary>
    /// A screen at the UI boundary that is not a GameObject component (layer 8): it reads the world, takes the
    /// player's UI input in <see cref="Update"/> and draws in <see cref="IUiDrawer.DrawUi"/>. OOP is allowed here;
    /// what goes is the GameObject it used to hang off.
    /// </summary>
    public interface IUiScreen : IUiDrawer
    {
        /// <summary>Once per frame, at the start of the frame (before the scene and the world run).</summary>
        void Update();
        /// <summary>The screen is gone: the scene was reset or it removed itself. Release what it holds.</summary>
        void Close();
    }

    /// <summary>
    /// The live <see cref="IUiScreen"/>s, and a hook for frame-start work that is not a screen (the session's
    /// deferred transitions). <see cref="Application"/> updates them before the scene each frame, the render pipeline
    /// draws them with the GameObject drawers (same <see cref="IUiLayer"/> sort), and the scene reset closes them all.
    /// </summary>
    public static class UiScreens
    {
        private static readonly List<IUiScreen> _screens = new List<IUiScreen>();
        private static readonly List<IUiScreen> _iterate = new List<IUiScreen>();

        /// <summary>Runs at the start of every frame, before the screens update (the session's pending transition).</summary>
        public static System.Action BeforeFrame;

        public static IReadOnlyList<IUiScreen> All => _screens;

        public static T Add<T>(T screen) where T : IUiScreen
        {
            if (!_screens.Contains(screen)) _screens.Add(screen);
            return screen;
        }

        public static void Remove(IUiScreen screen)
        {
            if (_screens.Remove(screen)) screen.Close();
        }

        /// <summary>The scene reset: every screen closes.</summary>
        public static void Clear()
        {
            _iterate.Clear(); _iterate.AddRange(_screens);
            _screens.Clear();
            foreach (var s in _iterate) s.Close();
        }

        internal static void UpdateAll()
        {
            BeforeFrame?.Invoke();
            _iterate.Clear(); _iterate.AddRange(_screens); // a screen may add or remove screens while updating
            foreach (var s in _iterate) if (_screens.Contains(s)) s.Update();
        }
    }
}
