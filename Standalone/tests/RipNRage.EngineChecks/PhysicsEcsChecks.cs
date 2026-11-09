using System;
using System.Collections.Generic;
using System.Linq;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

/// <summary>Records physics events for the checks.</summary>
[DisableAutoCreation]
public sealed class PhysicsEventLog : SystemBase, IConsumes<TriggerEnterEvent>, IConsumes<TriggerExitEvent>, IConsumes<CollisionEvent>
{
    public readonly List<(Entity, Entity)> Enters = new List<(Entity, Entity)>(), Exits = new List<(Entity, Entity)>();
    public readonly List<CollisionEvent> Collisions = new List<CollisionEvent>();
    public void Consume(SystemContext ctx, ReadOnlySpan<TriggerEnterEvent> events) { foreach (var e in events) Enters.Add((e.Trigger, e.Other)); }
    public void Consume(SystemContext ctx, ReadOnlySpan<TriggerExitEvent> events) { foreach (var e in events) Exits.Add((e.Trigger, e.Other)); }
    public void Consume(SystemContext ctx, ReadOnlySpan<CollisionEvent> events) { foreach (var e in events) Collisions.Add(e); }
}

[DisableAutoCreation]
public sealed class JointEventLog : SystemBase, IConsumes<JointBrokenEvent>, IConsumes<CollisionEvent>, IConsumes<TriggerEnterEvent>
{
    public readonly List<Entity> Broken = new List<Entity>();
    public readonly List<CollisionEvent> Hits = new List<CollisionEvent>();
    public readonly List<(Entity, Entity)> Enters = new List<(Entity, Entity)>();
    public void Consume(SystemContext ctx, ReadOnlySpan<JointBrokenEvent> events) { foreach (var e in events) Broken.Add(e.Entity); }
    public void Consume(SystemContext ctx, ReadOnlySpan<CollisionEvent> events) { foreach (var e in events) Hits.Add(e); }
    public void Consume(SystemContext ctx, ReadOnlySpan<TriggerEnterEvent> events) { foreach (var e in events) Enters.Add((e.Trigger, e.Other)); }
}

/// <summary>Layer 2: physics components / configs, and the ECS-only simulation.</summary>
internal static class PhysicsEcsChecks
{
    public static void Run(Action<bool, string> True)
    {

        // ---- 2.1 configs: defaults match the solver, prefab JSON bakes, bad data is rejected
        {
            var sb = new SolverBody();
            var b = new BodyConfig().Bake();
            True(b.Kind == BodyKind.Dynamic && b.Mass == sb.mass && b.LinearDamping == sb.linearDamping
                 && b.AngularDamping == sb.angularDamping && b.UseGravity == sb.useGravity && b.CanSleep == sb.canSleep
                 && b.SleepThreshold == sb.sleepThreshold && b.MaxSpeed == sb.maxSpeed, "Body defaults equal the solver's defaults");

            using var w = new World(0);
            var lib = new PrefabLibrary();
            lib.AddJson(@"{ ""name"": ""crate"", ""components"": {
                ""Body"": { ""mass"": 20, ""collisionDetection"": ""ContinuousDynamic"" },
                ""BodyVelocity"": { ""linear"": [0, 5, 0] },
                ""ColliderShape"": { ""kind"": ""Box"", ""size"": [1, 2, 3] },
                ""SurfaceMaterial"": { ""restitution"": 0.4, ""frictionCombine"": ""Minimum"" },
                ""CollisionFilter"": { ""layer"": 3, ""group"": -2 } } }");
            lib.AddJson(@"{ ""name"": ""ball"", ""base"": ""crate"", ""components"": {
                ""ColliderShape"": { ""kind"": ""Sphere"", ""radius"": 0.25 }, ""Trigger"": {} } }");
            var crate = lib.Get("crate").Instantiate(w);
            var ball = lib.Get("ball").Instantiate(w);
            w.Sync();
            var cb = w.Get<Body>(crate); var cs = w.Get<ColliderShape>(crate);
            True(cb.Mass == 20 && cb.CollisionDetection == CollisionDetectionMode.ContinuousDynamic && w.Get<BodyVelocity>(crate).Linear.y == 5,
                 "Body / BodyVelocity bake from prefab JSON (enums by name)");
            True(cs.Kind == ShapeKind.Box && cs.Size.x == 1 && cs.Size.z == 3 && !w.Has<Trigger>(crate), "box collider bakes its size");
            True(w.Get<SurfaceMaterial>(crate).Restitution == 0.4f && w.Get<SurfaceMaterial>(crate).FrictionCombine == CombineMode.Minimum
                 && w.Get<CollisionFilter>(crate).Layer == 3 && w.Get<CollisionFilter>(crate).Group == -2 && w.Get<CollisionFilter>(crate).Mask == ~0,
                 "material and filter bake");
            var bs = w.Get<ColliderShape>(ball);
            True(bs.Kind == ShapeKind.Sphere && bs.Radius == 0.25f && w.Has<Trigger>(ball) && w.Get<Body>(ball).Mass == 20,
                 "prefab inheritance: child overrides the shape, keeps the body, adds the sparse Trigger tag");

