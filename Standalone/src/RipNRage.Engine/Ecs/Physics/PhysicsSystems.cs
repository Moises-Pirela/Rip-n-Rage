using System;
using System.Collections.Generic;
using RipNRage.Engine.Ecs.Transforms;
using LegacyPhysics = RipNRage.Engine.Physics;

namespace RipNRage.Engine.Ecs.Physics
{
    /// <summary>
    /// Puts ECS entities into the one simulation and keeps both sides current. Runs first in FixedTick (no
    /// [Reads]/[Writes]: physics state is global, so it runs alone).
    ///
    /// Per fixed step: registers entities that gained a <see cref="ColliderShape"/> or <see cref="Body"/> (slot in
    /// <see cref="PhysicsWorld.Table"/>, <see cref="SolverBody"/> for bodies, a <see cref="PhysicsHandle"/> on the
    /// entity), then pushes external changes at the solver: a <see cref="LocalTransform"/> someone moved is a
    /// teleport, a <see cref="BodyVelocity"/> someone wrote wins over the solver's value.
    ///
    /// Physics entities must be roots: the solver works in world space and writes <see cref="LocalTransform"/> back
    /// directly. A parented physics entity is skipped with a warning. ECS mesh colliders are not simulated yet
    /// (layer 2.6); they are skipped with a warning too.
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

        private void Register(SystemContext ctx, Entity e, bool hasShape)
        {
            if (World.Has<Parent>(e))
            {
                if (_warned.Add(e)) Debug.LogWarning($"[ecs physics] {e} has a Parent: physics entities must be roots; not simulated");
                return;
            }
            var shape = hasShape ? World.Get<ColliderShape>(e) : default;
            if (hasShape && shape.Kind == ShapeKind.Mesh)
            {
                if (_warned.Add(e)) Debug.LogWarning($"[ecs physics] {e}: ECS mesh colliders are not simulated yet (coming with layer 2.6); skipped");
                return;
            }
            var lt = World.Get<LocalTransform>(e);
            bool hasBody = World.Has<Body>(e);
            var world = LegacyPhysics.World;

            var reg = new Registration { Entity = e, LastPos = lt.Position, LastRot = lt.Rotation, LastScale = lt.Scale };
            if (hasShape)
            {
                var slot = world.AddEcs(e, isStatic: shape.IsStatic && !hasBody);
                slot.ecsShape = shape;
                slot.isTrigger = World.Has<Trigger>(e);
                if (World.Has<SurfaceMaterial>(e)) { slot.ecsMaterial = World.Get<SurfaceMaterial>(e); slot.hasEcsMaterial = true; }
                reg.Slot = slot;
            }
            int layer = 0, mask = ~0, group = 0; bool useMatrix = true;
            if (World.Has<CollisionFilter>(e))
            {
                var f = World.Get<CollisionFilter>(e);
                layer = f.Layer; mask = f.Mask; group = f.Group; useMatrix = f.UseLayerMatrix;
            }
            if (reg.Slot != null) reg.Slot.layer = layer;

            if (hasBody)
            {
                var body = new SolverBody { ecsAlive = true, collider = reg.Slot };
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
                if (reg.Slot != null) { reg.Slot.ecsBounds = body.worldBounds; UpdateWorldShape(reg.Slot, body); }
                world.AddRigidBody(body);
                reg.Body = body;
                if (World.Has<BodyVelocity>(e))
                {
                    var v = World.Get<BodyVelocity>(e);
                    body.linearVelocity = v.Linear; body.angularVelocity = v.Angular;
                    reg.LastVel = v.Linear; reg.LastAngVel = v.Angular; reg.HasVelocity = true;
                }
            }
            else if (reg.Slot != null)
            {
                BakeStaticShape(reg.Slot, lt);
            }
            if (reg.Slot != null) world.InsertEcs(reg.Slot);
            _regs[e] = reg;
            ctx.Commands.Add(e, new PhysicsHandle { Proxy = ColliderTable.ProxyOf(reg.Slot) });
        }

        /// <summary>World shape of a slot that follows a body (rounded kinds get exact shapes; box / convex simulate as the body's oriented box).</summary>
        internal static void UpdateWorldShape(ColliderTable.Slot slot, SolverBody body)
        {
            slot.worldShape = body.WorldShape;
            slot.ecsBounds = body.worldBounds;
        }

