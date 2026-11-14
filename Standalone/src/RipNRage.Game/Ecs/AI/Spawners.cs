using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs.AI
{
    /// <summary>
    /// A wave spawner, as data on an entity (the editor's "monster_spawner"): which kind it makes, how many in total,
    /// how often, scattered how far, woken by the player at what distance, and how many of its monsters may stand at
    /// once. The running half (Spawned, Timer, Active, the scatter dice) lives in the same value; the save system
    /// round-trips <see cref="Spawned"/>.
    /// </summary>
    public struct Spawner : IComponent
    {
        public int Kind;
        /// <summary>Total monsters this spawner ever makes.</summary>
        public int Count;
        /// <summary>Seconds between spawns.</summary>
        public float Interval;
        /// <summary>Scatter around the origin (a square, as the legacy spawner rolled it).</summary>
        public float Radius;
        /// <summary>0 = starts immediately, otherwise waits for the player within this distance.</summary>
        public float ActivateRange;
        /// <summary>Monsters of this spawner standing at once (corpses count until the dissolve removes them).</summary>
        public int MaxAlive;
        /// <summary>Facing handed to the spawned monsters.</summary>
        public float Yaw;
        public int Spawned;
        public float Timer;
        public bool Active;
        /// <summary>Scatter dice (xorshift, seeded by the entity) — never the shared time-seeded Random.</summary>
        public uint Rng;
    }

    /// <summary>
    /// Steps every spawner: wake on the player's approach, then one monster per interval through the command buffer,
    /// never over <see cref="Spawner.MaxAlive"/> standing. A spawner's monsters carry its level index in
    /// <see cref="LevelRef.SpawnerIndex"/> — that is both the standing count and how a save knows them. Replaces the
    /// MonsterSpawner MonoBehaviour.
    /// </summary>
    // After the frame's combat: new monsters are an end-of-frame affair. (6.7 first credited this constraint with
    // fixing a style-gate regression; the investigation after showed the schedule was identical either way and the
    // regression was the time-seeded Random — see the schedule check and --seed. The ordering stays for its intent.)
    [UpdateAfter(typeof(EnemyAttackSystem))]
    public sealed class SpawnerSystem : SystemBase, ITick
    {
        private Query _spawners, _players, _monsters;
        private readonly System.Collections.Generic.Dictionary<int, int> _standing = new System.Collections.Generic.Dictionary<int, int>();

        public override void OnCreate()
        {
            _spawners = World.Query().With<Spawner>().WithReadOnly<LevelRef>().WithReadOnly<LocalToWorld>().Build();
            _players = World.Query().With<PlayerTag>().WithReadOnly<LocalToWorld>().Without<Dead>().Build();
            _monsters = World.Query().WithReadOnly<Enemy>().WithReadOnly<LevelRef>().Build();
        }

        public void Tick(SystemContext ctx)
        {
            bool playerKnown = TryPlayer(out var player);

            // how many of each spawner's monsters still stand
            _standing.Clear();
            foreach (var chunk in _monsters)
            {
                var refs = chunk.Get<LevelRef>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    int s = refs[i].SpawnerIndex;
                    if (s >= 0) _standing[s] = _standing.TryGetValue(s, out int n) ? n + 1 : 1;
                }
            }

            float dt = ctx.DeltaTime;
            foreach (var chunk in _spawners)
            {
                var spawners = chunk.Get<Spawner>(); var refs = chunk.Get<LevelRef>(); var ltw = chunk.Get<LocalToWorld>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var s = ref spawners[i];
                    if (s.Spawned >= s.Count) continue;
                    if (s.Kind < 0 || s.Kind >= Enemies.All.Length) { s.Spawned = s.Count; continue; } // unknown monster: warned at creation, gives up as the legacy did
                    if (!s.Active)
                    {
                        if (s.ActivateRange > 0f && (!playerKnown || (player - ltw[i].Position).magnitude > s.ActivateRange)) continue;
                        s.Active = true;
                        s.Timer = 0f; // the first monster spawns right away
                    }
                    s.Timer -= dt;
                    if (s.Timer > 0f) continue;
                    // over the cap the timer keeps running down, so a freed slot is refilled at once (the legacy order)
                    int index = refs[i].Index;
                    if (index >= 0 && _standing.TryGetValue(index, out int standing) && standing >= s.MaxAlive) continue;
                    s.Timer = s.Interval;
                    if (s.Rng == 0) s.Rng = (uint)(ents[i].Index + 1) * 2654435761u;
                    var scatter = new Vector3((AiBrains.Roll(ref s.Rng) * 2f - 1f) * s.Radius, 0f, (AiBrains.Roll(ref s.Rng) * 2f - 1f) * s.Radius);
                    Enemies.Spawn(ctx.Commands, s.Kind, ltw[i].Position + scatter, s.Yaw, index);
                    s.Spawned++;
                    if (index >= 0) _standing[index] = _standing.TryGetValue(index, out int n) ? n + 1 : 1; // counted at once: a zero interval cannot overshoot the cap
                }
            }
        }

        private bool TryPlayer(out Vector3 position)
        {
            foreach (var chunk in _players)
            {
                var ltw = chunk.Get<LocalToWorld>();
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i)) { position = ltw[i].Position; return true; }
            }
            position = default;
            return false;
        }
    }

    /// <summary>Ad-hoc access for the save system — one cached query per world, the <see cref="Enemies.Of"/> pattern.</summary>
    public static class Spawners
    {
        private static World _queryWorld;
        private static Query _query;

        public static Query Of(World w)
        {
            if (!ReferenceEquals(_queryWorld, w)) { _queryWorld = w; _query = w.Query().With<Spawner>().Build(); }
            return _query;
        }

        /// <summary>Every spawner, into <paramref name="results"/> (cleared first).</summary>
        public static void Collect(World w, System.Collections.Generic.List<Entity> results)
        {
            results.Clear();
            foreach (var chunk in Of(w))
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i)) results.Add(chunk.Entities[i]);
        }
    }
}
