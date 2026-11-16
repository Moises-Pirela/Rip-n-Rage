using System;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;

namespace RipNRage.Engine.Ecs.Physics
{
    /// <summary>
    /// "My collider is this big" (10.4): the size a level writes, independent of which shape the collider is.
    /// <see cref="ShapeSizeSystem"/> puts it into the <see cref="ColliderShape"/> the way that shape wants it — a box
    /// takes the size, a sphere half the longest side, a capsule the width and the height. Two numbers mean
    /// "w l" with <see cref="Thickness"/> between them (the maps' plane form).
    /// </summary>
    public struct ShapeSize : IComponent
    {
        public Vector3 Size;
    }

    /// <summary>
    /// "My transform is my base, not my middle" — a level places a prop by the floor it stands on, so the body is
    /// lifted half its height (plus a hair of <see cref="Clearance"/>) as it is created.
    /// </summary>
    public struct StandOnBase : IComponent
    {
        public float Clearance;
    }

    public enum BoxEdge { Left, Right, Top, Bottom }

    /// <summary>
    /// "My hinge runs along one of my edges" (a door): the anchor and the axis come from the edge and the size, so a
    /// door of any width hangs correctly without anyone computing an anchor.
    /// </summary>
    public struct HingeOnEdge : IComponent
    {
        public BoxEdge Edge;
    }

    /// <summary>
    /// "I hang from where I was placed" (a pendulum, a prop on a spring): the entity drops <see cref="Length"/> below
    /// its own spawn point — swung out by <see cref="StartAngle"/> — and its joint reaches back up to that point.
    /// </summary>
    public struct HangsFrom : IComponent
    {
        public float Length, StartAngle;
        /// <summary>Where it was placed: the hook the joint holds on to (filled as it is created).</summary>
        public Vector3 Hook;
    }

    /// <summary>
    /// A joint between two props, authored as its own entity standing at the anchor (a level's <c>joint_*</c>): it
    /// carries the joint to give, who gives it and who receives it. <see cref="JointConnectorSystem"/> converts the
    /// anchor and axis into the body's local space, hands the body the joint, and removes this entity — a connector
    /// is an instruction, not a thing in the world.
    /// </summary>
    [NotInPreview] public struct JointConnector : IComponent
    {
        public Entity Body, Connected;
        public Joint Joint;
        /// <summary>World anchor (where this entity stands) and world axis (its own rotation applied).</summary>
        public Vector3 Anchor, Axis;
        /// <summary>Rods and springs reach to a separate anchor on the far body; the rest join where the bodies stand.</summary>
        public bool Reaches;
        public bool HasConnectedAnchor;
        public Vector3 ConnectedAnchor;
        /// <summary>Distance joints: NaN = "take it from how far apart they are right now".</summary>
        public float MinDistance, MaxDistance;
    }

    public sealed class ShapeSizeConfig : ComponentConfig<ShapeSize>
    {
        public float[] size = { 1, 1, 1 };
        /// <summary>The middle number when <see cref="size"/> is given as "w l" (a plane's slab).</summary>
        [InspectTip("the middle number when size is given as \"w l\" (a plane's slab)")]
        public float thickness = 1f;
        public override ShapeSize Bake() => new ShapeSize
        {
            Size = size != null && size.Length == 2 ? new Vector3(size[0], thickness, size[1]) : ShapeConfigUtil.Vec(size, 1f),
        };
    }

    public sealed class StandOnBaseConfig : ComponentConfig<StandOnBase>
    {
        [InspectRange(0, 1, 0.005f), InspectTip("a hair above the floor so it does not start intersecting")]
        public float clearance = 0.02f;
        public override StandOnBase Bake() => new StandOnBase { Clearance = clearance };
    }

    public sealed class HingeOnEdgeConfig : ComponentConfig<HingeOnEdge>
    {
        [InspectTip("the edge the hinge runs along: left / right turn about up, top / bottom about right")]
        public BoxEdge edge = BoxEdge.Left;
        public override HingeOnEdge Bake() => new HingeOnEdge { Edge = edge };
    }

    public sealed class HangsFromConfig : ComponentConfig<HangsFrom>
    {
        [InspectTip("metres below the hook; degrees swung out to start")]
        public float length = 1f, startAngle;
        public override HangsFrom Bake() => new HangsFrom { Length = length, StartAngle = startAngle };
        public override HangsFrom Bake(in BakeContext ctx) =>
            new HangsFrom { Length = length, StartAngle = startAngle, Hook = ctx.HasEntity ? ctx.Position : Vector3.zero };
    }

