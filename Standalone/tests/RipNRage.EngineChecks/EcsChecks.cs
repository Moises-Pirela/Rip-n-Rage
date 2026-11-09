using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RipNRage.Engine.Ecs;

// ---- test components / configs / events / systems (discovered by reflection like game code would be)
public struct Position : IComponent { public float X, Y; }
public struct Velocity : IComponent { public float X, Y; }
public struct Health : IComponent { public float Value, Max; }
[SparseComponent] public struct Stunned : IComponent { public float Seconds; }
[SparseComponent] public struct Tagged : IComponent { }

public sealed class HealthConfig : ComponentConfig<Health>
{
    public float max = 100f;
    public override Health Bake() => new Health { Value = max, Max = max };
}
public sealed class PositionConfig : ComponentConfig<Position>
{
    public float x, y;
    public override Position Bake() => new Position { X = x, Y = y };
}

public struct DamageEvent : IPostProcessEvent { public Entity Target; public float Amount; }

[Reads(typeof(Velocity)), Writes(typeof(Position))]
public sealed class MoveSystem : SystemBase, ITick
{
    private Query _q;
    public override void OnCreate() => _q = World.Query().With<Position>().WithReadOnly<Velocity>().Build();
    public void Tick(SystemContext ctx)
    {
        foreach (var c in _q)
        {
            var p = c.Get<Position>(); var v = c.Get<Velocity>();
            for (int i = 0; i < c.Length; i++) { p[i].X += v[i].X * ctx.DeltaTime; p[i].Y += v[i].Y * ctx.DeltaTime; }
        }
    }
}

[Writes(typeof(Health))]
public sealed class RegenSystem : SystemBase, ITick { public void Tick(SystemContext ctx) { } }

[Reads(typeof(Health))]
public sealed class HealthReaderSystem : SystemBase, ITick { public void Tick(SystemContext ctx) { } }

[UpdateAfter(typeof(MoveSystem)), Reads(typeof(Position))]
public sealed class AfterMoveSystem : SystemBase, ITick
{
    public static List<string> Log = new List<string>();
    public void Tick(SystemContext ctx) => Log.Add("after");
}

[Writes(typeof(Health))]
public sealed class DamageSystem : SystemBase, IConsumes<DamageEvent>, ITick, ILateTick
{
    public int Consumed, PendingAtLate;
    public bool PostInTick;
    public Entity Target;
    public void Tick(SystemContext ctx) { if (PostInTick) ctx.Post(new DamageEvent { Target = Target, Amount = 10 }); }
    public void LateTick(SystemContext ctx) => PendingAtLate = ctx.World.PendingEvents<DamageEvent>();
    public void Consume(SystemContext ctx, ReadOnlySpan<DamageEvent> events)
    {
        foreach (var e in events)
        {
            Consumed++;
            ref var h = ref ctx.World.Get<Health>(e.Target);
            h.Value -= e.Amount;
            if (h.Value <= 0) ctx.Commands.DestroyEntity(e.Target);
        }
    }
}

[EntityFilter(typeof(Health))]
public sealed class LifecycleSystem : SystemBase, IOnEntityCreated, IOnEntityDestroyed
{
    public List<float> CreatedHealth = new List<float>(), DestroyedHealth = new List<float>();
    public void OnEntityCreated(SystemContext ctx, Entity e) => CreatedHealth.Add(ctx.World.Get<Health>(e).Value);
    public void OnEntityDestroyed(SystemContext ctx, Entity e) => DestroyedHealth.Add(ctx.World.Get<Health>(e).Value);
}

public sealed class FixedCounter : SystemBase, IFixedTick, IOnSimStart, IOnSimEnd
{
    public int Steps, Starts, Ends;
    public void FixedTick(SystemContext ctx) => Steps++;
    public void OnSimStart(SystemContext ctx) => Starts++;
    public void OnSimEnd(SystemContext ctx) => Ends++;
}

