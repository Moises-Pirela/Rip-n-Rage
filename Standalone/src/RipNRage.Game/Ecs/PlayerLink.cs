using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// MIGRATION BRIDGE (shrinks as layers 5–8 port the gameplay, gone in layer 9): ties the PlayerCharacter
    /// GameObject to its ECS character entity. The entity is the truth — it is the body the solver moves — and this
    /// copies its pose back onto the GameObject hierarchy every frame so the scripts that have not been ported yet
    /// (combat, HUD, death sequence, save / load, the enemies' target lookups) keep reading
    /// <c>PlayerState.Current.transform.position</c> and get the right answer.
    ///
    /// <para>It also aims the camera pivot and the "Orientation" child from <see cref="PlayerLook"/>, which is where
    /// the view angles live now.</para>
    /// </summary>
    public sealed class PlayerLink : MonoBehaviour
    {
        /// <summary>The live player character entity (<see cref="Entity.Null"/> before the prefab finishes building).</summary>
        public static Entity PlayerEntity { get; private set; } = Entity.Null;
        public static PlayerLink Current { get; private set; }

        public Entity entity { get; private set; }
        /// <summary>The "Camera" pivot: takes the look angles (and the slide's camera drop through HeadBob).</summary>
        public Transform cameraPivot;
        /// <summary>The "Orientation" child: yaw only, the movement basis other scripts still read.</summary>
        public Transform orientation;

        private static World W => Application.Current.World;

        /// <summary>Capsule radius 0.4 / height 1.3, centre 0.25 m up, 80 kg, riding 0.5 m clear of the ground.</summary>
        public const float Radius = 0.4f, Height = 1.3f, CentreY = 0.25f, Mass = 80f, RideHeight = 0.5f;

        /// <summary>
        /// Creates the character entity for <paramref name="go"/> at its current transform: the force-driven capsule
        /// (<see cref="Body"/> + <see cref="ColliderShape"/> + <see cref="CharacterMotor"/>) plus the player's own
        /// locomotion and look components. Structural work goes through the command buffer, so the components are
        /// not readable until the next sync point.
        /// </summary>
        public static PlayerLink Attach(GameObject go, Transform cameraPivot, Transform orientation)
        {
            var link = go.AddComponent<PlayerLink>();
            link.cameraPivot = cameraPivot;
            link.orientation = orientation;

            var cmd = W.Commands;
            var e = cmd.CreateEntity();
            cmd.Add(e, new LocalTransform { Position = go.transform.position, Rotation = Quaternion.identity, Scale = Vector3.one });
            cmd.Add(e, new ColliderShape { Kind = ShapeKind.Capsule, Radius = Radius, Height = Height, Center = new Vector3(0f, CentreY, 0f) });
            cmd.Add(e, new BodyConfig { mass = Mass, interpolate = true, freezeRotation = true, canSleep = false }.Bake());
            cmd.Add(e, new BodyVelocity());
            cmd.Add(e, new CollisionFilter { Layer = Layers.Player, Mask = ~0, UseLayerMatrix = true });

            var motor = new CharacterMotorConfig
            {
                profile = CharacterProfileKind.Player,
                rideHeight = RideHeight,
                groundMask = GroundMask,
            }.Bake();
            cmd.Add(e, motor);

            var pm = new PlayerMotor { StandHeight = Height, ObstacleLayers = ObstacleMask, AirDashesLeft = 1, SettingsVersion = -1, PreviouslyGrounded = true };
            MovementSettings.Current.ApplyTo(ref pm);
            cmd.Add(e, pm);

            cmd.Add(e, new PlayerLook
            {
                Sensitivity = 5f, SensitivityMultiplier = 1f,
                ZSwayAngle = 5f, ZSwayMultiplier = 100f, ZSmoothSway = -50f, ZSmoothLerp = 0f,
            });
            cmd.Add(e, new PlayerTag());
            // the player's trigger overlaps (checkpoints, water, pickups) have to be reported
            cmd.Add(e, new ReportCollisions());

            link.entity = e;
            PlayerEntity = e;
            Current = link;
            EcsObjects.Register(e, go); // so trigger events naming this entity reach the player's GameObject
            return link;
        }

        /// <summary>What the player stands on (the prefab's groundLayer 3895, as a collider-layer mask).</summary>
        private const int GroundMask = ~0;
        /// <summary>What the wall-run and roof probes treat as solid (the prefab's obstacleLayers 2304).</summary>
        private const int ObstacleMask = (1 << Layers.Default) | (1 << Layers.Walkable) | (1 << Layers.Wall) | (1 << Layers.OnlyGroundCollision);

        // ---------------------------------------------------------------- component access for the legacy scripts

        public bool Alive => W != null && W.IsAlive(entity);
        public ref PlayerMotor Motor => ref W.Get<PlayerMotor>(entity);
        public ref CharacterMotor Character => ref W.Get<CharacterMotor>(entity);
        public ref PlayerLook Look => ref W.Get<PlayerLook>(entity);
        public bool HasMotor => Alive && W.Has<PlayerMotor>(entity) && W.Has<CharacterMotor>(entity);

        public Vector3 Velocity => Alive && W.Has<BodyVelocity>(entity) ? W.Get<BodyVelocity>(entity).Linear : Vector3.zero;
        public bool IsGrounded => HasMotor && Character.IsGrounded;

        /// <summary>Puts the body somewhere else at rest (fall respawn, save load, teleports).</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            if (!Alive) return;
            if (W.Has<LocalTransform>(entity))
            {
                ref var lt = ref W.Get<LocalTransform>(entity);
                lt.Position = position; // the physics sync reads a LocalTransform write as a teleport
            }
            ResetVelocity();
            if (W.Has<PlayerLook>(entity)) { ref var l = ref W.Get<PlayerLook>(entity); l.Yaw = yaw; l.Pitch = 0f; }
            transform.position = position;
            // PlayerLook owns the whole view yaw now, and the camera pivot is a child of this transform: leaving the
            // spawn rotation on the root would add it a second time, aiming the camera and the movement in different
            // directions (the legacy controller kept the yaw relative to a rotated root instead).
            transform.rotation = Quaternion.identity;
        }

        /// <summary>Drops every accumulated velocity and cancels the special moves.</summary>
        public void ResetVelocity()
        {
            if (!Alive) return;
            if (W.Has<BodyVelocity>(entity)) { ref var v = ref W.Get<BodyVelocity>(entity); v.Linear = Vector3.zero; v.Angular = Vector3.zero; }
            if (!HasMotor) return;
            ref var m = ref Motor;
            ref var c = ref Character;
            if (m.IsSliding) { m.IsSliding = false; c.PendingHeight = m.StandHeight; }
            m.IsWallRunning = false; m.WallSide = 0;
            m.SmoothMoveDir = Vector3.zero; m.SmoothInputVector = Vector2.zero; m.SmoothCurrentSpeed = 0f; m.FinalMove = Vector3.zero;
            m.InAirTimer = 0f;
            c.PendingStopDash = true;
            c.Desired = Vector3.zero;
        }

        /// <summary>Knockback / rocket jump as a velocity change on the body.</summary>
        public void AddImpulse(Vector3 impulse)
        {
            const float maxImpulse = 40f; // defensive cap: a mistuned knockback must not fling the player into orbit
            if (impulse.sqrMagnitude > maxImpulse * maxImpulse) impulse = impulse.normalized * maxImpulse;
            if (!HasMotor) return;
            ref var c = ref Character;
            if (c.IsDashing) c.PendingStopDash = true;
            c.PendingImpulse += impulse;
        }

        // ---------------------------------------------------------------- per-frame bridge

        /// <summary>
        /// The entity is created while the prefab is being built, before <c>Instantiate</c> places the GameObject, so
        /// the spawn position only exists on the transform. The first frame hands it over (GameObject → entity);
        /// after that the entity is the truth and the transform follows it.
        /// </summary>
        private bool _spawned;

        protected override void LateUpdate()
        {
            if (!Alive || !W.Has<LocalTransform>(entity)) return;

            if (!_spawned)
            {
                _spawned = true;
                Teleport(transform.position, transform.rotation.eulerAngles.y);
                return;
            }

            // the entity is the truth: the GameObject hierarchy follows it
            transform.position = W.Get<LocalTransform>(entity).Position;

            if (!W.Has<PlayerLook>(entity)) return;
            var look = W.Get<PlayerLook>(entity);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(look.Pitch, look.Yaw, look.Roll);
            if (orientation != null) orientation.localRotation = Quaternion.Euler(0f, look.Yaw, 0f);
        }

        protected override void OnDestroy()
        {
            EcsObjects.Unregister(entity);
            if (Current == this) { Current = null; PlayerEntity = Entity.Null; }
            if (Alive) W.Commands.DestroyEntity(entity);
        }
    }
}
