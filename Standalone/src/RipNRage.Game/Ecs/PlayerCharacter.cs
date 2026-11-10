using System;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Audio;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content;
using PhysicsApi = RipNRage.Engine.Physics;

namespace RipNRage.Game.Ecs
{
    /// <summary>Marks the player's character entity (one per world).</summary>
    [SparseComponent]
    public struct PlayerTag : IComponent { }

    /// <summary>
    /// Where the player is looking. <see cref="PlayerLookSystem"/> integrates the mouse / stick delta into
    /// <see cref="Yaw"/> / <see cref="Pitch"/> every frame (not every fixed step: the view must not stutter) and
    /// derives <see cref="Roll"/> — the strafe sway plus whatever <see cref="ExternalRoll"/> the wall run asks for.
    /// The camera entity takes its rotation from these three angles; <see cref="PlayerMoveSystem"/> takes its
    /// movement basis from <see cref="Yaw"/> alone (the old "Orientation" child).
    /// </summary>
    public struct PlayerLook : IComponent
    {
        public float Yaw, Pitch, Roll;
        public float Sensitivity, SensitivityMultiplier;
        /// <summary>Roll the movement asks for (a wall run tilts the view away from the wall); smoothed into Roll.</summary>
        public float ExternalRoll;
        public float ZSwayAngle, ZSwayMultiplier, ZSmoothSway, ZSmoothLerp;
        internal float SmoothExternalRoll, SwayRoll;
    }

    /// <summary>
    /// The player's locomotion: the tunables from <c>tunables.cfg [movement]</c> (<see cref="MovementSettings"/>,
    /// tracked live) and the state of the three standalone moves — slide, wall run and dash.
    ///
    /// <para><see cref="PlayerMoveSystem"/> turns the input into a wanted horizontal velocity and hands it to the
    /// entity's <see cref="CharacterMotor"/> as intent; the motor accelerates the 80 kg body towards it with forces,
    /// so the player pushes crates with its mass, is shoved by blasts and slides along walls through the contact
    /// solver. Jumps, dashes and knockback are the motor's <c>Pending*</c> commands.</para>
    /// </summary>
    public struct PlayerMotor : IComponent
    {
        // ---- locomotion tunables ----
        public bool EnableBunnyHop;
        public float WalkSpeed, RunSpeed, BunnyHopSpeed, JumpSpeed;
        public float BackwardsPercent, SidePercent;
        public float GravityMultiplier;
        public float SmoothInput, SmoothVelocity, SmoothDirection;
        // ---- slide ----
        public bool EnableSlide;
        public float SlideSpeedBoost, SlideFriction, SlideMinSpeed, SlideStartSpeed, SlideHeight, SlideCameraDrop, SlideCooldown, SlideSteer;
        // ---- wall run ----
        public bool EnableWallRun;
        public float WallRunMaxTime, WallRunGravity, WallRunSpeed, WallCheckDistance, WallJumpUp, WallJumpAway, WallRunTilt, WallRunEntryBoost;
        // ---- dash ----
        public bool EnableDash;
        public float DashSpeed, DashDuration, DashCooldown;
        public int AirDashes;
        /// <summary>Capsule height to return to when a slide ends (captured from the collider at spawn).</summary>
        public float StandHeight;
        /// <summary>Collider layers the wall / roof probes treat as solid.</summary>
        public int ObstacleLayers;

        // ---- state (read by the HUD, the overlay, the weapons) ----
        public bool IsSliding, IsWallRunning, IsDashing;
        /// <summary>-1 = wall on the left, +1 = wall on the right, 0 = none.</summary>
        public int WallSide;
        /// <summary>Camera drop the slide asks for (metres, negative); HeadBob follows it.</summary>
        public float CameraHeightOffset;
        /// <summary>Speed the motor is being asked for this step (the overlay's MOVER line).</summary>
        public float CurrentSpeed;

        // ---- internals ----
        internal Vector2 SmoothInputVector;
        internal Vector3 SmoothMoveDir, FinalMove;
        internal float SmoothCurrentSpeed;
        internal Vector3 SlideDir, WallNormal;
        internal float SlideSpeed, SlideTime, SlideCooldownTimer;
        internal float WallRunTimer, WallJumpCooldown;
        internal Vector3 DashDir;
        internal float DashTimer, DashCooldownTimer;
        internal int AirDashesLeft;
        internal float InAirTimer;
        internal bool PreviouslyGrounded;
        internal float StepCycle, NextStep;
        internal int SettingsVersion;
    }

