using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Nav;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>
    /// What an enemy has asked the navigation for. <see cref="Pending"/> is the ask; <see cref="Serial"/> rises with
    /// every new one, so a result that arrives for an ask already replaced is dropped rather than walked.
    /// </summary>
    [NotInPreview] public struct PathRequest : IComponent
    {
        public Vector3 Goal;
        public int Serial;
        public bool Pending;
    }

    /// <summary>
    /// The corners, kept beside the components. A path is a variable-length thing and a component is a fixed-size
    /// struct; rather than cap it inline, each asker keeps one reused <see cref="NavPath"/> here — the "side buffer"
    /// the plan named. Entries go when the entity does.
    /// </summary>
    public static class NavPaths
    {
        /// <summary>Searches served per tick. A tunable: four is the whole dock's worth of grunts in one tick.</summary>
        public static int Budget = 4;

        private static readonly Dictionary<Entity, NavPath> _paths = new Dictionary<Entity, NavPath>();

        public static NavPath Of(Entity e)
        {
            if (!_paths.TryGetValue(e, out var p)) _paths[e] = p = new NavPath();
            return p;
        }

        public static bool TryGet(Entity e, out NavPath p) => _paths.TryGetValue(e, out p);
        public static void Forget(Entity e) => _paths.Remove(e);
        public static void Clear() => _paths.Clear();
        public static int Count => _paths.Count;
    }

    /// <summary>
    /// Serves path requests, at most <see cref="NavPaths.Budget"/> a tick.
    ///
    /// The queue is ordered by <see cref="AiBrain.Order"/> — the placement seed — and not by the order the query
    /// walks the chunks: entity indices depend on how many entities happen to exist first, and a buffered spawn's
    /// index is not even final until playback (6.7b). Two grunts asking on the same tick must be served in the same
    /// order every run, or the sweep's probe lines drift.
    /// </summary>
    [UpdateAfter(typeof(AiBrainSystem))]
    [UpdateBefore(typeof(SteeringSystem))]
    [Reads(typeof(Dead))]
    public sealed class NavPathSystem : SystemBase, ITick, IOnEntityDestroyed
    {
        private Query _askers;
        private readonly NavSearcher _searcher = new NavSearcher();
        private readonly List<(uint order, int index, Entity e)> _queue = new List<(uint, int, Entity)>();

        /// <summary>Searches run last tick — the probe the gates print.</summary>
        public static int ServedLastTick;

        public override void OnCreate() =>
            _askers = World.Query().With<PathRequest>().With<PathFollow>()
                .WithReadOnly<AiBrain>().WithReadOnly<LocalToWorld>().Build();

        public void OnEntityDestroyed(SystemContext ctx, Entity e) => NavPaths.Forget(e);

        public void Tick(SystemContext ctx)
        {
            ServedLastTick = 0;
            var nav = Navigation.Current;
            if (nav == null || nav.TotalSpans == 0) return;

            _queue.Clear();
            foreach (var chunk in _askers)
            {
                var reqs = chunk.Get<PathRequest>(); var brains = chunk.Get<AiBrain>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i) || !reqs[i].Pending) continue;
                    if (World.Has<Dead>(ents[i])) { reqs[i].Pending = false; continue; }
                    _queue.Add((brains[i].Order, ents[i].Index, ents[i]));
                }
            }
            if (_queue.Count == 0) return;
            _queue.Sort(Compare);

            int served = 0;
            foreach (var (_, _, e) in _queue)
            {
                if (served >= NavPaths.Budget) break;
                ref var req = ref World.Get<PathRequest>(e);
                ref var follow = ref World.Get<PathFollow>(e);
                req.Pending = false;
                served++;

                var self = World.Get<LocalToWorld>(e).Position;
                var path = NavPaths.Of(e);
                if (!nav.TryFindSpan(self, out int from) || !nav.TryFindSpan(req.Goal, out int to))
                {
                    follow.Count = 0; follow.Status = NavStatus.Unreachable; follow.Serial = req.Serial;
                    continue;
                }
                var status = _searcher.Search(nav, from, to, path);
                follow.Status = status;
                follow.Serial = req.Serial;
                follow.Corner = 0;
                follow.Count = status == NavStatus.Unreachable ? 0 : path.Count;
                follow.Goal = req.Goal;
                follow.Version = nav.Version;
            }
            ServedLastTick = served;
        }

        private static int Compare((uint order, int index, Entity e) a, (uint order, int index, Entity e) b) =>
            CompareAskers(a.order, a.index, b.order, b.index);

        /// <summary>
        /// The serving order: placement seed first, entity index only to break a tie between two identical
        /// placements. Pinned by a check — serving in entity order instead would drift the moment a buffered spawn
        /// renumbered the world (6.7b).
        /// </summary>
        public static int CompareAskers(uint orderA, int indexA, uint orderB, int indexB)
        {
            int c = orderA.CompareTo(orderB);
            return c != 0 ? c : indexA.CompareTo(indexB);
        }
    }
}
