using System;

namespace RipNRage.Engine
{
    /// <summary>How penetration / joint error is corrected.</summary>
    public enum PositionCorrectionMode
    {
        /// <summary>Soft constraints: a stiff spring-damper in the velocity solve (Catto's soft step). Stable, no energy gain.</summary>
        Soft,
        /// <summary>Baumgarte stabilisation: a velocity bias proportional to the error (classic sequential impulses).</summary>
        Baumgarte,
        /// <summary>Non-linear Gauss-Seidel: the poses are corrected directly after the velocity solve (no velocity is added).</summary>
        NGS,
    }

    /// <summary>Per-step numbers the constraints are solved with (filled by the solver from its settings).</summary>
    public struct SolverParams
    {
        public float h;                         // substep length
        public float invH;
        public PositionCorrectionMode mode;
        public float baumgarte;                 // error fraction corrected per substep (Baumgarte / NGS)
        public float linearSlop, angularSlop;   // tolerated error
        public float maxLinearCorrection;       // per NGS pass
        public float maxBiasVelocity;
        public bool warmStart;
    }

    /// <summary>Soft-constraint coefficients (Catto): a spring-damper solved in impulse form.</summary>
    internal struct Softness
    {
        public float biasRate, massScale, impulseScale;

        /// <summary>From a frequency (Hz, capped at a quarter of the substep rate) and a damping ratio.</summary>
        public static Softness FromFrequency(float hertz, float dampingRatio, float h)
        {
            float omega = 2f * MathF.PI * MathF.Min(MathF.Max(hertz, 0.1f), 0.25f / h);
            float a1 = 2f * dampingRatio + h * omega;
            float a2 = h * omega * a1;
            return new Softness { biasRate = omega / a1, massScale = a2 / (1f + a2), impulseScale = 1f / (1f + a2) };
        }

        /// <summary>
        /// From a physical spring: stiffness <paramref name="k"/> (N/m or N·m/rad), damping <paramref name="c"/>
        /// (N·s/m or N·m·s/rad) and the row's effective mass <paramref name="m"/> (the same formulas with
        /// ω = √(k/m), ζ = c / (2√(km)); no frequency cap, a stiff spring just becomes rigid).
        /// </summary>
        public static Softness FromSpring(float k, float c, float m, float h)
        {
            if (m <= 0f || k + c <= 0f) return Rigid;
            float a2 = h * (c + h * k) / m;
            float denom = c + h * k;
            return new Softness { biasRate = denom > 0f ? k / denom : 0f, massScale = a2 / (1f + a2), impulseScale = 1f / (1f + a2) };
        }

        public static Softness Rigid => new Softness { biasRate = 0f, massScale = 1f, impulseScale = 0f };
    }

    /// <summary>
    /// A constraint between two rigid bodies (or a body and the world). Joints derive from it; the solver calls the
    /// phases in order every substep: <see cref="Prepare"/> once per step, then per substep <see cref="WarmStart"/>,
    /// <see cref="SolveVelocity"/> (with bias), the position integration, <see cref="SolveVelocity"/> again without
    /// bias (relax) and, in NGS mode, <see cref="SolvePosition"/>; <see cref="Finish"/> closes the step (breaking).
    /// Accumulated impulses persist across steps for warm starting unless the solver runs cold.
    ///
    /// Conventions shared by every row: the linear error is <c>pB − pA</c> and <see cref="ApplyImpulse"/> puts
    /// +P on B and −P on A, so <c>impulse = −m·(Δv + bias)</c> pulls the anchors together; axis rows that describe
    /// A's motion relative to B (hinge angle, slider position) negate accordingly and say so.
    /// </summary>
    public abstract class Constraint
    {
        public SolverBody bodyA;
        /// <summary>Null = the world.</summary>
        public SolverBody bodyB;
        public bool enabled = true;
        /// <summary>Force (N) the constraint can transmit before it breaks; infinity = unbreakable.</summary>
        public float breakForce = float.PositiveInfinity;
        /// <summary>Torque (N·m) the constraint can transmit before it breaks (constraints with angular rows); infinity = unbreakable.</summary>
        public float breakTorque = float.PositiveInfinity;
        /// <summary>Impulse (N·s, per substep) above which the constraint breaks; 0 = not used. <see cref="breakForce"/> is the usual knob.</summary>
        public float breakImpulse = 0f;
        /// <summary>Stiffness (cycles per second, capped at a quarter of the substep rate) and damping ratio of the rigid rows in Soft mode.</summary>
        public float hertz = DefaultHertz;
        public float dampingRatio = DefaultDampingRatio;
        /// <summary>Defaults of new constraints (tunables.cfg [solver]).</summary>
        public static float DefaultHertz = 60f, DefaultDampingRatio = 2f;
        /// <summary>Whether the two bodies may still collide with each other (off for ragdoll limbs that overlap at the joint).</summary>
        public bool collideConnected = false;
        public bool broken { get; internal set; }
        public event Action<Constraint> Broke;
        /// <summary>Largest linear impulse transmitted in one substep of the last step (N·s).</summary>
        public float lastImpulse { get; internal set; }
        /// <summary>Largest angular impulse transmitted in one substep of the last step (N·m·s).</summary>
        public float lastAngularImpulse { get; internal set; }
        /// <summary>Reaction force of the last step (N): <see cref="lastImpulse"/> over the substep.</summary>
        public float lastForce { get; internal set; }
        /// <summary>Reaction torque of the last step (N·m).</summary>
        public float lastTorque { get; internal set; }

        internal Softness soft = Softness.Rigid;
        internal float peakImpulse, peakAngularImpulse;

        public Vector3 worldAnchorA => bodyA != null ? bodyA.position + bodyA.rotation * LocalAnchorA : LocalAnchorA;
        public Vector3 worldAnchorB => bodyB != null ? bodyB.position + bodyB.rotation * LocalAnchorB : LocalAnchorB;
        protected virtual Vector3 LocalAnchorA => Vector3.zero;
        protected virtual Vector3 LocalAnchorB => Vector3.zero;

        // ---- phases ----
        internal abstract bool Prepare(in SolverParams p);
        internal abstract void WarmStart();
        internal abstract void SolveVelocity(bool useBias, in SolverParams p);
        internal abstract void SolvePosition(in SolverParams p);

        internal virtual void Finish(float h)
        {
            lastImpulse = peakImpulse; lastAngularImpulse = peakAngularImpulse;
            lastForce = h > 0f ? peakImpulse / h : 0f; lastTorque = h > 0f ? peakAngularImpulse / h : 0f;
            peakImpulse = 0f; peakAngularImpulse = 0f;
            if ((breakImpulse > 0f && lastImpulse > breakImpulse) || lastForce > breakForce || lastTorque > breakTorque) Break();
        }

        /// <summary>Breaks the constraint now (disabled, <see cref="Broke"/> raised, removed from the world by the solver).</summary>
        public void Break()
        {
            if (broken) return;
            broken = true; enabled = false;
            ClearImpulses();
            try { Broke?.Invoke(this); }
            catch (Exception e) { Debug.LogError("Constraint.Broke handler threw: " + e); }
        }

        /// <summary>Forgets the accumulated impulses (warm start restarts from zero).</summary>
        public virtual void ClearImpulses() { }

