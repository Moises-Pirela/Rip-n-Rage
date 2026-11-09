using System;
using System.Collections.Generic;
using System.Linq;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;

/// <summary>Layer 2: physics components / configs (2.1) and the collider table under the broadphase (2.2).</summary>
internal static class PhysicsEcsChecks
{
    private static Scene _scene;

    public static void Run(Action<bool, string> True)
    {
        // legacy behaviours Awake (and register with physics) in the scene's next lifecycle pass
        _scene = new Scene(); Scene.MakeCurrent(_scene);

        // ---- 2.1 configs: defaults match the legacy RigidBody, prefab JSON bakes, bad data is rejected
        {
            var legacy = new GameObject("legacy-defaults").AddComponent<RigidBody>();
            _scene.Tick(0f);
            var b = new BodyConfig().Bake();
            True(b.Kind == BodyKind.Dynamic && b.Mass == legacy.mass && b.LinearDamping == legacy.linearDamping
                 && b.AngularDamping == legacy.angularDamping && b.UseGravity == legacy.useGravity && b.CanSleep == legacy.canSleep
                 && b.SleepThreshold == legacy.sleepThreshold && b.MaxSpeed == legacy.maxSpeed, "Body defaults equal the legacy RigidBody");
            GameObject.DestroyImmediate(legacy.gameObject);

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
            var hull = PhysicsAssets.Hulls.Resolve(c1.Hull);
            True(!c1.Hull.IsNone && hull != null && hull.faceCount == 6, "a cube's 8 points derive 6 faces");
            True(hull.Contains(new Vector3(0.5f, -0.5f, 0.9f)) && !hull.Contains(new Vector3(1.2f, 0, 0)), "derived hull contains its inside, not its outside");
            var c2 = new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = cube }.Bake();
            True(c1.Hull.Equals(c2.Hull), "identical convex configs share one hull asset");
            float[][] tet = { new float[]{0,0,0}, new float[]{1,0,0}, new float[]{0,1,0}, new float[]{0,0,1} };
            True(PhysicsAssets.Hulls.Resolve(new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = tet }.Bake().Hull).faceCount == 4, "a tetrahedron derives 4 faces");
            float[][] cubePlanes = { new float[]{1,0,0,1}, new float[]{-1,0,0,1}, new float[]{0,1,0,1}, new float[]{0,-1,0,1}, new float[]{0,0,1,1}, new float[]{0,0,-1,1} };
            var c3 = new ColliderShapeConfig { kind = ShapeKind.Convex, vertices = cube, planes = cubePlanes }.Bake();
            True(PhysicsAssets.Hulls.Resolve(c3.Hull).faceCount == 6, "explicit planes are taken as given");
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
            True(PhysicsAssets.Hulls.Resolve(hw.Get<ColliderShape>(wedge).Hull).faceCount == 5, "convex prefab JSON bakes (wedge: 5 faces)");
            True(!hw.Get<PhysicsHandle>(wedge).IsRegistered, "PhysicsHandle always bakes unregistered");
        }

        // ---- 2.2 collider table: legacy colliders own slots; the broadphase finds them through it
        {
            var world = Physics.World;
            int before = world.Table.Alive;
            var a = Box("table-a", new Vector3(100, 0, 0), isStatic: true);
            var b = Box("table-b", new Vector3(100, 0, 3), isStatic: false);
            _scene.Tick(0f);
            var pa = a.Proxy; var pb = b.Proxy;
            True(!pa.IsNone && !pb.IsNone && !pa.Equals(pb) && world.Table.Alive == before + 2, "registration allocates distinct slots");
            True(world.Table.ColliderOf(pa) == a && world.Table.EntityOf(pa).IsNull, "a legacy slot resolves to its collider, no entity");

            var found = new List<Collider>();
            world.QueryBounds(new Bounds(new Vector3(99, -1, -1), new Vector3(101, 1, 4)), QueryFilter.Everything, found);
            True(found.Contains(a) && found.Contains(b), "static and dynamic grids return colliders through slot indices");

            GameObject.DestroyImmediate(b.gameObject);
            True(b.Proxy.IsNone && !world.Table.IsAlive(pb) && world.Table.ColliderOf(pb) == null && world.Table.Alive == before + 1,
                 "unregistering frees the slot; the old proxy goes stale");

            var c = Box("table-c", new Vector3(100, 0, 6), isStatic: false);
            _scene.Tick(0f);
            True(c.Proxy.Index == pb.Index && c.Proxy.Generation != pb.Generation && world.Table.ColliderOf(pb) == null && world.Table.ColliderOf(c.Proxy) == c,
                 "a reused slot gets a new generation; the stale proxy cannot reach the new collider");
            found.Clear();
            world.QueryBounds(new Bounds(new Vector3(99, -1, -1), new Vector3(101, 1, 7)), QueryFilter.Everything, found);
            True(found.Contains(a) && found.Contains(c) && !found.Contains(b), "broadphase sees the reused slot's new collider only");

            a.isStatic = false; // static -> dynamic re-insert keeps the slot
            True(a.Proxy.Equals(pa), "Refresh keeps the proxy");
            GameObject.DestroyImmediate(a.gameObject); GameObject.DestroyImmediate(c.gameObject);
            True(world.Table.Alive == before, "all test slots released");
        }

        SolverBodyChecks(True);
    }

    /// <summary>2.3: the solver runs on SolverBody; RigidBody is an adapter. Called from Run.</summary>
    private static void SolverBodyChecks(Action<bool, string> True)
    {
        var world = Physics.World;
        // collider first, then body (the usual order)
        var go1 = new GameObject("sb-1"); go1.transform.position = new Vector3(200, 10, 0);
        var col1 = go1.AddComponent<BoxCollider>(); var rb1 = go1.AddComponent<RigidBody>();
        // body first, then collider: the slot / body link must still form
        var go2 = new GameObject("sb-2"); go2.transform.position = new Vector3(203, 10, 0);
        var rb2 = go2.AddComponent<RigidBody>(); var col2 = go2.AddComponent<SphereCollider>();
        _scene.Tick(0f);
        rb2.ComputeMass(); // the sphere arrived after the body's Awake: recompute like game code does after changing the collider

        True(rb1.body.legacy == rb1 && ReferenceEquals((SolverBody)rb1, rb1.body), "the adapter owns its SolverBody (and converts implicitly)");
        rb1.mass = 7f; rb1.linearVelocity = new Vector3(1, 2, 3);
        True(rb1.body.mass == 7f && rb1.body.linearVelocity == new Vector3(1, 2, 3), "public fields forward to the SolverBody");
        True(rb1.body.id < rb2.body.id, "body ids follow creation order (the solver's pair order relies on it)");
        True(world.Table.BodyOf(col1.Proxy) == rb1.body && world.Table.BodyOf(col2.Proxy) == rb2.body,
             "collider slot and body link up in either Awake order");
        True(world.Bodies.Contains(rb1.body) && world.Bodies.Contains(rb2.body), "the world lists SolverBodies");
        True(rb2.isRounded && !rb1.isRounded, "mass properties come from the legacy collider through the adapter");

        rb1.linearVelocity = Vector3.zero; rb1.useGravity = true;
        float y0 = go1.transform.position.y;
        for (int i = 0; i < 10; i++) world.StepFixed(1f / 60f);
        True(go1.transform.position.y < y0 - 0.05f && rb1.linearVelocity.y < 0f, "a SolverBody falls under gravity and writes the transform back");

        var proxy1 = col1.Proxy;
        GameObject.DestroyImmediate(go1);
        True(!world.Bodies.Contains(rb1.body) && world.Table.BodyOf(proxy1) == null, "destroying the owner removes the body and clears the slot link");
        GameObject.DestroyImmediate(go2);
        True(!world.Bodies.Contains(rb2.body), "second body removed");
    }

    private static BoxCollider Box(string name, Vector3 at, bool isStatic)
    {
        var go = new GameObject(name); go.transform.position = at;
        var col = go.AddComponent<BoxCollider>(); col.isStatic = isStatic;
        return col;
    }

    private static bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }
}
