using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Nav;
using RipNRage.Engine.Ecs.Levels;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Game.Content.Level;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>
    /// What the grid is baked for. The clearance, the headroom and the step are **derived from the body that will
    /// walk it** — the Enemy character profile and the grunt's capsule — so a profile change cannot silently leave
    /// the grid describing a body that no longer exists. A check pins every one of these to its source.
    /// </summary>
    public static class NavProfile
    {
        /// <summary>The capsule's radius: the clearance a span must have.</summary>
        public static float Radius => Enemies.CapsuleRadius;

        /// <summary>The capsule's height (the body minus the gap it hovers over): the headroom a span must have.</summary>
        public static float Height => Enemies.Height - CharacterProfiles.Enemy.rideHeight;

        /// <summary>
        /// How far up a step may be. A character here has no step height: it rides a spring, and what it climbs is
        /// what the spring swallows — the ride height plus the ground tolerance.
        /// </summary>
        public static float StepUp => CharacterProfiles.Enemy.rideHeight + CharacterProfiles.Enemy.groundTolerance;

        public static float MaxSlopeDeg => CharacterProfiles.Enemy.maxSlopeAngle;

        /// <summary>The settings for a level, its three map keys over the body's derived limits.</summary>
        public static NavSettings For(in Worldspawn ws) => new NavSettings
        {
            CellSize = ws.NavCellSize > 0f ? ws.NavCellSize : 0.5f,
            CellHeight = ws.NavCellHeight > 0f ? ws.NavCellHeight : 0.2f,
            MaxDrop = ws.NavMaxDrop > 0f ? ws.NavMaxDrop : 2.5f,
            Radius = Radius, Height = Height, StepUp = StepUp, MaxSlopeDeg = MaxSlopeDeg,
        };
    }

    /// <summary>
    /// Bakes the level's navigation grid, once per level.
    ///
    /// **Why a system and not a call in <c>LevelBuild.Build</c>** (where 11.1's spec first put it): a brush becomes
    /// a static collider through <c>PhysicsEntities</c>, and those "register at the next fixed step" — at the end
    /// of the build the broadphase is still empty, so a bake there would cast its rays through nothing and find no
    /// floor anywhere. This runs after <see cref="PhysicsSyncSystem"/> has taken them in. It creates no entities,
    /// so the 9.x "builds are immediate, systems are not" rule is not in play.
    ///
    /// It rebakes whenever the worldspawn's <see cref="Worldspawn.Version"/> changes — which the loader bumps on
    /// every level build, a scene reset included, exactly as <c>WorldspawnSystem</c> uses it.
    /// </summary>
    [UpdateAfter(typeof(PhysicsSyncSystem))]
    [Exclusive] // reads the physics broadphase and writes engine-side shared data, not components
    public sealed class NavBakeSystem : SystemBase, IFixedTick
    {
        private int _bakedVersion = int.MinValue;

        public override void OnCreate() => World.SingletonEntity<Worldspawn>();

        public void FixedTick(SystemContext ctx)
        {
            ref var ws = ref World.Singleton<Worldspawn>();
            if (ws.Version == _bakedVersion) return;
            _bakedVersion = ws.Version;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var nav = NavBake.Build(World, NavProfile.For(in ws), LevelLoader.MeshChunkSize);
            watch.Stop();
            Navigation.Current = nav;

            int tiles = 0;
            foreach (var t in nav.Tiles) if (t != null) tiles++;
            if (tiles > 0) // a world with no level (the checks, the title before its backdrop) has nothing to say
                Debug.LogWarning($"nav: baked {tiles} tiles, {nav.TotalSpans} spans at {nav.Settings.CellSize:0.00} m in {watch.Elapsed.TotalMilliseconds:0.0} ms");
        }
    }
}
