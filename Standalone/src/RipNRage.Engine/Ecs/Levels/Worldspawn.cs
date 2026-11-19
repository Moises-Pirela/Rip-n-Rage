using System;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.Graphics.Lighting;

namespace RipNRage.Engine.Ecs.Levels
{
    /// <summary>Which of the level's lighting keys were actually given: an absent key leaves the tunables' value alone.</summary>
    [Flags]
    public enum WorldspawnGiven
    {
        None = 0, SunElevation = 1, SunAzimuth = 2, SunColor = 4, SkyZenith = 8, SkyHorizon = 16, FogColor = 32,
    }

    /// <summary>
    /// The level's environment — a <b>component singleton</b> (10.5): one carrier entity, reached through
    /// <see cref="World.Singleton{T}"/> like <c>Flow</c> and <c>InputState</c>, baked from the map's worldspawn keys
    /// through the <c>worldspawn</c> prefab, written by the loader (and one day the editor), read by the systems
    /// that act on it. The sun, the sky and the fog override the tunables where a key was given; <see cref="KillY"/>
    /// is the height below which the level ends; <see cref="SkySpin"/> turns the sky, <see cref="SkyAngle"/> is how far
    /// it has turned. Whoever writes it bumps <see cref="Version"/> so <see cref="WorldspawnSystem"/> re-applies.
    /// </summary>
    public struct Worldspawn : IComponent
    {
        public WorldspawnGiven Given;
        public float SunElevation, SunAzimuth;
        public Color SunColor, SkyZenith, SkyHorizon, FogColor;
        public float KillY;
        /// <summary>The navigation bake's cell size and vertical quantum, and how far the AI may drop (11.1).</summary>
        public float NavCellSize, NavCellHeight, NavMaxDrop;
        /// <summary>Degrees per second the sky turns (0 = still).</summary>
        public float SkySpin;
        public float SkyAngle;
        public int Version;
    }

    public sealed class WorldspawnConfig : ComponentConfig<Worldspawn>
    {
        /// <summary>The level's title (a map key nothing reads yet; kept so the key is known).</summary>
        public string message;
        [InspectTip("degrees above the horizon; degrees around (0 = light travelling towards +Z). Empty = the tunables' value")]
        public float? sunElevation, sunAzimuth;
        [InspectColor, InspectTip("empty = the tunables' value")]
        public float[] sunColor, skyZenith, skyHorizon, fogColor;
        /// <summary>NaN = the loader derives it from the level (a metre under the water, or under the lowest brush).</summary>
        [InspectTip("the height the level ends below; NaN = a metre under the water, else under the lowest brush")]
        public float killY = float.NaN;
        [InspectRange(-90, 90, 0.1f), InspectTip("degrees per second the sky turns")]
        public float skySpin = 1.2f;

        [InspectRange(0.1f, 2f, 0.05f), InspectTip("navigation: metres per grid cell. Smaller is finer and costs memory (the dock is 8 MB at 0.5, 33 MB at 0.25)")]
        public float navCellSize = 0.5f;
        [InspectRange(0.05f, 1f, 0.05f), InspectTip("navigation: the vertical quantum - two floors closer than this in one column are one span")]
        public float navCellHeight = 0.2f;
        [InspectRange(0f, 10f, 0.25f), InspectTip("navigation: how far the AI may drop down on purpose. At least the ledge guard's reach, or the AI refuses what it used to walk off")]
        public float navMaxDrop = Nav.NavSettings.DefaultMaxDrop;

        public override Worldspawn Bake()
        {
            var ws = new Worldspawn
            {
                KillY = killY, SkySpin = skySpin,
                NavCellSize = navCellSize, NavCellHeight = navCellHeight, NavMaxDrop = navMaxDrop,
            };
            if (sunElevation.HasValue) { ws.Given |= WorldspawnGiven.SunElevation; ws.SunElevation = sunElevation.Value; }
            if (sunAzimuth.HasValue) { ws.Given |= WorldspawnGiven.SunAzimuth; ws.SunAzimuth = sunAzimuth.Value; }
            if (sunColor != null) { ws.Given |= WorldspawnGiven.SunColor; ws.SunColor = ConfigUtil.Color(sunColor); }
            if (skyZenith != null) { ws.Given |= WorldspawnGiven.SkyZenith; ws.SkyZenith = ConfigUtil.Color(skyZenith); }
            if (skyHorizon != null) { ws.Given |= WorldspawnGiven.SkyHorizon; ws.SkyHorizon = ConfigUtil.Color(skyHorizon); }
            if (fogColor != null) { ws.Given |= WorldspawnGiven.FogColor; ws.FogColor = ConfigUtil.Color(fogColor); }
            return ws;
        }
    }

    /// <summary>
    /// Acts on the <see cref="Worldspawn"/> singleton: pushes the given lighting keys into the renderer's settings
    /// whenever the singleton changes (<see cref="Worldspawn.Version"/>) or the settings object is replaced (a
    /// tunables reload used to wipe the level's sun — not any more), and turns the sky on sim time.
    /// </summary>
    [Exclusive] // the renderer's lighting and RenderSettings are engine state, not components
    public sealed class WorldspawnSystem : SystemBase, ITick
    {
        private int _appliedVersion = int.MinValue;
        private LightingSettings _appliedTo;

        /// <summary>Claims the singleton outside a phase (creating its carrier is a structural change).</summary>
        public override void OnCreate() => World.SingletonEntity<Worldspawn>();

        public void Tick(SystemContext ctx)
        {
            ref var ws = ref World.Singleton<Worldspawn>();
            var lighting = Application.Current?.Renderer?.Lighting;
            if (lighting != null && (ws.Version != _appliedVersion || !ReferenceEquals(lighting, _appliedTo)))
            {
                Apply(in ws, lighting);
                _appliedVersion = ws.Version; _appliedTo = lighting;
            }
            if (ws.SkySpin != 0f)
            {
                ws.SkyAngle = (ws.SkyAngle + ws.SkySpin * ctx.DeltaTime) % 360f;
                RenderSettings.skyboxRotation = ws.SkyAngle;
            }
        }

        /// <summary>The given keys, onto the lighting settings; the rest stay what the tunables say.</summary>
        public static void Apply(in Worldspawn ws, LightingSettings lt)
        {
            if ((ws.Given & WorldspawnGiven.SunElevation) != 0) lt.sunElevation = ws.SunElevation;
            if ((ws.Given & WorldspawnGiven.SunAzimuth) != 0) lt.sunAzimuth = ws.SunAzimuth;
            if ((ws.Given & WorldspawnGiven.SunColor) != 0) lt.sunColor = ws.SunColor;
            if ((ws.Given & WorldspawnGiven.SkyZenith) != 0) lt.skyZenith = ws.SkyZenith;
            if ((ws.Given & WorldspawnGiven.SkyHorizon) != 0) lt.skyHorizon = ws.SkyHorizon;
            if ((ws.Given & WorldspawnGiven.FogColor) != 0) lt.fogColor = ws.FogColor;
        }
    }
}
