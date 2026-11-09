using System;
using System.Collections.Generic;
using RipNRage.Engine.Ecs.Transforms;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Engine.Ecs.Physics
{
    /// <summary>A trigger collider started overlapping a body (posted at the end of the frame of the fixed step that saw it).</summary>
    public struct TriggerEnterEvent : IPostProcessEvent { public Entity Trigger, Other; }
    /// <summary>A trigger collider stopped overlapping a body (or one of the two was destroyed).</summary>
    public struct TriggerExitEvent : IPostProcessEvent { public Entity Trigger, Other; }
    /// <summary>One contact of a body tagged <see cref="ReportCollisions"/> (Other is Entity.Null for nothing; never for level geometry, which is an entity too).</summary>
    public struct CollisionEvent : IPostProcessEvent
    {
        public Entity Self, Other;
        public Vector3 Point, Normal;
        /// <summary>Normal impulse (N·s): how hard the hit was.</summary>
        public float Impulse;
        /// <summary>Relative normal speed before the step (negative = approaching).</summary>
        public float ApproachSpeed;
    }

    /// <summary>
    /// Puts entities into the simulation and keeps both sides current. Runs first in FixedTick (no [Reads]/[Writes]:
    /// physics state is global, so it runs alone).
    ///
    /// Per fixed step: registers entities that gained a <see cref="ColliderShape"/> or <see cref="Body"/> (slot in
    /// <see cref="PhysicsWorld.Table"/>, a <see cref="SolverBody"/> for bodies, a <see cref="PhysicsHandle"/> on the
    /// entity), then pushes external changes at the solver: a <see cref="LocalTransform"/> someone moved is a
    /// teleport (statics re-bake), a <see cref="BodyVelocity"/> someone wrote wins over the solver's value.
    ///
    /// Physics entities must be roots: the solver works in world space and writes <see cref="LocalTransform"/> back
    /// directly. Mesh colliders must be static (no <see cref="Body"/>). Both are skipped with a warning otherwise.
    /// </summary>
    public sealed class PhysicsSyncSystem : SystemBase, IFixedTick, IOnEntityDestroyed, IOnSimEnd
    {
        internal sealed class Registration
        {
            public Entity Entity;
            public ColliderTable.Slot Slot;          // null: a body without a collider
            public SolverBody Body;                  // null: static collider
            public Vector3 LastPos, LastVel, LastAngVel, LastScale;
            public Quaternion LastRot;
            public bool WasSleeping;
            public bool HasVelocity;
        }

        private readonly Dictionary<Entity, Registration> _regs = new Dictionary<Entity, Registration>();
        private readonly HashSet<Entity> _warned = new HashSet<Entity>();
        private Query _unregistered, _bodiesOnly;
        internal IReadOnlyDictionary<Entity, Registration> Registrations => _regs;
        /// <summary>The solver body of a registered entity (null: not in the simulation yet, or static).</summary>
        public SolverBody BodyOf(Entity e) => _regs.TryGetValue(e, out var r) ? r.Body : null;
        internal ColliderTable.Slot SlotOf(Entity e) => _regs.TryGetValue(e, out var r) ? r.Slot : null;

        /// <summary>A body-less collider was moved by a system this step (kinematic movers): re-bake it now so the solver sees it.</summary>
        internal void Rebake(Entity e)
        {
            if (!_regs.TryGetValue(e, out var reg) || reg.Body != null || reg.Slot == null || !World.Has<LocalTransform>(e)) return;
            var lt = World.Get<LocalTransform>(e);
            BakeStaticShape(reg.Slot, lt);
            PhysicsApi.World.RefreshEcs(reg.Slot);
            reg.LastPos = lt.Position; reg.LastRot = lt.Rotation; reg.LastScale = lt.Scale;
        }
        /// <summary>Raised (with the entity) when an entity leaves the simulation: the trigger system closes its overlaps.</summary>
        internal event Action<Registration> Unregistered;

        public override void OnCreate()
        {
            _unregistered = World.Query().WithReadOnly<ColliderShape>().With<LocalTransform>().Without<PhysicsHandle>().Build();
            _bodiesOnly = World.Query().WithReadOnly<Body>().With<LocalTransform>().Without<ColliderShape>().Without<PhysicsHandle>().Build();
        }

        public void FixedTick(SystemContext ctx)
        {
            RegisterNew(ctx);
            PushExternalChanges();
        }

        private void RegisterNew(SystemContext ctx)
        {
            foreach (var chunk in _unregistered)
            {
                var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i) && !_regs.ContainsKey(ents[i])) Register(ctx, ents[i], hasShape: true);
            }
            foreach (var chunk in _bodiesOnly)
            {
                var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i) && !_regs.ContainsKey(ents[i])) Register(ctx, ents[i], hasShape: false);
            }
        }

        private bool Skip(Entity e, string why)
        {
            if (_warned.Add(e)) Debug.LogWarning($"[ecs physics] {e}: {why}; not simulated");
            return false;
        }

        private bool Register(SystemContext ctx, Entity e, bool hasShape)
        {
            if (World.Has<Parent>(e)) return Skip(e, "has a Parent (physics entities must be roots)");
            var shape = hasShape ? World.Get<ColliderShape>(e) : default;
            bool hasBody = World.Has<Body>(e);
            if (hasShape && shape.Kind == ShapeKind.Mesh && hasBody) return Skip(e, "mesh colliders must be static (no Body)");
            if (hasBody && World.Has<KinematicMover>(e)) return Skip(e, "a KinematicMover must not also have a Body (use Body.Kind = Kinematic for a solver-driven kinematic body)");
            var lt = World.Get<LocalTransform>(e);
            var world = PhysicsApi.World;

            var reg = new Registration { Entity = e, LastPos = lt.Position, LastRot = lt.Rotation, LastScale = lt.Scale };
            int layer = 0, mask = ~0, group = 0; bool useMatrix = true;
            if (World.Has<CollisionFilter>(e))
            {
                var f = World.Get<CollisionFilter>(e);
                layer = f.Layer; mask = f.Mask; group = f.Group; useMatrix = f.UseLayerMatrix;
            }
            if (hasShape)
            {
                var slot = world.AddEcs(e, isStatic: shape.IsStatic && !hasBody);
                slot.ecsShape = shape;
                slot.layer = layer;
                slot.isTrigger = World.Has<Trigger>(e);
                if (World.Has<SurfaceMaterial>(e)) { slot.ecsMaterial = World.Get<SurfaceMaterial>(e); slot.hasEcsMaterial = true; }
                reg.Slot = slot;
            }

            if (hasBody)
            {
                var body = new SolverBody { ecsAlive = true, collider = reg.Slot, entity = e };
                if (reg.Slot != null) reg.Slot.body = body;
                body.SetupEcs(World.Get<Body>(e), shape, lt.Scale);
                body.collisionMask = mask; body.useLayerMatrix = useMatrix; body.collisionGroup = group;
                if (World.Has<SurfaceMaterial>(e))
                {
                    var m = World.Get<SurfaceMaterial>(e);
                    body.staticFriction = m.StaticFriction; body.friction = m.DynamicFriction; body.restitution = m.Restitution;
                }
                body.ecsPosition = lt.Position; body.ecsRotation = lt.Rotation;
                body.ReadTransform(); // also computes worldBounds
                if (reg.Slot != null) UpdateWorldShape(reg.Slot, body);
                world.AddRigidBody(body);
                reg.Body = body;
                if (World.Has<BodyVelocity>(e))
                {
                    var v = World.Get<BodyVelocity>(e);
                    body.linearVelocity = v.Linear; body.angularVelocity = v.Angular;
                    reg.LastVel = v.Linear; reg.LastAngVel = v.Angular; reg.HasVelocity = true;
                }
            }
            else if (reg.Slot != null) BakeStaticShape(reg.Slot, lt);
            if (reg.Slot != null) world.InsertEcs(reg.Slot);
            _regs[e] = reg;
            ctx.Commands.Add(e, new PhysicsHandle { Proxy = ColliderTable.ProxyOf(reg.Slot) });
            return true;
        }

        /// <summary>World shape of a slot that follows a body (rounded kinds exact; box / convex simulate as the body's oriented box).</summary>
        internal static void UpdateWorldShape(ColliderTable.Slot slot, SolverBody body)
        {
            slot.worldShape = body.WorldShape;
            slot.worldBounds = body.worldBounds;
        }

        /// <summary>Bakes the world geometry of a body-less collider from its local description and transform.</summary>
        internal static void BakeStaticShape(ColliderTable.Slot slot, in LocalTransform lt)
        {
            var s = slot.ecsShape;
            Vector3 scale = lt.Scale;
            Vector3 center = lt.Position + lt.Rotation * Vector3.Scale(s.Center, scale);
            float maxScale = MathF.Max(MathF.Abs(scale.x), MathF.Max(MathF.Abs(scale.y), MathF.Abs(scale.z)));
            slot.polytope = null; slot.hull = null; slot.mesh = null;
            switch (s.Kind)
            {
                case ShapeKind.Sphere:
                    slot.worldShape = CollisionShape.Sphere(center, s.Radius * maxScale);
                    break;
                case ShapeKind.Capsule:
                {
                    float r = s.Radius * MathF.Max(MathF.Abs(scale.x), MathF.Abs(scale.z));
                    float hh = MathF.Max(s.Height * MathF.Abs(scale.y) * 0.5f - r, 0f);
                    slot.worldShape = CollisionShape.Capsule(center, lt.Rotation, r, hh);
                    break;
                }
                case ShapeKind.Box:
                {
                    Vector3 size = Vector3.Scale(s.Size, scale);
                    slot.worldShape = CollisionShape.Box(center, lt.Rotation, size * 0.5f);
                    slot.polytope = ConvexShape.CreateBox();
                    slot.polytope.SetBox(center, size * 0.5f, lt.Rotation);
                    slot.hull = ConvexHull.FromBox(Matrix4x4.TRS(center, lt.Rotation, Vector3.one), Vector3.zero, size);
                    break;
                }
                case ShapeKind.Convex:
                {
                    var asset = PhysicsAssets.Hulls.Resolve(s.Hull);
                    if (asset == null) { slot.worldShape = CollisionShape.AxisAlignedBox(center, Vector3.one * 0.05f); break; }
                    // world faces / vertices (rotation + translation + per-axis scale; shear is not supported)
                    var verts = new Vector3[asset.Vertices.Length];
                    for (int i = 0; i < verts.Length; i++) verts[i] = lt.Position + lt.Rotation * Vector3.Scale(asset.Vertices[i] + s.Center, scale);
                    var faces = new Plane[asset.Faces.Length];
                    for (int i = 0; i < faces.Length; i++)
                    {
                        var f = asset.Faces[i];
                        Vector3 n = (lt.Rotation * new Vector3(f.normal.x / scale.x, f.normal.y / scale.y, f.normal.z / scale.z)).normalized;
                        Vector3 onPlane = lt.Position + lt.Rotation * Vector3.Scale(f.normal * f.dist + s.Center, scale);
                        faces[i] = new Plane(n, Vector3.Dot(n, onPlane));
                    }
                    slot.polytope = ConvexShape.FromPlanes(faces, verts);
                    slot.hull = ConvexHull.FromPlanes(faces, verts, true);
                    slot.worldShape = CollisionShape.Polytope(slot.polytope);
                    slot.worldBounds = slot.hull.bounds;
                    return;
                }
                case ShapeKind.Mesh:
                {
                    var tm = PhysicsAssets.Meshes.Resolve(s.Mesh);
                    slot.mesh = tm != null
                        ? MeshShape.FromLocal(tm.Vertices, tm.Triangles, Matrix4x4.TRS(lt.Position, lt.Rotation, scale) * Matrix4x4.Translate(s.Center))
                        : new MeshShape(Array.Empty<Vector3>(), Array.Empty<int>());
                    slot.worldShape = CollisionShape.FromBounds(slot.mesh.Bounds);
                    slot.worldBounds = slot.mesh.Bounds;
                    return;
                }
            }
            slot.worldBounds = slot.worldShape.bounds;
        }

        /// <summary>Teleports (someone moved LocalTransform) and external velocity writes, detected by comparing to what the write-back last wrote.</summary>
        private void PushExternalChanges()
        {
            foreach (var reg in _regs.Values)
            {
                if (!World.Has<LocalTransform>(reg.Entity)) continue;
                ref var lt = ref World.Get<LocalTransform>(reg.Entity);
                bool moved = lt.Position != reg.LastPos || lt.Rotation != reg.LastRot;
                if (reg.Body != null)
                {
                    if (moved)
                    {
                        reg.Body.SetSourcePose(lt.Position, lt.Rotation);
                        reg.Body.ReadTransform();
                        reg.Body.WakeUp();
                        if (reg.Slot != null) UpdateWorldShape(reg.Slot, reg.Body);
                        reg.LastPos = lt.Position; reg.LastRot = lt.Rotation;
                    }
                    if (reg.HasVelocity && World.Has<BodyVelocity>(reg.Entity))
                    {
                        ref var v = ref World.Get<BodyVelocity>(reg.Entity);
                        if (v.Linear != reg.LastVel) { reg.Body.linearVelocity = v.Linear; reg.Body.WakeUp(); }
                        if (v.Angular != reg.LastAngVel) { reg.Body.angularVelocity = v.Angular; reg.Body.WakeUp(); }
                    }
                }
                else if (reg.Slot != null && (moved || lt.Scale != reg.LastScale))
                {
                    BakeStaticShape(reg.Slot, lt);
                    PhysicsApi.World.RefreshEcs(reg.Slot);
                    reg.LastPos = lt.Position; reg.LastRot = lt.Rotation; reg.LastScale = lt.Scale;
                }
            }
        }

        public void OnEntityDestroyed(SystemContext ctx, Entity e)
        {
            if (_regs.TryGetValue(e, out var reg)) { _regs.Remove(e); Unregister(reg); }
            _warned.Remove(e);
        }

        public void OnSimEnd(SystemContext ctx)
        {
            var all = new List<Registration>(_regs.Values);
            _regs.Clear(); _warned.Clear();
            foreach (var reg in all) Unregister(reg);
        }

        private void Unregister(Registration reg)
        {
            Unregistered?.Invoke(reg);
            if (reg.Body != null)
            {
                reg.Body.ecsAlive = false; reg.Body.live = false;
                PhysicsApi.World.RemoveRigidBody(reg.Body);
            }
            if (reg.Slot != null)
            {
                reg.Slot.body = null;
                PhysicsApi.World.RemoveEcs(reg.Slot);
            }
        }
    }

    /// <summary>Steps the simulation (FixedTick, after the sync).</summary>
    [UpdateAfter(typeof(PhysicsSyncSystem))]
    public sealed class PhysicsStepSystem : SystemBase, IFixedTick
    {
        public void FixedTick(SystemContext ctx) => PhysicsApi.FixedStep(ctx.DeltaTime);
    }

    /// <summary>
    /// After the step: solver poses back to <see cref="LocalTransform"/>, velocities back to <see cref="BodyVelocity"/>,
    /// the <see cref="Sleeping"/> tag in sync with the solver, the slot's world shape refreshed, and the contacts of
    /// <see cref="ReportCollisions"/> bodies posted as <see cref="CollisionEvent"/>s.
    /// </summary>
    [UpdateAfter(typeof(PhysicsStepSystem))]
    public sealed class PhysicsWriteBackSystem : SystemBase, IFixedTick
    {
        public void FixedTick(SystemContext ctx)
        {
            var sync = World.GetSystem<PhysicsSyncSystem>();
            if (sync == null) return;
            foreach (var reg in sync.Registrations.Values)
            {
                var body = reg.Body;
                if (body == null || !World.Has<LocalTransform>(reg.Entity)) continue;
                ref var lt = ref World.Get<LocalTransform>(reg.Entity);
                lt.Position = body.ecsPosition; lt.Rotation = body.ecsRotation;
                reg.LastPos = lt.Position; reg.LastRot = lt.Rotation;
                if (reg.HasVelocity && World.Has<BodyVelocity>(reg.Entity))
                {
                    ref var v = ref World.Get<BodyVelocity>(reg.Entity);
                    v.Linear = body.linearVelocity; v.Angular = body.angularVelocity;
                    reg.LastVel = v.Linear; reg.LastAngVel = v.Angular;
                }
                if (reg.Slot != null) PhysicsSyncSystem.UpdateWorldShape(reg.Slot, body);
                if (body.sleeping != reg.WasSleeping)
                {
                    reg.WasSleeping = body.sleeping;
                    if (body.sleeping) ctx.Commands.Add(reg.Entity, new Sleeping());
                    else ctx.Commands.Remove<Sleeping>(reg.Entity);
                }
                if (body.contacts.Count > 0 && World.Has<ReportCollisions>(reg.Entity))
                    foreach (var c in body.contacts)
                        ctx.Post(new CollisionEvent { Self = reg.Entity, Other = c.other, Point = c.point, Normal = c.normal, Impulse = c.impulse, ApproachSpeed = c.approachSpeed });
            }
        }
    }

    /// <summary>
    /// Trigger colliders against bodies: after each step every <see cref="Trigger"/> entity is tested (exact shapes)
    /// against the bodies and movers (any non-static collider) near it; started overlaps post <see cref="TriggerEnterEvent"/>, ended ones (or either side
    /// leaving the simulation) <see cref="TriggerExitEvent"/>. Static level geometry never counts as an overlap.
    /// </summary>
    [UpdateAfter(typeof(PhysicsWriteBackSystem))]
    public sealed class TriggerSystem : SystemBase, IFixedTick
    {
        private HashSet<(Entity trigger, Entity other)> _overlaps = new HashSet<(Entity, Entity)>();
        private HashSet<(Entity trigger, Entity other)> _current = new HashSet<(Entity, Entity)>();
        private readonly List<ColliderTable.Slot> _candidates = new List<ColliderTable.Slot>(32);
        private readonly List<(Entity, Entity)> _gone = new List<(Entity, Entity)>();
        private PhysicsSyncSystem _sync;
        private readonly List<(Entity trigger, Entity other)> _pendingExits = new List<(Entity, Entity)>();

        public override void OnCreate() { }

        public void FixedTick(SystemContext ctx)
        {
            if (_sync == null)
            {
                _sync = World.GetSystem<PhysicsSyncSystem>();
                if (_sync == null) return;
                _sync.Unregistered += OnUnregistered;
            }
            foreach (var p in _pendingExits) ctx.Post(new TriggerExitEvent { Trigger = p.trigger, Other = p.other });
            _pendingExits.Clear();

            _current.Clear();
            var world = PhysicsApi.World;
            foreach (var reg in _sync.Registrations.Values)
            {
                var s = reg.Slot;
                if (s == null || !s.isTrigger) continue;
                _candidates.Clear();
                var filter = new QueryFilter { layerMask = ~0, self = ColliderTable.ProxyOf(s), includeTriggers = false, useLayerMatrix = true, selfLayer = s.layer };
                world.QueryBounds(s.worldBounds, filter, _candidates);
                foreach (var o in _candidates)
                {
                    if (o.isStatic || o.entity == reg.Entity) continue; // only things that move count (bodies, movers)
                    if (!PhysicsWorld.CollidersOverlap(s, o)) continue;
                    _current.Add((reg.Entity, o.entity));
                }
            }
            foreach (var p in _current) if (!_overlaps.Contains(p)) ctx.Post(new TriggerEnterEvent { Trigger = p.trigger, Other = p.other });
            foreach (var p in _overlaps) if (!_current.Contains(p)) ctx.Post(new TriggerExitEvent { Trigger = p.trigger, Other = p.other });
            (_overlaps, _current) = (_current, _overlaps);
        }

        private void OnUnregistered(PhysicsSyncSystem.Registration reg)
        {
            _gone.Clear();
            foreach (var p in _overlaps) if (p.trigger == reg.Entity || p.other == reg.Entity) _gone.Add(p);
            foreach (var p in _gone) { _overlaps.Remove(p); _pendingExits.Add(p); }
        }
    }
}

