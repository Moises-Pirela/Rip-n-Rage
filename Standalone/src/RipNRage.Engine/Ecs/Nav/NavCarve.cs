using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Engine.Ecs.Nav
{
    /// <summary>
    /// A thing that moves and blocks the way while it is there: a door, a slider, a crate big enough to stop a
    /// body. Its footprint is stamped onto the spans it covers and lifted off the ones it leaves.
    ///
    /// Not every moving thing is one. Ragdolls, pickups, projectiles, other enemies and the player are deliberately
    /// not carved — the contact solver and the follower's fallback deal with those, and carving them would re-stamp
    /// tiles every tick for things a body can walk through or push aside.
    /// </summary>
    [NotInPreview] public struct NavObstacle : IComponent
    {
        /// <summary>Half the footprint, in the obstacle's own frame. Zero = taken from its <see cref="ColliderShape"/>.</summary>
        public Vector3 HalfExtents;
        /// <summary>Where the stamp on the grid currently is (world AABB), and whether there is one.</summary>
        public Vector3 StampCenter, StampHalf;
        public bool Stamped;
    }

    public sealed class NavObstacleConfig : ComponentConfig<NavObstacle>
    {
        [InspectTip("half the footprint; empty or zero = the collider's own size")]
        public float[] halfExtents;
        public override NavObstacle Bake() =>
            new NavObstacle { HalfExtents = halfExtents != null && halfExtents.Length == 3 ? new Vector3(halfExtents[0], halfExtents[1], halfExtents[2]) : Vector3.zero };
    }

    /// <summary>Stamping a footprint onto the spans it covers, and lifting it off again.</summary>
    public static class NavCarve
    {
        /// <summary>How far an obstacle must move before its stamp is redone (half a cell): a crate that jitters must not re-stamp every tick.</summary>
        public static float Threshold(in NavSettings s) => s.CellSize * 0.5f;

        /// <summary>
        /// Adds <paramref name="delta"/> to the carve counter of every span the box covers. A **counter, not a
        /// flag**: two crates on one span and one taken away leaves it blocked, which a flag would get wrong.
        /// Returns how many spans it touched.
        /// </summary>
        public static int Stamp(NavWorld nav, Vector3 center, Vector3 half, int delta)
        {
            if (nav == null || nav.TotalSpans == 0 || delta == 0) return 0;
            var s = nav.Settings;
            float minX = center.x - half.x, maxX = center.x + half.x;
            float minZ = center.z - half.z, maxZ = center.z + half.z;
            // a body standing on a span is blocked by anything from its feet up to its head
            float minY = center.y - half.y - s.StepUp, maxY = center.y + half.y;
            int touched = 0;

            int tx0 = Mathf.FloorToInt((minX - nav.OriginX) / nav.TileSize), tx1 = Mathf.FloorToInt((maxX - nav.OriginX) / nav.TileSize);
            int tz0 = Mathf.FloorToInt((minZ - nav.OriginZ) / nav.TileSize), tz1 = Mathf.FloorToInt((maxZ - nav.OriginZ) / nav.TileSize);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    var tile = nav.TileAt(tx, tz);
                    if (tile == null) continue;
                    int cx0 = Mathf.Max(0, Mathf.FloorToInt((minX - tile.MinX) / s.CellSize));
                    int cx1 = Mathf.Min(tile.Width - 1, Mathf.FloorToInt((maxX - tile.MinX) / s.CellSize));
                    int cz0 = Mathf.Max(0, Mathf.FloorToInt((minZ - tile.MinZ) / s.CellSize));
                    int cz1 = Mathf.Min(tile.Depth - 1, Mathf.FloorToInt((maxZ - tile.MinZ) / s.CellSize));
                    bool any = false;
                    for (int cz = cz0; cz <= cz1; cz++)
                        for (int cx = cx0; cx <= cx1; cx++)
                        {
                            var col = tile.Columns[cz * tile.Width + cx];
                            for (int i = col.First; i < col.First + col.Count; i++)
                            {
                                float floor = tile.Spans[i].Floor;
                                if (floor < minY || floor > maxY) continue;
                                int v = tile.Spans[i].Dynamic + delta;
                                tile.Spans[i].Dynamic = (ushort)Mathf.Max(0, v);
                                touched++; any = true;
                            }
                        }
                    if (any) { tile.Version++; nav.Version++; }
                }
            return touched;
        }
    }

    /// <summary>
    /// Keeps every <see cref="NavObstacle"/>'s stamp where the obstacle is. One writer of the carve counters, at one
    /// fixed point in the frame, so a search never sees half a move. <see cref="NavWorld.Version"/> rises whenever
    /// anything changed, which is what tells a follower its path may be stale (11.6).
    /// </summary>
    [Exclusive] // writes engine-side shared data (the grid), not components
    [EntityFilter(typeof(NavObstacle))] // the destroy hook is for obstacles, not for every bullet and noise that ends
    public sealed class NavCarveSystem : SystemBase, ITick, IOnEntityDestroyed
    {
        private Query _q;

        /// <summary>The bake the stamps were made on. A fresh bake has none, so every obstacle is stamped again.</summary>
        private int _stampedFor = -1;

        public override void OnCreate()
        {
            _q = World.Query().With<NavObstacle>().WithReadOnly<LocalToWorld>().Build();
            World.SingletonEntity<NavStats>(); // claimed outside a phase: creating the carrier is a structural change
        }

        public void OnEntityDestroyed(SystemContext ctx, Entity e)
        {
            // an obstacle that is destroyed takes its stamp with it, or the way stays shut for ever
            ref var o = ref World.Get<NavObstacle>(e);
            if (o.Stamped) { World.Singleton<NavStats>().BlockedSpans -= NavCarve.Stamp(Navigation.Current, o.StampCenter, o.StampHalf, -1); o.Stamped = false; }
        }

        public void Tick(SystemContext ctx)
        {
            var nav = Navigation.Current;
            if (nav == null || nav.TotalSpans == 0) return;
            float threshold = NavCarve.Threshold(in nav.Settings);

            // a rebake (a respawn, a load, Navigation.Invalidate) is a new grid with no stamps on it, while every
            // obstacle still believes it is stamped where it stands. Without this a door that has not moved is
            // never carved again and stands open to the search until something shoves it (found at the 11.9 review).
            ref var stats = ref World.Singleton<NavStats>();
            bool fresh = nav.BakeId != _stampedFor;
            if (fresh) { _stampedFor = nav.BakeId; stats.BlockedSpans = 0; }

            foreach (var chunk in _q)
            {
                var obs = chunk.Get<NavObstacle>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var o = ref obs[i];
                    if (fresh) o.Stamped = false; // the old stamp is on a grid that no longer exists
                    var half = o.HalfExtents;
                    if (half == Vector3.zero && World.Has<ColliderShape>(ents[i]))
                    {
                        var shape = World.Get<ColliderShape>(ents[i]);
                        half = shape.Kind == ShapeKind.Box ? shape.Size * 0.5f
                             : new Vector3(shape.Radius, Mathf.Max(shape.Radius, shape.Height * 0.5f), shape.Radius);
                    }
                    if (half == Vector3.zero) continue;

                    // the world box of the oriented footprint
                    var m = ltw[i].Value;
                    var centre = ltw[i].Position;
                    var worldHalf = new Vector3(
                        Mathf.Abs(m.m00) * half.x + Mathf.Abs(m.m01) * half.y + Mathf.Abs(m.m02) * half.z,
                        Mathf.Abs(m.m10) * half.x + Mathf.Abs(m.m11) * half.y + Mathf.Abs(m.m12) * half.z,
                        Mathf.Abs(m.m20) * half.x + Mathf.Abs(m.m21) * half.y + Mathf.Abs(m.m22) * half.z);

                    if (o.Stamped && (centre - o.StampCenter).sqrMagnitude <= threshold * threshold
                        && (worldHalf - o.StampHalf).sqrMagnitude <= threshold * threshold) continue; // near enough: leave it

                    if (o.Stamped) stats.BlockedSpans -= NavCarve.Stamp(nav, o.StampCenter, o.StampHalf, -1);
                    stats.BlockedSpans += NavCarve.Stamp(nav, centre, worldHalf, +1);
                    o.StampCenter = centre; o.StampHalf = worldHalf; o.Stamped = true;
                }
            }
        }
    }
}