    // ================================================================= look

    /// <summary>Integrates the look input into <see cref="PlayerLook"/> (per frame, before the transforms are built).</summary>
    [UpdateAfter(typeof(EcsInputSystem))]
    public sealed class PlayerLookSystem : SystemBase, ITick
    {
        private Query _q;
        public override void OnCreate() => _q = World.Query().With<PlayerLook>().Build();

        public void Tick(SystemContext ctx)
        {
            var input = World.GetSingleton<InputState>();
            float dt = Time.deltaTime;
            foreach (var chunk in _q)
            {
                var looks = chunk.Get<PlayerLook>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref var look = ref looks[i];
                    // Unity's Mouse/delta was pixels per frame and the old controller scaled it by fixedDeltaTime;
                    // keeping that factor keeps the configured sensitivity numbers meaning the same thing.
                    float scale = look.Sensitivity * look.SensitivityMultiplier * Time.fixedDeltaTime;
                    look.Yaw += input.Look.x * scale;
                    look.Pitch = Mathf.Clamp(look.Pitch - input.Look.y * scale, -90f, 90f);

                    float targetSway = Mathf.Clamp(input.Move.x * look.ZSwayMultiplier * dt * look.ZSmoothSway, -look.ZSwayAngle, look.ZSwayAngle);
                    look.SwayRoll = Mathf.LerpAngle(look.SwayRoll, input.Move.x != 0f ? targetSway : 0f, dt * look.ZSmoothLerp);
                    look.SmoothExternalRoll = Mathf.Lerp(look.SmoothExternalRoll, look.ExternalRoll, dt * 8f);
                    look.Roll = look.SwayRoll + look.SmoothExternalRoll;
                }
            }
        }
    }

    // ================================================================= movement

    /// <summary>
    /// The player's locomotion, a fixed step at a time: smoothing, speed, slide / wall run / dash, then the wanted
    /// velocity handed to the <see cref="CharacterMotor"/>. Runs before the motor so the intent it writes is consumed
    /// by the same step.
    /// </summary>
    [UpdateAfter(typeof(PhysicsSyncSystem)), UpdateBefore(typeof(CharacterMotorSystem))]
    public sealed class PlayerMoveSystem : SystemBase, IFixedTick
    {
        private Query _q;
        public override void OnCreate() =>
            _q = World.Query().With<PlayerMotor>().With<CharacterMotor>().With<PlayerLook>().WithReadOnly<LocalTransform>().Build();

        public void FixedTick(SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            if (dt <= 0f) return;
            var input = World.GetSingleton<InputState>();
            // one queued jump press is spent per fixed step, by whichever move claims it (slide / wall / ground)
            bool jump = World.Singleton<InputState>().ConsumeJump();

            foreach (var chunk in _q)
            {
                var pms = chunk.Get<PlayerMotor>(); var motors = chunk.Get<CharacterMotor>();
                var looks = chunk.Get<PlayerLook>(); var xforms = chunk.Get<LocalTransform>();
                var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    Step(ctx, ents[i], ref pms[i], ref motors[i], ref looks[i], xforms[i], input, ref jump, dt);
                }
            }
        }

        private void Step(SystemContext ctx, Entity e, ref PlayerMotor p, ref CharacterMotor m, ref PlayerLook look,
                          in LocalTransform lt, in InputState input, ref bool jump, float dt)
        {
            if (p.SettingsVersion != MovementSettings.Version)
            {
                // tunables.cfg [movement] changed (file reload or the editor's physics panel): take the new values
                p.SettingsVersion = MovementSettings.Version;
                MovementSettings.Current.ApplyTo(ref p);
            }

            bool grounded = m.IsGrounded;
            Vector3 velocity = World.TryGet<BodyVelocity>(e, out var bv) ? bv.Linear : Vector3.zero;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

            // the movement basis: the old "Orientation" child, yaw only
            Quaternion yaw = Quaternion.Euler(0f, look.Yaw, 0f);
            Vector3 forward = yaw * Vector3.forward, right = yaw * Vector3.right;

            // ---- speed ----
            float speed = p.EnableBunnyHop ? (grounded ? p.RunSpeed : p.BunnyHopSpeed) : p.RunSpeed;
            if (Math.Abs(input.Move.y - (-1f)) < 1f) speed *= p.BackwardsPercent;
            if (input.Move.x != 0f && input.Move.y == 0f) speed *= p.SidePercent;
            if (PlayerState.Current != null && PlayerState.Current.CurrentRageState != null)
                speed *= PlayerState.Current.CurrentRageState.movementSpeedModifier;
            speed *= StyleMeter.SpeedMultiplier; // S rank: faster

            // ---- smoothing (input, speed, direction) ----
            Vector2 rawInput = input.Move.normalized;
            p.SmoothInputVector = Vector2.Lerp(p.SmoothInputVector, rawInput, dt * p.SmoothInput);
            p.SmoothCurrentSpeed = Mathf.Lerp(p.SmoothCurrentSpeed, speed, dt * p.SmoothVelocity);

            Vector3 wish = forward * p.SmoothInputVector.y + right * p.SmoothInputVector.x;
            // on a slope the wish direction follows the floor (the legacy code meant to do this but probed nothing,
            // so it projected on a zero normal and the call did nothing; the motor's ground normal is the real one)
            if (grounded) wish = Vector3.ProjectOnPlane(wish, m.GroundNormal);
            p.SmoothMoveDir = Vector3.Lerp(p.SmoothMoveDir, wish, dt * p.SmoothDirection);

            Vector3 planar = p.SmoothMoveDir * p.SmoothCurrentSpeed;
            p.FinalMove = new Vector3(planar.x, p.FinalMove.y, planar.z);

            // ---- the standalone moves override the horizontal motion computed above ----
            UpdateSlide(ref p, ref m, in lt, input, ref jump, horizontal, grounded, dt);
            UpdateWallRun(ctx, ref p, ref m, ref look, in lt, input, ref jump, horizontal, grounded, dt);
            UpdateDash(ctx, ref p, ref m, in lt, input, forward, right, grounded, dt);

            // ---- landing ----
            if (!p.PreviouslyGrounded && grounded) Sfx.Play(GameAssets.Land);

            // ---- gravity + jump timing ----
            if (!p.IsDashing)
            {
                m.GravityScale = p.GravityMultiplier * (p.IsWallRunning ? p.WallRunGravity : 1f);
                if (grounded)
                {
                    p.InAirTimer = 0f;
                    if (jump && !p.IsSliding) { jump = false; m.PendingJump = p.JumpSpeed; Sfx.Play(GameAssets.Jump); }
                }
                else p.InAirTimer += dt;
            }

            // ---- footsteps ----
            ProgressStepCycle(ref p, velocity, grounded, dt);

            // ---- hand the wanted velocity to the motor ----
            m.Desired = new Vector3(p.FinalMove.x, 0f, p.FinalMove.z);
            p.CurrentSpeed = m.Desired.magnitude;
            p.IsDashing = m.IsDashing;
            p.PreviouslyGrounded = grounded;
        }

        private static void ProgressStepCycle(ref PlayerMotor p, Vector3 velocity, bool grounded, float dt)
        {
            if (velocity.sqrMagnitude <= 0f) return;
            if (p.SmoothInputVector.sqrMagnitude <= 1E-04f) return;
            p.StepCycle += velocity.magnitude * dt;
            if (p.StepCycle <= p.NextStep) return;
            p.NextStep = p.StepCycle + 2f; // m_StepInterval from the prefab
            if (grounded) Sfx.Play(GameAssets.RandomFootstep());
        }

        // ---------------------------------------------------------------- slide

        private void UpdateSlide(ref PlayerMotor p, ref CharacterMotor m, in LocalTransform lt, in InputState input,
                                 ref bool jump, Vector3 horizontal, bool grounded, float dt)
        {
            if (p.SlideCooldownTimer > 0f) p.SlideCooldownTimer -= dt;

            if (!p.IsSliding)
            {
                if (p.EnableSlide && grounded && !p.IsWallRunning && input.SlidePressed && p.SlideCooldownTimer <= 0f)
                {
                    Vector3 wish = new Vector3(p.FinalMove.x, 0f, p.FinalMove.z);
                    Vector3 dir = horizontal.sqrMagnitude > wish.sqrMagnitude ? horizontal : wish;
                    if (dir.magnitude >= p.SlideStartSpeed * 0.75f)
                    {
                        p.IsSliding = true;
                        p.SlideTime = 0f;
                        p.SlideDir = dir.normalized;
                        p.SlideSpeed = Mathf.Max(dir.magnitude, p.RunSpeed) + p.SlideSpeedBoost;
                        m.PendingHeight = p.SlideHeight; // the capsule shrinks from the top, the bottom stays on the ground
                    }
                }
                p.CameraHeightOffset = Mathf.Lerp(p.CameraHeightOffset, 0f, dt * 10f);
                return;
            }

            p.SlideTime += dt;
            // steer a little, lose speed to friction (downhill keeps it: gravity adds along the slope through the motor)
            Vector3 side = Vector3.Cross(Vector3.up, p.SlideDir);
            p.SlideDir = (p.SlideDir + side * (input.Move.x * p.SlideSteer * dt)).normalized;
            p.SlideSpeed -= p.SlideFriction * dt;
            p.FinalMove.x = p.SlideDir.x * p.SlideSpeed;
            p.FinalMove.z = p.SlideDir.z * p.SlideSpeed;
            p.CameraHeightOffset = Mathf.Lerp(p.CameraHeightOffset, -p.SlideCameraDrop, dt * 12f);

            bool wantStop = p.SlideSpeed < p.SlideMinSpeed || !grounded || (p.SlideTime > 0.25f && !input.SlideHeld);
            if (jump && grounded)
            {
                // slide jump: keep the momentum, pop up
                jump = false;
                m.PendingJump = p.JumpSpeed;
                Sfx.Play(GameAssets.Jump);
                wantStop = true;
            }
            if (wantStop && !RoofAbove(ref p, in lt, m)) EndSlide(ref p, ref m);
            else if (wantStop) p.SlideSpeed = Mathf.Max(p.SlideSpeed, 2f); // crawl on until there is room to stand
        }

        private static void EndSlide(ref PlayerMotor p, ref CharacterMotor m)
        {
            p.IsSliding = false;
            p.SlideCooldownTimer = p.SlideCooldown;
            m.PendingHeight = p.StandHeight;
        }

        /// <summary>Is there something overhead that stops the capsule standing back up?</summary>
        private static bool RoofAbove(ref PlayerMotor p, in LocalTransform lt, in CharacterMotor m) =>
            PhysicsApi.SphereCast(lt.Position, 0.1f, Vector3.up, out _, p.StandHeight, p.ObstacleLayers);

        // ---------------------------------------------------------------- wall run

        private void UpdateWallRun(SystemContext ctx, ref PlayerMotor p, ref CharacterMotor m, ref PlayerLook look,
                                   in LocalTransform lt, in InputState input, ref bool jump, Vector3 horizontal, bool grounded, float dt)
        {
            if (p.WallJumpCooldown > 0f) p.WallJumpCooldown -= dt;
            if (!p.EnableWallRun || p.IsSliding || grounded)
            {
                if (p.IsWallRunning) EndWallRun(ref p, ref look);
                return;
            }

            Quaternion yaw = Quaternion.Euler(0f, look.Yaw, 0f);
            Vector3 fwd = yaw * Vector3.forward;
            Vector3 origin = lt.Position + new Vector3(0f, 0.9f, 0f); // the old "_standCenter", mid-capsule
            Vector3 rightDir = Vector3.Cross(Vector3.up, fwd).normalized;
            float reach = 0.4f + p.WallCheckDistance; // capsule radius + the configured distance
            int side = 0; Vector3 normal = Vector3.zero;
            if (PhysicsApi.Raycast(origin, rightDir, out var rh, reach, p.ObstacleLayers) && Mathf.Abs(rh.normal.y) < 0.35f) { side = 1; normal = rh.normal; }
            else if (PhysicsApi.Raycast(origin, -rightDir, out var lh, reach, p.ObstacleLayers) && Mathf.Abs(lh.normal.y) < 0.35f) { side = -1; normal = lh.normal; }

            bool pushingForward = input.Move.y > 0.3f;
            if (!p.IsWallRunning)
            {
                if (side != 0 && pushingForward && p.WallJumpCooldown <= 0f && p.InAirTimer > 0.05f && horizontal.magnitude > 3f)
                {
                    p.IsWallRunning = true; p.WallSide = side; p.WallNormal = normal; p.WallRunTimer = 0f;
                    m.PendingImpulse += new Vector3(0f, Mathf.Max(0f, p.WallRunEntryBoost - Mathf.Max(horizontal.y, 0f)), 0f);
                }
                else return;
            }
            else if (side == 0 || !pushingForward || p.WallRunTimer > p.WallRunMaxTime)
            {
                EndWallRun(ref p, ref look);
                return;
            }
            else { p.WallSide = side; p.WallNormal = normal; }

            p.WallRunTimer += dt;
            look.ExternalRoll = -p.WallSide * p.WallRunTilt;
            Vector3 along = Vector3.ProjectOnPlane(fwd, p.WallNormal);
            if (along.sqrMagnitude < 1E-04f) { EndWallRun(ref p, ref look); return; }
            along.Normalize();
            Vector3 run = along * p.WallRunSpeed - p.WallNormal * 1.5f; // hug the wall so the side ray keeps seeing it
            p.FinalMove.x = run.x; p.FinalMove.z = run.z;
            m.PendingMinVerticalSpeed = -3f; // glide instead of falling

            if (jump)
            {
                // wall jump: away from the wall, up, keep the run speed
                jump = false;
                Vector3 launch = along * (p.WallRunSpeed * 0.8f) + p.WallNormal * p.WallJumpAway;
                p.FinalMove.x = launch.x; p.FinalMove.z = launch.z;
                m.PendingImpulse += new Vector3(launch.x - horizontal.x, 0f, launch.z - horizontal.z);
                m.PendingJump = p.WallJumpUp;
                p.WallJumpCooldown = 0.35f;
                Sfx.Play(GameAssets.Jump);
                EndWallRun(ref p, ref look);
            }
        }

        private static void EndWallRun(ref PlayerMotor p, ref PlayerLook look)
        {
            p.IsWallRunning = false;
            p.WallSide = 0;
            look.ExternalRoll = 0f;
        }

        // ---------------------------------------------------------------- dash

        private void UpdateDash(SystemContext ctx, ref PlayerMotor p, ref CharacterMotor m, in LocalTransform lt,
                                in InputState input, Vector3 forward, Vector3 right, bool grounded, float dt)
        {
            if (p.DashCooldownTimer > 0f) p.DashCooldownTimer -= dt;
            if (grounded && !m.IsDashing) p.AirDashesLeft = p.AirDashes;

            if (!m.IsDashing && p.EnableDash && input.DashPressed && p.DashCooldownTimer <= 0f && (grounded || p.AirDashesLeft > 0))
            {
                Vector3 dir = forward * input.Move.y + right * input.Move.x;
                if (dir.sqrMagnitude < 0.05f) dir = forward;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1E-04f) return;
                dir.Normalize();
                if (p.IsSliding) EndSlide(ref p, ref m);
                if (p.IsWallRunning) { p.IsWallRunning = false; p.WallSide = 0; }
                if (!grounded) p.AirDashesLeft--;
                p.DashDir = dir; p.DashTimer = p.DashDuration; p.DashCooldownTimer = p.DashCooldown;
                m.PendingDash = dir * p.DashSpeed; m.PendingDashTime = p.DashDuration; // gravity off for the burst
                EnragedInjector<CameraRecoil>.GetInstance()?.AddTrauma(0.14f);
                Sfx.Play(ProceduralSounds.Dash, 0.6f);
                Content.Vfx.GameVfx.Spawn("dash", lt.Position - dir * 0.6f, -dir);
            }

            if (!m.IsDashing) return;
            p.DashTimer -= dt;
            p.FinalMove.x = p.DashDir.x * p.DashSpeed;
            p.FinalMove.z = p.DashDir.z * p.DashSpeed;
            if (p.DashTimer <= 0f)
            {
                // hand back a fast but normal run so the dash does not end in a dead stop
                p.FinalMove.x = p.DashDir.x * p.RunSpeed * 1.1f;
                p.FinalMove.z = p.DashDir.z * p.RunSpeed * 1.1f;
            }
        }
    }
}