namespace RipNRage.Engine.Ecs.Physics
{
    /// <summary>Code-side construction of physics entities (level loading, generated props); they register at the next fixed step.</summary>
    public static class PhysicsEntities
    {
        public static Entity StaticBox(World world, Vector3 center, Quaternion rotation, Vector3 size, int layer = 0, bool trigger = false) =>
            Create(world, new LocalTransform { Position = center, Rotation = rotation, Scale = Vector3.one },
                new ColliderShape { Kind = ShapeKind.Box, Size = size, IsStatic = true }, layer, trigger);

        public static Entity StaticSphere(World world, Vector3 center, float radius, int layer = 0, bool trigger = false) =>
            Create(world, LocalTransform.At(center), new ColliderShape { Kind = ShapeKind.Sphere, Radius = radius, IsStatic = true }, layer, trigger);

        /// <summary>A static convex solid from world-space face planes (outward) and polygon vertices (a level brush).</summary>
        public static Entity StaticConvex(World world, Plane[] worldPlanes, Vector3[] worldVertices, int layer = 0)
        {
            var asset = new ConvexColliderAsset(worldPlanes, worldVertices);
            var handle = PhysicsAssets.Hulls.Intern(asset);
            return Create(world, LocalTransform.At(Vector3.zero), new ColliderShape { Kind = ShapeKind.Convex, Hull = handle, IsStatic = true }, layer, false);
        }