/// <summary>Tries an immediate structural change during Tick: must throw.</summary>
public sealed class IllegalSystem : SystemBase, ITick
{
    public Exception Caught;
    public void Tick(SystemContext ctx) { try { ctx.World.CreateEntity(); } catch (Exception e) { Caught = e; } }
}

[UpdateAfter(typeof(CycleB))] public sealed class CycleA : SystemBase, ITick { public void Tick(SystemContext ctx) { } }
[UpdateAfter(typeof(CycleA))] public sealed class CycleB : SystemBase, ITick { public void Tick(SystemContext ctx) { } }

public struct SumJob : IChunkJob
{
    public long[] PerWorker;
    public void Execute(in Chunk chunk, int worker)
    {
        var p = chunk.Get<Position>();
        long s = 0;
        for (int i = 0; i < chunk.Length; i++) s += (long)p[i].X;
        PerWorker[worker] += s;
    }
}

internal static class EcsChecks
{
    public static void Run(Action<bool, string> True)
    {
        // ---- signature
        var sig = new Signature(); sig.Set(3); sig.Set(70); sig.Set(255);
        True(sig.Has(3) && sig.Has(70) && sig.Has(255) && !sig.Has(4) && sig.Count == 3, "signature set/has across words");
        var req = Signature.Of(70);
        True(sig.ContainsAll(req) && !Signature.Of(5).ContainsAll(req), "signature ContainsAll");
        bool threw = false; try { sig.Set(256); } catch (ArgumentOutOfRangeException) { threw = true; }
        True(threw, "signature rejects bit 256");

        using var w = new World(workerThreads: 3);

        // ---- storage: archetype moves keep data, swap-remove fixes rows
        var a = w.CreateEntity(); w.Add(a, new Position { X = 1 });
        var b = w.CreateEntity(); w.Add(b, new Position { X = 2 }); w.Add(b, new Velocity { X = 5 });
        var c = w.CreateEntity(); w.Add(c, new Position { X = 3 });
        True(w.Get<Position>(b).X == 2 && w.Get<Velocity>(b).X == 5, "data survives an archetype move");
        w.Remove<Position>(a); // swap-remove from the {Position} table moves c into a's row
        True(w.Get<Position>(c).X == 3 && !w.Has<Position>(a), "swap-remove keeps the moved entity's row correct");
        True(w.SignatureOf(b).Has(ComponentType<Velocity>.Id), "signature tracks components");

        // ---- sparse components never move tables
        var archBefore = w.Archetypes.Count;
        w.Add(c, new Stunned { Seconds = 2 });
        True(w.Archetypes.Count == archBefore && w.Has<Stunned>(c) && w.Get<Stunned>(c).Seconds == 2, "sparse add does not create/move archetypes");
        w.Remove<Stunned>(c);
        True(!w.Has<Stunned>(c) && w.Get<Position>(c).X == 3, "sparse remove");

        // ---- queries: table matching, later archetypes, sparse terms
        var moving = w.Query().With<Position>().WithReadOnly<Velocity>().Build();
        var stillPos = w.Query().With<Position>().Without<Velocity>().Build();
        True(moving.Count() == 1 && stillPos.Count() == 1, "With/Without match the right tables");
        var d = w.CreateEntity(); w.Add(d, new Position()); w.Add(d, new Velocity()); w.Add(d, new Health { Value = 1 }); // new archetype after the query
        True(moving.Count() == 2, "queries pick up archetypes created after them");
        w.Add(d, new Stunned());
        var stunnedMovers = w.Query().With<Position>().With<Stunned>().Build();
        var freeMovers = w.Query().With<Position>().WithReadOnly<Velocity>().Without<Stunned>().Build();
        True(stunnedMovers.Count() == 1 && freeMovers.Count() == 1, "sparse With/Without filter per entity");
        int included = 0;
        foreach (var ch in freeMovers) for (int i = 0; i < ch.Length; i++) if (ch.Includes(i)) included++;
        True(included == 1, "Chunk.Includes applies sparse terms");
        int visitedTables = 0;
        foreach (var ch in w.Query().With<Health>().Build()) visitedTables++;
        True(visitedTables == 1, "iteration visits only matching tables");

        // ---- stale handles
        w.DestroyEntity(d);
        True(!w.IsAlive(d) && !w.Has<Position>(d), "destroyed entity is dead");
        var e2 = w.CreateEntity();
        True(e2.Index == d.Index && e2 != d && !w.IsAlive(d), "recycled index gets a new generation");

        // ---- systems, ordering, batching
        var move = w.AddSystem(new MoveSystem());
        w.AddSystem(new AfterMoveSystem());
        w.AddSystem(new RegenSystem());
        w.AddSystem(new HealthReaderSystem());
        var illegal = w.AddSystem(new IllegalSystem());
        var batches = w.DescribeBatches("Tick");
        int Batch(Type t) => batches.FindIndex(x => x.Contains(t));
        True(Batch(typeof(AfterMoveSystem)) > Batch(typeof(MoveSystem)), "UpdateAfter orders batches");
        True(Batch(typeof(RegenSystem)) != Batch(typeof(HealthReaderSystem)), "writer and reader of Health never share a batch");
        True(Batch(typeof(MoveSystem)) == Batch(typeof(RegenSystem)), "non-conflicting systems share a parallel batch");
        True(batches[Batch(typeof(IllegalSystem))].Count == 1, "systems without Reads/Writes run alone");

        float before = w.Get<Position>(b).X;
        w.Update(0.5f);
        True(Math.Abs(w.Get<Position>(b).X - (before + 2.5f)) < 1e-4f, "MoveSystem integrates through a query");
        True(AfterMoveSystem.Log.Count == 1, "dependent system ran");
        True(illegal.Caught is InvalidOperationException, "immediate structural change during a phase throws");

        // ---- cycle detection
        using (var w2 = new World(0))
        {
            w2.AddSystem(new CycleA()); w2.AddSystem(new CycleB());
            threw = false; try { w2.Update(0.1f); } catch (InvalidOperationException) { threw = true; }
            True(threw, "ordering cycles are reported");
        }

        // ---- lifecycle events, post-process events, command buffers
        using (var w3 = new World(2))
        {
            var life = w3.AddSystem(new LifecycleSystem());
            var dmg = w3.AddSystem(new DamageSystem());
            var target = w3.CreateEntity(); w3.Add(target, new Health { Value = 15, Max = 15 });
            var noHealth = w3.CreateEntity(); w3.Add(noHealth, new Position());
            w3.Sync();
            True(life.CreatedHealth.Count == 1 && life.CreatedHealth[0] == 15, "OnEntityCreated fires with components, filtered");

            dmg.Target = target; dmg.PostInTick = true;
            w3.Update(0.016f);
            True(dmg.PendingAtLate == 1 && dmg.Consumed == 1, "events posted in Tick are pending at LateTick, consumed at end of frame");
            True(w3.PendingEvents<DamageEvent>() == 0, "event list cleared after the frame");
            True(w3.Get<Health>(target).Value == 5, "consumer applied damage");
            w3.Update(0.016f);
            True(!w3.IsAlive(target), "consumer destroyed the entity through its command buffer");
            True(life.DestroyedHealth.Count == 1 && life.DestroyedHealth[0] == -5, "OnEntityDestroyed sees components before removal");
        }

        // ---- fixed tick + sim start/end
        using (var w4 = new World(0))
        {
            var fc = w4.AddSystem(new FixedCounter());
            w4.FixedDeltaTime = 0.02f;
            for (int i = 0; i < 50; i++) w4.Update(0.01f); // 0.5 s
            True(fc.Starts == 1, "OnSimStart once");
            True(fc.Steps == 25 || fc.Steps == 24, $"fixed steps follow sim time ({fc.Steps})");
            w4.EndSim();
            True(fc.Ends == 1, "OnSimEnd");
        }

        // ---- prefabs: JSON, inheritance, field merge, bake
        using (var w5 = new World(0))
        {
            var lib = new PrefabLibrary();
            lib.AddJson(@"{ ""name"": ""base"", ""components"": { ""Health"": { ""max"": 50 }, ""Position"": { ""x"": 1, ""y"": 2 } } }");
            lib.AddJson(@"{ ""name"": ""tough"", ""base"": ""base"", ""components"": { ""HealthConfig"": { ""max"": 200 }, ""Position"": { ""y"": 9 } } }");
            var life = w5.AddSystem(new LifecycleSystem());
            var e = lib.Get("tough").Instantiate(w5);
            w5.Sync();
            True(w5.Get<Health>(e).Max == 200 && w5.Get<Position>(e).X == 1 && w5.Get<Position>(e).Y == 9, "prefab inheritance merges fields, child wins");
            True(life.CreatedHealth.Count == 1 && life.CreatedHealth[0] == 200, "prefab spawn fires OnEntityCreated with baked components");
        }

        // ---- parallel chunk job over many entities and archetypes
        using (var w6 = new World(4))
        {
            const int N = 200_000;
            long expected = 0;
            for (int i = 0; i < N; i++)
            {
                var e = w6.CreateEntity();
                w6.Add(e, new Position { X = i % 100 });
                if (i % 3 == 0) w6.Add(e, new Velocity());
                if (i % 7 == 0) w6.Add(e, new Health());
                expected += i % 100;
            }
            w6.Sync();
            var q = w6.Query().With<Position>().Build();
            var job = new SumJob { PerWorker = new long[w6.Jobs.ThreadCount + 1] };
            q.ScheduleParallel(ref job, 4096);
            True(job.PerWorker.Sum() == expected, "parallel chunk job covers every row exactly once");

            var onlyHealth = w6.Query().With<Health>().Build();
            var sw = Stopwatch.StartNew(); int n = 0;
            for (int rep = 0; rep < 100; rep++) n = onlyHealth.Count();
            double us = sw.Elapsed.TotalMilliseconds * 10.0;
            True(n == (N + 6) / 7, "query count");
            Console.WriteLine($"  ecs: {N} entities, {w6.Archetypes.Count} archetypes; Health query touches {onlyHealth.MatchedArchetypes.Count} tables, {us:0.0} us per Count()");

            var vq = w6.Query().With<Position>().WithReadOnly<Velocity>().Build();
            sw.Restart();
            for (int rep = 0; rep < 100; rep++)
                foreach (var ch in vq) { var p = ch.Get<Position>(); for (int i = 0; i < ch.Length; i++) p[i].Y += 1; }
            Console.WriteLine($"  ecs: iterate {vq.Count()} Position+Velocity rows: {sw.Elapsed.TotalMilliseconds * 10.0:0.0} us per pass");
        }

        // ---- asset registry: handles are plain data, interning is idempotent, names round-trip
        {
            var reg = new AssetRegistry<string>();
            True(AssetHandle<string>.None.IsNone && reg.Resolve(default) == null, "default handle is None and resolves to null");
            var h1 = reg.Intern("crate-mesh", "crate");
            var h2 = reg.Intern("crate-mesh");
            True(h1.Equals(h2) && !h1.IsNone, "interning the same reference returns the same handle");
            True(ReferenceEquals(reg.Resolve(h1), "crate-mesh"), "resolve returns the interned asset");
            True(reg.NameOf(h1) == "crate" && reg.Find("CRATE").Equals(h1), "names round-trip, case-insensitive");
            int loads = 0;
            var h3 = reg.GetOrLoad("barrel", _ => { loads++; return "barrel-mesh"; });
            var h4 = reg.GetOrLoad("barrel", _ => { loads++; return "barrel-mesh-2"; });
            True(h3.Equals(h4) && loads == 1 && (string)reg.Resolve(h3) == "barrel-mesh", "GetOrLoad loads once and caches by name");
            True(reg.GetOrLoad(null, _ => "x").IsNone && reg.Intern(null).IsNone, "null name / asset maps to None");
        }
    }
}
internal static class EcsDebug
{
    public static void Dump(World w) { foreach (var b in w.DescribeBatches("Tick")) Console.WriteLine("  batch: " + string.Join(", ", b.Select(t => t.Name))); }
}
