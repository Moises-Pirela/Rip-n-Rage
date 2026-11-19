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

        /// <summary>The body floats this high; its collider starts here above the feet.</summary>
        public static float Ride => CharacterProfiles.Enemy.rideHeight;
        /// <summary>Feet to crown.</summary>
        public static float BodyHeight => Enemies.Height;
        /// <summary>The transform's position is this far above the feet.</summary>
        public static float CentreAboveFeet => Enemies.Height * 0.5f + CharacterProfiles.Enemy.rideHeight;

        /// <summary>The settings for a level, its three map keys over the body's derived limits.</summary>
        public static NavSettings For(in Worldspawn ws) => new NavSettings
        {
            CellSize = ws.NavCellSize > 0f ? ws.NavCellSize : 0.5f,
            CellHeight = ws.NavCellHeight > 0f ? ws.NavCellHeight : 0.2f,
            MaxDrop = ws.NavMaxDrop > 0f ? ws.NavMaxDrop : NavSettings.DefaultMaxDrop,
            Radius = Radius, Height = Height, StepUp = StepUp, MaxSlopeDeg = MaxSlopeDeg,
        };
    }

    /// <summary>
    /// Bakes the level's navigation grid.
    ///
    /// **Where it runs, and why** (11.10d, after measuring): the bake samples with rays, so it needs the
    /// *broadphase*. A collider built outside a fixed step is in the ECS immediately and in the broadphase only
    /// after the next <c>PhysicsSyncSystem</c> — measured at the gates: 30 statics in the ECS against 29 in the
    /// broadphase on the frame the level was built. (The 11.12 note said this the other way round; it was wrong.)
    /// So the bake asks <see cref="PhysicsEntities.FlushRegistrations"/> for the broadphase to catch up, and then
    /// it can run anywhere — at level start, synchronously, instead of inside the first fixed tick where its
    /// 200 ms came out of the frame's step budget.
    /// </summary>
    public static class NavBaking
    {
        /// <summary>The worldspawn version the current grid was baked for.</summary>
        public static int BakedVersion = int.MinValue;

        // one query, kept: Query().Build() registers a new query on the world every call and nothing removes it,
        // and the world outlives a level (ResetScene destroys the entities, not the world) - 11.10d.
        private static Query _statics;
        private static World _statics_world;

        private static Query Statics(World world)
        {
            if (!ReferenceEquals(world, _statics_world)) { _statics_world = world; _statics = NavBake.StaticQuery(world); }
            return _statics;
        }

        /// <summary>Bakes (or loads) the grid for the level in <paramref name="world"/> now.</summary>
        public static void Bake(World world, string reason)
        {
            ref var ws = ref world.Singleton<Worldspawn>();
            var settings = NavProfile.For(in ws);
            float tileSize = LevelLoader.MeshChunkSize;
            bool stale = reason == "rebake";

            PhysicsEntities.FlushRegistrations(world); // the rays need the broadphase, not the ECS
            int queriesBefore = world.QueryCount;
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var statics = Statics(world);
            NavBake.Bounds(world, statics, out _, out _, out _, out var allBoxes);
            // the two views of the level must agree before a grid is written down under a key the ECS measured:
            // the rays read the broadphase, and a collider reaches it one step after the ECS has it.
            bool consistent = allBoxes.Count == RipNRage.Engine.Physics.World.StaticCount;

            ulong key = NavCache.Key(allBoxes, in settings, tileSize);
            string map = System.IO.Path.GetFileNameWithoutExtension(RipNRage.Game.Content.GameSession.LevelPath ?? "level");
            string file = System.IO.Path.Combine(Application.persistentDataPath, "nav", $"{map}-{key:x16}.nav");

            NavWorld nav = null;
            bool warm = !stale && NavCache.TryLoad(file, in settings, tileSize, out nav, world.Jobs);
            if (!warm)
            {
                nav = NavBake.Build(world, in settings, tileSize, statics);
                if (!stale && consistent && nav.TotalSpans > 0)
                {
                    try { NavCache.Save(file, nav); } catch (System.Exception e) { Debug.LogWarning($"nav cache: could not write {file}: {e.Message}"); }
                }
            }
            watch.Stop();

            Navigation.Current = nav;
            Navigation.Stale = false;
            BakedVersion = ws.Version;
            NavPaths.Clear(); // the corners in the side buffer were made on the old grid

            int tiles = 0;
            foreach (var t in nav.Tiles) if (t != null) tiles++;
            if (tiles > 0) // a world with no level (the checks, the title before its backdrop) has nothing to say
                Debug.LogWarning($"nav: {(warm ? "loaded" : "baked")} {tiles} tiles, {nav.TotalSpans} spans at {nav.Settings.CellSize:0.00} m "
                                 + $"in {watch.Elapsed.TotalMilliseconds:0.0} ms ({reason}, key {key:x16}"
                                 + $"{(consistent ? "" : ", views disagree: not cached")}, queries +{world.QueryCount - queriesBefore} of {world.QueryCount})");
        }
    }

    /// <summary>
    /// The grid's keeper inside the frame: it bakes when something invalidated the level's geometry
    /// (<see cref="Navigation.Invalidate"/> — a gate's wall, one day an editor edit), and as a backstop for a
    /// level that was built without going through <c>GameSession.StartLevel</c>. The ordinary level start bakes
    /// synchronously there, so in the common case this system does nothing at all.
    /// </summary>
    [UpdateAfter(typeof(PhysicsSyncSystem))]
    [Exclusive] // reads the physics broadphase and writes engine-side shared data, not components
    public sealed class NavBakeSystem : SystemBase, IFixedTick
    {
        public override void OnCreate() => World.SingletonEntity<Worldspawn>();

        public void FixedTick(SystemContext ctx)
        {
            ref var ws = ref World.Singleton<Worldspawn>();
            if (!Navigation.Stale && ws.Version == NavBaking.BakedVersion) return;
            NavBaking.Bake(World, Navigation.Stale ? "rebake" : "level built outside StartLevel");
        }
    }
}