            True(Throws(() => new BodyConfig { mass = 0 }.Bake()) && Throws(() => new BodyConfig { mass = float.NaN }.Bake())
                 && !Throws(() => new BodyConfig { kind = BodyKind.Kinematic, mass = 0 }.Bake()), "dynamic body rejects mass <= 0 / NaN; kinematic does not care");
            True(Throws(() => new ColliderShapeConfig { size = new float[] { 1, 0, 1 } }.Bake())
                 && Throws(() => new ColliderShapeConfig { kind = ShapeKind.Sphere, radius = -1 }.Bake())
                 && Throws(() => new ColliderShapeConfig { kind = ShapeKind.Mesh }.Bake())
                 && Throws(() => new CollisionFilterConfig { layer = 32 }.Bake()), "invalid shapes / layers are rejected at bake");
        }

        // ---- convex colliders from config: faces derived or given, validated, shared by content
        {
            float[][] cube = { new float[]{-1,-1,-1}, new float[]{1,-1,-1}, new float[]{-1,1,-1}, new float[]{1,1,-1},
                               new float[]{-1,-1,1}, new float[]{1,-1,1}, new float[]{-1,1,1}, new float[]{1,1,1} };
            var c1 = new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = cube }.Bake();
            var hull = PhysicsAssets.Hulls.Resolve(c1.Hull)?.Hull;
            True(!c1.Hull.IsNone && hull != null && hull.faceCount == 6, "a cube's 8 points derive 6 faces");
            True(hull.Contains(new Vector3(0.5f, -0.5f, 0.9f)) && !hull.Contains(new Vector3(1.2f, 0, 0)), "derived hull contains its inside, not its outside");
            var c2 = new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = cube }.Bake();
            True(c1.Hull.Equals(c2.Hull), "identical convex configs share one hull asset");
            float[][] tet = { new float[]{0,0,0}, new float[]{1,0,0}, new float[]{0,1,0}, new float[]{0,0,1} };
            True(PhysicsAssets.Hulls.Resolve(new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = tet }.Bake().Hull).Hull.faceCount == 4, "a tetrahedron derives 4 faces");
            float[][] cubePlanes = { new float[]{1,0,0,1}, new float[]{-1,0,0,1}, new float[]{0,1,0,1}, new float[]{0,-1,0,1}, new float[]{0,0,1,1}, new float[]{0,0,-1,1} };
            var c3 = new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = cube, planes = cubePlanes }.Bake();
            True(PhysicsAssets.Hulls.Resolve(c3.Hull).Hull.faceCount == 6, "explicit planes are taken as given");
            float[][] flat = { new float[]{0,0,0}, new float[]{1,0,0}, new float[]{0,1,0}, new float[]{1,1,0} };
            float[][] badPlanes = { new float[]{1,0,0,0.5f}, new float[]{-1,0,0,1}, new float[]{0,1,0,1}, new float[]{0,-1,0,1}, new float[]{0,0,1,1}, new float[]{0,0,-1,1} };
            True(Throws(() => new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = flat }.Bake())
                 && Throws(() => new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = tet[..3] }.Bake())
                 && Throws(() => new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = cube, planes = badPlanes }.Bake())
                 && Throws(() => new ColliderShapeConfig { kind = ShapeKind.Convex }.Bake()),
                 "coplanar points, too few points, planes that cut vertices, and missing data are rejected");
            using var hw = new World(0);
            var lib = new PrefabLibrary();
            lib.AddJson(@"{ ""name"": ""wedge"", ""components"": { ""ColliderShape"": { ""kind"": ""Convex"",
                ""vertices"": [[0,0,0],[2,0,0],[0,0,2],[2,0,2],[0,1,0],[0,1,2]] }, ""PhysicsHandle"": {} } }");
            var wedge = lib.Get("wedge").Instantiate(hw); hw.Sync();
            True(PhysicsAssets.Hulls.Resolve(hw.Get<ColliderShape>(wedge).Hull).Hull.faceCount == 5, "convex prefab JSON bakes (wedge: 5 faces)");
            True(!hw.Get<PhysicsHandle>(wedge).IsRegistered, "PhysicsHandle always bakes unregistered");
        }

        EcsSimChecks(True);
        JointAndMoverChecks(True);
    }

    /// <summary>2.4: ECS entities in the one simulation via PhysicsSync / PhysicsStep / PhysicsWriteBack.</summary>
    private static void EcsSimChecks(Action<bool, string> True)
    {
        var simWorld = Physics.World;
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        w.FixedDeltaTime = 1f / 60f;
        var lib = new PrefabLibrary();
        lib.AddJson(@"{ ""name"": ""floor"", ""components"": {
            ""LocalTransform"": { ""position"": [300, 0, 0], ""scale"": [1, 1, 1] },
            ""ColliderShape"": { ""kind"": ""Box"", ""size"": [20, 1, 20], ""isStatic"": true } } }");
        lib.AddJson(@"{ ""name"": ""crate"", ""components"": {
            ""LocalTransform"": { ""position"": [300, 3, 0] },
            ""Body"": { ""mass"": 5 }, ""BodyVelocity"": {},
            ""ColliderShape"": { ""kind"": ""Box"", ""size"": [1, 1, 1] } } }");
        var floor = lib.Get("floor").Instantiate(w);
        var crate = lib.Get("crate").Instantiate(w);
        w.Sync();
        void Step(int n) { for (int i = 0; i < n; i++) w.Update(1f / 60f); }

        Step(2);
        True(w.Has<PhysicsHandle>(crate) && w.Has<PhysicsHandle>(floor), "sync registers entities and adds PhysicsHandle");
        var proxy = w.Get<PhysicsHandle>(crate).Proxy;
        True(simWorld.Table.IsAlive(proxy) && simWorld.Table.EntityOf(proxy) == crate && simWorld.Table.BodyOf(proxy) != null,
             "the crate's slot knows its entity and body");
        Step(180);
        var lt = w.Get<LocalTransform>(crate);
        True(MathF.Abs(lt.Position.y - 1.0f) < 0.05f && MathF.Abs(lt.Position.x - 300f) < 0.01f,
             $"an ECS crate falls onto an ECS static floor and rests (y={lt.Position.y:0.000})");
        True(w.Get<BodyVelocity>(crate).Linear.magnitude < 0.3f, "write-back keeps BodyVelocity current");
        Step(120);
        True(w.Has<Sleeping>(crate), "a resting body gets the Sleeping tag");

        // teleport by writing LocalTransform; wakes and falls again
        w.Get<LocalTransform>(crate).Position = new Vector3(300, 5, 0);
        Step(30);
        True(!w.Has<Sleeping>(crate) && w.Get<LocalTransform>(crate).Position.y < 4.9f, "moving LocalTransform teleports and wakes the body");
        Step(240);
        True(MathF.Abs(w.Get<LocalTransform>(crate).Position.y - 1.0f) < 0.05f, "it settles again");

        // ---- raycasts / sweeps name the entity
        bool rayOk = Physics.Raycast(new Vector3(300, 10, 0), Vector3.down, out var rh, 20f);
        True(rayOk && rh.entity == crate && MathF.Abs(rh.point.y - 1.5f) < 0.02f,
             "a ray down hits the crate's top and names its entity");
        True(Physics.Raycast(new Vector3(305, 10, 0), Vector3.down, out var fh, 20f) && fh.entity == floor && MathF.Abs(fh.point.y - 0.5f) < 0.01f,
             "a ray beside it hits the floor entity");
        var floorOnly = new QueryFilter { layerMask = ~0, ignore = crate };
        True(Physics.Raycast(new Ray(new Vector3(300, 10, 0), Vector3.down), 20f, floorOnly, out var ih) && ih.entity == floor, "QueryFilter.ignore skips an entity");
        var hits = Physics.OverlapSphere(new Vector3(300, 1, 0), 0.3f);
        True(hits.Contains(crate) && !hits.Contains(floor), "OverlapSphere returns entities");

        // ---- body vs body: a second crate dropped on the first stacks on it (regression: 2.4 bodies never saw each other)
        var top = lib.Get("crate").Instantiate(w); w.Sync();
        w.Get<LocalTransform>(top).Position = new Vector3(300, 3, 0);
        Step(300);
        True(MathF.Abs(w.Get<LocalTransform>(top).Position.y - 2.0f) < 0.06f && MathF.Abs(w.Get<LocalTransform>(crate).Position.y - 1.0f) < 0.05f,
             $"ECS bodies collide with each other: stacked crate at y={w.Get<LocalTransform>(top).Position.y:0.000}");
        w.DestroyEntity(top); w.Sync();

        // ---- static mesh collider (terrain): a body lands on its triangles
        var terrainMesh = PhysicsAssets.AddMesh("test-ramp", new[] { new Vector3(-5, 0, -5), new Vector3(5, 0, -5), new Vector3(5, 0, 5), new Vector3(-5, 0, 5) }, new[] { 0, 2, 1, 0, 3, 2 });
        var terrain = w.CreateEntity();
        w.Add(terrain, LocalTransform.At(new Vector3(360, 0, 0)));
        w.Add(terrain, new ColliderShape { Kind = ShapeKind.Mesh, Mesh = terrainMesh, IsStatic = true });
        var ball = lib.Get("crate").Instantiate(w); w.Sync();
        w.Get<LocalTransform>(ball).Position = new Vector3(360, 2, 0);
        Step(240);
        True(MathF.Abs(w.Get<LocalTransform>(ball).Position.y - 0.5f) < 0.05f, $"a crate rests on a static mesh collider (y={w.Get<LocalTransform>(ball).Position.y:0.000})");
        True(Physics.Raycast(new Vector3(362, 5, 2), Vector3.down, out var mh, 10f) && mh.entity == terrain && MathF.Abs(mh.point.y) < 0.01f, "rays hit mesh triangles");

        // ---- kinematic move-and-slide against ECS colliders
        var mr = Physics.MoveAndSlide(CollisionShape.AxisAlignedBox(new Vector3(305, 1.0f, 0), new Vector3(0.25f, 0.25f, 0.25f)), new Vector3(0, -2f, 0), MoveSettings.Character(QueryFilter.Everything));
        True(mr.isGrounded && mr.ground == floor && MathF.Abs(mr.position.y - 0.75f) < 0.03f, $"move-and-slide lands on the ECS floor and names it (y={mr.position.y:0.000})");

        // ---- triggers + collision reports
        var log = w.AddSystem(new PhysicsEventLog());
        var trig = w.CreateEntity();
        w.Add(trig, LocalTransform.At(new Vector3(300, 4, 6)));
        w.Add(trig, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(2, 2, 2), IsStatic = true });
        w.Add(trig, new Trigger());
        var dropper = lib.Get("crate").Instantiate(w); w.Sync();
        w.Get<LocalTransform>(dropper).Position = new Vector3(300, 8, 6);
        w.Add(dropper, new ReportCollisions());
        Step(240);
        True(log.Enters.Contains((trig, dropper)) && log.Exits.Contains((trig, dropper)), "a body falling through a trigger posts enter, then exit");
        True(MathF.Abs(w.Get<LocalTransform>(dropper).Position.y - 1.0f) < 0.05f, "the trigger does not block it");
        True(log.Collisions.Exists(c => c.Self == dropper && c.Other == floor && c.Impulse > 0f), "ReportCollisions posts CollisionEvents naming the floor");

        // destroying the entity frees slot and body
        int bodiesBefore = simWorld.Bodies.Count;
        w.DestroyEntity(crate); w.Sync();
        Step(1);
        True(!simWorld.Table.IsAlive(proxy) && simWorld.Bodies.Count == bodiesBefore - 1, "destroying the entity frees its slot and body");

        w.EndSim();
        True(simWorld.Table.EntityOf(w.Get<PhysicsHandle>(floor).Proxy).IsNull, "OnSimEnd unregisters the rest");
    }

    /// <summary>2.5 joints, 2.6 character motor and kinematic movers.</summary>
    private static void JointAndMoverChecks(Action<bool, string> True)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        var log = w.AddSystem(new JointEventLog());
        void Step(int n) { for (int i = 0; i < n; i++) w.Update(1f / 60f); }
        Entity Box(Vector3 at, Vector3 size, bool dynamic, float mass = 5f)
        {
            var e = w.CreateEntity();
            w.Add(e, LocalTransform.At(at));
            w.Add(e, new ColliderShape { Kind = ShapeKind.Box, Size = size, IsStatic = !dynamic });
            if (dynamic) { w.Add(e, new BodyConfig { mass = mass }.Bake()); w.Add(e, new BodyVelocity()); }
            return e;
        }
        var floor = Box(new Vector3(500, -0.5f, 0), new Vector3(40, 1, 40), false);

        // ---- pendulum: a distance joint to the world keeps the bob at its length while it swings
        var bob = Box(new Vector3(503, 5, 0), new Vector3(0.4f, 0.4f, 0.4f), true);
        w.Add(bob, new JointConfig { kind = JointKind.Distance, connectedAnchor = new float[] { 500, 5, 0 } }.Bake());
        Step(90);
        var bp = w.Get<LocalTransform>(bob).Position;
        float len = (bp - new Vector3(500, 5, 0)).magnitude;
        True(MathF.Abs(len - 3f) < 0.05f && bp.y < 4.5f, $"a distance joint holds the pendulum at 3 m while it swings down (len={len:0.000}, y={bp.y:0.00})");

        // ---- hinge with a motor spins its body about the axis and stays at the anchor
        var wheel = Box(new Vector3(510, 3, 0), new Vector3(1.5f, 0.2f, 0.2f), true);
        w.Add(wheel, new JointConfig { kind = JointKind.Hinge, axis = new float[] { 0, 0, 1 }, motorSpeed = 180f, motorForce = 500f }.Bake());
        Step(60);
        var wt = w.Get<LocalTransform>(wheel);
        True((wt.Position - new Vector3(510, 3, 0)).magnitude < 0.05f && MathF.Abs(w.Get<BodyVelocity>(wheel).Angular.z) > 2f,
             $"a motorised hinge spins in place (|w|={w.Get<BodyVelocity>(wheel).Angular.magnitude:0.0} rad/s)");

        // ---- body-to-body ball chain hangs under its anchor; links stay connected
        Entity prev = Entity.Null; var links = new List<Entity>();
        for (int i = 0; i < 4; i++)
        {
            var link = Box(new Vector3(520, 8 - i * 0.6f, 0), new Vector3(0.2f, 0.5f, 0.2f), true, 2f);
            var j = new JointConfig { kind = JointKind.Ball, anchor = new float[] { 0, 0.3f, 0 } }.Bake();
            j.Connected = prev;
            if (prev.IsNull) { j.AutoConnectedAnchor = false; j.ConnectedAnchor = new Vector3(520, 8.3f, 0); }
            w.Add(link, j);
            links.Add(link); prev = link;
        }
        w.Get<BodyVelocity>(links[3]) = new BodyVelocity { Linear = new Vector3(4, 0, 0) };
        Step(120);
        float gap = (w.Get<LocalTransform>(links[2]).Position - w.Get<LocalTransform>(links[3]).Position).magnitude;
        True(MathF.Abs(gap - 0.6f) < 0.05f, $"a ball-joint chain stays linked (gap={gap:0.000})");
        True(w.GetSystem<JointSystem>().ActiveJoints == 6, "six joints active");

        // ---- breakable weld: a heavy body hung on a weak fixed joint breaks it, tag + event
        var weak = Box(new Vector3(530, 4, 0), new Vector3(0.5f, 0.5f, 0.5f), true, 200f);
        w.Add(weak, new JointConfig { kind = JointKind.Fixed, breakForce = 300f }.Bake());
        Step(30);
        True(w.Has<JointBroken>(weak) && log.Broken.Contains(weak) && w.Get<LocalTransform>(weak).Position.y < 3.9f, "an overloaded fixed joint breaks: JointBroken tag, event, body falls");

        // ---- destroying the connected link breaks the joint that pointed at it
        w.DestroyEntity(links[1]); w.Sync();
        Step(2);
        True(w.Has<JointBroken>(links[2]) && !w.Has<JointBroken>(links[0]), "destroying a connected body breaks only the joints that used it");

        // ---- character motor: rides above the floor, walks to the requested speed, jumps
        var ch = w.CreateEntity();
        w.Add(ch, LocalTransform.At(new Vector3(500, 2, 10)));
        w.Add(ch, new ColliderShape { Kind = ShapeKind.Capsule, Radius = 0.4f, Height = 1.3f });
        w.Add(ch, new BodyConfig { mass = 80f }.Bake()); w.Add(ch, new BodyVelocity());
        w.Add(ch, new CharacterMotorConfig { rideHeight = 0.5f }.Bake());
        Step(120);
        var cm = w.Get<CharacterMotor>(ch);
        float bottom = w.Get<LocalTransform>(ch).Position.y - 0.65f;
        True(cm.IsGrounded && cm.Ground == floor && MathF.Abs(bottom - 0.5f) < 0.06f, $"a character floats its ride height above the ground (gap={bottom:0.000}, grounded={cm.IsGrounded})");
        w.Get<CharacterMotor>(ch).Desired = new Vector3(5, 0, 0);
        for (int i = 0; i < 60; i++) { w.Get<CharacterMotor>(ch).Desired = new Vector3(5, 0, 0); Step(1); }
        float vx = w.Get<BodyVelocity>(ch).Linear.x;
        True(MathF.Abs(vx - 5f) < 0.3f && w.Get<LocalTransform>(ch).Rotation == Quaternion.identity, $"the motor reaches the wanted speed and never tumbles (vx={vx:0.00})");
        w.Get<CharacterMotor>(ch).PendingJump = 6f;
        Step(12);
        True(!w.Get<CharacterMotor>(ch).IsGrounded && w.Get<LocalTransform>(ch).Position.y > 2f, "a jump leaves the ground");
        Step(120);
        True(w.Get<CharacterMotor>(ch).IsGrounded, "and lands again");

        // ---- capsules on a mesh (the ground benchmark's runners): a plain capsule body, then a character
        var meshFloor = PhysicsAssets.AddMesh("test-mesh-floor", new[] { new Vector3(-10, 0, -10), new Vector3(10, 0, -10), new Vector3(10, 0, 10), new Vector3(-10, 0, 10) }, new[] { 0, 2, 1, 0, 3, 2 });
        var mf = w.CreateEntity(); w.Add(mf, LocalTransform.At(new Vector3(600, 0, 0)));
        w.Add(mf, new ColliderShape { Kind = ShapeKind.Mesh, Mesh = meshFloor, IsStatic = true });
        var cap = w.CreateEntity(); w.Add(cap, LocalTransform.At(new Vector3(600, 2, 0)));
        w.Add(cap, new ColliderShape { Kind = ShapeKind.Capsule, Radius = 0.3f, Height = 1.6f }); w.Add(cap, new BodyConfig { mass = 85f, freezeRotation = true }.Bake()); w.Add(cap, new BodyVelocity());
        var runner = w.CreateEntity(); w.Add(runner, LocalTransform.At(new Vector3(603, 2, 0)));
        w.Add(runner, new ColliderShape { Kind = ShapeKind.Capsule, Radius = 0.3f, Height = 1.6f }); w.Add(runner, new BodyConfig { mass = 85f }.Bake()); w.Add(runner, new BodyVelocity());
        w.Add(runner, new CharacterMotorConfig { rideHeight = 0.35f }.Bake());
        Step(120);
        float capY = w.Get<LocalTransform>(cap).Position.y, runY = w.Get<LocalTransform>(runner).Position.y;
        True(MathF.Abs(capY - 0.8f) < 0.05f, $"a capsule body rests on a mesh collider (y={capY:0.000})");
        True(w.Get<CharacterMotor>(runner).IsGrounded && MathF.Abs(runY - (0.8f + 0.35f)) < 0.08f, $"a character rides above a mesh collider (y={runY:0.000})");

        // ---- kinematic mover in Stop mode: a fast projectile stops at the wall, reports it, no tunnelling
        var wall = Box(new Vector3(540, 2, 0), new Vector3(0.2f, 4, 4), false);
        var shot = w.CreateEntity();
        w.Add(shot, LocalTransform.At(new Vector3(530, 2, 0)));
        w.Add(shot, new KinematicMoverConfig { velocity = new float[] { 300, 0, 0 }, useGravity = false, mode = MotionMode.Stop, halfExtents = new float[] { 0.05f, 0.05f, 0.05f } }.Bake());
        w.Add(shot, new ReportCollisions());
        Step(10);
        var sp = w.Get<LocalTransform>(shot).Position;
        True(w.Get<KinematicMover>(shot).Stopped && sp.x < 539.9f && sp.x > 539.5f, $"a 300 m/s mover stops at a 20 cm wall (x={sp.x:0.000})");
        True(log.Hits.Exists(h => h.Self == shot && h.Other == wall), "and reports the wall as a CollisionEvent");

        // ---- a mover with a collider triggers trigger volumes
        var vol = w.CreateEntity();
        w.Add(vol, LocalTransform.At(new Vector3(550, 1, 0)));
        w.Add(vol, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(2, 2, 2), IsStatic = true }); w.Add(vol, new Trigger());
        var walker = w.CreateEntity();
        w.Add(walker, LocalTransform.At(new Vector3(546, 1, 0)));
        w.Add(walker, new ColliderShape { Kind = ShapeKind.Sphere, Radius = 0.3f });
        w.Add(walker, new KinematicMoverConfig { velocity = new float[] { 4, 0, 0 }, useGravity = false }.Bake());
        Step(90);
        True(log.Enters.Contains((vol, walker)), "a kinematic mover entering a trigger posts TriggerEnter");
    }

    private static bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }
}