        internal void TrackPeak(float magnitude) { if (magnitude > peakImpulse) peakImpulse = magnitude; }
        internal void TrackPeakAngular(float magnitude) { if (magnitude > peakAngularImpulse) peakAngularImpulse = magnitude; }

        // ---- shared helpers ----

        internal static bool Dynamic(SolverBody b) => b != null && !b.isKinematic && !b.sleeping && b.live;

        /// <summary>Both bodies usable for this step (one of them must be dynamic; a moving body wakes its partner).</summary>
        internal bool BodiesReady()
        {
            if (!enabled || broken || bodyA == null || !bodyA.live || (bodyB != null && !bodyB.live)) return false;
            
            if (bodyA.sleeping && bodyB != null && Dynamic(bodyB)) bodyA.WakeUp();
            if (bodyB != null && bodyB.sleeping && Dynamic(bodyA)) bodyB.WakeUp();
            return Dynamic(bodyA) || Dynamic(bodyB);
        }

        /// <summary>Wakes both bodies (a motor or spring target changed, a limit was edited...).</summary>
        public void WakeBodies() { bodyA?.WakeUp(); bodyB?.WakeUp(); }

        /// <summary>Bias velocity and scales for a scalar error (sign convention: <c>impulse = −m·(cdot + bias)</c>).</summary>
        internal void BiasFor(in SolverParams p, float error, bool useBias, out float bias, out float ms, out float isc)
        {
            if (!useBias) { bias = 0f; ms = 1f; isc = 0f; return; }
            switch (p.mode)
            {
                case PositionCorrectionMode.Soft: bias = error * soft.biasRate; ms = soft.massScale; isc = soft.impulseScale; break;
                case PositionCorrectionMode.Baumgarte: bias = Mathf.Clamp(error * p.baumgarte * p.invH, -p.maxBiasVelocity, p.maxBiasVelocity); ms = 1f; isc = 0f; break;
                default: bias = 0f; ms = 1f; isc = 0f; break; // NGS corrects positions separately
            }
        }

        /// <summary>Vector form of <see cref="BiasFor"/>.</summary>
        internal void BiasFor(in SolverParams p, Vector3 error, bool useBias, out Vector3 bias, out float ms, out float isc)
        {
            if (!useBias) { bias = Vector3.zero; ms = 1f; isc = 0f; return; }
            switch (p.mode)
            {
                case PositionCorrectionMode.Soft: bias = error * soft.biasRate; ms = soft.massScale; isc = soft.impulseScale; break;
                case PositionCorrectionMode.Baumgarte: bias = Vector3.ClampMagnitude(error * (p.baumgarte * p.invH), p.maxBiasVelocity); ms = 1f; isc = 0f; break;
                default: bias = Vector3.zero; ms = 1f; isc = 0f; break;
            }
        }

        /// <summary>Speculative / penalty bias of an inequality row: a gap may close this substep, an overlap is pushed out.</summary>
        internal void LimitBias(in SolverParams p, float c, bool useBias, out float bias, out float ms, out float isc)
        {
            if (c > 0f) { bias = c * p.invH; ms = 1f; isc = 0f; }
            else BiasFor(in p, c, useBias, out bias, out ms, out isc);
        }

        internal static Vector3 PointVelocity(SolverBody b, Vector3 r) => b != null && (Dynamic(b) || b.isKinematic) ? b.linearVelocity + Vector3.Cross(b.angularVelocity, r) : Vector3.zero;
        internal static Vector3 AngularVelocity(SolverBody b) => b != null && (Dynamic(b) || b.isKinematic) ? b.angularVelocity : Vector3.zero;

        /// <summary>Applies an impulse at the anchors: +P on B, −P on A.</summary>
        internal static void ApplyImpulse(SolverBody a, SolverBody b, Vector3 rA, Vector3 rB, Vector3 impulse)
        {
            if (Dynamic(a)) { a.linearVelocity -= impulse * a.inverseMass; a.angularVelocity -= a.ApplyInverseInertia(Vector3.Cross(rA, impulse)); }
            if (Dynamic(b)) { b.linearVelocity += impulse * b.inverseMass; b.angularVelocity += b.ApplyInverseInertia(Vector3.Cross(rB, impulse)); }
        }

        /// <summary>Applies an angular impulse: +L on B, −L on A.</summary>
        internal static void ApplyAngularImpulse(SolverBody a, SolverBody b, Vector3 angularImpulse)
        {
            if (Dynamic(a)) a.angularVelocity -= a.ApplyInverseInertia(angularImpulse);
            if (Dynamic(b)) b.angularVelocity += b.ApplyInverseInertia(angularImpulse);
        }

        /// <summary>Effective mass matrix of the anchor pair (how the relative anchor velocity responds to an impulse).</summary>
        internal static Matrix3x3 PointMass(SolverBody a, SolverBody b, Vector3 rA, Vector3 rB)
        {
            bool da = Dynamic(a), db = Dynamic(b);
            Vector3 c0 = Vector3.zero, c1 = Vector3.zero, c2 = Vector3.zero;
            for (int k = 0; k < 3; k++)
            {
                Vector3 e = Vector3.zero; e[k] = 1f;
                Vector3 col = Vector3.zero;
                if (da) col += e * a.inverseMass + Vector3.Cross(a.ApplyInverseInertia(Vector3.Cross(rA, e)), rA);
                if (db) col += e * b.inverseMass + Vector3.Cross(b.ApplyInverseInertia(Vector3.Cross(rB, e)), rB);
                if (k == 0) c0 = col; else if (k == 1) c1 = col; else c2 = col;
            }
            return Matrix3x3.FromColumns(c0, c1, c2);
        }

        /// <summary>Effective angular mass matrix (the sum of the world inverse inertias).</summary>
        internal static Matrix3x3 AngularMass(SolverBody a, SolverBody b)
        {
            Matrix3x3 k = new Matrix3x3();
            if (Dynamic(a)) k = k + a.InverseInertiaWorld();
            if (Dynamic(b)) k = k + b.InverseInertiaWorld();
            return k;
        }

        /// <summary>Effective mass of a single angular row about <paramref name="axis"/>.</summary>
        internal static float AngularRowMass(SolverBody a, SolverBody b, Vector3 axis)
        {
            float k = 0f;
            if (Dynamic(a)) k += Vector3.Dot(axis, a.ApplyInverseInertia(axis));
            if (Dynamic(b)) k += Vector3.Dot(axis, b.ApplyInverseInertia(axis));
            return k > 0f ? 1f / k : 0f;
        }

        /// <summary>Effective mass of a single linear row along <paramref name="axis"/> at the anchors.</summary>
        internal static float LinearRowMass(SolverBody a, SolverBody b, Vector3 rA, Vector3 rB, Vector3 axis)
        {
            float k = 0f;
            if (Dynamic(a)) { Vector3 rn = Vector3.Cross(rA, axis); k += a.inverseMass + Vector3.Dot(rn, a.ApplyInverseInertia(rn)); }
            if (Dynamic(b)) { Vector3 rn = Vector3.Cross(rB, axis); k += b.inverseMass + Vector3.Dot(rn, b.ApplyInverseInertia(rn)); }
            return k > 0f ? 1f / k : 0f;
        }

