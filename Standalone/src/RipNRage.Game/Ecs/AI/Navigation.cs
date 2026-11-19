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
            if (ws.Version == _bakedVersion && !Navigation.Stale) return;
            bool stale = ws.Version == _bakedVersion; // not a level load: something changed the geometry under us
            _bakedVersion = ws.Version;
            Navigation.Stale = false;

            var settings = NavProfile.For(in ws);
            float tileSize = LevelLoader.MeshChunkSize;
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // 11.12: read the grid back if this level, with these settings, has been baked before. The key is what
            // the bake consumes (every static collider's box, the derived body limits, the map keys), so a changed
            // brush or a changed profile misses and bakes. A gate script's wall also misses - which is right.
            // The cache is for level loads only. An Invalidate() rebake is told the geometry changed, and it must
            // believe that over the key: the first version loaded the bare grid back for the maze gate's wall,
            // because a collider built that frame is in the broadphase (the rays see it) before the ECS query does
            // (the key did not) - 30 statics either side of the wall. A stale bake is never read from the cache
            // and never written to it, so it cannot poison the level's entry either.
            ulong key = NavCache.Key(World, in settings, tileSize);
            string map = System.IO.Path.GetFileNameWithoutExtension(RipNRage.Game.Content.GameSession.LevelPath ?? "level");
            string file = System.IO.Path.Combine(Application.persistentDataPath, "nav", $"{map}-{key:x16}.nav");
            // Save only when the two views of the level agree: the ECS query the key hashes, and the broadphase the
            // rays sample. A collider built this very frame can be in one and not the other (seen both ways on the
            // gates: door's frame-2 bake once saw part of its wall), and a grid saved under a key that does not
            // describe it would be read back wrong for ever.
            NavBake.Bounds(World, out _, out _, out _, out var allBoxes);
            bool consistent = allBoxes.Count == RipNRage.Engine.Physics.World.StaticCount;
            NavWorld nav = null;
            bool warm = !stale && NavCache.TryLoad(file, in settings, tileSize, out nav, World.Jobs);
            if (!warm)
            {
                nav = NavBake.Build(World, in settings, tileSize);
                if (!stale && consistent && nav.TotalSpans > 0) { try { NavCache.Save(file, nav); } catch (System.Exception e) { Debug.LogWarning($"nav cache: could not write {file}: {e.Message}"); } }
            }
            watch.Stop();
            Navigation.Current = nav;
            NavPaths.Clear(); // the corners in the side buffer were made on the old grid

            int tiles = 0;
            foreach (var t in nav.Tiles) if (t != null) tiles++;
            if (tiles > 0) // a world with no level (the checks, the title before its backdrop) has nothing to say
                Debug.LogWarning($"nav: {(warm ? "loaded" : stale ? "rebaked" : "baked")} {tiles} tiles, {nav.TotalSpans} spans at {nav.Settings.CellSize:0.00} m in {watch.Elapsed.TotalMilliseconds:0.0} ms (key {key:x16}{(consistent ? "" : ", views disagree: not cached")})");
        }
    }
}
