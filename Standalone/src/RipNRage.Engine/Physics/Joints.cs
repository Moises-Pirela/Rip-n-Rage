using System;

namespace RipNRage.Engine
{
    /// <summary>Angle limits of a <see cref="HingeJoint"/> (degrees) or travel limits of a <see cref="SliderJoint"/> (metres).</summary>
    [Serializable]
    public struct JointLimits
    {
        public float min, max;
        public JointLimits(float min, float max) { this.min = min; this.max = max; }
    }

    /// <summary>Motor of a <see cref="HingeJoint"/> (target deg/s, max N·m) or <see cref="SliderJoint"/> (target m/s, max N).</summary>
    [Serializable]
    public struct JointMotor
    {
        /// <summary>Target velocity (degrees per second for hinges, metres per second for sliders).</summary>
        public float targetVelocity;
        /// <summary>Largest torque / force the motor applies to reach it.</summary>
        public float force;
        /// <summary>Free-spinning when the target is reached (true) or braking against external motion (false — the default, like Unity's motor).</summary>
        public bool freeSpin;
        public JointMotor(float targetVelocity, float force) { this.targetVelocity = targetVelocity; this.force = force; freeSpin = false; }
    }

    /// <summary>Spring of a <see cref="HingeJoint"/> (N·m/rad, N·m·s/rad, target degrees) or <see cref="SliderJoint"/> (N/m, N·s/m, target metres).</summary>
    [Serializable]
    public struct JointSpring
    {
        public float spring;
        public float damper;
        public float targetPosition;
        public JointSpring(float spring, float damper, float targetPosition = 0f) { this.spring = spring; this.damper = damper; this.targetPosition = targetPosition; }
    }

    /// <summary>
    /// A joint component (Unity's <c>Joint</c>): attaches the <see cref="RigidBody"/> on this object to
    /// <see cref="connectedBody"/> — or to the world when that is null — through a solver <see cref="Constraint"/>
    /// built from the component's fields when it is enabled. <see cref="anchor"/> and <see cref="axis"/> are in
    /// this object's local space; with <see cref="autoConfigureConnectedAnchor"/> the connected anchor is taken from
    /// the poses at that moment. The constraint breaks when it has to carry more than <see cref="breakForce"/> /
    /// <see cref="breakTorque"/> (<see cref="JointBroke"/>, then the component is disabled). Editable fields are
    /// pushed to the live constraint every fixed step, so limits, motors and springs can be changed at run time.
    /// </summary>
    public abstract class Joint : Behaviour
    {
        /// <summary>The other body; null = the world.</summary>
        public RigidBody connectedBody;
        /// <summary>Joint point in this object's local space.</summary>
        public Vector3 anchor;
        /// <summary>Joint axis in this object's local space (hinge / slider).</summary>
        public Vector3 axis = Vector3.right;
        /// <summary>Compute <see cref="connectedAnchor"/> from the poses when the joint is created.</summary>
        public bool autoConfigureConnectedAnchor = true;
        /// <summary>Joint point on the connected body (its local space) or in the world when there is none.</summary>
        public Vector3 connectedAnchor;
        /// <summary>Force (N) / torque (N·m) that breaks the joint; infinity = unbreakable.</summary>
        public float breakForce = float.PositiveInfinity;
        public float breakTorque = float.PositiveInfinity;
        /// <summary>Let the two bodies collide with each other (off by default, like Unity).</summary>
        public bool enableCollision = false;
        /// <summary>Soft-mode stiffness / damping of the joint's rigid rows; 0 = the solver defaults (tunables.cfg).</summary>
        public float hertz = 0f, dampingRatio = 0f;

        /// <summary>The solver constraint (null while the component is disabled or before the body exists).</summary>
        public Constraint constraint { get; private set; }
        /// <summary>The rigid body on this object.</summary>
        public RigidBody body { get; private set; }
        public bool broken { get; private set; }
        /// <summary>Raised once when the joint breaks (Unity's OnJointBreak).</summary>
        public event Action<Joint> JointBroke;
        /// <summary>Reaction force / torque of the last step.</summary>
        public float currentForce => constraint != null ? constraint.lastForce : 0f;
        public float currentTorque => constraint != null ? constraint.lastTorque : 0f;