    /// <summary>A level's <c>joint_*</c>: the joint's own parameters, plus who it joins.</summary>
    public sealed class JointConnectorConfig : ComponentConfig<JointConnector>
    {
        public JointKind kind = JointKind.Ball;
        /// <summary>The props by name (a level targetname); an empty "connected" joins the world.</summary>
        public EntityRef body, connected;
        [InspectTip("hinge / slider axis in the connector's own space (its angle applies)")]
        public float[] axis = { 0, 1, 0 };
        public float[] connectedAnchor;
        public float breakForce, breakTorque;
        public bool collideConnected;
        public float[] limits;
        public float motorSpeed, motorForce = 100f;
        public float springStiffness, springDamping = 1f, springTarget;
        public bool rope;
        [InspectTip("NaN = taken from how far apart the bodies are now")]
        public float minDistance = float.NaN, maxDistance = float.NaN;
        public float stiffness = 50f, damping = 2f, restLength = -1f;

        public override JointConnector Bake() => Bake(default);

        public override JointConnector Bake(in BakeContext ctx)
        {
            var j = new JointConfig
            {
                kind = kind, breakForce = breakForce, breakTorque = breakTorque, collideConnected = collideConnected,
                limits = limits, motorSpeed = motorSpeed, motorForce = motorForce,
                springStiffness = springStiffness, springDamping = springDamping, springTarget = springTarget,
                rope = rope, stiffness = stiffness, damping = damping, restLength = restLength,
                axis = new[] { 0f, 1f, 0f }, // the real axis is world-space, below
            }.Bake();
            bool reaches = kind == JointKind.Distance || kind == JointKind.Spring;
            var world = ShapeConfigUtil.Vec(axis, 0f);
            if (world.sqrMagnitude < 1E-6f) world = Vector3.up;
            return new JointConnector
            {
                Body = body.Resolve(ctx), Connected = connected.Resolve(ctx), Joint = j,
                Anchor = ctx.HasEntity ? ctx.Position : Vector3.zero,
                Axis = (ctx.HasEntity ? ctx.Rotation * world : world).normalized,
                Reaches = reaches,
                HasConnectedAnchor = connectedAnchor != null,
                ConnectedAnchor = ShapeConfigUtil.Vec(connectedAnchor),
                MinDistance = minDistance, MaxDistance = maxDistance,
            };
        }
    }

    /// <summary>Sizes a collider from the <see cref="ShapeSize"/> its level wrote, as the entity is created.</summary>
    [EntityFilter(typeof(ShapeSize), typeof(ColliderShape))]
    [Reads(typeof(ShapeSize)), Writes(typeof(ColliderShape))]
    public sealed class ShapeSizeSystem : SystemBase, IOnEntityCreated
    {
        public void OnEntityCreated(SystemContext ctx, Entity e)
        {
            var size = ctx.World.Get<ShapeSize>(e).Size;
            ref var shape = ref ctx.World.Get<ColliderShape>(e);
            switch (shape.Kind)
            {
                case ShapeKind.Sphere:
                    shape.Radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f;
                    break;
                case ShapeKind.Capsule:
                    shape.Radius = Mathf.Max(size.x, size.z) * 0.5f;
                    shape.Height = Mathf.Max(size.y, Mathf.Max(size.x, size.z));
                    break;
                default:
                    shape.Size = size;
                    break;
            }
        }
    }

    /// <summary>Lifts an entity that was placed by its base onto its own feet.</summary>
    [EntityFilter(typeof(StandOnBase), typeof(LocalTransform))]
    [Reads(typeof(StandOnBase), typeof(ShapeSize), typeof(ColliderShape)), Writes(typeof(LocalTransform))]
    [UpdateAfter(typeof(ShapeSizeSystem)), UpdateBefore(typeof(TransformSystem))]
    public sealed class StandOnBaseSystem : SystemBase, IOnEntityCreated
    {
        public void OnEntityCreated(SystemContext ctx, Entity e)
        {
            var w = ctx.World;
            float height = w.Has<ShapeSize>(e) ? w.Get<ShapeSize>(e).Size.y : HeightOf(w, e);
            w.Get<LocalTransform>(e).Position += new Vector3(0f, height * 0.5f + w.Get<StandOnBase>(e).Clearance, 0f);
        }

        private static float HeightOf(World w, Entity e)
        {
            if (!w.Has<ColliderShape>(e)) return 0f;
            var s = w.Get<ColliderShape>(e);
            return s.Kind switch { ShapeKind.Sphere => s.Radius * 2f, ShapeKind.Capsule => s.Height, _ => s.Size.y };
        }
    }

