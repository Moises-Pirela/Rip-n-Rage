using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    public enum ForceMode { Force, Impulse, Acceleration, VelocityChange }

    /// <summary>How a rigid body is kept from tunnelling (Unity's names).</summary>
    public enum CollisionDetectionMode
    {
        /// <summary>Contacts are found once per step from the current poses (fast bodies can pass through thin geometry).</summary>
        Discrete,
        /// <summary>The body's motion is swept against static geometry (brushes, meshes, props, movers) every substep and clamped at the first contact.</summary>
        Continuous,
        /// <summary>As Continuous, and also swept against the other rigid bodies with their motion.</summary>
        ContinuousDynamic,
    }

    /// <summary>One contact of a rigid body, reported after the step.</summary>
    public struct RigidContact
    {
        public Collider other;
        public RigidBody otherBody;
        public Vector3 point;
        /// <summary>Normal pointing away from this body's surface towards the other.</summary>
        public Vector3 normal;
        /// <summary>Normal impulse applied this step (N·s): how hard the hit was.</summary>
        public float impulse;
        /// <summary>Relative speed along the normal before the step (negative = approaching).</summary>
        public float approachSpeed;
    }

    /// <summary>
    /// A dynamic body: mass, inertia, forces and torques, simulated in the fixed step by the world's contact
    /// solver (sequential impulses with warm starting, Coulomb friction, restitution, sleeping). The shape is the
    /// <see cref="Collider"/> on the same object: a <see cref="BoxCollider"/> (oriented by the transform) or a
    /// <see cref="SphereCollider"/>; other colliders are simulated as their bounding box. Bodies collide with every
    /// solid — brushes, meshes, props, the player and other movers (which are treated as immovable) — and with each
    /// other. The transform is written after every step; move a body by velocity, forces or impulses, or make it
    /// <see cref="isKinematic"/> and move it by hand (it then pushes with infinite mass).
    /// </summary>
    public sealed class RigidBody : Behaviour
    {
        /// <summary>The simulation state this behaviour adapts (the solver only sees this). Deleted with the legacy model in layer 9.</summary>
        public readonly SolverBody body;
        public RigidBody() { body = new SolverBody { legacy = this }; }
        /// <summary>Legacy call sites (joint constructors, solver helpers) pass a RigidBody where the solver wants its SolverBody.</summary>
        public static implicit operator SolverBody(RigidBody rb) => rb?.body;

        /// <summary>Defaults of new bodies (tunables.cfg [collision]).</summary>
        public static float DefaultSleepThreshold = 0.08f, DefaultSleepDelay = 0.6f, DefaultMaxSpeed = 80f, DefaultMaxAngularSpeed = 40f;

        public float mass { get => body.mass; set => body.mass = value; }
        public Vector3 linearVelocity { get => body.linearVelocity; set => body.linearVelocity = value; }
        public Vector3 angularVelocity { get => body.angularVelocity; set => body.angularVelocity = value; }
        public float linearDamping { get => body.linearDamping; set => body.linearDamping = value; }
        public float angularDamping { get => body.angularDamping; set => body.angularDamping = value; }
        public float restitution { get => body.restitution; set => body.restitution = value; }
        public float friction { get => body.friction; set => body.friction = value; }
        public float staticFriction { get => body.staticFriction; set => body.staticFriction = value; }
        public bool useGravity { get => body.useGravity; set => body.useGravity = value; }
        public float gravityScale { get => body.gravityScale; set => body.gravityScale = value; }
        public bool isKinematic { get => body.isKinematic; set => body.isKinematic = value; }
        public bool freezeRotation { get => body.freezeRotation; set => body.freezeRotation = value; }
        public CollisionDetectionMode collisionDetection { get => body.collisionDetection; set => body.collisionDetection = value; }
        public bool interpolate { get => body.interpolate; set => body.interpolate = value; }
        public int collisionMask { get => body.collisionMask; set => body.collisionMask = value; }
        public bool useLayerMatrix { get => body.useLayerMatrix; set => body.useLayerMatrix = value; }
        public int collisionGroup { get => body.collisionGroup; set => body.collisionGroup = value; }
        public bool canSleep { get => body.canSleep; set => body.canSleep = value; }
        public float sleepThreshold { get => body.sleepThreshold; set => body.sleepThreshold = value; }
        public float sleepDelay { get => body.sleepDelay; set => body.sleepDelay = value; }
        public float maxSpeed { get => body.maxSpeed; set => body.maxSpeed = value; }
        public float maxAngularSpeed { get => body.maxAngularSpeed; set => body.maxAngularSpeed = value; }

        /// <summary>Raised once per contact after the step (only when someone listens).</summary>
        public event Action<RigidBody, RigidContact> Collided;

        private Collider _collider;

        public bool isSleeping => body.sleeping;
        public Collider Shape => _collider;
        public CollisionShape WorldShape => body.WorldShape;
        public bool isRounded => body.isRounded;
        public Vector3 worldCenterOfMass => body.position;
        public float inverseMassValue => body.inverseMass;
        public IReadOnlyList<RigidContact> Contacts => body.contacts;

        protected override void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider?.InvalidateBody();
            ComputeMass();
            ReadTransform();
            Physics.World.AddRigidBody(body);
            if (_collider != null) Physics.RegisterMover(_collider); // trigger volumes notice rigid bodies too
        }

        /// <summary>Writes the solver pose to the transform (the transform may hold an interpolated one).</summary>
        internal void WriteTransform() => body.WriteTransform();

        protected override void OnDestroy()
        {
            Physics.World.RemoveRigidBody(body);
            body.live = false;
            if (_collider?.broadphase != null && _collider.broadphase.body == body) _collider.broadphase.body = null;
            _collider?.InvalidateBody();
        }

        /// <summary>Recomputes the mass properties from <see cref="mass"/> and the collider (call after changing either).</summary>
        public void ComputeMass()
        {
            _collider ??= GetComponent<Collider>();
            if (_collider?.broadphase != null) _collider.broadphase.body = body;
            var b = body;
            Vector3 scale = transform.lossyScale;
            b.isSphere = false; b.isCapsule = false;
            b.comOffsetLocal = Vector3.zero;
            if (_collider is SphereCollider sc)
            {
                b.isSphere = true;
                b.sphereRadius = sc.worldRadius;
                b.boxHalfExtents = new Vector3(b.sphereRadius, b.sphereRadius, b.sphereRadius);
                b.comOffsetLocal = Vector3.Scale(sc.center, scale);
            }
            else if (_collider is CapsuleCollider cap)
            {
                b.isCapsule = true;
                b.sphereRadius = cap.worldRadius;
                b.capsuleHalfHeight = cap.worldHalfHeight;
                b.boxHalfExtents = new Vector3(b.sphereRadius, b.capsuleHalfHeight + b.sphereRadius, b.sphereRadius);
                b.comOffsetLocal = Vector3.Scale(cap.center, scale);
            }
            else if (_collider is BoxCollider bc)
            {
                b.boxHalfExtents = Vector3.Scale(bc.size, scale) * 0.5f;
                b.comOffsetLocal = Vector3.Scale(bc.center, scale);
            }
            else if (_collider != null)
            {
                Bounds bb = _collider.bounds;
                b.boxHalfExtents = bb.extents;
                b.comOffsetLocal = Quaternion.Inverse(transform.rotation) * (bb.center - transform.position);
            }
            else b.boxHalfExtents = new Vector3(0.25f, 0.25f, 0.25f);
            b.boxHalfExtents = Vector3.Max(b.boxHalfExtents, new Vector3(0.01f, 0.01f, 0.01f));
            b.ComputeMassProperties();
        }

        /// <summary>Takes the pose from the transform (after a teleport, or every step for kinematic bodies).</summary>
        public void ReadTransform() => body.ReadTransform();

        internal ColliderTable.Slot ColliderSlot => _collider?.broadphase;

        public void AddForce(Vector3 f, ForceMode mode = ForceMode.Force) => body.AddForce(f, mode);
        public void AddTorque(Vector3 t, ForceMode mode = ForceMode.Force) => body.AddTorque(t, mode);
        public void AddForceAtPosition(Vector3 f, Vector3 worldPoint, ForceMode mode = ForceMode.Force) => body.AddForceAtPosition(f, worldPoint, mode);
        public void AddImpulse(Vector3 impulse) => body.AddImpulse(impulse);
        public void AddImpulseAtPosition(Vector3 impulse, Vector3 worldPoint) => body.AddImpulseAtPosition(impulse, worldPoint);
        public void AddExplosionForce(float impulse, Vector3 origin, float radius, float upwardsModifier = 0f) => body.AddExplosionForce(impulse, origin, radius, upwardsModifier);
        public Vector3 GetPointVelocity(Vector3 worldPoint) => body.GetPointVelocity(worldPoint);
        public void WakeUp() => body.WakeUp();
        public void Sleep() => body.Sleep();

        /// <summary>Teleports the body (velocities kept unless <paramref name="resetVelocity"/>).</summary>
        public void MovePosition(Vector3 worldPosition, bool resetVelocity = false)
        {
            transform.position = worldPosition;
            ReadTransform();
            if (resetVelocity) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.WakeUp();
        }

        public void MoveRotation(Quaternion worldRotation)
        {
            transform.rotation = worldRotation;
            ReadTransform();
            body.WakeUp();
        }

        internal void RaiseContacts()
        {
            if (Collided == null) return;
            var contacts = body.contacts;
            for (int i = 0; i < contacts.Count; i++)
            {
                if (destroyed) return;
                try { Collided(this, contacts[i]); }
                catch (Exception e) { Debug.LogError($"RigidBody.Collided handler on '{name}' threw: {e}"); }
            }
        }
    }
}