        public Vector3 worldAnchor => body != null ? BodyPoint(body, anchor) : transform.TransformPoint(anchor);
        public Vector3 worldAxis => (body != null ? body.body.rotation * axis : transform.TransformDirection(axis)).normalized;
        public Vector3 connectedWorldAnchor => connectedBody != null ? BodyPoint(connectedBody, connectedAnchor) : connectedAnchor;

        /// <summary>World position of a point given in a body's transform space (the body pose is authoritative, the transform may be interpolated).</summary>
        private static Vector3 BodyPoint(RigidBody b, Vector3 local)
        {
            Vector3 scaled = Vector3.Scale(local, b.transform.lossyScale);
            return b.body.position + b.body.rotation * (scaled - b.body.comOffsetLocal);
        }

        protected override void OnEnable() { Attach(); }
        protected override void OnDisable() { Detach(); }
        protected override void OnDestroy() { Detach(); }

        /// <summary>Destroys and recreates the constraint from the current fields and poses (re-anchors it).</summary>
        public void Rebuild() { Detach(); if (isActiveAndEnabled) Attach(); }

        private void Attach()
        {
            if (constraint != null || broken) return;
            body = GetComponent<RigidBody>();
            if (body == null) { Debug.LogWarning($"{GetType().Name} on '{name}' needs a RigidBody on the same object"); return; }
            body.EnsureAwake();
            connectedBody?.EnsureAwake();
            Vector3 wa = worldAnchor;
            if (autoConfigureConnectedAnchor)
                connectedAnchor = connectedBody != null ? Quaternion.Inverse(connectedBody.body.rotation) * (wa - connectedBody.body.position) + connectedBody.body.comOffsetLocal : wa;
            constraint = Create(body, connectedBody, wa, connectedWorldAnchor, worldAxis);
            if (constraint == null) return;
            constraint.collideConnected = enableCollision;
            Sync(constraint);
            constraint.Broke += OnConstraintBroke;
            Physics.World.AddConstraint(constraint);
            body.WakeUp(); connectedBody?.WakeUp();
        }

        private void Detach()
        {
            if (constraint == null) return;
            constraint.Broke -= OnConstraintBroke;
            Physics.World.RemoveConstraint(constraint);
            constraint = null;
        }

        private void OnConstraintBroke(Constraint c)
        {
            if (c != constraint) return;
            broken = true;
            Detach();
            try { JointBroke?.Invoke(this); }
            catch (Exception e) { Debug.LogError("JointBroke handler threw: " + e); }
            OnJointBreak();
            enabled = false;
        }

        /// <summary>Breaks the joint now.</summary>
        public void Break() { constraint?.Break(); }

        /// <summary>Override for Unity-style OnJointBreak handling.</summary>
        protected virtual void OnJointBreak() { }

        protected override void FixedUpdate()
        {
            if (constraint == null) return;
            if (connectedBody != null && connectedBody.destroyed) { Detach(); return; } // the world dropped it with the body
            Sync(constraint);
            if (KeepsAwake) { body.WakeUp(); connectedBody?.WakeUp(); } // a running motor must not let its body fall asleep against an obstacle
        }

        /// <summary>True while the joint actively drives its bodies (motors), so they never sleep.</summary>
        protected virtual bool KeepsAwake => false;

        /// <summary>Builds the constraint for the current poses (<paramref name="worldAnchor"/> on this body, <paramref name="connectedWorldAnchor"/> on the other / in the world).</summary>
        protected abstract Constraint Create(RigidBody body, RigidBody other, Vector3 worldAnchor, Vector3 connectedWorldAnchor, Vector3 worldAxis);

        /// <summary>Copies the editable fields into the live constraint (called on attach and every fixed step).</summary>
        protected virtual void Sync(Constraint c)
        {
            c.breakForce = breakForce; c.breakTorque = breakTorque;
            c.hertz = hertz > 0f ? hertz : Constraint.DefaultHertz;
            c.dampingRatio = dampingRatio > 0f ? dampingRatio : Constraint.DefaultDampingRatio;
        }
    }

