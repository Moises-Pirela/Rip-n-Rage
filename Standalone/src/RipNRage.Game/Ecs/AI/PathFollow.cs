using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Nav;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.Combat;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>
    /// The way an enemy is walking. <see cref="Direct"/> means it is not walking one at all — the line to the goal
    /// was clear, which is the common case and costs nothing.
    /// </summary>
    [NotInPreview] public struct PathFollow : IComponent
    {
        /// <summary>Which corner of the path is being walked to.</summary>
        public int Corner;
        /// <summary>Corners in the side buffer (0 = no path).</summary>
        public int Count;
        /// <summary>The request this path answered.</summary>
        public int Serial;
        /// <summary>Where the path was made for: when the goal wanders further than <see cref="PathFollowing.Repath"/> from it, ask again.</summary>
        public Vector3 Goal;
        public NavStatus Status;
        /// <summary>The straight line was clear at the last look.</summary>
        public bool Direct;
        /// <summary>Sim time of the next straight-line look (the test costs rays; it does not need doing every tick).</summary>
        public float Recheck;
        /// <summary>The grid version this path was made against (11.6 repaths when a carve moves it on).</summary>
        public int Version;
    }

    public static class PathFollowing
    {
        /// <summary>Seconds between straight-line looks.</summary>
        public const float RecheckEvery = 0.2f;
        /// <summary>The goal may wander this far from the one the path was made for before it is asked again.</summary>
        public const float Repath = 2f;
        /// <summary>A corner is reached within this many cells.</summary>
        public const float CornerCells = 1.5f;
        /// <summary>How far apart the ground samples along the straight line are.</summary>
        public const float GroundStep = 1.5f;

        /// <summary>
        /// Can the body just walk at the goal? A capsule cast along the flattened line says whether anything is in
        /// the way; then the ground is sampled along it, because a clear line over a hole is still not a way — that
        /// is the `ledge` gate, where the wall is an absence of floor.
        /// </summary>
        public static bool StraightLineClear(Vector3 self, Vector3 goal, float radius, float height, float maxDrop, float stepUp)
        {
            Vector3 d = goal - self; d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.05f) return true;
            d /= dist;

            float r = radius;
            var p1 = self + Vector3.up * (r - height * 0.5f + 0.1f);
            var p2 = self + Vector3.up * (height * 0.5f - r);
            if (PhysicsApi.CapsuleCast(p1, p2, r, d, out _, dist, Noises.WallMask)) return false;

            // the floor under the line: each sample must be within a step up or a drop down of the one before.
            // Finding *some* floor is not enough — a 3 m fall has floor at the bottom of it.
            int steps = Mathf.Clamp(Mathf.CeilToInt(dist / GroundStep), 1, 24);
            float y = self.y;
            for (int i = 1; i <= steps; i++)
            {
                var at = self + d * (dist * i / steps);
                if (!PhysicsApi.Raycast(new Vector3(at.x, y + height, at.z), Vector3.down, out var hit, height + maxDrop + 1f, Noises.WallMask))
                    return false;
                float fall = y - hit.point.y, rise = hit.point.y - y;
                if (fall > maxDrop || rise > stepUp) return false;
                y = hit.point.y;
            }
            return true;
        }
    }

    /// <summary>
    /// Puts the next corner of the path where the brain put the goal, so the body walks round what is in the way.
    /// The brain is not touched and neither is the steering: <see cref="AiBrainSystem"/> still decides *where* to
    /// go, <see cref="SteeringSystem"/> still walks at whatever <c>AiIntent.MoveTo</c> says, and this sits between
    /// them rewriting that one field.
    ///
    /// Straight line first: most chases are in the open, and a clear line costs one cast and no search at all.
    /// A path is asked for only when the line is blocked, and when the search fails the straight line is used
    /// anyway — degraded, never frozen.
    /// </summary>
    [UpdateAfter(typeof(NavPathSystem))]
    [UpdateBefore(typeof(SteeringSystem))]
    [Reads(typeof(Dead))]
    public sealed class PathFollowSystem : SystemBase, ITick
    {
        private Query _q;

        public override void OnCreate() =>
            _q = World.Query().With<AiIntent>().With<PathFollow>().With<PathRequest>()
                .WithReadOnly<Enemy>().WithReadOnly<LocalToWorld>().Build();

        public void Tick(SystemContext ctx)
        {
            var nav = Navigation.Current;
            float now = Time.time;
            float radius = NavProfile.Radius, height = NavProfile.Height;
            float maxDrop = nav?.Settings.MaxDrop ?? 2.5f;
            float cell = nav?.Settings.CellSize ?? 0.5f;

            foreach (var chunk in _q)
            {
                var intents = chunk.Get<AiIntent>(); var follows = chunk.Get<PathFollow>(); var reqs = chunk.Get<PathRequest>();
                var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var intent = ref intents[i];
                    ref var follow = ref follows[i];
                    ref var req = ref reqs[i];

                    if (!intent.Move || World.Has<Dead>(ents[i])) { follow.Count = 0; follow.Direct = false; continue; }

                    var self = ltw[i].Position;
                    var goal = intent.MoveTo;

                    // 1. is the way clear? (re-looked a few times a second, not every tick)
                    if (now >= follow.Recheck)
                    {
                        follow.Recheck = now + PathFollowing.RecheckEvery;
                        follow.Direct = PathFollowing.StraightLineClear(self, goal, radius, height, maxDrop, NavProfile.StepUp);
                    }
                    if (follow.Direct) { follow.Count = 0; continue; } // the brain's goal stands

                    if (nav == null || nav.TotalSpans == 0) continue; // no grid: the straight line, as before

                    // 2. ask, when there is no path or the goal has wandered off the one we have
                    bool stale = follow.Count == 0 || (goal - follow.Goal).sqrMagnitude > PathFollowing.Repath * PathFollowing.Repath;
                    if (stale && !req.Pending)
                    {
                        req.Goal = goal; req.Serial++; req.Pending = true;
                    }

                    // 3. walk the corners we have
                    if (follow.Count <= 0 || !NavPaths.TryGet(ents[i], out var path) || path.Count < follow.Count) continue;
                    float reach = cell * PathFollowing.CornerCells;
                    while (follow.Corner < follow.Count)
                    {
                        Vector3 c = path.Corners[follow.Corner];
                        Vector3 flat = c - self; flat.y = 0f;
                        if (flat.sqrMagnitude > reach * reach) break;
                        follow.Corner++;
                    }
                    if (follow.Corner >= follow.Count) { follow.Count = 0; continue; } // walked it out; the goal stands
                    intent.MoveTo = path.Corners[follow.Corner];
                }
            }
        }
    }
}