        /// <summary>Position-level correction at the anchors (NGS): +P moves B, −P moves A.</summary>
        internal static void ApplyPositionImpulse(SolverBody a, SolverBody b, Vector3 rA, Vector3 rB, Vector3 impulse)
        {
            if (Dynamic(a)) a.ApplyPositionImpulse(-impulse, rA);
            if (Dynamic(b)) b.ApplyPositionImpulse(impulse, rB);
        }

        internal static void ApplyPositionRotation(SolverBody a, SolverBody b, Vector3 angularImpulse)
        {
            if (Dynamic(a)) a.ApplyPositionRotation(-angularImpulse);
            if (Dynamic(b)) b.ApplyPositionRotation(angularImpulse);
        }

        /// <summary>Small-angle rotation vector taking <paramref name="from"/> onto <paramref name="to"/> (unit vectors).</summary>
        internal static Vector3 RotationError(Vector3 from, Vector3 to) => Vector3.Cross(from, to);

        /// <summary>Rotation vector (world) that takes B's orientation to <c>qA · reference</c> (positive = what B still needs).</summary>
        internal static Vector3 OrientationError(Quaternion qa, Quaternion qb, Quaternion referenceBinA)
        {
            Quaternion target = qa * referenceBinA;
            Quaternion rel = target * Quaternion.Inverse(qb);
            if (rel.w < 0f) rel = new Quaternion(-rel.x, -rel.y, -rel.z, -rel.w);
            return new Vector3(rel.x, rel.y, rel.z) * 2f;
        }

        /// <summary>Already moving faster than the motor target in its direction (a free-spinning motor then does nothing).</summary>
        internal static bool Overspeeding(float rate, float target) => target != 0f && MathF.Sign(rate) == MathF.Sign(target) && MathF.Abs(rate) > MathF.Abs(target);

