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

    private static bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }
}
