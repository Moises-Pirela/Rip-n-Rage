using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Ecs.View;
using RipNRage.Game.Content;

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// The player (9.4: a static handle — the GameObject the facades hung off is gone). <see cref="Spawn"/> builds the
    /// character entity (the force-driven capsule, locomotion, look, rage, health) and its view rig (pivot with the
    /// bob, camera with the lens and shake); the statics are the scene-side surface the session, the save, the falls
    /// and the automation probes use. Cleared by the scene reset (<see cref="Clear"/>).
    /// </summary>
    public static class PlayerLink
    {
        /// <summary>The character entity (health, weapons, rage ride on it). Null when no level is up.</summary>
        public static Entity PlayerEntity { get; private set; } = Entity.Null;
        /// <summary>The view rig: the pivot carries the look angles and the bob, the camera the lens and the shake.</summary>
        public static Entity PivotEntity { get; private set; } = Entity.Null;
        public static Entity CameraEntity { get; private set; } = Entity.Null;

        private static World W => Application.Current.World;

        /// <summary>Capsule radius 0.4 / height 1.3, centre 0.25 m up, 80 kg, riding 0.5 m clear of the ground.</summary>
        public const float Radius = 0.4f, Height = 1.3f, CentreY = 0.25f, Mass = 80f, RideHeight = 0.5f;

        /// <summary>View rig values from the prefab: the "Camera" node sits 0.5 m above the body, lens fov 77.</summary>
        public const float PivotHeight = 0.5f, FieldOfView = 77f, BobFrequency = 15f, BobWaveLength = 0.1f;

        public const float MaxHealthValue = 100f, MaxArmorValue = 100f;

        /// <summary>What the player stands on (the prefab's groundLayer 3895, as a collider-layer mask).</summary>
        private const int GroundMask = ~0;
        /// <summary>What the wall-run and roof probes treat as solid (the prefab's obstacleLayers 2304).</summary>
        private const int ObstacleMask = (1 << Layers.Default) | (1 << Layers.Walkable) | (1 << Layers.Wall) | (1 << Layers.OnlyGroundCollision);

        /// <summary>
        /// Creates the character at <paramref name="position"/> facing <paramref name="yaw"/>. Entities are created
        /// immediately (the build is not inside a system): everything the build does next must see them alive.
        /// </summary>
        public static void Spawn(World w, Vector3 position, float yaw)
        {
            var e = w.CreateEntity();
            w.Add(e, new LocalTransform { Position = position, Rotation = Quaternion.identity, Scale = Vector3.one });
            w.Add(e, new ColliderShape { Kind = ShapeKind.Capsule, Radius = Radius, Height = Height, Center = new Vector3(0f, CentreY, 0f) });
            w.Add(e, new BodyConfig { mass = Mass, interpolate = true, freezeRotation = true, canSleep = false }.Bake());
            w.Add(e, new BodyVelocity());
            w.Add(e, new CollisionFilter { Layer = Layers.Player, Mask = ~0, UseLayerMatrix = true });

            var motor = new CharacterMotorConfig
            {
                profile = CharacterProfileKind.Player,
                rideHeight = RideHeight,
                groundMask = GroundMask,
            }.Bake();
            w.Add(e, motor);

            var pm = new PlayerMotor { StandHeight = Height, ObstacleLayers = ObstacleMask, AirDashesLeft = 1, SettingsVersion = -1, PreviouslyGrounded = true };
            MovementSettings.Current.ApplyTo(ref pm);
            w.Add(e, pm);

            w.Add(e, new PlayerLook
            {
                Yaw = yaw, // the look owns the whole view yaw; the body itself never rotates (freezeRotation)
                Sensitivity = 5f, SensitivityMultiplier = 1f,
                ZSwayAngle = 5f, ZSwayMultiplier = 100f, ZSmoothSway = -50f, ZSmoothLerp = 0f,
            });
            w.Add(e, new PlayerTag());
            // falling out of the level is lethal (FallSystem, 8.4); the watch starts from the spawn, as if stood on
            w.Add(e, new FallWatch { LastGrounded = position, DropPosition = position });
            w.Add(e, new Rage()); // both meters empty, Normal
            w.Add(e, new RageFx()); // what it looks and sounds like
            // the player's trigger overlaps (checkpoints, water, pickups) have to be reported
            w.Add(e, new ReportCollisions());
            // hit points and armour (layer 5). Resistance 1: the player's armour eats a hit one for one.
            w.Add(e, new Combat.Health { Current = MaxHealthValue, Max = MaxHealthValue });
            w.Add(e, new Combat.Armor { Current = MaxArmorValue, Max = MaxArmorValue, Resistance = 1f });

            // ---- the view rig: pivot (bob + look) -> camera (lens + shake), both children of the character ----
            Vector3 pivotLocal = new Vector3(0f, PivotHeight, 0f);
            var pivot = w.CreateEntity();
            w.Add(pivot, new LocalTransform { Position = pivotLocal, Rotation = Quaternion.identity, Scale = Vector3.one });
            w.Add(pivot, new Parent { Value = e });
            w.Add(pivot, new ViewBob { Frequency = BobFrequency, WaveLength = BobWaveLength, BasePosition = pivotLocal });

            var camera = w.CreateEntity();
            w.Add(camera, new LocalTransform { Position = Vector3.zero, Rotation = Quaternion.identity, Scale = Vector3.one });
            w.Add(camera, new Parent { Value = pivot });
            w.Add(camera, new RipNRage.Engine.Ecs.Rendering.CameraLensConfig { fov = FieldOfView, near = 0.05f, far = 2000f }.Bake());
            w.Add(camera, CameraShake.Default);

            w.Add(e, new PlayerViewRig { Pivot = pivot, Camera = camera });

            PlayerEntity = e;
            PivotEntity = pivot;
            CameraEntity = camera;
        }

        /// <summary>The scene reset: the entities are destroyed with the world; the handles must not outlive them.</summary>
        public static void Clear()
        {
            PlayerEntity = Entity.Null;
            PivotEntity = Entity.Null;
            CameraEntity = Entity.Null;
        }

        // ---------------------------------------------------------------- the scene-side surface

        public static bool Alive => !PlayerEntity.IsNull && W != null && W.IsAlive(PlayerEntity);
        public static ref PlayerMotor Motor => ref W.Get<PlayerMotor>(PlayerEntity);
        public static ref CharacterMotor Character => ref W.Get<CharacterMotor>(PlayerEntity);
        public static ref PlayerLook Look => ref W.Get<PlayerLook>(PlayerEntity);
        public static bool HasMotor => Alive && W.Has<PlayerMotor>(PlayerEntity) && W.Has<CharacterMotor>(PlayerEntity);

        public static Vector3 Velocity => Alive && W.Has<BodyVelocity>(PlayerEntity) ? W.Get<BodyVelocity>(PlayerEntity).Linear : Vector3.zero;
        public static bool IsGrounded => HasMotor && Character.IsGrounded;
        public static Vector3 Position => Alive && W.Has<LocalTransform>(PlayerEntity) ? W.Get<LocalTransform>(PlayerEntity).Position : Vector3.zero;
        public static float Yaw => Alive && W.Has<PlayerLook>(PlayerEntity) ? W.Get<PlayerLook>(PlayerEntity).Yaw : 0f;

        public static float Health => Alive && W.Has<Combat.Health>(PlayerEntity) ? W.Get<Combat.Health>(PlayerEntity).Current : 0f;
        public static float Armor => Alive && W.Has<Combat.Armor>(PlayerEntity) ? W.Get<Combat.Armor>(PlayerEntity).Current : 0f;
        public static bool IsDead => Alive && (W.Has<Combat.Dead>(PlayerEntity) || Health <= 0f);

        /// <summary>Scripted damage from nowhere in particular (the probes; applied by HealthSystem at end of frame).</summary>
        public static void TakeDamage(float amount)
        {
            if (!Alive) return;
            W.Post(new Combat.DamageEvent { Target = PlayerEntity, Amount = amount, Point = Position, Direction = Vector3.forward });
        }

        /// <summary>Puts the body somewhere else at rest (fall respawn, save load, teleports).</summary>
        public static void Teleport(Vector3 position, float yaw)
        {
            if (!Alive) return;
            if (W.Has<LocalTransform>(PlayerEntity))
            {
                ref var lt = ref W.Get<LocalTransform>(PlayerEntity);
                lt.Position = position; // the physics sync reads a LocalTransform write as a teleport
            }
            ResetVelocity();
            if (W.Has<PlayerLook>(PlayerEntity)) { ref var l = ref W.Get<PlayerLook>(PlayerEntity); l.Yaw = yaw; l.Pitch = 0f; }
        }

        /// <summary>Drops every accumulated velocity and cancels the special moves.</summary>
        public static void ResetVelocity()
        {
            if (!Alive) return;
            if (W.Has<BodyVelocity>(PlayerEntity)) { ref var v = ref W.Get<BodyVelocity>(PlayerEntity); v.Linear = Vector3.zero; v.Angular = Vector3.zero; }
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
    }
}