        /// <summary>A static triangle mesh placed by <paramref name="transform"/> (terrain, docks).</summary>
        public static Entity StaticMesh(World world, AssetHandle<TriangleMesh> mesh, in LocalTransform transform, int layer = 0) =>
            Create(world, transform, new ColliderShape { Kind = ShapeKind.Mesh, Mesh = mesh, IsStatic = true }, layer, false);

        /// <summary>A dynamic body with the given shape.</summary>
        public static Entity Dynamic(World world, in LocalTransform transform, in ColliderShape shape, float mass, int layer = 0)
        {
            var e = Create(world, transform, shape, layer, false);
            var body = new BodyConfig { mass = mass }.Bake();
            world.Commands.Add(e, body);
            world.Commands.Add(e, new BodyVelocity());
            return e;
        }

        private static Entity Create(World world, in LocalTransform transform, in ColliderShape shape, int layer, bool trigger)
        {
            var cmd = world.Commands;
            var e = cmd.CreateEntity();
            cmd.Add(e, transform);
            cmd.Add(e, shape);
            if (layer != 0) cmd.Add(e, new CollisionFilter { Layer = layer, Mask = ~0, UseLayerMatrix = true });
            if (trigger) cmd.Add(e, new Trigger());
            return e;
        }
    }
}
