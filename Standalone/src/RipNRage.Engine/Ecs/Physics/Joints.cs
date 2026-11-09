using System;
using System.Collections.Generic;
using RipNRage.Engine.Ecs.Transforms;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Engine.Ecs.Physics
{
    public enum JointKind
    {
        /// <summary>Ball and socket: the anchors coincide, rotation is free.</summary>
        Ball,
        /// <summary>Weld: no relative motion (breakable).</summary>
        Fixed,
        /// <summary>Rotation about <see cref="Joint.Axis"/> only (doors, levers, wheels); limits / motor / spring in degrees.</summary>
        Hinge,
        /// <summary>Translation along <see cref="Joint.Axis"/> only (pistons, lifts); limits / motor / spring in metres.</summary>
        Slider,
        /// <summary>Rod (or rope with <see cref="Joint.Rope"/>) keeping the anchors between MinDistance and MaxDistance.</summary>
        Distance,
        /// <summary>Damped spring between the anchors (Stiffness N/m, Damping N·s/m, RestLength m; optional rope limits).</summary>
        Spring,
    }

    /// <summary>
    /// A constraint from this entity's <see cref="Body"/> to <see cref="Connected"/> (another body entity) or the world
    /// (<see cref="Entity.Null"/>). <see cref="Anchor"/> and <see cref="Axis"/> are in this entity's local space (scaled);
    /// <see cref="ConnectedAnchor"/> is in the connected entity's local space, or world space when joined to the world.
    /// One joint per entity (ragdolls and chains joint each piece to its parent). Parameters (motor speed, limits...)
    /// may change at runtime; the <see cref="JointSystem"/> copies them into the solver every step.
    /// </summary>
    public struct Joint : IComponent
    {
        public JointKind Kind;
        public Entity Connected;
        public Vector3 Anchor, Axis;
        /// <summary>Compute <see cref="ConnectedAnchor"/> from the poses when the joint is created (the bodies stay where they are).</summary>
        public bool AutoConnectedAnchor;
        public Vector3 ConnectedAnchor;
        /// <summary>Break thresholds (N / N·m); infinity = unbreakable.</summary>
        public float BreakForce, BreakTorque;
        public bool CollideConnected;
        /// <summary>Softness (0 = the solver defaults).</summary>
        public float Hertz, DampingRatio;
        // hinge / slider
        public bool UseLimits; public float LowerLimit, UpperLimit;
        public bool UseMotor; public float MotorSpeed, MotorForce; public bool MotorFreeSpin;
        public bool UseSpring; public float SpringStiffness, SpringDamping, SpringTarget;
        // distance / spring
        /// <summary>Distance: measure the length from the poses at creation. Spring: RestLength &lt; 0 does the same.</summary>
        public bool AutoDistance;
        public float MinDistance, MaxDistance;
        public bool Rope;
        public float Stiffness, Damping, RestLength;
    }

    /// <summary>Tag: the joint broke (or lost its connected body); the constraint is gone. Remove it and set the Joint again to re-attach.</summary>
    [SparseComponent] public struct JointBroken : IComponent { }

    /// <summary>A joint broke this frame (force / torque over its threshold, or the connected body left the simulation).</summary>
    public struct JointBrokenEvent : IPostProcessEvent { public Entity Entity, Connected; public float Force, Torque; }

    public sealed class JointConfig : ComponentConfig<Joint>
    {
        public JointKind kind = JointKind.Ball;
        public float[] anchor = { 0, 0, 0 }, axis = { 0, 1, 0 };
        /// <summary>Null = computed from the poses at creation.</summary>
        public float[] connectedAnchor;
        /// <summary>0 = unbreakable.</summary>
        public float breakForce, breakTorque;
        public bool collideConnected;
        public float hertz, dampingRatio;
        public float[] limits;
        public float motorSpeed, motorForce = 100f; public bool motorFreeSpin;
        public float springStiffness, springDamping = 1f, springTarget;
        public float minDistance = -1f, maxDistance = -1f; public bool rope;
        public float stiffness = 50f, damping = 2f, restLength = -1f;

        public override Joint Bake()
        {
            var ax = PhysicsConfigUtil.Vec(axis);
            if ((kind == JointKind.Hinge || kind == JointKind.Slider) && ax.sqrMagnitude < 1E-8f)
                throw new ArgumentException("hinge / slider joints need a non-zero axis", nameof(axis));
            if (limits != null && limits.Length != 2) throw new ArgumentException("limits must be [min, max]", nameof(limits));
            if (breakForce < 0f || breakTorque < 0f || stiffness < 0f || damping < 0f) throw new ArgumentOutOfRangeException(nameof(breakForce), "break thresholds, stiffness and damping must be ≥ 0");
            return new Joint
            {
                Kind = kind, Connected = Entity.Null, Anchor = PhysicsConfigUtil.Vec(anchor), Axis = ax,
                AutoConnectedAnchor = connectedAnchor == null, ConnectedAnchor = PhysicsConfigUtil.Vec(connectedAnchor),
                BreakForce = breakForce > 0f ? breakForce : float.PositiveInfinity, BreakTorque = breakTorque > 0f ? breakTorque : float.PositiveInfinity,
                CollideConnected = collideConnected, Hertz = hertz, DampingRatio = dampingRatio,
                UseLimits = limits != null, LowerLimit = limits != null ? MathF.Min(limits[0], limits[1]) : 0f, UpperLimit = limits != null ? MathF.Max(limits[0], limits[1]) : 0f,
                UseMotor = motorSpeed != 0f, MotorSpeed = motorSpeed, MotorForce = motorForce, MotorFreeSpin = motorFreeSpin,
                UseSpring = springStiffness > 0f, SpringStiffness = springStiffness, SpringDamping = springDamping, SpringTarget = springTarget,
                AutoDistance = minDistance < 0f && maxDistance < 0f, MinDistance = MathF.Max(minDistance, 0f), MaxDistance = maxDistance < 0f ? float.PositiveInfinity : maxDistance,
                Rope = rope, Stiffness = stiffness, Damping = damping, RestLength = restLength,
            };
        }
    }

    public sealed class JointBrokenConfig : ComponentConfig<JointBroken> { public override JointBroken Bake() => default; }

    /// <summary>
    /// Turns <see cref="Joint"/> components into solver constraints once both ends are in the simulation, keeps their
    /// parameters current, keeps motor-driven bodies awake, and reports breaks (<see cref="JointBroken"/> tag +
    /// <see cref="JointBrokenEvent"/>). Runs between the physics sync and the step.
    /// </summary>
    [UpdateAfter(typeof(PhysicsSyncSystem)), UpdateBefore(typeof(PhysicsStepSystem))]
    public sealed class JointSystem : SystemBase, IFixedTick, IOnEntityDestroyed, IOnSimEnd
    {
        private sealed class Rec { public Constraint C; public SolverBody A, B; public Entity Connected; }

        private readonly Dictionary<Entity, Rec> _recs = new Dictionary<Entity, Rec>();
        private readonly List<Entity> _scratch = new List<Entity>();
        private readonly HashSet<Entity> _warned = new HashSet<Entity>();
        private Query _joints;
        private PhysicsSyncSystem _sync;

        public int ActiveJoints => _recs.Count;

        public override void OnCreate() => _joints = World.Query().With<Joint>().WithReadOnly<PhysicsHandle>().Without<JointBroken>().Build();

        public void FixedTick(SystemContext ctx)
        {
            _sync ??= World.GetSystem<PhysicsSyncSystem>();
            if (_sync == null) return;
            _scratch.Clear();
            foreach (var chunk in _joints)
            {
                var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++) if (chunk.Includes(i)) _scratch.Add(ents[i]);
            }
            foreach (var e in _scratch)
            {
                ref var j = ref World.Get<Joint>(e);
                if (!_recs.TryGetValue(e, out var rec))
                {
                    rec = TryCreate(e, ref j);
                    if (rec == null) continue;
                    _recs[e] = rec;
                }
                if (rec.C.broken || (rec.B != null && !rec.B.ecsAlive) || !rec.A.ecsAlive || rec.Connected != j.Connected)
                {
                    bool reconnect = rec.Connected != j.Connected && !rec.C.broken;
                    Drop(e, rec);
                    if (reconnect) continue; // re-created next step against the new body
                    ctx.Commands.Add(e, new JointBroken());
                    ctx.Post(new JointBrokenEvent { Entity = e, Connected = rec.Connected, Force = rec.C.lastForce, Torque = rec.C.lastTorque });
                    continue;
                }
                Apply(rec.C, j);
                if (j.UseMotor && j.MotorSpeed != 0f) { rec.A.WakeUp(); rec.B?.WakeUp(); } // a running motor must not sleep against an obstacle
            }
        }

        private Rec TryCreate(Entity e, ref Joint j)
        {
            var a = _sync.BodyOf(e);
            if (a == null)
            {
                if (_warned.Add(e)) Debug.LogWarning($"[ecs joints] {e}: a Joint needs a Body on the same entity");
                return null;
            }
            SolverBody b = null;
            if (!j.Connected.IsNull)
            {
                if (!World.IsAlive(j.Connected)) { if (_warned.Add(e)) Debug.LogWarning($"[ecs joints] {e}: connected {j.Connected} does not exist"); return null; }
                b = _sync.BodyOf(j.Connected);
                if (b == null) return null; // registers next step
            }
            Vector3 scaleA = World.Has<LocalTransform>(e) ? World.Get<LocalTransform>(e).Scale : Vector3.one;
            Vector3 wa = BodyPoint(a, scaleA, j.Anchor);
            Vector3 waxis = (a.rotation * j.Axis).normalized;
            if (j.AutoConnectedAnchor)
            {
                j.ConnectedAnchor = b != null ? Quaternion.Inverse(b.rotation) * (wa - b.position) + b.comOffsetLocal : wa;
                if (b != null && World.Has<LocalTransform>(j.Connected)) j.ConnectedAnchor = Unscale(j.ConnectedAnchor, World.Get<LocalTransform>(j.Connected).Scale);
                j.AutoConnectedAnchor = false;
            }
            Vector3 wb = b != null ? BodyPoint(b, World.Has<LocalTransform>(j.Connected) ? World.Get<LocalTransform>(j.Connected).Scale : Vector3.one, j.ConnectedAnchor) : j.ConnectedAnchor;
            Constraint c;
            switch (j.Kind)
            {
                case JointKind.Fixed: c = new FixedConstraint(a, b, wa); break;
                case JointKind.Hinge: c = new HingeConstraint(a, b, wa, waxis); break;
                case JointKind.Slider: c = new SliderConstraint(a, b, wa, waxis); break;
                case JointKind.Distance:
                    c = new DistanceConstraint(a, b, wa, wb);
                    if (j.AutoDistance) { float d = (wb - wa).magnitude; j.MinDistance = j.Rope ? 0f : d; j.MaxDistance = d; j.AutoDistance = false; }
                    break;
                case JointKind.Spring:
                    c = new SpringConstraint(a, b, wa, wb, j.Stiffness, j.Damping);
                    if (j.RestLength < 0f) j.RestLength = (wb - wa).magnitude;
                    break;
                default:
                {
                    var ball = new BallConstraint(a, b, wa);
                    ball.localAnchorB = b != null ? Quaternion.Inverse(b.rotation) * (wb - b.position) : wb;
                    c = ball;
                    break;
                }
            }
            c.collideConnected = j.CollideConnected;
            Apply(c, j);
            PhysicsApi.World.AddConstraint(c);
            a.WakeUp(); b?.WakeUp();
            return new Rec { C = c, A = a, B = b, Connected = j.Connected };
        }

        /// <summary>World position of a point given in an entity's local space (scaled), from the solver pose.</summary>
        private static Vector3 BodyPoint(SolverBody b, Vector3 scale, Vector3 local) =>
            b.position + b.rotation * (Vector3.Scale(local, scale) - b.comOffsetLocal);

        private static Vector3 Unscale(Vector3 v, Vector3 s) =>
            new Vector3(s.x != 0f ? v.x / s.x : v.x, s.y != 0f ? v.y / s.y : v.y, s.z != 0f ? v.z / s.z : v.z);

        private static void Apply(Constraint c, in Joint j)
        {
            c.breakForce = j.BreakForce > 0f ? j.BreakForce : float.PositiveInfinity;
            c.breakTorque = j.BreakTorque > 0f ? j.BreakTorque : float.PositiveInfinity;
            c.hertz = j.Hertz > 0f ? j.Hertz : Constraint.DefaultHertz;
            c.dampingRatio = j.DampingRatio > 0f ? j.DampingRatio : Constraint.DefaultDampingRatio;
            switch (c)
            {
                case HingeConstraint h:
                    h.useLimits = j.UseLimits; h.lowerLimit = j.LowerLimit; h.upperLimit = j.UpperLimit;
                    h.useMotor = j.UseMotor; h.motorSpeed = j.MotorSpeed * Mathf.Deg2Rad; h.maxMotorTorque = j.MotorForce; h.motorFreeSpin = j.MotorFreeSpin;
                    h.useSpring = j.UseSpring; h.springStiffness = j.SpringStiffness; h.springDamping = j.SpringDamping; h.springTargetAngle = j.SpringTarget;
                    break;
                case SliderConstraint s:
                    s.useLimits = j.UseLimits; s.lowerLimit = j.LowerLimit; s.upperLimit = j.UpperLimit;
                    s.useMotor = j.UseMotor; s.motorSpeed = j.MotorSpeed; s.maxMotorForce = j.MotorForce; s.motorFreeSpin = j.MotorFreeSpin;
                    s.useSpring = j.UseSpring; s.springStiffness = j.SpringStiffness; s.springDamping = j.SpringDamping; s.springTargetPosition = j.SpringTarget;
                    break;
                case SpringConstraint sp:
                    sp.stiffness = MathF.Max(j.Stiffness, 0f); sp.damping = MathF.Max(j.Damping, 0f); sp.restLength = MathF.Max(j.RestLength, 0f);
                    sp.minLength = MathF.Max(j.MinDistance, 0f); sp.maxLength = j.MaxDistance > 0f ? j.MaxDistance : float.PositiveInfinity;
                    break;
                case DistanceConstraint d:
                    d.minLength = j.Rope ? 0f : MathF.Min(j.MinDistance, j.MaxDistance); d.maxLength = MathF.Max(j.MinDistance, j.MaxDistance);
                    d.stiffness = 0f;
                    break;
            }
        }

        private void Drop(Entity e, Rec rec)
        {
            PhysicsApi.World.RemoveConstraint(rec.C);
            _recs.Remove(e);
        }

        public void OnEntityDestroyed(SystemContext ctx, Entity e)
        {
            if (_recs.TryGetValue(e, out var rec)) Drop(e, rec);
            _warned.Remove(e);
        }

        public void OnSimEnd(SystemContext ctx)
        {
            foreach (var kv in _recs) PhysicsApi.World.RemoveConstraint(kv.Value.C);
            _recs.Clear(); _warned.Clear();
        }
    }
}
