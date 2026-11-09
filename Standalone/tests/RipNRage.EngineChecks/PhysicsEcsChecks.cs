using System;
using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Dynamics;

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
    }

    private static BoxCollider Box(string name, Vector3 at, bool isStatic)
    {
        var go = new GameObject(name); go.transform.position = at;
        var col = go.AddComponent<BoxCollider>(); col.isStatic = isStatic;
        return col;
    }

    private static bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }
}
