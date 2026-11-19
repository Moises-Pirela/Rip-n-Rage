using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

/// <summary>
/// V2: bodies made of parts. First that the parts add up — mass, centre, the full inertia tensor — then that they
/// collide as parts, then the reason for all of it: vessels. A speedboat with a hull, rails and a cabin; a ship with a
/// deck, a hatch, a ramp down into its hold, a deckhouse with a door; and a character getting from one to the other,
/// standing still and at speed, and off again onto a quay.
/// </summary>
internal static class CompoundChecks
{
    private const float Dt = 1f / 60f;

    public static void Run(Action<bool, string> True)
    {
        MassAddsUp(True);
        CollidesAsParts(True);
        FromAPrefab(True);
        Pile(True);
        try
        {
            WaterSurface.HeightOf = (x, z) => 0f;
            Speedboat(True);
            Boarding(True);
        }
        finally { WaterSurface.Clear(); }
        AboardTheShip(True);
        OnScreen(True);
    }

    /// <summary>
    /// What the player sees. Characters are drawn interpolated between the last two physics steps; a deck that is not
    /// is drawn a step ahead of whoever stands on it - up to 42 cm at a speedboat's 25 m/s, judder into the wall every
    /// frame. Whatever carries an interpolated character is now drawn interpolated with it: at frame times that do not
    /// line up with the physics step, a character standing still on a deck doing 25 m/s must be drawn standing still.
    /// </summary>
    private static void OnScreen(Action<bool, string> True)
    {
        using var w = NewWorld();
        var ship = Ship(w, new Vector3(7000, 0, 0), new Vector3(0, 0, 25f));
        var ch = Character(w, new Vector3(7000, 4.2f, 15f));
        w.Sync();
        Step(w, 90);
        True(w.Get<CharacterMotor>(ch).Ground == ship, "a character stands on a ship's deck at 25 m/s");
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < 120; i++)
        {
            w.Update(Dt * 0.37f); // frames that never line up with the physics step: the blend factor goes all over
            float drawn = Pos(w, ch).z - Pos(w, ship).z;
            if (i >= 20) { lo = Math.Min(lo, drawn); hi = Math.Max(hi, drawn); }
        }
        True(hi - lo < 0.01f, $"and is drawn standing still on it, frame after frame ({(hi - lo) * 100f:0.0} cm of judder; a step at 25 m/s is 42)");
        True(BodyOf(w, ship).interpolate, "because what carries an interpolated character is drawn interpolated with it");
    }

    // ------------------------------------------------------------------ builders

    private static World NewWorld()
    {
        var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        w.FixedDeltaTime = Dt;
        return w;
    }

    private static CompoundChild Part(Vector3 centre, Vector3 size, float density = 1f, Quaternion? rot = null) => new CompoundChild
    {
        Shape = new ColliderShape { Kind = ShapeKind.Box, Size = size }, Position = centre,
        Rotation = rot ?? Quaternion.identity, Density = density,
    };

    private static Entity Compound(World w, string name, Vector3 at, float mass, CompoundChild[] parts,
                                   bool kinematic = false, bool gravity = true, CollisionDetectionMode ccd = CollisionDetectionMode.Discrete)
    {
        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(at));
        w.Add(e, new CompoundShape { Asset = PhysicsAssets.AddCompound(name + "-" + Guid.NewGuid().ToString("N"), parts) });
        var body = new BodyConfig { mass = mass, kind = kinematic ? BodyKind.Kinematic : BodyKind.Dynamic, useGravity = gravity }.Bake();
        body.CollisionDetection = ccd;
        w.Add(e, body);
        w.Add(e, new BodyVelocity());
        return e;
    }

    private static Entity Box(World w, Vector3 at, Vector3 size, float mass, bool isStatic = false, bool gravity = true)
    {
        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(at));
        w.Add(e, new ColliderShape { Kind = ShapeKind.Box, Size = size, IsStatic = isStatic });
        if (!isStatic) { w.Add(e, new BodyConfig { mass = mass, useGravity = gravity }.Bake()); w.Add(e, new BodyVelocity()); }
        return e;
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

    private static SolverBody BodyOf(World w, Entity e) => w.GetSystem<PhysicsSyncSystem>().BodyOf(e);
    private static Vector3 Pos(World w, Entity e) => w.Get<LocalTransform>(e).Position;
    private static void Step(World w, int n) { for (int i = 0; i < n; i++) w.Update(Dt); }

    // ------------------------------------------------------------------ mass

    private static void MassAddsUp(Action<bool, string> True)
    {
        using var w = NewWorld();
        // one part at the origin is exactly the box it is made of
        var plain = Box(w, new Vector3(3000, 50, 0), new Vector3(2, 1, 4), 10f, gravity: false);
        var single = Compound(w, "single", new Vector3(3010, 50, 0), 10f, new[] { Part(Vector3.zero, new Vector3(2, 1, 4)) }, gravity: false);

        // an L of two boxes: 4 m along x on the ground, 3 m up the y axis at its end - products of inertia and all
        var lShape = Compound(w, "L", new Vector3(3020, 50, 0), 7f, new[]
        {
            Part(new Vector3(2f, 0.5f, 0f), new Vector3(4, 1, 1)),
            Part(new Vector3(0.5f, 2.5f, 0f), new Vector3(1, 3, 1)),
        }, gravity: false);

        // a long box lying along x, and the same box built along z and turned a quarter
        var along = Compound(w, "along", new Vector3(3030, 50, 0), 4f, new[] { Part(Vector3.zero, new Vector3(4, 1, 1)) }, gravity: false);
        var turned = Compound(w, "turned", new Vector3(3040, 50, 0), 4f,
            new[] { Part(Vector3.zero, new Vector3(1, 1, 4), rot: Quaternion.AngleAxis(90f, Vector3.up)) }, gravity: false);

        // two equal boxes, one three times as dense
        var dense = Compound(w, "dense", new Vector3(3050, 50, 0), 8f, new[]
        {
            Part(new Vector3(-1f, 0f, 0f), Vector3.one, density: 3f), Part(new Vector3(1f, 0f, 0f), Vector3.one),
        }, gravity: false);
        w.Sync();
        Step(w, 1);

        Vector3 Spin(Entity e, Vector3 angularImpulse)
        {
            var b = BodyOf(w, e);
            b.angularVelocity = Vector3.zero;
            b.AddTorque(angularImpulse, ForceMode.Impulse);
            return b.angularVelocity;
        }

        var L = new Vector3(1f, 2f, 3f);
        True((Spin(plain, L) - Spin(single, L)).magnitude < 1e-4f, "a one-part compound turns under a torque exactly as the box it is made of");
        True((BodyOf(w, plain).worldCenterOfMass - new Vector3(3000, 50, 0) - (BodyOf(w, single).worldCenterOfMass - new Vector3(3010, 50, 0))).magnitude < 1e-5f,
             "and has its centre of mass in the same place");

        // the L, worked out here independently: centre of mass, then the full tensor by parallel axes, inverted
        Vector3 c1 = new Vector3(2f, 0.5f, 0f), c2 = new Vector3(0.5f, 2.5f, 0f);
        float m1 = 4f, m2 = 3f;
        Vector3 com = (c1 * m1 + c2 * m2) / 7f;
        var comBody = BodyOf(w, lShape).worldCenterOfMass - new Vector3(3020, 50, 0);
        True((comBody - com).magnitude < 1e-4f, $"the L's centre of mass is where its parts put it ({comBody} vs {com})");

        Matrix3x3 Tensor(Vector3 size, float m, Vector3 offset)
        {
            float ix = m / 12f * (size.y * size.y + size.z * size.z), iy = m / 12f * (size.x * size.x + size.z * size.z), iz = m / 12f * (size.x * size.x + size.y * size.y);
            float d2 = offset.sqrMagnitude;
            return new Matrix3x3
            {
                m00 = ix + m * (d2 - offset.x * offset.x), m11 = iy + m * (d2 - offset.y * offset.y), m22 = iz + m * (d2 - offset.z * offset.z),
                m01 = -m * offset.x * offset.y, m10 = -m * offset.x * offset.y,
                m02 = -m * offset.x * offset.z, m20 = -m * offset.x * offset.z,
                m12 = -m * offset.y * offset.z, m21 = -m * offset.y * offset.z,
            };
        }
        var I = Tensor(new Vector3(4, 1, 1), m1, c1 - com) + Tensor(new Vector3(1, 3, 1), m2, c2 - com);
        var expected = I.Inverse().Multiply(L);
        var got = Spin(lShape, L);
        True((got - expected).magnitude < 1e-3f * expected.magnitude,
             $"the L turns as its full inertia tensor says - off-diagonal terms included ({got} vs {expected})");

        True((Spin(along, L) - Spin(turned, L)).magnitude < 1e-4f, "a part built along z and turned a quarter is the same body as one built along x");

        var denseCom = BodyOf(w, dense).worldCenterOfMass - new Vector3(3050, 50, 0);
        True(Mathf.Abs(denseCom.x - (-0.5f)) < 1e-4f, $"a part three times as dense pulls the centre of mass three-quarters of the way to it (x={denseCom.x:0.000})");
    }

    // ------------------------------------------------------------------ collision

    private static void CollidesAsParts(Action<bool, string> True)
    {
        using var w = NewWorld();
        Box(w, new Vector3(3200, -0.5f, 0), new Vector3(40, 1, 40), 0f, isStatic: true);
        // a table: a top and four legs, with nothing in between
        var parts = new[]
        {
            Part(new Vector3(0, 1.0f, 0), new Vector3(2f, 0.2f, 2f)),
            Part(new Vector3(-0.8f, 0.45f, -0.8f), new Vector3(0.2f, 0.9f, 0.2f)), Part(new Vector3(0.8f, 0.45f, -0.8f), new Vector3(0.2f, 0.9f, 0.2f)),
            Part(new Vector3(-0.8f, 0.45f, 0.8f), new Vector3(0.2f, 0.9f, 0.2f)), Part(new Vector3(0.8f, 0.45f, 0.8f), new Vector3(0.2f, 0.9f, 0.2f)),
        };
        var table = Compound(w, "table", new Vector3(3200, 1f, 0), 30f, parts);
        w.Sync();
        Step(w, 240);

        var p = Pos(w, table);
        True(Mathf.Abs(p.y) < 0.03f, $"a table settles on its four legs (origin at y={p.y:0.000})");
        True(BodyOf(w, table).linearVelocity.magnitude < 0.05f && Quaternion.Angle(w.Get<LocalTransform>(table).Rotation, Quaternion.identity) < 1f,
             "and stands still and level on them");

        // rays: on a part they name the table; between the parts they find nothing of it
        True(Physics.Raycast(new Vector3(3200.8f, 5f, 0.8f), Vector3.down, out var top, 10f, ~0) && top.entity == table,
             $"a ray onto the table top hits the table (hit {top.entity} at y={top.point.y:0.00})");
        True(Physics.World.Table.BodyOf(top.proxy) == BodyOf(w, table), "and its collider names the table's body");
        bool under = Physics.Raycast(new Vector3(3196f, 0.5f, 0f), Vector3.right, out var gap, 8f, ~0);
        True(!under || gap.entity != table, "a ray between the legs passes under the table - the parts are parts, not one box");
        // in line with a pair of legs, from where the table actually stands (it settles a few centimetres from where it fell)
        var tp = Pos(w, table);
        True(Physics.Raycast(new Vector3(3196f, 0.5f, tp.z + 0.8f), Vector3.right, out var leg, 8f, ~0) && leg.entity == table && Mathf.Abs(leg.point.x - (tp.x - 0.9f)) < 0.02f,
             $"and one in line with the legs stops at the first leg's face (x={leg.point.x:0.000}, the face is at {tp.x - 0.9f:0.000})");

        // a crate stacked on the table top rests on that part
        var crate = Box(w, new Vector3(3200, 2.5f, 0), new Vector3(0.6f, 0.6f, 0.6f), 5f);
        w.Sync();
        Step(w, 240);
        float crateY = Pos(w, crate).y;
        True(Mathf.Abs(crateY - (Pos(w, table).y + 1.1f + 0.3f)) < 0.03f, $"a crate dropped on the table rests on its top (y={crateY:0.000})");

        // a fast compound cannot tunnel through a thin wall
        Box(w, new Vector3(3230, 2f, 0), new Vector3(0.2f, 4f, 4f), 0f, isStatic: true);
        var dart = Compound(w, "dart", new Vector3(3215, 2f, 0), 5f, new[] { Part(Vector3.zero, new Vector3(0.4f, 0.4f, 0.4f)), Part(new Vector3(-0.6f, 0, 0), new Vector3(0.4f, 0.4f, 0.4f)) },
                            gravity: false, ccd: CollisionDetectionMode.Continuous);
        w.Sync();
        Step(w, 1);
        BodyOf(w, dart).linearVelocity = new Vector3(80f, 0f, 0f);
        Step(w, 30);
        True(Pos(w, dart).x < 3230f, $"a compound thrown at 80 m/s at a 20 cm wall stops at it (x={Pos(w, dart).x:0.00}, the wall is at 3229.9)");
    }

    /// <summary>A hundred compounds, stacked three high: part-to-part contacts between bodies, by the hundred, settling.</summary>
    private static void Pile(Action<bool, string> True)
    {
        using var w = NewWorld();
        Box(w, new Vector3(3600, -0.5f, 0), new Vector3(80, 1, 80), 0f, isStatic: true);
        // stools: a seat and three legs, so every contact in the pile is part against part
        var stool = new[]
        {
            Part(new Vector3(0, 0.55f, 0), new Vector3(1.2f, 0.1f, 1.2f)),
            Part(new Vector3(-0.45f, 0.25f, -0.45f), new Vector3(0.12f, 0.5f, 0.12f)),
            Part(new Vector3(0.45f, 0.25f, -0.45f), new Vector3(0.12f, 0.5f, 0.12f)),
            Part(new Vector3(0f, 0.25f, 0.45f), new Vector3(0.12f, 0.5f, 0.12f)),
        };
        var all = new System.Collections.Generic.List<Entity>();
        for (int i = 0; i < 100; i++)
        {
            int col = i / 3, level = i % 3;
            var at = new Vector3(3600 + (col % 6) * 2.5f, 0.02f + level * 0.62f, (col / 6) * 2.5f);
            all.Add(Compound(w, "stool", at, 6f, stool));
        }
        w.Sync();
        Step(w, 600);
        float fastest = 0f, lowest = float.MaxValue; int fallen = 0;
        foreach (var e in all)
        {
            fastest = Math.Max(fastest, BodyOf(w, e).linearVelocity.magnitude);
            lowest = Math.Min(lowest, Pos(w, e).y);
            if (Pos(w, e).y < -0.05f) fallen++;
        }
        True(fallen == 0, $"a hundred compounds stacked three high: none goes through the floor ({fallen}; lowest at y={lowest:0.000})");
        True(fastest < 0.1f, $"and the pile settles instead of buzzing or exploding (fastest {fastest:0.000} m/s after ten seconds)");
    }

    /// <summary>A compound written as a prefab, the way a level's vessels will be: parts of every kind, a convex one among them.</summary>
    private static void FromAPrefab(Action<bool, string> True)
    {
        using var w = NewWorld();
        Box(w, new Vector3(3400, -0.5f, 0), new Vector3(40, 1, 40), 0f, isStatic: true);
        var lib = new PrefabLibrary();
        lib.AddJson(@"{ ""name"": ""skiff"", ""components"": {
            ""LocalTransform"": { ""position"": [3400, 2, 0] },
            ""Body"": { ""mass"": 300 }, ""BodyVelocity"": {},
            ""CompoundShape"": { ""parts"": [
                { ""shape"": { ""kind"": ""Box"", ""size"": [2, 0.3, 5] }, ""position"": [0, 0.15, 0], ""density"": 3 },
                { ""shape"": { ""kind"": ""Box"", ""size"": [0.2, 0.6, 5] }, ""position"": [-0.9, 0.6, 0] },
                { ""shape"": { ""kind"": ""Box"", ""size"": [0.2, 0.6, 5] }, ""position"": [0.9, 0.6, 0] },
                { ""shape"": { ""kind"": ""Capsule"", ""radius"": 0.15, ""height"": 1.6 }, ""position"": [0, 0.6, 2.2], ""rotation"": [0, 0, 90] },
                { ""shape"": { ""kind"": ""Convex"", ""vertices"": [[-0.5,0,0],[0.5,0,0],[0,0,-0.8],[0,0.6,-0.3]] }, ""position"": [0, 0.3, -2.6] }
            ] } } }");
        var skiff = lib.Get("skiff").Instantiate(w);
        w.Sync();
        Step(w, 300);
        var body = BodyOf(w, skiff);
        True(body != null && body.IsCompound && body.PartCount == 5, $"a prefab builds a body of five parts, a convex one among them (parts={body?.PartCount})");
        True(Mathf.Abs(Pos(w, skiff).y) < 0.03f && body.linearVelocity.magnitude < 0.05f,
             $"and it settles on the floor on its lowest part (y={Pos(w, skiff).y:0.000})");
        // the wedge rises from its front edge (part z = 0) to its apex 0.6 up at z = -0.3: halfway along, 0.3 above the
        // part's base - and the box round it would be hit at its top, 0.6 above the base
        float baseY = Pos(w, skiff).y + 0.3f;
        True(Physics.Raycast(new Vector3(3400f, 5f, -2.75f), Vector3.down, out var cone, 10f, ~0) && cone.entity == skiff,
             $"a ray onto its convex part hits the skiff (hit {cone.entity})");
        True(Mathf.Abs(cone.point.y - (baseY + 0.3f)) < 0.05f,
             $"on the part's own slope, not on a box round it (y={cone.point.y:0.000}; the slope is at {baseY + 0.3f:0.000}, a box's top at {baseY + 0.6f:0.000})");
    }

    // ------------------------------------------------------------------ vessels

    /// <summary>
    /// A speedboat, bow towards +z: a heavy hull, rails along both sides, and a cabin forward with a roof and a front
    /// wall, open at the back. It floats on its envelope, not on its parts.
    /// </summary>
    private static Entity Speedboat(World w, Vector3 at, bool kinematic = false, bool engine = false)
    {
        var parts = new[]
        {
            Part(new Vector3(0, -0.2f, 0), new Vector3(3f, 0.4f, 8f), density: 4f),       // hull: the deck is its top, at y = 0
            Part(new Vector3(-1.4f, 0.4f, 0), new Vector3(0.2f, 0.8f, 8f), density: 0.5f), // port rail
            Part(new Vector3(1.4f, 0.4f, 0), new Vector3(0.2f, 0.8f, 8f), density: 0.5f),  // starboard rail
            Part(new Vector3(0, 1.1f, 3.9f), new Vector3(2.6f, 2.2f, 0.2f), density: 0.5f), // cabin front
            Part(new Vector3(0, 2.3f, 2.8f), new Vector3(3.0f, 0.2f, 2.4f), density: 0.5f), // cabin roof
        };
        var e = Compound(w, "speedboat", at, 2500f, parts, kinematic: kinematic);
        if (!kinematic)
        {
            w.Add(e, new BuoyancyConfig { center = new[] { 0f, 0.2f, 0f }, size = new[] { 3f, 1.2f, 8f }, resolution = 3,
                                         linearDrag = 20f, quadraticDrag = 0.3f, angularDrag = 3f }.Bake());
            if (engine)
            {
                w.Add(e, new VesselEngineConfig { maxThrust = 14000f, sternOffset = new[] { 0f, -0.35f, -4f }, steerAngle = 25f }.Bake());
                w.Add(e, new VesselControl());
            }
        }
        return e;
    }

    /// <summary>
    /// A container ship, short enough to test on: 60 m along z, 16 m of beam, a deck at y = 3 with a hatch across it
    /// (z 4..12) and a ramp down into the hold, a deckhouse aft with a door in its front wall, and a container forward.
    /// Kinematic: its contacts treat it as immovable, which at a real ship's mass is what it is.
    /// </summary>
    private static Entity Ship(World w, Vector3 at, Vector3 velocity = default)
    {
        var ramp = new Vector3(0f, 3f, 8f);
        var rampRot = Quaternion.LookRotation(ramp.normalized, Vector3.up);
        var parts = new[]
        {
            Part(new Vector3(0, -0.25f, 0), new Vector3(16f, 0.5f, 60f)),               // the hold floor, top at y = 0
            Part(new Vector3(-8.25f, 1.5f, 0), new Vector3(0.5f, 3f, 60f)),            // port side
            Part(new Vector3(8.25f, 1.5f, 0), new Vector3(0.5f, 3f, 60f)),             // starboard side
            Part(new Vector3(0, 1.5f, -30.25f), new Vector3(16.5f, 3f, 0.5f)),         // stern
            Part(new Vector3(0, 1.5f, 30.25f), new Vector3(16.5f, 3f, 0.5f)),          // bow
            Part(new Vector3(0, 2.85f, -13f), new Vector3(16f, 0.3f, 34f)),            // deck aft of the hatch, top at y = 3
            Part(new Vector3(0, 2.85f, 21f), new Vector3(16f, 0.3f, 18f)),             // deck forward of it
            Part(new Vector3(0, 1.5f, 8f) - rampRot * new Vector3(0, 0.15f, 0), new Vector3(4f, 0.3f, ramp.magnitude), rot: rampRot), // ramp, hold floor to deck
            Part(new Vector3(0, 4.5f, -28.15f), new Vector3(12f, 3f, 0.3f)),           // deckhouse: back wall
            Part(new Vector3(-6.15f, 4.5f, -23f), new Vector3(0.3f, 3f, 10f)),         //   side walls
            Part(new Vector3(6.15f, 4.5f, -23f), new Vector3(0.3f, 3f, 10f)),
            Part(new Vector3(-3.5f, 4.5f, -17.85f), new Vector3(5f, 3f, 0.3f)),        //   front wall, either side of a 2 m door
            Part(new Vector3(3.5f, 4.5f, -17.85f), new Vector3(5f, 3f, 0.3f)),
            Part(new Vector3(0, 6.15f, -23f), new Vector3(12.6f, 0.3f, 10.6f)),        //   roof
            Part(new Vector3(4f, 4.3f, 24f), new Vector3(2.4f, 2.6f, 6f)),             // a container on deck
        };
        var e = Compound(w, "ship", at, 1e6f, parts, kinematic: true);
        if (velocity != Vector3.zero) w.Add(e, new PlatformMotion { Velocity = velocity, Direction = 1 });
        return e;
    }

    private static void Speedboat(Action<bool, string> True)
    {
        using var w = NewWorld();
        var boat = Speedboat(w, new Vector3(4000, 0.3f, 0), engine: true);
        w.Sync();
        Step(w, 600);
        float deck = Pos(w, boat).y;
        // 2500 kg on 3 × 8 m of waterplane: 2500 / (1025 × 24) = 0.102 m under, so the deck rides at 0.4 - 0.102
        True(Mathf.Abs(deck - (0.4f - 2500f / (1025f * 24f))) < 0.02f, $"a speedboat floats on its envelope, not on its parts (deck at y={deck:0.000})");
        float tilt = Quaternion.Angle(w.Get<LocalTransform>(boat).Rotation, Quaternion.identity);
        True(tilt < 3f, $"and floats near level with its heavy hull under a light cabin ({tilt:0.0}°)");

        // a character aboard as it gets under way
        var ch = Character(w, Pos(w, boat) + new Vector3(0, 1.3f, -2f));
        w.Sync();
        Step(w, 120);
        True(w.Get<CharacterMotor>(ch).Ground == boat, "a character stands on the boat's deck");
        Vector3 Aboard() => Quaternion.Inverse(w.Get<LocalTransform>(boat).Rotation) * (Pos(w, ch) - Pos(w, boat));
        var start = Aboard();
        w.Get<VesselControl>(boat).Throttle = 1f;
        int off = 0;
        for (int i = 0; i < 480; i++) { w.Update(Dt); if (w.Get<CharacterMotor>(ch).Ground != boat) off++; }
        float speed = BodyOf(w, boat).linearVelocity.magnitude;
        var drift = Aboard() - start;
        True(speed > 4f, $"the boat gets under way ({speed:0.0} m/s)");
        True(off == 0, $"and the character stays on its deck the whole time ({off} steps off it)");
        True(new Vector3(drift.x, 0, drift.z).magnitude < 0.5f, $"without being left behind as it accelerates (drifted {new Vector3(drift.x, 0, drift.z).magnitude:0.00} m on deck)");

        // forward into the cabin, until its front wall stops them
        for (int i = 0; i < 180; i++)
        {
            w.Get<CharacterMotor>(ch).Desired = w.Get<LocalTransform>(boat).Rotation * new Vector3(0, 0, 3f);
            w.Update(Dt);
        }
        var inCabin = Aboard();
        True(inCabin.z > 1.7f, $"they walk forward under the cabin roof (z={inCabin.z:0.00} on deck, the roof starts at 1.6)");
        True(inCabin.z < 3.85f - 0.4f + 0.05f, $"and the cabin's front wall stops them (z={inCabin.z:0.00}, the wall's face is at 3.8)");
        True(w.Get<CharacterMotor>(ch).Ground == boat, "still standing on the boat");
    }

    /// <summary>
    /// Getting from a boat onto a ship and off again. Each crossing is made the way a player would make it: walk to
    /// the edge, jump, and see where the character comes down - and what it is standing on when it does.
    /// </summary>
    private static void Boarding(Action<bool, string> True)
    {
        // ---- boat to ship, both at rest: a real floating boat alongside, the ship's deck 2.7 m above its own
        using (var w = NewWorld())
        {
            var ship = Ship(w, new Vector3(5000, 0, 0));
            var boat = Speedboat(w, new Vector3(5000 - 10.5f, 0.3f, 0));
            w.Sync();
            Step(w, 300);
            float boatAlone = 0; for (int i = 0; i < 60; i++) { w.Update(Dt); boatAlone += Pos(w, boat).y / 60f; } // floating with nobody aboard
            var ch = Character(w, Pos(w, boat) + new Vector3(0, 1.3f, 0));
            w.Sync();
            Step(w, 60);
            True(w.Get<CharacterMotor>(ch).Ground == boat, "a character stands on a boat moored alongside the ship");

            CrossTo(w, ch, new Vector3(5f, 0, 0), jumpAtX: 5000 - 10.25f);
            True(w.Get<CharacterMotor>(ch).Ground == ship, $"they jump from the boat onto the ship's deck (standing on {w.Get<CharacterMotor>(ch).Ground}, at {Pos(w, ch)})");
            True(Pos(w, ch).y > 3.3f, $"up on the deck, not in the gap (y={Pos(w, ch).y:0.00})");

            // ... and back down, off the edge of the deck onto the boat, which takes their weight
            Step(w, 120);
            float boatEmpty = 0; for (int i = 0; i < 60; i++) { w.Update(Dt); boatEmpty += Pos(w, boat).y / 60f; }
            CrossTo(w, ch, new Vector3(-4f, 0, 0), jumpAtX: float.NegativeInfinity, steps: 150);
            True(w.Get<CharacterMotor>(ch).Ground == boat, $"they step off the ship's deck and come down on the boat (standing on {w.Get<CharacterMotor>(ch).Ground})");
            Step(w, 240);
            float boatLoaded = 0; for (int i = 0; i < 60; i++) { w.Update(Dt); boatLoaded += Pos(w, boat).y / 60f; }
            float sank = boatEmpty - boatLoaded;
            True(sank > 0.002f && sank < 0.006f, $"and the boat sinks under them by their weight ({sank * 1000f:0.0} mm; 80 kg on 24 m² is 3.3)");
            True(Mathf.Abs(boatAlone - boatEmpty) < 0.001f,
                 $"having come back up, while they were on the ship, to where it floated before anyone boarded it ({(boatAlone - boatEmpty) * 1000f:0.00} mm)");
        }

        // ---- boat to ship at 20 m/s: the same crossing in a frame that is going somewhere
        using (var w = NewWorld())
        {
            var v = new Vector3(0, 0, 20f);
            var ship = Ship(w, new Vector3(5200, 0, 0), v);
            var boat = Speedboat(w, new Vector3(5200 - 10.5f, 0.3f, 0), kinematic: true);
            w.Add(boat, new PlatformMotion { Velocity = v, Direction = 1 });
            var ch = Character(w, new Vector3(5200 - 10.5f, 1.6f, -1f));
            w.Sync();
            Step(w, 90);
            True(w.Get<CharacterMotor>(ch).Ground == boat && Mathf.Abs(w.Get<CharacterMotor>(ch).FrameVelocity.z - 20f) < 0.3f,
                 "a character rides a boat at 20 m/s");
            Vector3 OnShip() => Pos(w, ch) - Pos(w, ship);
            CrossTo(w, ch, new Vector3(5f, 0, 0), jumpAtX: 5200 - 10.25f);
            True(w.Get<CharacterMotor>(ch).Ground == ship, $"they jump from it onto the ship alongside, both doing 20 m/s (standing on {w.Get<CharacterMotor>(ch).Ground})");
            True(Mathf.Abs(w.Get<CharacterMotor>(ch).FrameVelocity.z - 20f) < 0.3f, "and the ship is now the frame they move in");
            var landed = OnShip();
            for (int i = 0; i < 120; i++) { w.Get<CharacterMotor>(ch).Desired = Vector3.zero; w.Update(Dt); }
            float drift = new Vector3(OnShip().x - landed.x, 0, OnShip().z - landed.z).magnitude;
            True(drift < 0.6f, $"standing where they landed, not sliding off the stern ({drift:0.00} m in two seconds)");
        }

        // ---- off the ship onto a quay: the frame becomes still ground
        using (var w = NewWorld())
        {
            var ship = Ship(w, new Vector3(5400, 0, 0));
            var quay = Box(w, new Vector3(5400 + 14.5f, 1.5f, 0), new Vector3(12f, 3f, 40f), 0f, isStatic: true); // flush with the deck
            var ch = Character(w, new Vector3(5400 + 5f, 4.2f, -6f));
            w.Sync();
            Step(w, 60);
            True(w.Get<CharacterMotor>(ch).Ground == ship, "a character stands on the ship's deck at the quay");
            CrossTo(w, ch, new Vector3(4f, 0, 0), jumpAtX: float.PositiveInfinity, steps: 120);
            True(w.Get<CharacterMotor>(ch).Ground == quay, $"they walk off the ship onto the quay (standing on {w.Get<CharacterMotor>(ch).Ground}, x={Pos(w, ch).x:0.00})");
            True(w.Get<CharacterMotor>(ch).FrameVelocity.magnitude < 0.05f, "and the ground they move in is still");
        }
    }

    /// <summary>Walk at <paramref name="velocity"/>; jump once the character passes <paramref name="jumpAtX"/> (in the walking direction).</summary>
    private static void CrossTo(World w, Entity ch, Vector3 velocity, float jumpAtX, int steps = 150)
    {
        bool jumped = false;
        for (int i = 0; i < steps; i++)
        {
            var m = w.Get<CharacterMotor>(ch);
            bool past = velocity.x > 0 ? Pos(w, ch).x >= jumpAtX : Pos(w, ch).x <= jumpAtX;
            if (!jumped && past && m.IsGrounded) { w.Get<CharacterMotor>(ch).PendingJump = 11f; jumped = true; }
            // once over, stop: the test is where they come down, not how far they run
            bool settled = jumped && i > 40 && m.IsGrounded;
            w.Get<CharacterMotor>(ch).Desired = settled ? Vector3.zero : velocity;
            w.Update(Dt);
        }
    }

    /// <summary>Inside the ship: through the deckhouse door until its back wall stops you, and down the ramp into the hold and back.</summary>
    private static void AboardTheShip(Action<bool, string> True)
    {
        using var w = NewWorld();
        var ship = Ship(w, new Vector3(6000, 0, 0), new Vector3(0, 0, 8f));
        var ch = Character(w, new Vector3(6000, 4.2f, -14f));
        w.Sync();
        Step(w, 60);
        Vector3 OnShip() => Pos(w, ch) - Pos(w, ship);
        True(w.Get<CharacterMotor>(ch).Ground == ship, "a character stands on the deck of a ship under way");

        void Walk(Vector3 v, int steps) { for (int i = 0; i < steps; i++) { w.Get<CharacterMotor>(ch).Desired = v; w.Update(Dt); } }

        Walk(new Vector3(0, 0, -3f), 150); // in through the door
        var room = OnShip();
        True(room.z < -18.5f, $"they walk in through the deckhouse door (z={room.z:0.00}; the door is at -17.85)");
        True(w.Get<CharacterMotor>(ch).Ground == ship && room.y > 3.3f && room.y < 6f - 0.9f, $"under its roof, on the deck (y={room.y:0.00})");
        Walk(new Vector3(0, 0, -3f), 240); // on into the back wall
        True(OnShip().z > -28f + 0.4f - 0.05f, $"and its back wall stops them (z={OnShip().z:0.00}; the wall's face is at -28)");
        True(w.Get<CharacterMotor>(ch).Ground == ship, "still standing in the deckhouse");

        // forward again, past the hatch, onto the deck the far side of it, then down the ramp into the hold
        w.Get<LocalTransform>(ch).Position = Pos(w, ship) + new Vector3(0, 4.2f, 15f);
        Step(w, 60);
        Walk(new Vector3(0, 0, -3f), 300);
        var hold = OnShip();
        True(hold.y < 1.5f && w.Get<CharacterMotor>(ch).Ground == ship, $"they walk down the ramp into the hold (y={hold.y:0.00}, z={hold.z:0.00})");
        True(hold.z < 4f, "and on under the deck");
        Walk(new Vector3(0, 0, 3f), 300);
        var back = OnShip();
        True(back.y > 3.3f && w.Get<CharacterMotor>(ch).Ground == ship, $"and back up the ramp onto the deck (y={back.y:0.00}, z={back.z:0.00})");
    }
}
