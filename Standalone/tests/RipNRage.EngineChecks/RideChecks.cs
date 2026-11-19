using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

/// <summary>
/// What a character stands on, and what it does to it. Each check here was a failure first:
///
/// <list type="bullet">
/// <item>A disposed world left its colliders in the global physics world, still simulated — the next world's
/// characters stood on ghosts (the checks had been dodging it by placing every scenario at a different x).</item>
/// <item>Buoyancy cut a sample's lift from half a cell's worth to nothing as its centre crossed the surface, so a
/// light hull chattered across that edge for ever; every load measured on it was an average over the chatter.</item>
/// <item>The ride was a spring applied to the character's velocity before the step, with the reaction thrown at the
/// ground as a force; landing on a light crate put thousands of newtons into it in one step.</item>
/// <item>Traction matched the rider's velocity to the ground's by pushing both and ignoring that the ground moves
/// too: an eleven-fold overshoot on an 8 kg crate, which buzzed under the rider and walked away.</item>
/// </list>
/// </summary>
internal static class RideChecks
{
    private const float Dt = 1f / 60f;
    private const float Weight = 80f * 9.81f;

    public static void Run(Action<bool, string> True)
    {
        NoGhosts(True);
        try
        {
            WaterSurface.HeightOf = (x, z) => 0f;
            BuoyancyAtRest(True);
            WeightLaw(True);
        }
        finally { WaterSurface.Clear(); }
        LightGround(True);
        SameFeet(True);
    }

    /// <summary>
    /// Moving the feet into the solver must not change how a character runs: on still ground the speed has to follow
    /// the old motor's curve exactly - v = min(target, a·t) away, v = v0 − a·t to a stop - step for step.
    /// </summary>
    private static void SameFeet(Action<bool, string> True)
    {
        using var w = NewWorld();
        Box(w, new Vector3(0, -0.5f, 0), new Vector3(400, 1, 400), 0f, isStatic: true);
        var ch = Character(w, new Vector3(-150, 1.2f, 0));
        w.Sync();
        for (int i = 0; i < 120; i++) w.Update(Dt);
        var m0 = w.Get<CharacterMotor>(ch);
        float worst = 0f;
        for (int i = 1; i <= 20; i++)
        {
            w.Get<CharacterMotor>(ch).Desired = new Vector3(16f, 0f, 0f);
            w.Update(Dt);
            worst = Math.Max(worst, Math.Abs(w.Get<BodyVelocity>(ch).Linear.x - Math.Min(16f, m0.Acceleration * i * Dt)));
        }
        True(worst < 0.01f, $"running up to speed follows the old motor's curve exactly (worst {worst:0.0000} m/s off)");
        worst = 0f;
        for (int i = 1; i <= 18; i++)
        {
            w.Get<CharacterMotor>(ch).Desired = Vector3.zero;
            w.Update(Dt);
            worst = Math.Max(worst, Math.Abs(w.Get<BodyVelocity>(ch).Linear.x - Math.Max(0f, 16f - m0.StopDeceleration * i * Dt)));
        }
        True(worst < 0.01f, $"and so does stopping (worst {worst:0.0000} m/s off)");
    }

    private static World NewWorld()
    {
        var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        w.FixedDeltaTime = Dt;
        return w;
    }

    private static Entity Character(World w, Vector3 at)
    {
        var ch = w.CreateEntity();
        w.Add(ch, LocalTransform.At(at));
        w.Add(ch, new ColliderShape { Kind = ShapeKind.Capsule, Radius = 0.4f, Height = 1.3f });
        w.Add(ch, new BodyConfig { mass = 80f }.Bake());
        w.Add(ch, new BodyVelocity());
        w.Add(ch, new CharacterMotorConfig { rideHeight = 0.5f }.Bake());
        return ch;
    }

