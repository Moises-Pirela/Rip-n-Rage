using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>What a <see cref="KinematicBody"/> does with its velocity when it touches something.</summary>
    public enum MotionMode
    {
        /// <summary>Slide along the surface (characters, enemies, pushed crates).</summary>
        Slide,
        /// <summary>Reflect off the surface with <see cref="KinematicBody.bounciness"/>, losing <see cref="KinematicBody.friction"/> of the tangential speed (grenades, debris, casings).</summary>
        Bounce,
        /// <summary>Stop dead at the first contact and report it (projectiles).</summary>
        Stop,
    }

    /// <summary>When a <see cref="KinematicBody"/> integrates its velocity.</summary>
    public enum SimulationMode
    {
        /// <summary>In the fixed time step, after the FixedUpdate callbacks (default: deterministic, independent of frame rate).</summary>
        FixedStep,
        /// <summary>Once per rendered frame, after Update (things whose motion must be smooth on screen at any frame rate).</summary>
        PerFrame,
        /// <summary>Never on its own: the owner calls <see cref="KinematicBody.Move"/> / <see cref="KinematicBody.Simulate"/>.</summary>
        Manual,
    }

    /// <summary>
    /// A collider-bearing object moved by code through the physics world. It is not a rigid body — nothing pushes
    /// it around, it has no mass, inertia or contacts with other bodies — but it moves *physically*: its velocity
    /// is integrated with gravity and drag, swept through the world so it never tunnels, and redirected by what it
    /// touches according to <see cref="motionMode"/>. Use it for enemies, projectiles, thrown objects, moving
    /// props; player characters keep using <see cref="CharacterController"/>, which is the same solver with the
    /// Unity-shaped API.
    ///
    /// The collision shape is an axis-aligned box: by default the bounds of the <see cref="Collider"/> on the same
    /// object, or <see cref="halfExtents"/> around <see cref="center"/> when set explicitly.
    /// </summary>
    public sealed class KinematicBody : Behaviour
    {
        // ---- motion ----
        public Vector3 velocity;
        public bool useGravity = true;
        public float gravityScale = 1f;
        /// <summary>Fraction of the velocity lost per second (0 = none).</summary>
        public float drag = 0f;
        public MotionMode motionMode = MotionMode.Slide;
        public SimulationMode simulation = SimulationMode.FixedStep;
        /// <summary>Bounce mode: kept fraction of the speed into the surface.</summary>
        public float bounciness = 0.4f;
        /// <summary>Bounce mode: fraction of the speed along the surface lost per bounce.</summary>
        public float friction = 0.2f;
        /// <summary>Bounce mode: below this speed on a floor the body comes to rest.</summary>
        public float restSpeed = 0.6f;
        /// <summary>Fastest the body may ever fall (metres per second, 0 = unlimited).</summary>
        public float terminalSpeed = 60f;

        // ---- collision ----
        /// <summary>Layers the body collides with (combined with the layer matrix of its own collider when <see cref="useLayerMatrix"/>).</summary>
        public int collisionMask = ~0;
        public bool useLayerMatrix = true;
        /// <summary>Also stop at trigger volumes (normally only solids block a body; triggers are reported through <see cref="ITriggerListener"/>).</summary>
        public bool collideWithTriggers = false;
        /// <summary>Extra hierarchy the body passes through (its shooter, its carrier).</summary>
        public Transform ignore;
        /// <summary>Per-collider veto: return false to pass through that collider.</summary>
        public Func<Collider, bool> collisionPredicate;
        /// <summary>Register the body's collider as a mover so trigger enter / exit callbacks fire for it.</summary>
        public bool reportTriggers = true;

        // ---- shape ----
        /// <summary>Half size of the collision box; zero = use the collider on this object.</summary>
        public Vector3 halfExtents;
        /// <summary>Centre of the collision box relative to the transform (only with explicit <see cref="halfExtents"/>).</summary>
        public Vector3 center;

        /// <summary>Mass the body pushes rigid bodies with when it runs into them (0 = never push).</summary>
        public float pushMass = 10f;

        // ---- character-style options ----
        public float floorMaxAngle = MoveSettings.DefaultFloorMaxAngle;
        public float stepHeight = 0f;
        public float snapLength = 0f;
        public float skin = 0f; // 0 = Physics.defaultSkin
        public int maxSlides = 4;

        /// <summary>Raised for every contact of a move, after the body has been moved.</summary>
        public event Action<KinematicBody, Contact> Collided;

        private readonly MoveResult _result = new MoveResult();
        private Collider _collider;
        private bool _colliderLooked;
        private bool _moverRegistered;
        private bool _stopped;

        // ---- state ----
        public bool isGrounded => _result.isGrounded;
        public Vector3 groundNormal => _result.groundNormal;
        public Collider ground => _result.ground;
        public bool isOnWall => _result.hitWall;
        public bool isOnCeiling => _result.hitCeiling;
        public CollisionFlags collisionFlags => _result.flags;
        /// <summary>Contacts of the last move.</summary>
        public IReadOnlyList<Contact> Contacts => _result.contacts;
        /// <summary>Full result of the last move.</summary>
        public MoveResult LastMove => _result;
        /// <summary>Stop mode: set once the body hit something; clear it (or <see cref="Warp"/>) to let it move again.</summary>
        public bool Stopped { get => _stopped; set => _stopped = value; }
        /// <summary>The collider that gives the body its shape (null with explicit half extents and no collider).</summary>
        public Collider Shape { get { if (!_colliderLooked) { _collider = GetComponent<Collider>(); _colliderLooked = true; } return _collider; } }

        protected override void Awake() => Physics.World.AddBody(this);

        protected override void OnDestroy()
        {
            Physics.World.RemoveBody(this);
            if (_moverRegistered && _collider != null) Physics.UnregisterMover(_collider);
        }

        /// <summary>The world-space collision shape right now: explicit box half extents, or the exact shape of the collider (sphere, capsule, box, brush).</summary>
        public CollisionShape WorldShape
        {
            get
            {
                if (halfExtents.sqrMagnitude > 0f) return CollisionShape.AxisAlignedBox(transform.position + center, halfExtents);
                var c = Shape;
                if (c != null && !(c is MeshCollider)) return c.shape;
                return CollisionShape.AxisAlignedBox(transform.position, new Vector3(0.25f, 0.25f, 0.25f));
            }
        }

        /// <summary>Bounds of the collision shape.</summary>
        public Bounds Box => WorldShape.bounds;

        /// <summary>Filter the body's sweeps use (its own collider and hierarchy are never hit).</summary>
        public QueryFilter Filter => new QueryFilter
        {
            layerMask = collisionMask, self = Shape, ignore = ignore,
            useLayerMatrix = useLayerMatrix && Shape != null, includeTriggers = collideWithTriggers, predicate = collisionPredicate,
        };

        private MoveSettings Settings(bool wasGrounded) => new MoveSettings
        {
            filter = Filter, skin = skin, maxSlides = maxSlides, floorMaxAngle = floorMaxAngle,
            stepHeight = stepHeight, snapLength = snapLength, minMove = 1E-05f, wasGrounded = wasGrounded,
        };

        /// <summary>Teleports without collision and clears the motion state.</summary>
        public void Warp(Vector3 position)
        {
            transform.position = position;
            Physics.World.Touch(Shape);
            velocity = Vector3.zero;
            _stopped = false;
            _result.Reset(WorldShape.center);
        }

        /// <summary>
        /// Moves the body by <paramref name="motion"/> (world units) through the world, sliding along what it
        /// touches; the transform is updated and contacts are raised. Does not touch <see cref="velocity"/> —
        /// use it for hand-driven motion, or with <see cref="SimulationMode.Manual"/>.
        /// </summary>
        public MoveResult Move(Vector3 motion)
        {
            EnsureMoverRegistered();
            CollisionShape shape = WorldShape;
            Vector3 offset = shape.center - transform.position;
            Physics.MoveAndSlide(in shape, motion, Settings(_result.isGrounded), _result);
            transform.position = _result.position - offset;
            Physics.World.Touch(Shape);
            Physics.PushRigidBodies(_result, _result.motion / MathF.Max(Time.deltaTime, 1E-04f), pushMass);
            RaiseContacts();
            return _result;
        }

        /// <summary>Integrates gravity / drag and moves by velocity for <paramref name="dt"/> seconds, applying the <see cref="motionMode"/> response.</summary>
        public void Simulate(float dt) => Integrate(dt);

        internal void Integrate(float dt)
        {
            if (dt <= 0f) return;
            if (_stopped && motionMode == MotionMode.Stop) return;
            EnsureMoverRegistered();

            if (useGravity) velocity += Physics.gravity * (gravityScale * dt);
            if (drag > 0f) velocity *= MathF.Max(0f, 1f - drag * dt);
            if (terminalSpeed > 0f && velocity.y < -terminalSpeed) velocity.y = -terminalSpeed;

            CollisionShape shape = WorldShape;
            Vector3 offset = shape.center - transform.position;
            Physics.MoveAndSlide(in shape, velocity * dt, Settings(_result.isGrounded), _result);
            transform.position = _result.position - offset;
            Physics.World.Touch(Shape);
            Physics.PushRigidBodies(_result, _result.motion / dt, pushMass);

            switch (motionMode)
            {
                case MotionMode.Slide:
                    velocity = _result.ClipVelocity(velocity);
                    break;
                case MotionMode.Bounce:
                    if (_result.contacts.Count > 0)
                    {
                        Vector3 n = _result.contacts[0].normal;
                        float into = Vector3.Dot(velocity, n);
                        if (into < 0f)
                        {
                            Vector3 normalPart = n * into;
                            Vector3 tangent = velocity - normalPart;
                            velocity = tangent * (1f - friction) - normalPart * bounciness;
                        }
                        if (_result.isGrounded && velocity.magnitude < restSpeed) velocity = Vector3.zero;
                    }
                    break;
                case MotionMode.Stop:
                    if (_result.contacts.Count > 0) { velocity = Vector3.zero; _stopped = true; }
                    break;
            }
            RaiseContacts();
        }

        private void RaiseContacts()
        {
            if (Collided == null) return;
            for (int i = 0; i < _result.contacts.Count; i++)
            {
                if (destroyed) return;
                try { Collided(this, _result.contacts[i]); }
                catch (Exception e) { Debug.LogError($"KinematicBody.Collided handler on '{name}' threw: {e}"); }
                if (motionMode == MotionMode.Stop) break; // one report for a body that stops at its first contact
            }
        }

        private void EnsureMoverRegistered()
        {
            if (_moverRegistered || !reportTriggers) return;
            var c = Shape;
            if (c == null) return;
            Physics.RegisterMover(c);
            _moverRegistered = true;
        }
    }
}
