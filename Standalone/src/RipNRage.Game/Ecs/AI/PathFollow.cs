using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Nav;
using RipNRage.Engine.Ecs.Physics;
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
        /// <summary>Nodes the last search opened, and how many times this enemy has asked — the probes 11.0 wanted.</summary>
        public int Nodes, Repaths;
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
        /// Does what is left of this path run over a span something has since been parked on? Corner to corner,
        /// sampled at the cell size — a shut door is wider than a cell, so stepping the segments finds it.
        /// </summary>
        public static bool Crosses(NavWorld nav, NavPath path, int from, int count)
        {
            float cell = nav.Settings.CellSize;
            for (int i = Mathf.Max(1, from); i < count && i < path.Count; i++)
            {
                Vector3 a = path.Corners[i - 1], b = path.Corners[i];
                int steps = Mathf.Clamp(Mathf.CeilToInt((b - a).magnitude / cell), 1, 64);
                for (int k = 0; k <= steps; k++)
                {
                    var at = Vector3.Lerp(a, b, (float)k / steps);
                    if (!nav.TryFindSpan(at, out int span)) return true;          // the floor itself went
                    if (nav.Locate(span, out var tile, out int local) && !tile.Spans[local].Open) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Can the body just walk at the goal? A capsule cast along the flattened line says whether anything is in
        /// the way; then the ground is sampled along it, because a clear line over a hole is still not a way — that
        /// is the `ledge` gate, where the wall is an absence of floor.
        /// </summary>
        public static bool StraightLineClear(Vector3 self, Vector3 goal, float radius, float maxDrop, float stepUp,
                                             float centreAboveFeet, float ride, float bodyHeight)
        {
            Vector3 d = goal - self; d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.05f) return true;
            d /= dist;

            // the capsule is the body's: its collider starts at the ride height above the feet and ends at the
            // crown. (A first version built it about the transform's centre and missed anything under 0.6 m -
            // a knee-high crate read as a clear line.)
            float r = radius, feet = self.y - centreAboveFeet;
            var p1 = new Vector3(self.x, feet + ride + r, self.z);
            var p2 = new Vector3(self.x, feet + bodyHeight - r, self.z);
            if (PhysicsApi.CapsuleCast(p1, p2, r, d, out _, dist, Noises.WallMask)) return false;

            // the floor under the line: each sample must be within a step up or a drop down of the one before.
            // Finding *some* floor is not enough — a 3 m fall has floor at the bottom of it.
            int steps = Mathf.Clamp(Mathf.CeilToInt(dist / GroundStep), 1, 24);
            float y = feet;
            for (int i = 1; i <= steps; i++)
            {
                var at = self + d * (dist * i / steps);
                if (!PhysicsApi.RaycastConcurrent(new Vector3(at.x, y + bodyHeight, at.z), Vector3.down, out var hit, bodyHeight + maxDrop + 1f, Noises.WallMask))
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
    [Exclusive] // the straight-line test is a capsule cast on the main-thread query path, which writes visit stamps:
                // this system must never share a batch (a worker running it would race every other query there)
    public sealed class PathFollowSystem : SystemBase, ITick
    {
        private Query _q;

        public override void OnCreate() =>
            _q = World.Query().With<AiIntent>().With<PathFollow>().With<PathRequest>()
                .WithReadOnly<Enemy>().WithReadOnly<LocalToWorld>().WithReadOnly<CharacterMotor>().Build();

        public void Tick(SystemContext ctx)
        {
            var nav = Navigation.Current;
            float now = (float)ctx.Time;
            float radius = NavProfile.Radius;
            float centre = NavProfile.CentreAboveFeet, ride = NavProfile.Ride, body = NavProfile.BodyHeight;
            float maxDrop = nav?.Settings.MaxDrop ?? 2.5f;
            float cell = nav?.Settings.CellSize ?? 0.5f;

            foreach (var chunk in _q)
            {
                var intents = chunk.Get<AiIntent>(); var follows = chunk.Get<PathFollow>(); var reqs = chunk.Get<PathRequest>();
                var ltw = chunk.Get<LocalToWorld>(); var motors = chunk.Get<CharacterMotor>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var intent = ref intents[i];
                    ref var follow = ref follows[i];
                    ref var req = ref reqs[i];

                    if (!intent.Move || World.Has<Dead>(ents[i])) { follow.Count = 0; follow.Direct = false; continue; }

                    // A deck under way is not in the grid — the grid is baked for the world, and the sea it sails
                    // over is not walkable. Anyone standing on something that moves walks straight at the goal
                    // instead, which is all a deck needs: they are small, open, and the way across is the way across.
                    if (motors[i].OnMovingFrame) { follow.Count = 0; follow.Direct = true; continue; }

                    var self = ltw[i].Position;
                    var goal = intent.MoveTo;

                    // 1. is the way clear? (re-looked a few times a second, not every tick)
                    if (now >= follow.Recheck)
                    {
                        follow.Recheck = now + PathFollowing.RecheckEvery;
                        follow.Direct = PathFollowing.StraightLineClear(self, goal, radius, maxDrop, NavProfile.StepUp, centre, ride, body);
                    }
                    if (follow.Direct) { follow.Count = 0; continue; } // the brain's goal stands

                    if (nav == null || nav.TotalSpans == 0) continue; // no grid: the straight line, as before

                    // 2a. the world carved since this path was made (11.6): the cheap test is the version, the
                    //     exact one is whether a corner still to be walked now stands on a blocked span. A door
                    //     shut somewhere else in the level costs one integer compare and nothing more.
                    if (follow.Count > 0 && follow.Version != nav.Version)
                    {
                        follow.Version = nav.Version;
                        if (NavPaths.TryGet(ents[i], out var walking) && PathFollowing.Crosses(nav, walking, follow.Corner, follow.Count))
                            follow.Count = 0; // dropped: ask again below, and walk the straight line until it comes
                    }

                    // 2b. ask, when there is no path or the goal has wandered off the one we have
                    bool stale = follow.Count == 0 || (goal - follow.Goal).sqrMagnitude > PathFollowing.Repath * PathFollowing.Repath;
                    if (stale && !req.Pending)
                    {
                        req.Goal = goal; req.Serial++; req.Pending = true; follow.Repaths++;
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
