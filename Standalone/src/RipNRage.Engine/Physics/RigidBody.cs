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
        // ---- properties ----
        public float mass = 1f;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
        /// <summary>Fraction of the linear velocity lost per second.</summary>
        public float linearDamping = 0.05f;
        public float angularDamping = 0.1f;
        /// <summary>Bounciness 0..1 (combined with the other surface per its material's combine mode; Maximum by default).</summary>
        public float restitution = 0.1f;
        /// <summary>Dynamic (sliding) friction coefficient, used when the collider has no <see cref="PhysicsMaterial"/>.</summary>
        public float friction = 0.6f;
        /// <summary>Static friction coefficient: the tangential force that must be exceeded before the body starts sliding.</summary>
        public float staticFriction = 0.7f;
        public bool useGravity = true;
        public float gravityScale = 1f;
        /// <summary>Infinite mass: not affected by anything, moved by its velocities / transform, pushes everything.</summary>
        public bool isKinematic = false;
        public bool freezeRotation = false;
        /// <summary>Continuous collision: sweep the motion so the body can never skip through geometry (or, with ContinuousDynamic, through other bodies).</summary>
        public CollisionDetectionMode collisionDetection = CollisionDetectionMode.Discrete;
        /// <summary>Write the transform every rendered frame, blended between the last two fixed steps, so a body moves smoothly whatever the frame rate (characters, the player).</summary>
        public bool interpolate = false;
        public int collisionMask = ~0;
        public bool useLayerMatrix = true;
        /// <summary>
        /// Bodies sharing the same <b>negative</b> group never collide with each other, bodies sharing the same positive group
        /// always do (Box2D's group index); 0 = no group. A ragdoll puts its pieces in one negative group so they do not
        /// grind against each other — the cheapest pairs are the ones the narrow phase never sees.
        /// </summary>
        public int collisionGroup = 0;
        public bool canSleep = true;
        /// <summary>Defaults of new bodies (tunables.cfg [collision]).</summary>
        public static float DefaultSleepThreshold = 0.08f, DefaultSleepDelay = 0.6f, DefaultMaxSpeed = 80f, DefaultMaxAngularSpeed = 40f;
        /// <summary>Speed (m/s and rad/s) under which the body may fall asleep after <see cref="sleepDelay"/> seconds.</summary>
        public float sleepThreshold = DefaultSleepThreshold;
        public float sleepDelay = DefaultSleepDelay;
        /// <summary>Largest speed the body may ever reach (stops explosions from tunnelling things into orbit).</summary>
        public float maxSpeed = DefaultMaxSpeed;
        public float maxAngularSpeed = DefaultMaxAngularSpeed;

        /// <summary>Raised once per contact after the step (only when someone listens).</summary>
        public event Action<RigidBody, RigidContact> Collided;

        // ---- state ----
        internal Vector3 position;      // centre of mass, world
        internal Quaternion rotation = Quaternion.identity;
        internal Vector3 force, torque;
        internal float inverseMass;
        internal Vector3 inverseInertiaLocal; // diagonal, body space
        internal Vector3 comOffsetLocal;      // centre of mass relative to the transform origin, local
        internal bool isSphere;
        internal bool isCapsule;
        internal float sphereRadius;          // sphere / capsule radius
        internal float capsuleHalfHeight;     // capsule core half length (local Y)
        internal Vector3 boxHalfExtents;
        internal float motionScale = 1f;      // continuous collision: fraction of this substep's motion that is free
        internal ConvexShape shape;           // world box shape (null for spheres)
        internal Bounds worldBounds;
        internal float sleepTimer;
        internal bool sleeping;
        internal Vector3 prevPosition;
        internal Quaternion prevRotation = Quaternion.identity;
        internal bool hasPrev;
        internal int islandStamp;
        internal readonly List<RigidContact> contacts = new List<RigidContact>();
        private Collider _collider;
        private bool _initialised;

        public bool isSleeping => sleeping;
        public Collider Shape => _collider;
        /// <summary>The body's exact collision shape at its current pose (sphere, capsule or oriented box).</summary>
        public CollisionShape WorldShape => isSphere ? CollisionShape.Sphere(position, sphereRadius)
            : isCapsule ? CollisionShape.Capsule(position, rotation, sphereRadius, capsuleHalfHeight)
            : CollisionShape.Box(position, rotation, boxHalfExtents);
        public bool isRounded => isSphere || isCapsule;
        /// <summary>World-space centre of mass.</summary>
        public Vector3 worldCenterOfMass => position;
        public float inverseMassValue => inverseMass;
        /// <summary>Contacts of the last step.</summary>
        public IReadOnlyList<RigidContact> Contacts => contacts;

        protected override void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider?.InvalidateBody();
            ComputeMass();
            ReadTransform();
            Physics.World.AddRigidBody(this);
            if (_collider != null) Physics.RegisterMover(_collider); // trigger volumes notice rigid bodies too
        }

        protected override void OnDestroy()
        {
            Physics.World.RemoveRigidBody(this);
            _collider?.InvalidateBody();
        }

        /// <summary>Recomputes the mass properties from <see cref="mass"/> and the collider (call after changing either).</summary>
        public void ComputeMass()
        {
            _collider ??= GetComponent<Collider>();
            Vector3 scale = transform.lossyScale;
            isSphere = false; isCapsule = false;
            comOffsetLocal = Vector3.zero;
            if (_collider is SphereCollider sc)
            {
                isSphere = true;
                sphereRadius = sc.worldRadius;
                boxHalfExtents = new Vector3(sphereRadius, sphereRadius, sphereRadius);
                comOffsetLocal = Vector3.Scale(sc.center, scale);
            }
            else if (_collider is CapsuleCollider cap)
            {
                isCapsule = true;
                sphereRadius = cap.worldRadius;
                capsuleHalfHeight = cap.worldHalfHeight;
                boxHalfExtents = new Vector3(sphereRadius, capsuleHalfHeight + sphereRadius, sphereRadius);
                comOffsetLocal = Vector3.Scale(cap.center, scale);
            }
            else if (_collider is BoxCollider bc)
            {
                boxHalfExtents = Vector3.Scale(bc.size, scale) * 0.5f;
                comOffsetLocal = Vector3.Scale(bc.center, scale);
            }
            else if (_collider != null)
            {
                Bounds b = _collider.bounds;
                boxHalfExtents = b.extents;
                comOffsetLocal = Quaternion.Inverse(transform.rotation) * (b.center - transform.position);
            }
            else boxHalfExtents = new Vector3(0.25f, 0.25f, 0.25f);
            boxHalfExtents = Vector3.Max(boxHalfExtents, new Vector3(0.01f, 0.01f, 0.01f));

            float m = MathF.Max(mass, 1E-04f);
            inverseMass = isKinematic ? 0f : 1f / m;
            if (isSphere)
            {
                float i = 0.4f * m * sphereRadius * sphereRadius;
                inverseInertiaLocal = new Vector3(1f / i, 1f / i, 1f / i);
            }
            else if (isCapsule)
            {
                // cylinder of length h plus two hemispheres
                float r = sphereRadius, hgt = capsuleHalfHeight * 2f;
                float mc = m * hgt / (hgt + 4f * r / 3f), ms = m - mc;
                float iAxis = mc * r * r * 0.5f + ms * 0.4f * r * r;
                float iPerp = mc * (r * r * 0.25f + hgt * hgt / 12f) + ms * (0.4f * r * r + hgt * hgt * 0.25f + 3f * hgt * r / 8f);
                inverseInertiaLocal = new Vector3(1f / iPerp, 1f / iAxis, 1f / iPerp);
            }
            else
            {
                Vector3 s = boxHalfExtents * 2f;
                float ix = m / 12f * (s.y * s.y + s.z * s.z), iy = m / 12f * (s.x * s.x + s.z * s.z), iz = m / 12f * (s.x * s.x + s.y * s.y);
                inverseInertiaLocal = new Vector3(1f / ix, 1f / iy, 1f / iz);
            }
            if (isKinematic || freezeRotation) inverseInertiaLocal = Vector3.zero;
            if (shape == null && !isSphere) shape = ConvexShape.CreateBox();
            _initialised = true;
        }

        /// <summary>Takes the pose from the transform (after a teleport, or every step for kinematic bodies).</summary>
        public void ReadTransform()
        {
            rotation = transform.rotation;
            position = transform.position + rotation * comOffsetLocal;
            prevPosition = position; prevRotation = rotation;
            UpdateShape();
            if (_collider != null && _collider.broadphase != null) Physics.World.Touch(_collider, worldBounds);
        }

        internal void WriteTransform()
        {
            transform.rotation = rotation;
            transform.position = position - rotation * comOffsetLocal;
        }

        internal void UpdateShape()
        {
            if (isSphere || isCapsule)
            {
                worldBounds = WorldShape.bounds;
                return;
            }
            shape ??= ConvexShape.CreateBox();
            shape.SetBox(position, boxHalfExtents, rotation);
            Vector3 e = new Vector3(
                MathF.Abs(shape.faces[0].normal.x) * boxHalfExtents.x + MathF.Abs(shape.faces[2].normal.x) * boxHalfExtents.y + MathF.Abs(shape.faces[4].normal.x) * boxHalfExtents.z,
                MathF.Abs(shape.faces[0].normal.y) * boxHalfExtents.x + MathF.Abs(shape.faces[2].normal.y) * boxHalfExtents.y + MathF.Abs(shape.faces[4].normal.y) * boxHalfExtents.z,
                MathF.Abs(shape.faces[0].normal.z) * boxHalfExtents.x + MathF.Abs(shape.faces[2].normal.z) * boxHalfExtents.y + MathF.Abs(shape.faces[4].normal.z) * boxHalfExtents.z);
            worldBounds = Bounds.FromCenterExtents(position, e);
        }

        /// <summary>Filter for this body's collisions.</summary>
        internal QueryFilter Filter => new QueryFilter { layerMask = collisionMask, self = _collider, useLayerMatrix = useLayerMatrix && _collider != null };

        // ---- forces ----

        public void AddForce(Vector3 f, ForceMode mode = ForceMode.Force)
        {
            if (isKinematic) return;
            WakeUp();
            switch (mode)
            {
                case ForceMode.Force: force += f; break;
                case ForceMode.Acceleration: force += f * MathF.Max(mass, 1E-04f); break;
                case ForceMode.Impulse: linearVelocity += f * inverseMass; break;
                case ForceMode.VelocityChange: linearVelocity += f; break;
            }
        }

        public void AddTorque(Vector3 t, ForceMode mode = ForceMode.Force)
        {
            if (isKinematic || freezeRotation) return;
            WakeUp();
            switch (mode)
            {
                case ForceMode.Force: torque += t; break;
                case ForceMode.Acceleration: torque += t * MathF.Max(mass, 1E-04f); break;
                case ForceMode.Impulse: angularVelocity += ApplyInverseInertia(t); break;
                case ForceMode.VelocityChange: angularVelocity += t; break;
            }
        }

        /// <summary>Force (or impulse) applied at a world point: pushes and spins.</summary>
        public void AddForceAtPosition(Vector3 f, Vector3 worldPoint, ForceMode mode = ForceMode.Force)
        {
            if (isKinematic) return;
            AddForce(f, mode);
            Vector3 r = worldPoint - position;
            AddTorque(Vector3.Cross(r, f), mode);
        }

        public void AddImpulse(Vector3 impulse) => AddForce(impulse, ForceMode.Impulse);
        public void AddImpulseAtPosition(Vector3 impulse, Vector3 worldPoint) => AddForceAtPosition(impulse, worldPoint, ForceMode.Impulse);

        /// <summary>Explosion impulse falling off linearly with distance (Unity's AddExplosionForce); <paramref name="upwardsModifier"/> lifts the apparent origin.</summary>
        public void AddExplosionForce(float impulse, Vector3 origin, float radius, float upwardsModifier = 0f)
        {
            if (isKinematic || radius <= 0f) return;
            Vector3 closest = isRounded ? position + (origin - position).normalized * MathF.Min(sphereRadius, (origin - position).magnitude) : ClosestPointOnBox(origin);
            Vector3 d = closest - origin;
            float dist = d.magnitude;
            if (dist >= radius) return;
            float k = 1f - dist / radius;
            Vector3 dir = (closest - (origin - Vector3.up * upwardsModifier)).normalized;
            if (dir == Vector3.zero) dir = Vector3.up;
            AddForceAtPosition(dir * (impulse * k), closest, ForceMode.Impulse);
        }

        private Vector3 ClosestPointOnBox(Vector3 p)
        {
            Vector3 local = Quaternion.Inverse(rotation) * (p - position);
            local = new Vector3(Mathf.Clamp(local.x, -boxHalfExtents.x, boxHalfExtents.x), Mathf.Clamp(local.y, -boxHalfExtents.y, boxHalfExtents.y), Mathf.Clamp(local.z, -boxHalfExtents.z, boxHalfExtents.z));
            return position + rotation * local;
        }

        /// <summary>Velocity of the body's material at a world point.</summary>
        public Vector3 GetPointVelocity(Vector3 worldPoint) => linearVelocity + Vector3.Cross(angularVelocity, worldPoint - position);

        // The rotation matrix and the world inverse inertia are cached and re-derived only when the rotation (or the
        // local tensor) changed — the solver applies them thousands of times per substep, the rotation changes once.
        private Matrix3x3 _rotationMatrix = Matrix3x3.Identity, _invInertiaWorld;
        private Quaternion _cachedRotation = Quaternion.identity;
        private Vector3 _cachedInertia = new Vector3(float.NaN, 0f, 0f);

        private void RefreshRotationCache()
        {
            Quaternion q = rotation;
            bool rotChanged = q.x != _cachedRotation.x || q.y != _cachedRotation.y || q.z != _cachedRotation.z || q.w != _cachedRotation.w;
            if (!rotChanged && inverseInertiaLocal == _cachedInertia) return;
            if (rotChanged) { _rotationMatrix = Matrix3x3.FromQuaternion(q); _cachedRotation = q; }
            _invInertiaWorld = Matrix3x3.RotateDiagonal(_rotationMatrix, inverseInertiaLocal);
            _cachedInertia = inverseInertiaLocal;
        }

        /// <summary>World-space inverse inertia applied to a vector: R · I⁻¹ · Rᵀ · v.</summary>
        internal Vector3 ApplyInverseInertia(Vector3 v)
        {
            RefreshRotationCache();
            return _invInertiaWorld.Multiply(v);
        }

        /// <summary>World-space inverse inertia tensor.</summary>
        internal Matrix3x3 InverseInertiaWorld() { RefreshRotationCache(); return _invInertiaWorld; }

        /// <summary>Rotates a body-space vector into world space (cached rotation matrix: cheaper than the quaternion product).</summary>
        internal Vector3 RotateToWorld(Vector3 local) { RefreshRotationCache(); return _rotationMatrix.Multiply(local); }

        /// <summary>Rotates a world vector into body space.</summary>
        internal Vector3 RotateToLocal(Vector3 world) { RefreshRotationCache(); return _rotationMatrix.MultiplyTransposed(world); }

        /// <summary>Position-level correction (NGS): moves and rotates the pose directly by an impulse-like quantity at arm <paramref name="r"/>.</summary>
        internal void ApplyPositionImpulse(Vector3 impulse, Vector3 r)
        {
            if (isKinematic || sleeping) return;
            position += impulse * inverseMass;
            Vector3 dTheta = ApplyInverseInertia(Vector3.Cross(r, impulse));
            RotateBy(dTheta);
        }

        /// <summary>Position-level angular correction by a rotation vector.</summary>
        internal void ApplyPositionRotation(Vector3 angularImpulse)
        {
            if (isKinematic || sleeping) return;
            RotateBy(ApplyInverseInertia(angularImpulse));
        }

        private void RotateBy(Vector3 dTheta)
        {
            float angle = dTheta.magnitude;
            if (angle < 1E-09f) return;
            rotation = Quaternion.Normalize(Quaternion.AngleAxis(angle * Mathf.Rad2Deg, dTheta / angle) * rotation);
        }

        public void WakeUp() { sleeping = false; sleepTimer = 0f; }

        public void Sleep()
        {
            if (!canSleep || isKinematic) return;
            sleeping = true; linearVelocity = Vector3.zero; angularVelocity = Vector3.zero;
        }

        /// <summary>Teleports the body (velocities kept unless <paramref name="resetVelocity"/>).</summary>
        public void MovePosition(Vector3 worldPosition, bool resetVelocity = false)
        {
            transform.position = worldPosition;
            ReadTransform();
            if (resetVelocity) { linearVelocity = Vector3.zero; angularVelocity = Vector3.zero; }
            WakeUp();
        }

        public void MoveRotation(Quaternion worldRotation)
        {
            transform.rotation = worldRotation;
            ReadTransform();
            WakeUp();
        }

        // ---- stepping (called by the world) ----

        /// <summary>Start of a step: kinematic bodies follow their transform, the others refresh their world shape.</summary>
        internal void BeginStep()
        {
            if (!_initialised) ComputeMass();
            if (sleeping && !isKinematic) return; // nothing moved: shape, bounds and caches are still current
            prevPosition = position; prevRotation = rotation; hasPrev = true;
            if (isKinematic)
            {
                inverseMass = 0f; inverseInertiaLocal = Vector3.zero;
                ReadTransform(); // follow hand-made transform changes
                if (_collider != null) Physics.World.Touch(_collider, worldBounds);
                sleeping = false;
                return;
            }
            UpdateShape();
        }

        /// <summary>One substep of velocity integration: gravity, accumulated forces, damping.</summary>
        internal void IntegrateVelocity(float h)
        {
            if (isKinematic || sleeping) return;
            if (useGravity) linearVelocity += Physics.gravity * (gravityScale * h);
            linearVelocity += force * (inverseMass * h);
            if (!freezeRotation) angularVelocity += ApplyInverseInertia(torque) * h;
            linearVelocity *= MathF.Max(0f, 1f - linearDamping * h);
            angularVelocity *= MathF.Max(0f, 1f - angularDamping * h);
            if (maxSpeed > 0f && linearVelocity.sqrMagnitude > maxSpeed * maxSpeed) linearVelocity = linearVelocity.normalized * maxSpeed;
            if (maxAngularSpeed > 0f && angularVelocity.sqrMagnitude > maxAngularSpeed * maxAngularSpeed) angularVelocity = angularVelocity.normalized * maxAngularSpeed;
        }

        /// <summary>One substep of position integration (kinematic bodies move by their velocities too); <see cref="motionScale"/> clamps it at a continuous-collision contact.</summary>
        internal void IntegratePosition(float h)
        {
            if (sleeping && !isKinematic) return;
            float hs = h * motionScale;
            position += linearVelocity * hs;
            if (angularVelocity.sqrMagnitude > 1E-12f)
            {
                Quaternion w = new Quaternion(angularVelocity.x, angularVelocity.y, angularVelocity.z, 0f);
                Quaternion dq = w * rotation;
                rotation = Quaternion.Normalize(new Quaternion(rotation.x + dq.x * 0.5f * hs, rotation.y + dq.y * 0.5f * hs, rotation.z + dq.z * 0.5f * hs, rotation.w + dq.w * 0.5f * hs));
            }
            motionScale = 1f;
        }

        /// <summary>End of a step: forces consumed, shape and transform updated (interpolated bodies write their transform per frame instead).</summary>
        internal void EndStep()
        {
            force = Vector3.zero; torque = Vector3.zero;
            if (sleeping && !isKinematic) return;
            UpdateShape();
            if (_collider != null) Physics.World.Touch(_collider, worldBounds);
            if (!interpolate || isKinematic) WriteTransform();
        }

        /// <summary>Per rendered frame: the pose blended between the previous and the current fixed step.</summary>
        internal void InterpolateTransform(float alpha)
        {
            if (!interpolate || isKinematic || sleeping || !hasPrev) return;
            Vector3 p = Vector3.LerpUnclamped(prevPosition, position, alpha);
            Quaternion r = Quaternion.Slerp(prevRotation, rotation, alpha);
            transform.rotation = r;
            transform.position = p - r * comOffsetLocal;
        }

        internal void UpdateSleep(float dt)
        {
            if (AdvanceSleepTimer(dt) >= sleepDelay) Sleep();
        }

        /// <summary>
        /// Accumulates the time the body has been slower than its sleep threshold (reset when it moves) and returns
        /// it; the island decides whether the whole island sleeps. Kinematic / non-sleeping bodies report 0.
        /// </summary>
        internal float AdvanceSleepTimer(float dt)
        {
            if (isKinematic || !canSleep) { sleeping = false; sleepTimer = 0f; return 0f; }
            if (sleeping) return float.MaxValue;
            if (linearVelocity.sqrMagnitude < sleepThreshold * sleepThreshold && angularVelocity.sqrMagnitude < sleepThreshold * sleepThreshold) sleepTimer += dt;
            else sleepTimer = 0f;
            return sleepTimer;
        }

        /// <summary>Makes the cached rotation matrix / world inertia current (the solver does this before worker threads read a body).</summary>
        internal void WarmCaches() => RefreshRotationCache();

        /// <summary>Index in the solver's body list for the current step.</summary>
        internal int solverIndex = -1;

        internal void RaiseContacts()
        {
            if (Collided == null) return;
            for (int i = 0; i < contacts.Count; i++)
            {
                if (destroyed) return;
                try { Collided(this, contacts[i]); }
                catch (Exception e) { Debug.LogError($"RigidBody.Collided handler on '{name}' threw: {e}"); }
            }
        }
    }
}