    /// <summary>Ball-and-socket joint: the anchors stay together, rotation is free (chains, ragdolls, hanging signs).</summary>
    public sealed class BallJoint : Joint
    {
        public BallConstraint ball => constraint as BallConstraint;

        protected override Constraint Create(RigidBody body, RigidBody other, Vector3 worldAnchor, Vector3 connectedWorldAnchor, Vector3 worldAxis)
        {
            var c = new BallConstraint(body, other, worldAnchor);
            if (!autoConfigureConnectedAnchor) c.localAnchorB = other != null ? Quaternion.Inverse(other.body.rotation) * (connectedWorldAnchor - other.body.position) : connectedWorldAnchor;
            return c;
        }
    }

    /// <summary>Weld to the connected body / the world; breakable (signs, planks, glued props).</summary>
    public sealed class FixedJoint : Joint
    {
        protected override Constraint Create(RigidBody body, RigidBody other, Vector3 worldAnchor, Vector3 connectedWorldAnchor, Vector3 worldAxis) =>
            new FixedConstraint(body, other, worldAnchor);
    }

    /// <summary>
    /// Hinge (Unity's HingeJoint): rotation about <see cref="Joint.axis"/> through <see cref="Joint.anchor"/>, with
    /// optional <see cref="limits"/> (degrees), <see cref="motor"/> (deg/s, N·m) and <see cref="spring"/>
    /// (N·m/rad, N·m·s/rad, target degrees). Doors, levers, wheels, seesaws, trapdoors.
    /// </summary>
    public sealed class HingeJoint : Joint
    {
        public bool useLimits;
        public JointLimits limits = new JointLimits(-90f, 90f);
        public bool useMotor;
        public JointMotor motor = new JointMotor(0f, 10f);
        public bool useSpring;
        public JointSpring spring = new JointSpring(10f, 1f, 0f);

        public HingeConstraint hinge => constraint as HingeConstraint;
        protected override bool KeepsAwake => useMotor && motor.targetVelocity != 0f;
        /// <summary>Current angle in degrees (0 where the joint was created).</summary>
        public float angle => hinge != null ? hinge.angle : 0f;
        /// <summary>Angular speed in degrees per second.</summary>
        public float velocity => hinge != null ? hinge.angularSpeed * Mathf.Rad2Deg : 0f;

        protected override Constraint Create(RigidBody body, RigidBody other, Vector3 worldAnchor, Vector3 connectedWorldAnchor, Vector3 worldAxis) =>
            new HingeConstraint(body, other, worldAnchor, worldAxis);

        protected override void Sync(Constraint c)
        {
            base.Sync(c);
            var h = (HingeConstraint)c;
            h.useLimits = useLimits; h.lowerLimit = limits.min; h.upperLimit = limits.max;
            h.useMotor = useMotor; h.motorSpeed = motor.targetVelocity * Mathf.Deg2Rad; h.maxMotorTorque = motor.force; h.motorFreeSpin = motor.freeSpin;
            h.useSpring = useSpring; h.springStiffness = spring.spring; h.springDamping = spring.damper; h.springTargetAngle = spring.targetPosition;
        }
    }

    /// <summary>
    /// Slider / prismatic joint: translation along <see cref="Joint.axis"/> through <see cref="Joint.anchor"/>, with
    /// optional <see cref="limits"/> (metres), <see cref="motor"/> (m/s, N) and <see cref="spring"/> (N/m, N·s/m,
    /// target metres). Drawers, pistons, lifts, sliding doors.
    /// </summary>
    public sealed class SliderJoint : Joint
    {
        public bool useLimits;
        public JointLimits limits = new JointLimits(-1f, 1f);
        public bool useMotor;
        public JointMotor motor = new JointMotor(0f, 100f);
        public bool useSpring;
        public JointSpring spring = new JointSpring(100f, 10f, 0f);

        public SliderConstraint slider => constraint as SliderConstraint;
        protected override bool KeepsAwake => useMotor && motor.targetVelocity != 0f;
        /// <summary>Travel along the axis from where the joint was created (m).</summary>
        public float position => slider != null ? slider.position : 0f;
        public float velocity => slider != null ? slider.speed : 0f;

