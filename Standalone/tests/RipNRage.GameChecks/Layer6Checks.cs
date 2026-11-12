using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.AI;
using RipNRage.Game.Ecs.Combat;

/// <summary>
/// Layer 6 (enemies, AI). 6.2: the behaviour tree became a state tree — a value (<see cref="AiBrain"/>) stepped by
/// pure functions, writing an <see cref="AiIntent"/>. The checks pin the tree's shape: the interrupt order (the
/// target branch is decided before the leaf runs), the wander dwell, and that a tick is a function of its inputs.
/// </summary>
internal static class Layer6Checks
{
    public static void Run(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var saved = Enemies.All;
        Enemies.All = new[] { new EnemyData { name = "TestGrunt", attackRange = 10f, wanderWait = 1f, wanderRadius = 15f, wanderMinDistance = 10f } };
        try
        {
            StateTree(True, Equal);
            BrainSystem(True);
        }
        finally { Enemies.All = saved; }
    }

    /// <summary>The pure step, driven by hand.</summary>
    private static void StateTree(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var def = Enemies.All[0];
        var self = new Vector3(300f, 1f, 0f);
        var b = new AiBrain(); var intent = new AiIntent();

        // unaware: idle for wanderWait, then one wander tick that moves and returns to idle
        for (int i = 0; i < 60; i++) AiBrains.Step(ref b, ref intent, def, self, false, default, 1f / 60f);
        True(b.State == AiState.Idle && !intent.Move && !intent.Attack, "a fresh brain idles quietly through the dwell");
        int moves = 0; Vector3 point = default;
        for (int i = 0; i < 120 && moves == 0; i++)
        {
            AiBrains.Step(ref b, ref intent, def, self, false, default, 1f / 60f);
            if (intent.Move) { moves++; point = intent.MoveTo; }
        }
        True(moves == 1, "after the dwell the brain wanders: exactly one move intent");
        float d = Vector3.Distance(point, self);
        True(d > def.wanderMinDistance && d <= def.wanderRadius, $"the wander point is {d:0.0} m away: beyond the minimum, inside the radius");
        True(b.State == AiState.Idle && b.StateTime == 0f, "arrival is instant for now: back to idle with the clock reset");

        // engaged: the target branch outranks the leaf
        var far = self + new Vector3(0f, 0f, 20f);
        AiBrains.Step(ref b, ref intent, def, self, true, far, 1f / 60f);
        True(b.State == AiState.Chase && intent.Move && intent.MoveTo == far && !intent.Attack, "a live target 20 m away: chase towards it");
        var near = self + new Vector3(0f, 0f, 5f);
        AiBrains.Step(ref b, ref intent, def, self, true, near, 1f / 60f);
        True(b.State == AiState.Attack && intent.Attack && intent.FacePoint == near && !intent.Move, "the target 5 m away: attack, aimed at it, standing still");
        Equal(0f, b.StateTime, "entering a leaf resets its clock");
        AiBrains.Step(ref b, ref intent, def, self, true, far, 1f / 60f);
        True(b.State == AiState.Chase, "the target backing out of range drops to chase again");

        // the target is lost (dead or gone): back to the unaware half, quietly
        b.Target = new Entity(7, 1);
        AiBrains.Step(ref b, ref intent, def, self, false, default, 1f / 60f);
        True(b.State == AiState.Idle && b.Target.IsNull && !intent.Move && !intent.Attack, "a lost target drops the brain to idle and clears it");

        // purity: the same inputs give the same outputs (wander rolls the dice, so pin it on the deterministic leaves)
        var b1 = new AiBrain { State = AiState.Chase, StateTime = 3f }; var i1 = new AiIntent();
        var b2 = b1; var i2 = new AiIntent();
        AiBrains.Step(ref b1, ref i1, def, self, true, far, 0.5f);
        AiBrains.Step(ref b2, ref i2, def, self, true, far, 0.5f);
        True(b1.State == b2.State && b1.StateTime == b2.StateTime && b1.MoveTo == b2.MoveTo && i1.Move == i2.Move && i1.MoveTo == i2.MoveTo,
             "a step is a function of its inputs");
        Equal(3.5f, b1.StateTime, "the leaf clock advances by dt");
    }

    /// <summary>The system over a world: the target's liveness is read off the world, and a dead enemy's brain stops.</summary>
    private static void BrainSystem(Action<bool, string> True)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(RipNRage.Game.Ecs.PlayerLink).Assembly);
        w.FixedDeltaTime = 1f / 60f;

        var enemy = w.CreateEntity();
        w.Add(enemy, LocalTransform.At(new Vector3(400f, 1f, 0f)));
        w.Add(enemy, new Enemy { Kind = 0 });
        w.Add(enemy, new AiBrain { State = AiState.Idle });
        w.Add(enemy, new AiIntent());
        var player = w.CreateEntity();
        w.Add(player, LocalTransform.At(new Vector3(400f, 1f, 6f)));
        w.Add(player, new Health { Current = 100f, Max = 100f });
        w.Update(1f / 60f);

        w.Get<AiBrain>(enemy).Target = player; // what the vision sensor does
        w.Update(1f / 60f);
        True(w.Get<AiBrain>(enemy).State == AiState.Attack && w.Get<AiIntent>(enemy).Attack, "the system reads the target's pose off the world: in range → attack");

        w.Add(player, new Dead());
        w.Update(1f / 60f);
        True(w.Get<AiBrain>(enemy).State == AiState.Idle && w.Get<AiBrain>(enemy).Target.IsNull, "a dead target is dropped");

        w.Get<AiBrain>(enemy).Target = player;
        w.Remove<Dead>(player);
        w.Add(enemy, new Dead());
        var before = w.Get<AiBrain>(enemy);
        w.Update(1f / 60f);
        var after = w.Get<AiBrain>(enemy);
        True(after.State == before.State && after.StateTime == before.StateTime && !w.Get<AiIntent>(enemy).Attack && !w.Get<AiIntent>(enemy).Move,
             "a dead enemy's brain stops and its intent reads quiet");

        w.DestroyAllEntities();
        w.Update(1f / 60f);
    }
}