    private static Entity Box(World w, Vector3 at, Vector3 size, float mass, bool isStatic = false)
    {
        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(at));
        w.Add(e, new ColliderShape { Kind = ShapeKind.Box, Size = size, IsStatic = isStatic });
        if (!isStatic) { w.Add(e, new BodyConfig { mass = mass }.Bake()); w.Add(e, new BodyVelocity()); }
        return e;
    }

    /// <summary>A disposed world takes its colliders with it.</summary>
    private static void NoGhosts(Action<bool, string> True)
    {
        var spot = new Vector3(2000f, 0f, 0f);
        using (var w = NewWorld())
        {
            Box(w, spot, new Vector3(4, 1, 4), 50f);
            w.Sync();
            for (int i = 0; i < 5; i++) w.Update(Dt);
            True(Physics.Raycast(spot + Vector3.up * 10f, Vector3.down, out _, 30f, ~0), "a live world's collider is in the physics world");
        }
        True(!Physics.Raycast(spot + Vector3.up * 10f, Vector3.down, out _, 30f, ~0),
             "and gone from it once the world is disposed - no ghost left for the next world to stand on");
    }

    /// <summary>A floating body under a steady load settles where Archimedes says, and stops there.</summary>
    private static void BuoyancyAtRest(Action<bool, string> True)
    {
        // a raft of 6 m³ displacement over a 6 × 0.5 × 6 box: it floats on 120.7 kN/m of waterplane, empty at 0.2012
        foreach (float load in new[] { 0f, 785f, 3140f })
        {
            using var w = NewWorld();
            var raft = Raft(w);
            w.Sync();
            var sync = w.GetSystem<PhysicsSyncSystem>();
            double y = 0, speed = 0; int n = 0;
            for (int i = 0; i < 1800; i++)
            {
                sync.BodyOf(raft)?.AddForce(new Vector3(0, -load, 0), ForceMode.Force);
                w.Update(Dt);
                if (i >= 1200) { n++; y += w.Get<LocalTransform>(raft).Position.y; speed += Math.Abs(w.Get<BodyVelocity>(raft).Linear.y); }
            }
            float expected = 0.2012f - load / 120700f;
            True(Math.Abs(y / n - expected) < 0.001, $"under {load:0} N the raft floats where Archimedes puts it ({y / n:0.0000} m, expected {expected:0.0000})");
            True(speed / n < 0.001, $"and comes to rest there rather than chattering at the waterline (mean speed {speed / n:0.0000} m/s)");
        }
    }

    private static Entity Raft(World w)
    {
        var raft = Box(w, Vector3.zero, new Vector3(6f, 0.5f, 6f), 600f);
        w.Add(raft, new BuoyancyConfig { volume = 6f, resolution = 3, angularDrag = 4f, heaveDamping = 0.9f }.Bake());
        return raft;
    }

    /// <summary>A character's weight reaches what it stands on - all of it, the same as a box of its mass.</summary>
    private static void WeightLaw(Action<bool, string> True)
    {
        float Settle(Func<World, Entity, Entity> load)
        {
            using var w = NewWorld();
            var raft = Raft(w);
            load(w, raft);
            w.Sync();
            double y = 0; int n = 0;
            for (int i = 0; i < 1800; i++) { w.Update(Dt); if (i >= 1200) { n++; y += w.Get<LocalTransform>(raft).Position.y; } }
            return (float)(y / n);
        }
        float empty = Settle((w, r) => Entity.Null);
        float box = Settle((w, r) => Box(w, new Vector3(0, 0.8f, 0), new Vector3(0.8f, 0.8f, 0.8f), 80f));
        float one = Settle((w, r) => Character(w, new Vector3(0, 1.6f, 0)));
        float four = Settle((w, r) =>
        {
            foreach (var at in new[] { new Vector3(-1.5f, 1.6f, -1.5f), new Vector3(1.5f, 1.6f, -1.5f), new Vector3(-1.5f, 1.6f, 1.5f), new Vector3(1.5f, 1.6f, 1.5f) })
                Character(w, at);
            return Entity.Null;
        });

        float boxSank = empty - box, oneSank = empty - one, fourSank = empty - four;
        True(Math.Abs(boxSank - Weight / 120700f) < 0.0005f, $"an 80 kg box sinks the raft by its weight ({boxSank * 1000f:0.00} mm)");
        True(Math.Abs(oneSank - boxSank) < 0.0005f,
             $"a character sinks it exactly as far as a box of its mass ({oneSank * 1000f:0.00} mm vs {boxSank * 1000f:0.00} mm) - its whole weight reaches the raft");
        True(Math.Abs(fourSank - 4f * boxSank) < 0.002f, $"and four of them four times as far ({fourSank * 1000f:0.00} mm)");
    }

    /// <summary>
    /// Standing on, and landing on, bodies from a fortieth of the rider's mass to five times it: the crate stays
    /// where it is and the rider stays on it. A body lighter than a tenth of the rider is treated as a tenth in the
    /// ride's rows (RideConstraint.maxMassRatio) - an iterative solver cannot otherwise converge a stiff spring above
    /// it against a rigid floor below - so 2 kg and 8 kg behave alike here.
    /// </summary>
    private static void LightGround(Action<bool, string> True)
    {
        foreach (bool drop in new[] { false, true })
        foreach (float mass in new[] { 2f, 8f, 80f, 400f })
        {
            using var w = NewWorld();
            Box(w, new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), 0f, isStatic: true);
            var crate = Box(w, new Vector3(0, 0.5f, 0), new Vector3(1.5f, 1f, 1.5f), mass);
            var ch = Character(w, new Vector3(0, drop ? 5f : 2.2f, 0));
            w.Sync();
            float fastest = 0f; int on = 0;
            for (int i = 0; i < 600; i++)
            {
                w.Update(Dt);
                if (i >= 60) fastest = Math.Max(fastest, w.Get<BodyVelocity>(crate).Linear.magnitude);
                if (i >= 300 && w.Get<CharacterMotor>(ch).Ground == crate) on++;
            }
            var p = w.Get<LocalTransform>(crate).Position;
            string what = $"{(drop ? "landing from 3 m on" : "standing on")} a {mass:0} kg crate";
            True(new Vector3(p.x, 0f, p.z).magnitude < 0.05f && Math.Abs(p.y - 0.5f) < 0.03f,
                 $"{what}: the crate stays where it is (ends at {p.x:0.000}, {p.y:0.000}, {p.z:0.000})");
            True(fastest < 0.5f, $"{what}: and does not rattle or buzz under the rider (fastest {fastest:0.00} m/s)");
            True(on == 300, $"{what}: and the rider stays on it ({on} of 300 steps)");
        }

        // a character standing on a character's head
        using (var w = NewWorld())
        {
            Box(w, new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), 0f, isStatic: true);
            var under = Character(w, new Vector3(0, 1.2f, 0));
            var top = Character(w, new Vector3(0, 3.4f, 0));
            w.Sync();
            for (int i = 0; i < 600; i++) w.Update(Dt);
            True(w.Get<CharacterMotor>(top).Ground == under && w.Get<CharacterMotor>(under).IsGrounded,
                 "a character can stand on another's head, and the one underneath stays standing");
        }
    }
}