        protected override Constraint Create(RigidBody body, RigidBody other, Vector3 worldAnchor, Vector3 connectedWorldAnchor, Vector3 worldAxis) =>
            new SliderConstraint(body, other, worldAnchor, worldAxis);

        protected override void Sync(Constraint c)
        {
            base.Sync(c);
            var s = (SliderConstraint)c;
            s.useLimits = useLimits; s.lowerLimit = limits.min; s.upperLimit = limits.max;
            s.useMotor = useMotor; s.motorSpeed = motor.targetVelocity; s.maxMotorForce = motor.force; s.motorFreeSpin = motor.freeSpin;
            s.useSpring = useSpring; s.springStiffness = spring.spring; s.springDamping = spring.damper; s.springTargetPosition = spring.targetPosition;
        }
    }

    /// <summary>
    /// Distance joint: keeps the anchors between <see cref="minDistance"/> and <see cref="maxDistance"/> apart —
    /// a rigid rod when both are equal (or <see cref="autoConfigureDistance"/> takes the creation distance), a rope
    /// with min 0. Pendulums, chains, hanging cages, rope bridges.
    /// </summary>
    public sealed class DistanceJoint : Joint
    {
        /// <summary>Take both distances from the anchors' distance when the joint is created (a rod).</summary>
        public bool autoConfigureDistance = true;
        public float minDistance = 0f, maxDistance = 1f;
        /// <summary>Rope: only the maximum distance is enforced.</summary>
        public bool rope = false;

        public DistanceConstraint distance => constraint as DistanceConstraint;
        public float currentDistance => distance != null ? distance.currentLength : (connectedWorldAnchor - worldAnchor).magnitude;

        protected override Constraint Create(RigidBody body, RigidBody other, Vector3 worldAnchor, Vector3 connectedWorldAnchor, Vector3 worldAxis)
        {
            var c = new DistanceConstraint(body, other, worldAnchor, connectedWorldAnchor);
            if (autoConfigureDistance) { float d = (connectedWorldAnchor - worldAnchor).magnitude; minDistance = rope ? 0f : d; maxDistance = d; }
            return c;
        }

        protected override void Sync(Constraint c)
        {
            base.Sync(c);
            var d = (DistanceConstraint)c;
            d.minLength = rope ? 0f : MathF.Min(minDistance, maxDistance); d.maxLength = MathF.Max(minDistance, maxDistance);
            d.stiffness = 0f;
        }
    }

    /// <summary>
    /// Spring joint (Unity's SpringJoint): a spring of <see cref="spring"/> N/m and <see cref="damper"/> N·s/m
    /// pulls the anchors to <see cref="restDistance"/> (the creation distance with <see cref="autoConfigureDistance"/>);
    /// <see cref="minDistance"/> / <see cref="maxDistance"/> are optional hard stops (0 / infinity = none).
    /// Hanging lamps, bungee cords, bouncy platforms, suspension.
    /// </summary>
    public sealed class SpringJoint : Joint
    {
        public float spring = 50f;
        public float damper = 2f;
        public bool autoConfigureDistance = true;
        public float restDistance = 1f;
        public float minDistance = 0f;
        public float maxDistance = float.PositiveInfinity;

        public SpringConstraint springConstraint => constraint as SpringConstraint;
        public float currentDistance => springConstraint != null ? springConstraint.currentLength : (connectedWorldAnchor - worldAnchor).magnitude;

        protected override Constraint Create(RigidBody body, RigidBody other, Vector3 worldAnchor, Vector3 connectedWorldAnchor, Vector3 worldAxis)
        {
            var c = new SpringConstraint(body, other, worldAnchor, connectedWorldAnchor, spring, damper);
            if (autoConfigureDistance) restDistance = (connectedWorldAnchor - worldAnchor).magnitude;
            return c;
        }

        protected override void Sync(Constraint c)
        {
            base.Sync(c);
            var s = (SpringConstraint)c;
            s.stiffness = MathF.Max(spring, 0f); s.damping = MathF.Max(damper, 0f); s.restLength = MathF.Max(restDistance, 0f);
            s.minLength = MathF.Max(minDistance, 0f); s.maxLength = maxDistance > 0f ? maxDistance : float.PositiveInfinity;
        }
    }
}