    /// <summary>Hangs a hinge on the edge its component names, using the size the collider ended up with.</summary>
    [EntityFilter(typeof(HingeOnEdge), typeof(Joint))]
    [Reads(typeof(HingeOnEdge), typeof(ShapeSize), typeof(ColliderShape)), Writes(typeof(Joint))]
    [UpdateAfter(typeof(ShapeSizeSystem))]
    public sealed class HingeOnEdgeSystem : SystemBase, IOnEntityCreated
    {
        public void OnEntityCreated(SystemContext ctx, Entity e)
        {
            var w = ctx.World;
            var size = w.Has<ShapeSize>(e) ? w.Get<ShapeSize>(e).Size : w.Get<ColliderShape>(e).Size;
            ref var j = ref w.Get<Joint>(e);
            switch (w.Get<HingeOnEdge>(e).Edge)
            {
                case BoxEdge.Right: j.Anchor = new Vector3(size.x * 0.5f, 0f, 0f); j.Axis = Vector3.up; break;
                case BoxEdge.Top: j.Anchor = new Vector3(0f, size.y * 0.5f, 0f); j.Axis = Vector3.right; break;
                case BoxEdge.Bottom: j.Anchor = new Vector3(0f, -size.y * 0.5f, 0f); j.Axis = Vector3.right; break;
                default: j.Anchor = new Vector3(-size.x * 0.5f, 0f, 0f); j.Axis = Vector3.up; break;
            }
        }
    }

    /// <summary>Drops a hanging prop below its hook and points its joint back at it.</summary>
    [EntityFilter(typeof(HangsFrom), typeof(Joint), typeof(LocalTransform))]
    [Reads(typeof(HangsFrom)), Writes(typeof(Joint), typeof(LocalTransform))]
    [UpdateAfter(typeof(StandOnBaseSystem)), UpdateBefore(typeof(TransformSystem))]
    public sealed class HangsFromSystem : SystemBase, IOnEntityCreated
    {
        public void OnEntityCreated(SystemContext ctx, Entity e)
        {
            var w = ctx.World;
            var h = w.Get<HangsFrom>(e);
            ref var t = ref w.Get<LocalTransform>(e);
            float length = Mathf.Max(h.Length, 0.1f);
            Vector3 down = Quaternion.AngleAxis(h.StartAngle, t.Rotation * Vector3.forward) * Vector3.down;
            t.Position = h.Hook + down * length;
            ref var j = ref w.Get<Joint>(e);
            j.AutoConnectedAnchor = false;
            j.ConnectedAnchor = h.Hook; // joined to the world: the anchor is where it hangs from
        }
    }

    /// <summary>
    /// Hands a prop the joint a <c>joint_*</c> entity describes — anchor and axis converted into that body's local
    /// space — and removes the connector, which was only ever an instruction.
    /// </summary>
    [EntityFilter(typeof(JointConnector))]
    [Reads(typeof(JointConnector), typeof(LocalTransform)), Writes(typeof(Joint))]
    [UpdateAfter(typeof(HangsFromSystem)), UpdateAfter(typeof(StandOnBaseSystem))]
    public sealed class JointConnectorSystem : SystemBase, IOnEntityCreated
    {
        public void OnEntityCreated(SystemContext ctx, Entity e)
        {
            var w = ctx.World;
            var c = w.Get<JointConnector>(e);
            ctx.Commands.DestroyEntity(e);
            if (!w.IsAlive(c.Body) || !w.Has<LocalTransform>(c.Body))
            {
                Debug.LogWarning($"joint connector at {c.Anchor}: its body is missing (set targetname on a prop)");
                return;
            }
            var body = w.Get<LocalTransform>(c.Body);
            var inverse = Quaternion.Inverse(body.Rotation);
            var j = c.Joint;
            j.Anchor = inverse * (c.Anchor - body.Position);
            j.Axis = inverse * c.Axis;
            j.Connected = c.Connected;
            j.AutoConnectedAnchor = !c.Reaches;
            if (c.Reaches)
            {
                bool hasConnected = w.IsAlive(c.Connected) && w.Has<LocalTransform>(c.Connected);
                var far = hasConnected ? w.Get<LocalTransform>(c.Connected) : default;
                Vector3 anchor = c.HasConnectedAnchor ? c.ConnectedAnchor : hasConnected ? far.Position : c.Anchor;
                j.ConnectedAnchor = hasConnected ? Quaternion.Inverse(far.Rotation) * (anchor - far.Position) : anchor;
                if (!float.IsNaN(c.MinDistance) || !float.IsNaN(c.MaxDistance))
                {
                    float apart = (anchor - c.Anchor).magnitude;
                    j.AutoDistance = false;
                    j.MinDistance = !float.IsNaN(c.MinDistance) ? c.MinDistance : j.Rope ? 0f : apart;
                    j.MaxDistance = !float.IsNaN(c.MaxDistance) ? c.MaxDistance : apart;
                }
            }
            ctx.Commands.Add(c.Body, j);
        }
    }
}