        /// <summary>Bakes the world-space shape of a static ECS collider from its local description and transform.</summary>
        internal static void BakeStaticShape(ColliderTable.Slot slot, in LocalTransform lt)
        {
            var s = slot.ecsShape;
            Vector3 scale = lt.Scale;
            Vector3 center = lt.Position + lt.Rotation * Vector3.Scale(s.Center, scale);
            float maxScale = MathF.Max(MathF.Abs(scale.x), MathF.Max(MathF.Abs(scale.y), MathF.Abs(scale.z)));
            switch (s.Kind)
            {
                case ShapeKind.Sphere:
                    slot.worldShape = CollisionShape.Sphere(center, s.Radius * maxScale);
                    slot.polytope = null;
                    break;
                case ShapeKind.Capsule:
                {
                    float r = s.Radius * MathF.Max(MathF.Abs(scale.x), MathF.Abs(scale.z));
                    float hh = MathF.Max(s.Height * MathF.Abs(scale.y) * 0.5f - r, 0f);
                    slot.worldShape = CollisionShape.Capsule(center, lt.Rotation, r, hh);
                    slot.polytope = null;
                    break;
                }
                case ShapeKind.Box:
                {
                    Vector3 he = Vector3.Scale(s.Size, scale) * 0.5f;
                    slot.worldShape = CollisionShape.Box(center, lt.Rotation, he);
                    slot.polytope ??= ConvexShape.CreateBox();
                    slot.polytope.SetBox(center, he, lt.Rotation);
                    break;
                }
                case ShapeKind.Convex:
                {
                    var asset = PhysicsAssets.Hulls.Resolve(s.Hull);
                    if (asset == null) { slot.worldShape = CollisionShape.AxisAlignedBox(center, Vector3.one * 0.1f); slot.polytope = null; break; }
                    // world faces / vertices (rotation + translation + per-axis scale; shear is not supported)
                    var verts = new Vector3[asset.Vertices.Length];
                    for (int i = 0; i < verts.Length; i++) verts[i] = lt.Position + lt.Rotation * Vector3.Scale(asset.Vertices[i] + s.Center, scale);
                    var faces = new Plane[asset.Faces.Length];
                    for (int i = 0; i < faces.Length; i++)
                    {
                        var f = asset.Faces[i];
                        Vector3 n = lt.Rotation * new Vector3(f.normal.x / scale.x, f.normal.y / scale.y, f.normal.z / scale.z);
                        n = n.normalized;
                        Vector3 onPlane = lt.Position + lt.Rotation * Vector3.Scale(f.normal * f.dist + s.Center, scale);
                        faces[i] = new Plane(n, Vector3.Dot(n, onPlane));
                    }
                    slot.polytope = ConvexShape.FromPlanes(faces, verts);
                    Bounds b = new Bounds(verts[0], verts[0]);
                    foreach (var v in verts) b.Encapsulate(v);
                    slot.worldShape = CollisionShape.Polytope(slot.polytope);
                    slot.ecsBounds = b;
                    return;
                }
            }
            slot.ecsBounds = slot.worldShape.bounds;
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
                    LegacyPhysics.World.RefreshEcs(reg.Slot);
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
            if (reg.Body != null)
            {
                reg.Body.ecsAlive = false; reg.Body.live = false;
                LegacyPhysics.World.RemoveRigidBody(reg.Body);
            }
            if (reg.Slot != null)
            {
                reg.Slot.body = null;
                LegacyPhysics.World.RemoveEcs(reg.Slot);
            }
        }
    }

    /// <summary>
    /// Steps the one simulation (FixedTick, after the sync): the same <see cref="LegacyPhysics.FixedStep"/> the legacy
    /// scene used to call directly — kinematic movers, triggers, then the rigid solver over every body, legacy and ECS.
    /// </summary>
    [UpdateAfter(typeof(PhysicsSyncSystem))]
    public sealed class PhysicsStepSystem : SystemBase, IFixedTick
    {
        /// <summary>Off when something else still drives the step (worlds not wired through Scene.FixedPhysicsStep).</summary>
        public bool StepPhysics = true;

        public void FixedTick(SystemContext ctx)
        {
            if (StepPhysics) LegacyPhysics.FixedStep(ctx.DeltaTime);
        }
    }

    /// <summary>
    /// After the step: solver poses back to <see cref="LocalTransform"/>, velocities back to <see cref="BodyVelocity"/>,
    /// the <see cref="Sleeping"/> tag in sync with the solver, and the slot's world shape refreshed for next step.
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
            }
        }
    }
}