        /// <summary>Perpendicular frame (u, v) of a unit axis.</summary>
        internal static void Frame(Vector3 axis, out Vector3 u, out Vector3 v)
        {
            u = Vector3.Cross(axis, MathF.Abs(axis.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
            v = Vector3.Cross(axis, u);
        }

        // ---- shared rows ----

        /// <summary>3-D point row keeping two anchors together (error = pB − pA); returns the accumulated impulse.</summary>
        internal Vector3 SolvePointRow(Vector3 rA, Vector3 rB, Vector3 error, ref Vector3 accumulated, bool useBias, in SolverParams p)
        {
            Vector3 dv = PointVelocity(bodyB, rB) - PointVelocity(bodyA, rA);
            BiasFor(in p, error, useBias, out Vector3 bias, out float ms, out float isc);
            Matrix3x3 k = PointMass(bodyA, bodyB, rA, rB).Inverse();
            Vector3 impulse = -(k.Multiply((dv + bias) * ms)) - accumulated * isc;
            accumulated += impulse;
            ApplyImpulse(bodyA, bodyB, rA, rB, impulse);
            return accumulated;
        }

        /// <summary>3-D angular row locking the relative orientation (<paramref name="errorBNeeds"/> = rotation B still needs).</summary>
        internal void SolveOrientationRow(Vector3 errorBNeeds, ref Vector3 accumulated, bool useBias, in SolverParams p)
        {
            Vector3 wRel = AngularVelocity(bodyB) - AngularVelocity(bodyA);
            BiasFor(in p, -errorBNeeds, useBias, out Vector3 bias, out float ms, out float isc);
            Matrix3x3 k = AngularMass(bodyA, bodyB).Inverse();
            Vector3 impulse = -(k.Multiply((wRel + bias) * ms)) - accumulated * isc;
            accumulated += impulse;
            ApplyAngularImpulse(bodyA, bodyB, impulse);
        }

        /// <summary>NGS point correction (error = pB − pA).</summary>
        internal void CorrectPoint(Vector3 rA, Vector3 rB, Vector3 error, in SolverParams p)
        {
            if (error.magnitude <= p.linearSlop) return;
            Vector3 c = Vector3.ClampMagnitude(error * p.baumgarte, p.maxLinearCorrection);
            Matrix3x3 k = PointMass(bodyA, bodyB, rA, rB).Inverse();
            ApplyPositionImpulse(bodyA, bodyB, rA, rB, -k.Multiply(c));
        }

        /// <summary>NGS orientation correction (<paramref name="errorBNeeds"/> = rotation B still needs).</summary>
        internal bool CorrectOrientation(Vector3 errorBNeeds, in SolverParams p)
        {
            if (errorBNeeds.magnitude <= p.angularSlop) return false;
            Matrix3x3 k = AngularMass(bodyA, bodyB).Inverse();
            ApplyPositionRotation(bodyA, bodyB, k.Multiply(errorBNeeds * p.baumgarte));
            return true;
        }
    }

    // ================================================================================================ ball

    /// <summary>
    /// Ball-and-socket: keeps a point of body A and a point of body B together while letting them rotate freely
    /// (a ragdoll's neck, shoulders and hips; a chain link). <see cref="Constraint.bodyB"/> may be null to pin A
    /// to a point in the world.
    /// </summary>
    public sealed class BallConstraint : Constraint
    {
        /// <summary>Anchor in A's body frame (relative to its centre of mass).</summary>
        public Vector3 localAnchorA;
        /// <summary>Anchor in B's body frame, or the world point when B is null.</summary>
        public Vector3 localAnchorB;

        internal Vector3 accumulated;
        internal Vector3 rA, rB;

        protected override Vector3 LocalAnchorA => localAnchorA;
        protected override Vector3 LocalAnchorB => localAnchorB;

        public BallConstraint() { }

        /// <summary>Joint between two bodies meeting at <paramref name="worldAnchor"/> (their current poses).</summary>
        public BallConstraint(SolverBody a, SolverBody b, Vector3 worldAnchor)
        {
            bodyA = a; bodyB = b;
            SetWorldAnchor(worldAnchor);
        }

        /// <summary>Re-anchors the joint at a world point using the bodies' current poses.</summary>
        public void SetWorldAnchor(Vector3 worldAnchor)
        {
            localAnchorA = bodyA != null ? Quaternion.Inverse(bodyA.rotation) * (worldAnchor - bodyA.position) : worldAnchor;
            localAnchorB = bodyB != null ? Quaternion.Inverse(bodyB.rotation) * (worldAnchor - bodyB.position) : worldAnchor;
        }

        public override void ClearImpulses() => accumulated = Vector3.zero;

        internal override bool Prepare(in SolverParams p)
        {
            if (!BodiesReady()) return false;
            soft = Softness.FromFrequency(hertz, dampingRatio, p.h);
            if (!p.warmStart) accumulated = Vector3.zero;
            return true;
        }

        private void Arms()
        {
            rA = bodyA.RotateToWorld(localAnchorA);
            rB = bodyB != null ? bodyB.RotateToWorld(localAnchorB) : Vector3.zero;
        }

        internal override void WarmStart()
        {
            Arms();
            if (accumulated.sqrMagnitude > 0f) ApplyImpulse(bodyA, bodyB, rA, rB, accumulated);
        }

        internal override void SolveVelocity(bool useBias, in SolverParams p)
        {
            Arms();
            Vector3 pA = bodyA.position + rA;
            Vector3 pB = bodyB != null ? bodyB.position + rB : localAnchorB;
            SolvePointRow(rA, rB, pB - pA, ref accumulated, useBias, in p);
            TrackPeak(accumulated.magnitude);
        }

        internal override void SolvePosition(in SolverParams p)
        {
            Arms();
            Vector3 pA = bodyA.position + rA;
            Vector3 pB = bodyB != null ? bodyB.position + rB : localAnchorB;
            CorrectPoint(rA, rB, pB - pA, in p);
        }
    }

    // ================================================================================================ distance / spring

    /// <summary>
    /// Keeps the distance between two anchors inside [<see cref="minLength"/>, <see cref="maxLength"/>] — a rod
    /// (min = max), a rope (min = 0) or a slack range — and, with <see cref="stiffness"/> &gt; 0, pulls it towards
    /// <see cref="restLength"/> with a physical spring (N/m) and damper (N·s/m) inside that range. B may be null
    /// (a point in the world). <see cref="SpringConstraint"/> is this with spring defaults.
    /// </summary>
    public class DistanceConstraint : Constraint
    {
        public Vector3 localAnchorA, localAnchorB;
        public float minLength = 0f, maxLength = 1f;
        /// <summary>Spring stiffness (N/m); 0 = no spring (rigid within the limits).</summary>
        public float stiffness = 0f;
        /// <summary>Spring damping (N·s/m).</summary>
        public float damping = 0f;
        /// <summary>Length the spring is at rest.</summary>
        public float restLength = 1f;

        private float _impulse, _lowerImpulse, _upperImpulse;
        private Vector3 _rA, _rB, _axis;
        private float _mass, _length;
        private Softness _spring;

        protected override Vector3 LocalAnchorA => localAnchorA;
        protected override Vector3 LocalAnchorB => localAnchorB;

        public DistanceConstraint() { }

        /// <summary>Rigid rod between two world points on the bodies at their current distance.</summary>
        public DistanceConstraint(SolverBody a, SolverBody b, Vector3 worldAnchorA, Vector3 worldAnchorB)
        {
            bodyA = a; bodyB = b;
            localAnchorA = a != null ? Quaternion.Inverse(a.rotation) * (worldAnchorA - a.position) : worldAnchorA;
            localAnchorB = b != null ? Quaternion.Inverse(b.rotation) * (worldAnchorB - b.position) : worldAnchorB;
            minLength = maxLength = restLength = (worldAnchorB - worldAnchorA).magnitude;
        }

        public float currentLength => (worldAnchorB - worldAnchorA).magnitude;
        public bool hasSpring => stiffness > 0f;
        public bool hasLower => minLength > 0f && (hasSpring || maxLength > minLength);
        public bool hasUpper => maxLength < float.PositiveInfinity && (hasSpring || maxLength > minLength);
        public bool isRod => !hasSpring && MathF.Abs(maxLength - minLength) < 1E-05f;

        /// <summary>Sets the spring from a frequency and damping ratio using the bodies' current effective mass.</summary>
        public void SetSpringFrequency(float hertz, float dampingRatio)
        {
            Geometry();
            // _mass is the row's effective mass: k = m ω², c = 2 ζ m ω
            float omega = 2f * MathF.PI * MathF.Max(hertz, 0.01f);
            stiffness = _mass * omega * omega;
            damping = 2f * dampingRatio * _mass * omega;
        }

        public override void ClearImpulses() { _impulse = _lowerImpulse = _upperImpulse = 0f; }

        internal override bool Prepare(in SolverParams p)
        {
            if (!BodiesReady()) return false;
            soft = Softness.FromFrequency(hertz, dampingRatio, p.h);
            Geometry();
            _spring = hasSpring ? Softness.FromSpring(stiffness, damping, _mass, p.h) : Softness.Rigid;
            if (!p.warmStart) ClearImpulses();
            return true;
        }

        private void Geometry()
        {
            _rA = bodyA.RotateToWorld(localAnchorA);
            _rB = bodyB != null ? bodyB.RotateToWorld(localAnchorB) : Vector3.zero;
            Vector3 pA = bodyA.position + _rA, pB = bodyB != null ? bodyB.position + _rB : localAnchorB;
            Vector3 d = pB - pA;
            _length = d.magnitude;
            _axis = _length > 1E-06f ? d / _length : Vector3.up;
            _mass = LinearRowMass(bodyA, bodyB, _rA, _rB, _axis);
        }

        private float AxialVelocity() => Vector3.Dot(PointVelocity(bodyB, _rB) - PointVelocity(bodyA, _rA), _axis);

        internal override void WarmStart()
        {
            Geometry();
            Vector3 P = _axis * (_impulse + _lowerImpulse - _upperImpulse);
            if (P.sqrMagnitude > 0f) ApplyImpulse(bodyA, bodyB, _rA, _rB, P);
        }

        internal override void SolveVelocity(bool useBias, in SolverParams p)
        {
            Geometry();
            // along +axis the anchors move apart; c > 0 = too long → negative impulse pulls them together
            if (hasSpring)
            {
                if (useBias) // springs are soft in the biased pass only (the relax pass would make them rigid)
                {
                    float c = _length - restLength;
                    float impulse = -_mass * _spring.massScale * (AxialVelocity() + c * _spring.biasRate) - _spring.impulseScale * _impulse;
                    _impulse += impulse;
                    ApplyImpulse(bodyA, bodyB, _rA, _rB, _axis * impulse);
                }
            }
            else if (isRod)
            {
                float c = _length - maxLength;
                BiasFor(in p, c, useBias, out float bias, out float ms, out float isc);
                float impulse = -_mass * ms * (AxialVelocity() + bias) - isc * _impulse;
                _impulse += impulse;
                ApplyImpulse(bodyA, bodyB, _rA, _rB, _axis * impulse);
            }
            if (hasLower)
            {
                float c = _length - minLength;       // < 0: too short → push apart (positive impulse)
                LimitBias(in p, c, useBias, out float bias, out float ms, out float isc);
                float impulse = -_mass * ms * (AxialVelocity() + bias) - isc * _lowerImpulse;
                float newImpulse = MathF.Max(_lowerImpulse + impulse, 0f);
                impulse = newImpulse - _lowerImpulse; _lowerImpulse = newImpulse;
                ApplyImpulse(bodyA, bodyB, _rA, _rB, _axis * impulse);
            }
            if (hasUpper)
            {
                float c = maxLength - _length;       // < 0: too long → pull together (row along −axis)
                LimitBias(in p, c, useBias, out float bias, out float ms, out float isc);
                float impulse = -_mass * ms * (-AxialVelocity() + bias) - isc * _upperImpulse;
                float newImpulse = MathF.Max(_upperImpulse + impulse, 0f);
                impulse = newImpulse - _upperImpulse; _upperImpulse = newImpulse;
                ApplyImpulse(bodyA, bodyB, _rA, _rB, _axis * -impulse);
            }
            TrackPeak(MathF.Abs(_impulse) + _lowerImpulse + _upperImpulse);
        }

        internal override void SolvePosition(in SolverParams p)
        {
            Geometry();
            float c;
            if (isRod) c = _length - maxLength;
            else if (hasLower && _length < minLength) c = _length - minLength;
            else if (hasUpper && _length > maxLength) c = _length - maxLength;
            else return;
            if (MathF.Abs(c) <= p.linearSlop) return;
            float corr = Mathf.Clamp(c * p.baumgarte, -p.maxLinearCorrection, p.maxLinearCorrection);
            ApplyPositionImpulse(bodyA, bodyB, _rA, _rB, _axis * (-_mass * corr));
        }
    }

    /// <summary>
    /// A spring between two anchors: pulls towards <see cref="DistanceConstraint.restLength"/> with
    /// <see cref="DistanceConstraint.stiffness"/> / <see cref="DistanceConstraint.damping"/>, free otherwise
    /// (no hard limits unless <see cref="DistanceConstraint.minLength"/> / <see cref="DistanceConstraint.maxLength"/>
    /// are set). Hanging lamps, bungee ropes, suspension.
    /// </summary>
    public sealed class SpringConstraint : DistanceConstraint
    {
        public SpringConstraint() { minLength = 0f; maxLength = float.PositiveInfinity; stiffness = 100f; damping = 5f; }

        /// <summary>Spring between two world points, at rest at their current distance.</summary>
        public SpringConstraint(SolverBody a, SolverBody b, Vector3 worldAnchorA, Vector3 worldAnchorB, float stiffness, float damping)
            : base(a, b, worldAnchorA, worldAnchorB)
        {
            minLength = 0f; maxLength = float.PositiveInfinity;
            this.stiffness = stiffness; this.damping = damping;
        }
    }

    // ================================================================================================ hinge

    /// <summary>
    /// Revolute joint: the anchors coincide and the bodies may only rotate about one shared axis (doors, wheels,
    /// levers, seesaws), optionally within [<see cref="lowerLimit"/>, <see cref="upperLimit"/>] degrees, driven
    /// by a motor (<see cref="motorSpeed"/> rad/s up to <see cref="maxMotorTorque"/>) and/or pulled to
    /// <see cref="springTargetAngle"/> by a torsion spring (<see cref="springStiffness"/> N·m/rad,
    /// <see cref="springDamping"/> N·m·s/rad — a self-closing door). B may be null (hinged to the world). The
    /// angle, limits, motor and spring describe A's rotation relative to B about the axis (right-hand rule).
    /// </summary>
    public sealed class HingeConstraint : Constraint
    {
        public Vector3 localAnchorA, localAnchorB;
        /// <summary>Hinge axis in each body's frame (unit).</summary>
        public Vector3 localAxisA = Vector3.up, localAxisB = Vector3.up;
        public bool useLimits = false;
        public float lowerLimit = -45f, upperLimit = 45f;
        public bool useMotor = false;
        public float motorSpeed = 0f;
        public float maxMotorTorque = 10f;
        /// <summary>The motor only accelerates towards its speed and never brakes a faster spin (Unity's freeSpin).</summary>
        public bool motorFreeSpin = false;
        public bool useSpring = false;
        public float springStiffness = 10f;
        public float springDamping = 1f;
        /// <summary>Angle (degrees) the spring pulls towards.</summary>
        public float springTargetAngle = 0f;

        private Vector3 _pointImpulse;
        private float _swingImpulse0, _swingImpulse1, _lowerImpulse, _upperImpulse, _motorImpulse, _springImpulse;
        private Vector3 _rA, _rB, _axisA, _axisB, _u, _v;
        private Quaternion _referenceA = Quaternion.identity; // B-relative rotation of A at the zero angle
        private bool _hasReference;
        private Softness _spring;

        protected override Vector3 LocalAnchorA => localAnchorA;
        protected override Vector3 LocalAnchorB => localAnchorB;

        public HingeConstraint() { }

        /// <summary>Hinge between two bodies through <paramref name="worldAnchor"/> about <paramref name="worldAxis"/> (their current poses define angle 0).</summary>
        public HingeConstraint(SolverBody a, SolverBody b, Vector3 worldAnchor, Vector3 worldAxis)
        {
            bodyA = a; bodyB = b;
            worldAxis = worldAxis.sqrMagnitude > 1E-08f ? worldAxis.normalized : Vector3.up;
            localAnchorA = a != null ? Quaternion.Inverse(a.rotation) * (worldAnchor - a.position) : worldAnchor;
            localAnchorB = b != null ? Quaternion.Inverse(b.rotation) * (worldAnchor - b.position) : worldAnchor;
            localAxisA = a != null ? Quaternion.Inverse(a.rotation) * worldAxis : worldAxis;
            localAxisB = b != null ? Quaternion.Inverse(b.rotation) * worldAxis : worldAxis;
            SetReference();
        }

        /// <summary>Makes the current relative orientation the joint's zero angle.</summary>
        public void SetReference()
        {
            Quaternion qa = bodyA != null ? bodyA.rotation : Quaternion.identity, qb = bodyB != null ? bodyB.rotation : Quaternion.identity;
            _referenceA = Quaternion.Inverse(qb) * qa;
            _hasReference = true;
        }

        /// <summary>World hinge axis (from A).</summary>
        public Vector3 worldAxis => bodyA != null ? (bodyA.rotation * localAxisA).normalized : localAxisA.normalized;

        /// <summary>Current hinge angle in degrees: how far A has turned about the axis relative to B, 0 at the reference pose.</summary>
        public float angle
        {
            get
            {
                Quaternion qa = bodyA != null ? bodyA.rotation : Quaternion.identity, qb = bodyB != null ? bodyB.rotation : Quaternion.identity;
                // A's rotation relative to B with the reference removed, in B's frame: a hinge turn of θ about the
                // world axis shows up as AngleAxis(θ, localAxisB)
                Quaternion rel = (Quaternion.Inverse(qb) * qa) * Quaternion.Inverse(_referenceA);
                Vector3 axis = localAxisB.normalized;
                float dot = rel.x * axis.x + rel.y * axis.y + rel.z * axis.z;
                float a = 2f * MathF.Atan2(dot, rel.w) * Mathf.Rad2Deg;
                if (a > 180f) a -= 360f; else if (a < -180f) a += 360f;
                return a;
            }
        }

        /// <summary>Angular speed of A relative to B about the axis (rad/s).</summary>
        public float angularSpeed
        {
            get
            {
                Vector3 axis = worldAxis;
                Vector3 wA = bodyA != null ? bodyA.angularVelocity : Vector3.zero, wB = bodyB != null ? bodyB.angularVelocity : Vector3.zero;
                return Vector3.Dot(wA - wB, axis);
            }
        }

        public override void ClearImpulses()
        {
            _pointImpulse = Vector3.zero;
            _swingImpulse0 = _swingImpulse1 = _lowerImpulse = _upperImpulse = _motorImpulse = _springImpulse = 0f;
        }

        internal override bool Prepare(in SolverParams p)
        {
            if (!BodiesReady()) return false;
            if (!_hasReference) SetReference();
            soft = Softness.FromFrequency(hertz, dampingRatio, p.h);
            Geometry();
            _spring = useSpring ? Softness.FromSpring(springStiffness, springDamping, AngularRowMass(bodyA, bodyB, _axisA), p.h) : Softness.Rigid;
            if (!p.warmStart) ClearImpulses();
            return true;
        }

        private void Geometry()
        {
            _rA = bodyA.RotateToWorld(localAnchorA);
            _rB = bodyB != null ? bodyB.RotateToWorld(localAnchorB) : Vector3.zero;
            _axisA = (bodyA.rotation * localAxisA).normalized;
            _axisB = bodyB != null ? (bodyB.rotation * localAxisB).normalized : localAxisB.normalized;
            Frame(_axisA, out _u, out _v);
        }

        /// <summary>A's angular velocity relative to B along the axis (the rate of <see cref="angle"/>).</summary>
        private float AxisRate() => Vector3.Dot(AngularVelocity(bodyA) - AngularVelocity(bodyB), _axisA);

        /// <summary>Angular impulse λ about the axis on A (and −λ on B).</summary>
        private void ApplyAxisImpulse(float lambda) => ApplyAngularImpulse(bodyA, bodyB, _axisA * -lambda);

        internal override void WarmStart()
        {
            Geometry();
            if (_pointImpulse.sqrMagnitude > 0f) ApplyImpulse(bodyA, bodyB, _rA, _rB, _pointImpulse);
            // swing rows act on B relative to A; the axis rows (limits, motor, spring) on A relative to B
            Vector3 L = _u * _swingImpulse0 + _v * _swingImpulse1 + _axisA * -(_lowerImpulse - _upperImpulse + _motorImpulse + _springImpulse);
            if (L.sqrMagnitude > 0f) ApplyAngularImpulse(bodyA, bodyB, L);
        }

        internal override void SolveVelocity(bool useBias, in SolverParams p)
        {
            Geometry();
            float axisMass = AngularRowMass(bodyA, bodyB, _axisA);

            // torsion spring towards the target angle (soft, biased pass only)
            if (useSpring && useBias && springStiffness > 0f)
            {
                float c = (angle - springTargetAngle) * Mathf.Deg2Rad;
                float impulse = -axisMass * _spring.massScale * (AxisRate() + c * _spring.biasRate) - _spring.impulseScale * _springImpulse;
                _springImpulse += impulse;
                ApplyAxisImpulse(impulse);
            }

            // motor: drive the axis rate to motorSpeed within the torque budget
            if (useMotor && maxMotorTorque > 0f && !(motorFreeSpin && Overspeeding(AxisRate(), motorSpeed)))
            {
                float cdot = AxisRate() - motorSpeed;
                float impulse = -axisMass * cdot;
                float maxImpulse = maxMotorTorque * p.h;
                float newImpulse = Mathf.Clamp(_motorImpulse + impulse, -maxImpulse, maxImpulse);
                impulse = newImpulse - _motorImpulse; _motorImpulse = newImpulse;
                ApplyAxisImpulse(impulse);
            }

            // limits
            if (useLimits)
            {
                float ang = angle * Mathf.Deg2Rad;
                float lower = MathF.Min(lowerLimit, upperLimit) * Mathf.Deg2Rad, upper = MathF.Max(lowerLimit, upperLimit) * Mathf.Deg2Rad;
                {
                    float c = ang - lower;              // < 0: below the limit → the angle must grow
                    LimitBias(in p, c, useBias, out float bias, out float ms, out float isc);
                    float impulse = -axisMass * ms * (AxisRate() + bias) - isc * _lowerImpulse;
                    float newImpulse = MathF.Max(_lowerImpulse + impulse, 0f);
                    impulse = newImpulse - _lowerImpulse; _lowerImpulse = newImpulse;
                    ApplyAxisImpulse(impulse);
                }
                {
                    float c = upper - ang;              // < 0: above the limit → the angle must shrink
                    LimitBias(in p, c, useBias, out float bias, out float ms, out float isc);
                    float impulse = -axisMass * ms * (-AxisRate() + bias) - isc * _upperImpulse;
                    float newImpulse = MathF.Max(_upperImpulse + impulse, 0f);
                    impulse = newImpulse - _upperImpulse; _upperImpulse = newImpulse;
                    ApplyAxisImpulse(-impulse);
                }
            }

            // swing: the two axes stay aligned (two angular rows perpendicular to the hinge, B relative to A)
            {
                Vector3 err = RotationError(_axisA, _axisB); // B's excess rotation
                for (int row = 0; row < 2; row++)
                {
                    Vector3 axis = row == 0 ? _u : _v;
                    float m = AngularRowMass(bodyA, bodyB, axis);
                    BiasFor(in p, Vector3.Dot(err, axis), useBias, out float bias, out float ms, out float isc);
                    float acc = row == 0 ? _swingImpulse0 : _swingImpulse1;
                    float cdot = Vector3.Dot(AngularVelocity(bodyB) - AngularVelocity(bodyA), axis);
                    float impulse = -m * ms * (cdot + bias) - isc * acc;
                    if (row == 0) _swingImpulse0 += impulse; else _swingImpulse1 += impulse;
                    ApplyAngularImpulse(bodyA, bodyB, axis * impulse);
                }
            }

            // point: anchors together
            {
                Vector3 pA = bodyA.position + _rA;
                Vector3 pB = bodyB != null ? bodyB.position + _rB : localAnchorB;
                SolvePointRow(_rA, _rB, pB - pA, ref _pointImpulse, useBias, in p);
            }
            TrackPeak(_pointImpulse.magnitude);
            TrackPeakAngular(MathF.Sqrt(_swingImpulse0 * _swingImpulse0 + _swingImpulse1 * _swingImpulse1));
        }

        internal override void SolvePosition(in SolverParams p)
        {
            Geometry();
            // axis alignment: remove B's excess rotation
            Vector3 err = RotationError(_axisA, _axisB);
            if (CorrectOrientation(-err, in p)) Geometry();
            // limits
            if (useLimits)
            {
                float ang = angle, lower = MathF.Min(lowerLimit, upperLimit), upper = MathF.Max(lowerLimit, upperLimit);
                float c = ang < lower ? (ang - lower) : ang > upper ? (ang - upper) : 0f; // degrees, + = above
                if (MathF.Abs(c) * Mathf.Deg2Rad > p.angularSlop)
                {
                    // A must turn by −c about the axis (relative to B): rotate B by +c·k via the shared helper convention
                    float m = AngularRowMass(bodyA, bodyB, _axisA);
                    ApplyPositionRotation(bodyA, bodyB, _axisA * (m * c * Mathf.Deg2Rad * p.baumgarte));
                    Geometry();
                }
            }
            // anchors
            Vector3 pA = bodyA.position + _rA;
            Vector3 pB = bodyB != null ? bodyB.position + _rB : localAnchorB;
            CorrectPoint(_rA, _rB, pB - pA, in p);
        }
    }

    // ================================================================================================ slider

    /// <summary>
    /// Prismatic joint: A may only translate along one axis fixed in B (and keeps its orientation relative to B) —
    /// drawers, pistons, elevators, sliding doors. Travel is measured as A's anchor displacement along the axis
    /// from B's anchor (<see cref="position"/>, metres, positive along the axis), optionally limited to
    /// [<see cref="lowerLimit"/>, <see cref="upperLimit"/>], driven by a linear motor (<see cref="motorSpeed"/> m/s
    /// up to <see cref="maxMotorForce"/> N) and/or pulled to <see cref="springTargetPosition"/> by a spring
    /// (<see cref="springStiffness"/> N/m, <see cref="springDamping"/> N·s/m). B may be null (a rail in the world).
    /// </summary>
    public sealed class SliderConstraint : Constraint
    {
        public Vector3 localAnchorA, localAnchorB;
        /// <summary>Slide axis in B's frame (world when B is null), unit.</summary>
        public Vector3 localAxisB = Vector3.right;
        public bool useLimits = false;
        public float lowerLimit = -1f, upperLimit = 1f;
        public bool useMotor = false;
        public float motorSpeed = 0f;
        public float maxMotorForce = 100f;
        /// <summary>The motor only accelerates towards its speed and never brakes faster motion.</summary>
        public bool motorFreeSpin = false;
        public bool useSpring = false;
        public float springStiffness = 100f;
        public float springDamping = 10f;
        public float springTargetPosition = 0f;

        private Vector3 _angularImpulse;
        private float _perpImpulse0, _perpImpulse1, _lowerImpulse, _upperImpulse, _motorImpulse, _springImpulse;
        private Vector3 _rA, _rB, _rARail, _axis, _u, _v, _pA, _pB;
        private float _s;
        private Quaternion _referenceB = Quaternion.identity; // A-relative orientation of B (locked)
        private bool _hasReference;
        private Softness _spring;

        protected override Vector3 LocalAnchorA => localAnchorA;
        protected override Vector3 LocalAnchorB => localAnchorB;

        public SliderConstraint() { }

        /// <summary>Slider between two bodies through <paramref name="worldAnchor"/> along <paramref name="worldAxis"/> (the current poses are position 0).</summary>
        public SliderConstraint(SolverBody a, SolverBody b, Vector3 worldAnchor, Vector3 worldAxis)
        {
            bodyA = a; bodyB = b;
            worldAxis = worldAxis.sqrMagnitude > 1E-08f ? worldAxis.normalized : Vector3.right;
            localAnchorA = a != null ? Quaternion.Inverse(a.rotation) * (worldAnchor - a.position) : worldAnchor;
            localAnchorB = b != null ? Quaternion.Inverse(b.rotation) * (worldAnchor - b.position) : worldAnchor;
            localAxisB = b != null ? Quaternion.Inverse(b.rotation) * worldAxis : worldAxis;
            SetReference();
        }

        /// <summary>Locks the current relative orientation.</summary>
        public void SetReference()
        {
            Quaternion qa = bodyA != null ? bodyA.rotation : Quaternion.identity, qb = bodyB != null ? bodyB.rotation : Quaternion.identity;
            _referenceB = Quaternion.Inverse(qa) * qb;
            _hasReference = true;
        }

        /// <summary>World slide axis.</summary>
        public Vector3 worldAxis => bodyB != null ? (bodyB.rotation * localAxisB).normalized : localAxisB.normalized;

        /// <summary>Travel of A's anchor along the axis from B's anchor (m).</summary>
        public float position
        {
            get
            {
                Vector3 pA = worldAnchorA, pB = worldAnchorB;
                return Vector3.Dot(pA - pB, worldAxis);
            }
        }

        /// <summary>Speed of A along the axis relative to B (m/s).</summary>
        public float speed
        {
            get
            {
                Vector3 rA = bodyA != null ? bodyA.rotation * localAnchorA : Vector3.zero, rB = bodyB != null ? bodyB.RotateToWorld(localAnchorB) : Vector3.zero;
                Vector3 vA = bodyA != null ? bodyA.GetPointVelocity(bodyA.position + rA) : Vector3.zero, vB = bodyB != null ? bodyB.GetPointVelocity(bodyB.position + rB) : Vector3.zero;
                return Vector3.Dot(vA - vB, worldAxis);
            }
        }

        public override void ClearImpulses()
        {
            _angularImpulse = Vector3.zero;
            _perpImpulse0 = _perpImpulse1 = _lowerImpulse = _upperImpulse = _motorImpulse = _springImpulse = 0f;
        }

        internal override bool Prepare(in SolverParams p)
        {
            if (!BodiesReady()) return false;
            if (!_hasReference) SetReference();
            soft = Softness.FromFrequency(hertz, dampingRatio, p.h);
            Geometry();
            _spring = useSpring ? Softness.FromSpring(springStiffness, springDamping, LinearRowMass(bodyA, bodyB, _rA, _rB, _axis), p.h) : Softness.Rigid;
            if (!p.warmStart) ClearImpulses();
            return true;
        }

        private void Geometry()
        {
            _rA = bodyA.RotateToWorld(localAnchorA);
            _rB = bodyB != null ? bodyB.RotateToWorld(localAnchorB) : Vector3.zero;
            _pA = bodyA.position + _rA;
            _pB = bodyB != null ? bodyB.position + _rB : localAnchorB;
            _axis = worldAxis;
            Frame(_axis, out _u, out _v);
            Vector3 d = _pA - _pB;
            _s = Vector3.Dot(d, _axis);
            // the perpendicular rows hold B's anchor on A's rail: the point of A's rail level with B's anchor
            _rARail = _rA - _axis * _s;
        }

        /// <summary>A's velocity along the axis relative to B.</summary>
        private float AxisRate() => Vector3.Dot(PointVelocity(bodyA, _rA) - PointVelocity(bodyB, _rB), _axis);

        /// <summary>Linear impulse λ along the axis on A (and −λ on B).</summary>
        private void ApplyAxisImpulse(float lambda) => ApplyImpulse(bodyA, bodyB, _rA, _rB, _axis * -lambda);

        internal override void WarmStart()
        {
            Geometry();
            if (_angularImpulse.sqrMagnitude > 0f) ApplyAngularImpulse(bodyA, bodyB, _angularImpulse);
            Vector3 P = _u * _perpImpulse0 + _v * _perpImpulse1;
            if (P.sqrMagnitude > 0f) ApplyImpulse(bodyA, bodyB, _rARail, _rB, P);
            float axial = _lowerImpulse - _upperImpulse + _motorImpulse + _springImpulse;
            if (axial != 0f) ApplyAxisImpulse(axial);
        }

        internal override void SolveVelocity(bool useBias, in SolverParams p)
        {
            Geometry();
            float axisMass = LinearRowMass(bodyA, bodyB, _rA, _rB, _axis);

            // spring towards the target position (soft, biased pass only)
            if (useSpring && useBias && springStiffness > 0f)
            {
                float c = _s - springTargetPosition;
                float impulse = -axisMass * _spring.massScale * (AxisRate() + c * _spring.biasRate) - _spring.impulseScale * _springImpulse;
                _springImpulse += impulse;
                ApplyAxisImpulse(impulse);
            }

            // motor
            if (useMotor && maxMotorForce > 0f && !(motorFreeSpin && Overspeeding(AxisRate(), motorSpeed)))
            {
                float cdot = AxisRate() - motorSpeed;
                float impulse = -axisMass * cdot;
                float maxImpulse = maxMotorForce * p.h;
                float newImpulse = Mathf.Clamp(_motorImpulse + impulse, -maxImpulse, maxImpulse);
                impulse = newImpulse - _motorImpulse; _motorImpulse = newImpulse;
                ApplyAxisImpulse(impulse);
            }

            // limits
            if (useLimits)
            {
                float lower = MathF.Min(lowerLimit, upperLimit), upper = MathF.Max(lowerLimit, upperLimit);
                {
                    float c = _s - lower;               // < 0: below → A must move along +axis
                    LimitBias(in p, c, useBias, out float bias, out float ms, out float isc);
                    float impulse = -axisMass * ms * (AxisRate() + bias) - isc * _lowerImpulse;
                    float newImpulse = MathF.Max(_lowerImpulse + impulse, 0f);
                    impulse = newImpulse - _lowerImpulse; _lowerImpulse = newImpulse;
                    ApplyAxisImpulse(impulse);
                }
                {
                    float c = upper - _s;               // < 0: above → A must move along −axis
                    LimitBias(in p, c, useBias, out float bias, out float ms, out float isc);
                    float impulse = -axisMass * ms * (-AxisRate() + bias) - isc * _upperImpulse;
                    float newImpulse = MathF.Max(_upperImpulse + impulse, 0f);
                    impulse = newImpulse - _upperImpulse; _upperImpulse = newImpulse;
                    ApplyAxisImpulse(-impulse);
                }
            }

            // orientation lock (B relative to A)
            SolveOrientationRow(OrientationError(bodyA.rotation, bodyB != null ? bodyB.rotation : Quaternion.identity, _referenceB), ref _angularImpulse, useBias, in p);

            // perpendicular rows: B's anchor stays on A's rail (error = pB − rail point, B relative to A)
            Geometry();
            Vector3 railPoint = bodyA.position + _rARail;
            Vector3 error = _pB - railPoint;
            for (int row = 0; row < 2; row++)
            {
                Vector3 axis = row == 0 ? _u : _v;
                float m = LinearRowMass(bodyA, bodyB, _rARail, _rB, axis);
                BiasFor(in p, Vector3.Dot(error, axis), useBias, out float bias, out float ms, out float isc);
                float acc = row == 0 ? _perpImpulse0 : _perpImpulse1;
                float cdot = Vector3.Dot(PointVelocity(bodyB, _rB) - PointVelocity(bodyA, _rARail), axis);
                float impulse = -m * ms * (cdot + bias) - isc * acc;
                if (row == 0) _perpImpulse0 += impulse; else _perpImpulse1 += impulse;
                ApplyImpulse(bodyA, bodyB, _rARail, _rB, axis * impulse);
            }
            TrackPeak(MathF.Sqrt(_perpImpulse0 * _perpImpulse0 + _perpImpulse1 * _perpImpulse1) + _lowerImpulse + _upperImpulse);
            TrackPeakAngular(_angularImpulse.magnitude);
        }

        internal override void SolvePosition(in SolverParams p)
        {
            Geometry();
            if (CorrectOrientation(OrientationError(bodyA.rotation, bodyB != null ? bodyB.rotation : Quaternion.identity, _referenceB), in p)) Geometry();
            // limits along the axis
            if (useLimits)
            {
                float lower = MathF.Min(lowerLimit, upperLimit), upper = MathF.Max(lowerLimit, upperLimit);
                float c = _s < lower ? _s - lower : _s > upper ? _s - upper : 0f; // + = A too far along +axis
                if (MathF.Abs(c) > p.linearSlop)
                {
                    float corr = Mathf.Clamp(c * p.baumgarte, -p.maxLinearCorrection, p.maxLinearCorrection);
                    float m = LinearRowMass(bodyA, bodyB, _rA, _rB, _axis);
                    // move A by −corr along the axis: ApplyPositionImpulse puts +P on B, −P on A
                    ApplyPositionImpulse(bodyA, bodyB, _rA, _rB, _axis * (m * corr));
                    Geometry();
                }
            }
            // perpendicular
            Vector3 error = _pB - (bodyA.position + _rARail);
            error -= _axis * Vector3.Dot(error, _axis);
            CorrectPoint(_rARail, _rB, error, in p);
        }
    }

    // ================================================================================================ fixed

    /// <summary>Weld: the two bodies keep their relative position and orientation (breakable props, glued pieces, signs on walls).</summary>
    public sealed class FixedConstraint : Constraint
    {
        public Vector3 localAnchorA, localAnchorB;
        private Quaternion _referenceB = Quaternion.identity;
        private bool _hasReference;
        private Vector3 _pointImpulse, _angularImpulse;
        private Vector3 _rA, _rB;

        protected override Vector3 LocalAnchorA => localAnchorA;
        protected override Vector3 LocalAnchorB => localAnchorB;

        public FixedConstraint() { }

        public FixedConstraint(SolverBody a, SolverBody b, Vector3 worldAnchor)
        {
            bodyA = a; bodyB = b;
            localAnchorA = a != null ? Quaternion.Inverse(a.rotation) * (worldAnchor - a.position) : worldAnchor;
            localAnchorB = b != null ? Quaternion.Inverse(b.rotation) * (worldAnchor - b.position) : worldAnchor;
            SetReference();
        }

        public void SetReference()
        {
            Quaternion qa = bodyA != null ? bodyA.rotation : Quaternion.identity, qb = bodyB != null ? bodyB.rotation : Quaternion.identity;
            _referenceB = Quaternion.Inverse(qa) * qb;
            _hasReference = true;
        }

        public override void ClearImpulses() { _pointImpulse = Vector3.zero; _angularImpulse = Vector3.zero; }

        internal override bool Prepare(in SolverParams p)
        {
            if (!BodiesReady()) return false;
            if (!_hasReference) SetReference();
            soft = Softness.FromFrequency(hertz, dampingRatio, p.h);
            if (!p.warmStart) ClearImpulses();
            return true;
        }

        private void Arms()
        {
            _rA = bodyA.RotateToWorld(localAnchorA);
            _rB = bodyB != null ? bodyB.RotateToWorld(localAnchorB) : Vector3.zero;
        }

        private Vector3 AngularError() => OrientationError(bodyA.rotation, bodyB != null ? bodyB.rotation : Quaternion.identity, _referenceB);

        internal override void WarmStart()
        {
            Arms();
            if (_pointImpulse.sqrMagnitude > 0f) ApplyImpulse(bodyA, bodyB, _rA, _rB, _pointImpulse);
            if (_angularImpulse.sqrMagnitude > 0f) ApplyAngularImpulse(bodyA, bodyB, _angularImpulse);
        }

        internal override void SolveVelocity(bool useBias, in SolverParams p)
        {
            Arms();
            SolveOrientationRow(AngularError(), ref _angularImpulse, useBias, in p);
            Vector3 pA = bodyA.position + _rA;
            Vector3 pB = bodyB != null ? bodyB.position + _rB : localAnchorB;
            SolvePointRow(_rA, _rB, pB - pA, ref _pointImpulse, useBias, in p);
            TrackPeak(_pointImpulse.magnitude);
            TrackPeakAngular(_angularImpulse.magnitude);
        }

        internal override void SolvePosition(in SolverParams p)
        {
            Arms();
            if (CorrectOrientation(AngularError(), in p)) Arms();
            Vector3 pA = bodyA.position + _rA;
            Vector3 pB = bodyB != null ? bodyB.position + _rB : localAnchorB;
            CorrectPoint(_rA, _rB, pB - pA, in p);
        }
    }
}
