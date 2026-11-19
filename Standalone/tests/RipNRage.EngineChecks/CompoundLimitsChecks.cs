using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

/// <summary>
/// V2b: the four limits V2 left, closed before V3 builds vessels out of convex brushes on top of them. Each check is
/// built so the limit itself would fail it: a convex part's inertia must be its shape's, not its box's; a convex part
/// must sweep as itself, not stop where its box would; a blast must strike a part, not the air in the box round them;
/// and the entity's scale must reach the parts.
/// </summary>
internal static class CompoundLimitsChecks
{
    private const float Dt = 1f / 60f;

    public static void Run(Action<bool, string> True)
    {
        ExactConvexInertia(True);
        ConvexSweepsAsItself(True);
        BlastsStrikeParts(True);
        Scale(True);
    }

    // ------------------------------------------------------------------ builders

    private static World NewWorld()
    {
        var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        w.FixedDeltaTime = Dt;
        return w;
    }

    private static CompoundChild BoxPart(Vector3 at, Vector3 size, Quaternion? rot = null) => new CompoundChild
    {
        Shape = new ColliderShape { Kind = ShapeKind.Box, Size = size }, Position = at, Rotation = rot ?? Quaternion.identity, Density = 1f,
    };

    /// <summary>A convex part from its corners (faces derived), placed at <paramref name="at"/>.</summary>
    private static CompoundChild ConvexPart(Vector3 at, Vector3[] corners)
    {
        var pts = new float[corners.Length][];
        for (int i = 0; i < pts.Length; i++) pts[i] = new[] { corners[i].x, corners[i].y, corners[i].z };
        var shape = new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = pts }.Bake();
        return new CompoundChild { Shape = shape, Position = at, Rotation = Quaternion.identity, Density = 1f };
    }

    private static Entity Compound(World w, Vector3 at, float mass, CompoundChild[] parts, Vector3? scale = null,
                                   bool gravity = true, CollisionDetectionMode ccd = CollisionDetectionMode.Discrete)
    {
        var e = w.CreateEntity();
        var lt = LocalTransform.At(at);
        if (scale.HasValue) lt.Scale = scale.Value;
        w.Add(e, lt);
        w.Add(e, new CompoundShape { Asset = PhysicsAssets.AddCompound("v2b-" + Guid.NewGuid().ToString("N"), parts) });
        var body = new BodyConfig { mass = mass, useGravity = gravity }.Bake();
        body.CollisionDetection = ccd;
        w.Add(e, body);
        w.Add(e, new BodyVelocity());
        return e;
    }

    private static Entity StaticBox(World w, Vector3 at, Vector3 size)
    {
        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(at));
        w.Add(e, new ColliderShape { Kind = ShapeKind.Box, Size = size, IsStatic = true });
        return e;
    }

    private static SolverBody BodyOf(World w, Entity e) => w.GetSystem<PhysicsSyncSystem>().BodyOf(e);
    private static Vector3 Pos(World w, Entity e) => w.Get<LocalTransform>(e).Position;

    private static Vector3 Spin(World w, Entity e, Vector3 angularImpulse)
    {
        var b = BodyOf(w, e);
        b.angularVelocity = Vector3.zero;
        b.AddTorque(angularImpulse, ForceMode.Impulse);
        return b.angularVelocity;
    }

    private static Vector3[] BoxCorners(Vector3 size, Quaternion rot)
    {
        var h = size * 0.5f;
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++) c[i] = rot * new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
        return c;
    }

    /// <summary>A wedge, sloping back from its bottom front edge (x = +0.5) to a thin top at the back (x -0.5..-0.4).</summary>
    private static Vector3[] Wedge() => new[]
    {
        new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.4f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
        new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.4f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
    };

    // ------------------------------------------------------------------ L1

    private static void ExactConvexInertia(Action<bool, string> True)
    {
        using var w = NewWorld();
        var size = new Vector3(3f, 1f, 1.5f);
        var turn = Quaternion.Euler(20f, 35f, 10f);
        // the same turned box twice: as a box part turned, and as a convex part whose corners are already turned
        var asBox = Compound(w, new Vector3(8000, 50, 0), 12f, new[] { BoxPart(Vector3.zero, size, turn) }, gravity: false);
        var asHull = Compound(w, new Vector3(8010, 50, 0), 12f, new[] { ConvexPart(Vector3.zero, BoxCorners(size, turn)) }, gravity: false);
        var straightBox = Compound(w, new Vector3(8020, 50, 0), 12f, new[] { BoxPart(Vector3.zero, size) }, gravity: false);
        var straightHull = Compound(w, new Vector3(8030, 50, 0), 12f, new[] { ConvexPart(Vector3.zero, BoxCorners(size, Quaternion.identity)) }, gravity: false);
        w.Sync();
        w.Update(Dt);

        var L = new Vector3(1f, -2f, 3f);
        var a = Spin(w, straightBox, L); var b = Spin(w, straightHull, L);
        True((a - b).magnitude < 1e-3f * a.magnitude, $"a convex part shaped as a box has the box's inertia exactly ({b} vs {a})");
        var c = Spin(w, asBox, L); var d = Spin(w, asHull, L);
        True((c - d).magnitude < 1e-3f * c.magnitude,
             $"and turned, its full tensor - off-diagonal terms and all - is the turned box's ({d} vs {c}); its bounding box's would not be");
    }

    // ------------------------------------------------------------------ L2

    private static void ConvexSweepsAsItself(Action<bool, string> True)
    {
        using var w = NewWorld();
        // a bar across the wedge's path, high enough that the wedge's low front passes under it and only its slope
        // reaches it: its box would be stopped 0.73 m sooner than the wedge itself
        float barX = 8100f;
        StaticBox(w, new Vector3(barX, 50.3f, 0f), new Vector3(0.2f, 0.2f, 4f));
        var wedge = Compound(w, new Vector3(barX - 5f, 50f, 0f), 20f, new[] { ConvexPart(Vector3.zero, Wedge()) },
                             gravity: false, ccd: CollisionDetectionMode.Continuous);
        w.Sync();
        w.Update(Dt);
        BodyOf(w, wedge).linearVelocity = new Vector3(80f, 0f, 0f);
        BodyOf(w, wedge).angularVelocity = Vector3.zero;
        // where the sweep first holds it back: the first step that covers less than its full 80/60 m. What the contact
        // does after that - a slope striking an edge at 80 m/s deflects down and can ride under the bar - is not the sweep.
        float stoppedAt = float.NaN, last = Pos(w, wedge).x;
        for (int i = 0; i < 12 && float.IsNaN(stoppedAt); i++)
        {
            w.Update(Dt);
            float x = Pos(w, wedge).x;
            if (x - last < 80f * Dt - 0.05f) stoppedAt = x;
            last = x;
        }

        // the slope at the bar's underside (y = +0.2 on the wedge) is at x = 0.5 - 0.9 * 0.7 = -0.13
        float trueStop = (barX - 0.1f) + 0.13f, boxStop = (barX - 0.1f) - 0.5f;
        True(!float.IsNaN(stoppedAt), "a wedge thrown at 80 m/s at a bar is held back by it");
        True(stoppedAt > boxStop + 0.4f, $"not where its box would be (held at x={stoppedAt:0.000}; its box stops at {boxStop:0.000})");
        True(Mathf.Abs(stoppedAt - trueStop) < 0.05f, $"but where its own slope meets the bar (held at x={stoppedAt:0.000}; the slope meets it at {trueStop:0.000})");
    }

    // ------------------------------------------------------------------ L3

    private static void BlastsStrikeParts(Action<bool, string> True)
    {
        using var w = NewWorld();
        // an open deck with a deckhouse aft: the box the boat fits in reaches roof height over the whole deck, the
        // parts do not
        var parts = new[] { BoxPart(new Vector3(0, 0, 0), new Vector3(4f, 0.2f, 8f)), BoxPart(new Vector3(0, 3f, -3f), new Vector3(3f, 0.2f, 1f)) };
        var boat = Compound(w, new Vector3(8200, 50, 0), 500f, parts, gravity: false);
        var control = Compound(w, new Vector3(8300, 50, 0), 500f, parts, gravity: false);
        w.Sync();
        w.Update(Dt);

        // over the open foredeck, 0.4 m above the box's top but 3.4 m from the deck and 4.5 m from the roof
        Physics.AddExplosionForce(400f, new Vector3(8200, 53.5f, 2f), 2f);
        True(BodyOf(w, boat).linearVelocity.magnitude < 1e-4f && BodyOf(w, boat).angularVelocity.magnitude < 1e-4f,
             "a blast close to the box the boat fits in, but in reach of none of its parts, does not move it");
        // the same blast beside the deckhouse roof does
        Physics.AddExplosionForce(400f, new Vector3(8300, 53.5f, -3f), 2f);
        True(BodyOf(w, control).linearVelocity.magnitude > 0.05f, "while the same blast beside a part pushes the boat");
        True(BodyOf(w, control).angularVelocity.magnitude > 0.01f, "and spins it about the point it struck");
    }

    // ------------------------------------------------------------------ L4

    private static void Scale(Action<bool, string> True)
    {
        using (var w = NewWorld())
        {
            // uniform: an L at scale 2 is the L built twice the size
            var small = new[] { BoxPart(new Vector3(2f, 0.5f, 0f), new Vector3(4, 1, 1)), BoxPart(new Vector3(0.5f, 2.5f, 0f), new Vector3(1, 3, 1)) };
            var big = new[] { BoxPart(new Vector3(4f, 1f, 0f), new Vector3(8, 2, 2)), BoxPart(new Vector3(1f, 5f, 0f), new Vector3(2, 6, 2)) };
            var scaled = Compound(w, new Vector3(8400, 50, 0), 7f, small, scale: new Vector3(2, 2, 2), gravity: false);
            var built = Compound(w, new Vector3(8410, 50, 0), 7f, big, gravity: false);
            w.Sync();
            w.Update(Dt);
            var com1 = BodyOf(w, scaled).worldCenterOfMass - new Vector3(8400, 50, 0);
            var com2 = BodyOf(w, built).worldCenterOfMass - new Vector3(8410, 50, 0);
            True((com1 - com2).magnitude < 1e-4f, $"an L at scale 2 has the centre of mass of one built twice the size ({com1} vs {com2})");
            var L = new Vector3(1f, 0.5f, 0.25f);
            var s1 = Spin(w, scaled, L); var s2 = Spin(w, built, L);
            True((s1 - s2).magnitude < 1e-3f * s2.magnitude, $"and turns the same under a torque ({s1} vs {s2})");
        }

        using (var w = NewWorld())
        {
            // non-uniform (1, 2, 1): a box on the axes stays a box twice as tall; a box turned 45° becomes a stretched
            // diamond - a parallelepiped - with its top corner twice as high
            var upright = Compound(w, new Vector3(8500, 50, 0), 5f, new[] { BoxPart(Vector3.zero, Vector3.one) }, scale: new Vector3(1, 2, 1), gravity: false);
            var diamond = Compound(w, new Vector3(8510, 50, 0), 5f, new[] { BoxPart(Vector3.zero, Vector3.one, Quaternion.AngleAxis(45f, Vector3.forward)) },
                                   scale: new Vector3(1, 2, 1), gravity: false);
            // mirrored in x: the wedge's slope now faces the other way
            var mirrored = Compound(w, new Vector3(8520, 50, 0), 5f, new[] { ConvexPart(Vector3.zero, Wedge()) }, scale: new Vector3(-1, 1, 1), gravity: false);
            w.Sync();
            w.Update(Dt);

            True(Physics.Raycast(new Vector3(8500, 60, 0), Vector3.down, out var top, 20f, ~0) && Mathf.Abs(top.point.y - 51f) < 0.01f,
                 $"a box on the axes, scaled (1, 2, 1), is a box twice as tall (top at {top.point.y - 50f:0.000} above its origin; 1.000)");
            True(BodyOf(w, upright).PartCount == 1, "and still one part");
            float diamondTop = 2f * MathF.Sqrt(0.5f);
            True(Physics.Raycast(new Vector3(8510, 60, 0), Vector3.down, out var tip, 20f, ~0) && Mathf.Abs(tip.point.y - (50f + diamondTop)) < 0.01f,
                 $"a box turned 45°, scaled (1, 2, 1), has its top corner twice as high ({tip.point.y - 50f:0.000}; {diamondTop:0.000})");
            True(Physics.Raycast(new Vector3(8510.3f, 60, 0), Vector3.down, out var side, 20f, ~0) && Mathf.Abs(side.point.y - (50f + 2f * (MathF.Sqrt(0.5f) - 0.3f))) < 0.01f,
                 $"and its sloping sides are where the stretched diamond's are, not where a box round it would be (y={side.point.y - 50f:0.000}; {2f * (MathF.Sqrt(0.5f) - 0.3f):0.000})");
            // unmirrored, at x = +0.3 the slope is at -0.5 + (0.5 - 0.3) / 0.9; mirrored, at -0.5 + (0.3 + 0.5) / 0.9
            float expected = -0.5f + (0.3f + 0.5f) / 0.9f;
            True(Physics.Raycast(new Vector3(8520.3f, 60, 0), Vector3.down, out var slope, 20f, ~0) && Mathf.Abs(slope.point.y - (50f + expected)) < 0.01f,
                 $"a wedge mirrored by a negative scale slopes the other way, and is not turned inside out (y={slope.point.y - 50f:0.000}; {expected:0.000})");
        }

        using (var w = NewWorld())
        {
            // a table at scale 2 stands on its scaled legs; then made 1.5 at runtime, it is set up again at that size
            StaticBox(w, new Vector3(8600, -0.5f, 0), new Vector3(40, 1, 40));
            var table = new[]
            {
                BoxPart(new Vector3(0, 1.0f, 0), new Vector3(2f, 0.2f, 2f)),
                BoxPart(new Vector3(-0.8f, 0.45f, -0.8f), new Vector3(0.2f, 0.9f, 0.2f)), BoxPart(new Vector3(0.8f, 0.45f, -0.8f), new Vector3(0.2f, 0.9f, 0.2f)),
                BoxPart(new Vector3(-0.8f, 0.45f, 0.8f), new Vector3(0.2f, 0.9f, 0.2f)), BoxPart(new Vector3(0.8f, 0.45f, 0.8f), new Vector3(0.2f, 0.9f, 0.2f)),
            };
            var e = Compound(w, new Vector3(8600, 0.5f, 0), 30f, table, scale: new Vector3(2, 2, 2));
            w.Sync();
            for (int i = 0; i < 240; i++) w.Update(Dt);
            var p = Pos(w, e);
            True(Mathf.Abs(p.y) < 0.03f, $"a table at scale 2 settles on its scaled legs (origin at y={p.y:0.000})");
            True(Physics.Raycast(new Vector3(p.x + 1.6f, 10, p.z + 1.6f), Vector3.down, out var top, 20f, ~0) && Mathf.Abs(top.point.y - (p.y + 2.2f)) < 0.02f,
                 $"with its top at twice the height ({top.point.y - p.y:0.000}; 2.200)");

            var lt = w.Get<LocalTransform>(e); lt.Scale = new Vector3(1.5f, 1.5f, 1.5f); w.Get<LocalTransform>(e) = lt;
            for (int i = 0; i < 240; i++) w.Update(Dt);
            p = Pos(w, e);
            True(Physics.Raycast(new Vector3(p.x + 1.2f, 10, p.z + 1.2f), Vector3.down, out var top2, 20f, ~0) && Mathf.Abs(top2.point.y - (p.y + 1.65f)) < 0.02f,
                 $"rescaled to 1.5 while it stands there, it is set up again at that size (top at {top2.point.y - p.y:0.000}; 1.650)");
            True(Mathf.Abs(p.y) < 0.03f, $"and settles on legs of that length (origin at y={p.y:0.000})");
        }

        try
        {
            WaterSurface.HeightOf = (x, z) => 0f;
            using var w = NewWorld();
            // a unit cube with a unit displacement box, at scale 2, of half the density of the 8 m³ it now is
            var cube = Compound(w, new Vector3(8700, 3f, 0), 0.5f * 1025f * 8f, new[] { BoxPart(Vector3.zero, Vector3.one) }, scale: new Vector3(2, 2, 2));
            w.Add(cube, new BuoyancyConfig { size = new[] { 1f, 1f, 1f }, resolution = 3, linearDrag = 20f, quadraticDrag = 0.6f, angularDrag = 2f }.Bake());
            w.Sync();
            for (int i = 0; i < 1200; i++) w.Update(Dt);
            float y = Pos(w, cube).y;
            True(Mathf.Abs(y) < 0.05f, $"a hull at scale 2 floats on its scaled envelope: half the density, half under (centre at y={y:0.000})");
        }
        finally { WaterSurface.Clear(); }
    }
}
