using System;
using System.Collections.Generic;
using System.Globalization;

namespace RipNRage.Engine
{
    /// <summary>
    /// Every global knob of the physics simulation in one place: the fixed step, gravity, the collision world,
    /// the rigid-body solver, the kinematic movers and the character motors. Read from tunables.cfg
    /// (<c>[physics]</c>, <c>[collision]</c>, <c>[solver]</c>, <c>[movers]</c>, <c>[character]</c>,
    /// <c>[enemy_character]</c>) and pushed into the engine with <see cref="ApplyToWorld"/>; the level editor's
    /// physics panel edits and saves them, the game reloads the file live.
    /// </summary>
    public sealed class PhysicsSettings
    {
        // [physics]
        public float tickRate = 50f;           // fixed steps per second
        public int maxStepsPerFrame = 8;
        public float maxFrameDelta = 0.1f;      // seconds a frame may simulate at most
        public Vector3 gravity = new Vector3(0f, -9.81f, 0f);

        // [collision]
        public float contactMargin = 0.01f;    // shapes this close still touch
        public float skin = 0.01f;             // gap sweeps keep to surfaces
        public float broadphaseCell = 8f;      // grid cell size (m)
        public float ccdSkin = 0.003f;         // continuous collision stop distance
        public float maxBodySpeed = 80f;
        public float maxBodyAngularSpeed = 40f;
        public float sleepThreshold = 0.08f;
        public float sleepDelay = 0.6f;

        // [solver]
        public int substeps = 4;
        /// <summary>Rigid solver worker threads: 0 = one per core (max 16), 1 = single-threaded.</summary>
        public int threads = 0;
        public int velocityIterations = 1;
        public int relaxIterations = 1;
        public int positionIterations = 2;
        public PositionCorrectionMode positionCorrection = PositionCorrectionMode.Soft;
        public bool warmStarting = true;
        public int contactCacheLifetime = 3;
        public FrictionModel frictionModel = FrictionModel.StaticDynamic;
        public float slipSpeed = 0.05f;
        public float contactHertz = 30f;
        public float contactDamping = 10f;
        public float baumgarte = 0.2f;
        public float linearSlop = 0.005f;
        public float angularSlop = 2f;          // degrees
        public float maxLinearCorrection = 0.2f;
        public float maxBiasVelocity = 4f;
        public float restitutionThreshold = 1f;
        public float staticFriction = 0.6f;     // level geometry / movers
        public float dynamicFriction = 0.5f;
        public float staticRestitution = 0f;
        public CombineMode frictionCombine = CombineMode.Average;
        public CombineMode restitutionCombine = CombineMode.Maximum;
        public float jointHertz = 60f;
        public float jointDamping = 2f;

        // [movers]
        public float floorMaxAngle = 46f;
        public int maxSlides = 5;
        public float agentStepHeight = 0.4f;
        public float agentSlopeLimit = 50f;
        public float pushMass = 80f;

        // [character] / [enemy_character]
        public CharacterProfile player = new CharacterProfile();
        public CharacterProfile enemy = new CharacterProfile { rideHeight = 0.35f, acceleration = 40f, airAcceleration = 8f, stopDeceleration = 40f, mass = 85f };

        /// <summary>The settings currently applied to the engine.</summary>
        public static PhysicsSettings Current { get; private set; } = new PhysicsSettings();
        /// <summary>Raised after <see cref="ApplyToWorld"/>.</summary>
        public static event Action Applied;

        public PhysicsSettings Clone()
        {
            var c = (PhysicsSettings)MemberwiseClone();
            c.player = player.Clone(); c.enemy = enemy.Clone();
            return c;
        }

        public void CopyFrom(PhysicsSettings o)
        {
            tickRate = o.tickRate; maxStepsPerFrame = o.maxStepsPerFrame; maxFrameDelta = o.maxFrameDelta; gravity = o.gravity;
            contactMargin = o.contactMargin; skin = o.skin; broadphaseCell = o.broadphaseCell; ccdSkin = o.ccdSkin;
            maxBodySpeed = o.maxBodySpeed; maxBodyAngularSpeed = o.maxBodyAngularSpeed; sleepThreshold = o.sleepThreshold; sleepDelay = o.sleepDelay;
            substeps = o.substeps; threads = o.threads; velocityIterations = o.velocityIterations; relaxIterations = o.relaxIterations; positionIterations = o.positionIterations;
            positionCorrection = o.positionCorrection; warmStarting = o.warmStarting; contactCacheLifetime = o.contactCacheLifetime; frictionModel = o.frictionModel; slipSpeed = o.slipSpeed;
            contactHertz = o.contactHertz; contactDamping = o.contactDamping; baumgarte = o.baumgarte; linearSlop = o.linearSlop; angularSlop = o.angularSlop;
            maxLinearCorrection = o.maxLinearCorrection; maxBiasVelocity = o.maxBiasVelocity;
            restitutionThreshold = o.restitutionThreshold; staticFriction = o.staticFriction; dynamicFriction = o.dynamicFriction; staticRestitution = o.staticRestitution;
            frictionCombine = o.frictionCombine; restitutionCombine = o.restitutionCombine;
            jointHertz = o.jointHertz; jointDamping = o.jointDamping;
            floorMaxAngle = o.floorMaxAngle; maxSlides = o.maxSlides; agentStepHeight = o.agentStepHeight; agentSlopeLimit = o.agentSlopeLimit; pushMass = o.pushMass;
            player.CopyFrom(o.player); enemy.CopyFrom(o.enemy);
        }

        // ------------------------------------------------------------------ cfg

        public void Apply(Dictionary<string, Dictionary<string, string>> sections)
        {
            float F(string sec, string key, float cur) => Get(sections, sec, key, cur, v => float.Parse(v, CultureInfo.InvariantCulture));
            int I(string sec, string key, int cur) => Get(sections, sec, key, cur, v => int.Parse(v, CultureInfo.InvariantCulture));
            Vector3 V(string sec, string key, Vector3 cur) => Get(sections, sec, key, cur, ParseVector);
            bool B(string sec, string key, bool cur) => Get(sections, sec, key, cur, ParseBool);
            TEnum E<TEnum>(string sec, string key, TEnum cur) where TEnum : struct, Enum => Get(sections, sec, key, cur, v => Enum.Parse<TEnum>(v.Trim(), true));

            tickRate = Mathf.Clamp(F("physics", "tick_rate", tickRate), 10f, 1000f);
            maxStepsPerFrame = Mathf.Clamp(I("physics", "max_steps_per_frame", maxStepsPerFrame), 1, 64);
            maxFrameDelta = Mathf.Clamp(F("physics", "max_frame_delta", maxFrameDelta), 0.01f, 1f);
            gravity = V("physics", "gravity", gravity);

            contactMargin = Mathf.Clamp(F("collision", "contact_margin", contactMargin), 0.001f, 0.2f);
            skin = Mathf.Clamp(F("collision", "skin", skin), 0.001f, 0.1f);
            broadphaseCell = Mathf.Clamp(F("collision", "broadphase_cell", broadphaseCell), 1f, 64f);
            ccdSkin = Mathf.Clamp(F("collision", "ccd_skin", ccdSkin), 0.0005f, 0.05f);
            maxBodySpeed = Mathf.Clamp(F("collision", "max_body_speed", maxBodySpeed), 1f, 1000f);
            maxBodyAngularSpeed = Mathf.Clamp(F("collision", "max_body_angular_speed", maxBodyAngularSpeed), 1f, 500f);
            sleepThreshold = Mathf.Clamp(F("collision", "sleep_threshold", sleepThreshold), 0f, 5f);
            sleepDelay = Mathf.Clamp(F("collision", "sleep_delay", sleepDelay), 0f, 30f);

            substeps = Mathf.Clamp(I("solver", "substeps", substeps), 1, 16);
            threads = Mathf.Clamp(I("solver", "threads", threads), 0, 64);
            velocityIterations = Mathf.Clamp(I("solver", "velocity_iterations", velocityIterations), 1, 16);
            relaxIterations = Mathf.Clamp(I("solver", "relax_iterations", relaxIterations), 0, 16);
            positionIterations = Mathf.Clamp(I("solver", "position_iterations", positionIterations), 1, 16);
            positionCorrection = E("solver", "position_correction", positionCorrection);
            warmStarting = B("solver", "warm_starting", warmStarting);
            contactCacheLifetime = Mathf.Clamp(I("solver", "contact_cache_lifetime", contactCacheLifetime), 0, 60);
            frictionModel = E("solver", "friction_model", frictionModel);
            slipSpeed = Mathf.Clamp(F("solver", "slip_speed", slipSpeed), 0f, 5f);
            contactHertz = Mathf.Clamp(F("solver", "contact_hertz", contactHertz), 1f, 240f);
            contactDamping = Mathf.Clamp(F("solver", "contact_damping", contactDamping), 0f, 100f);
            baumgarte = Mathf.Clamp(F("solver", "baumgarte", baumgarte), 0.01f, 1f);
            linearSlop = Mathf.Clamp(F("solver", "linear_slop", linearSlop), 0f, 0.1f);
            angularSlop = Mathf.Clamp(F("solver", "angular_slop", angularSlop), 0f, 45f);
            maxLinearCorrection = Mathf.Clamp(F("solver", "max_linear_correction", maxLinearCorrection), 0.01f, 2f);
            maxBiasVelocity = Mathf.Clamp(F("solver", "max_bias_velocity", maxBiasVelocity), 0.1f, 50f);
            restitutionThreshold = Mathf.Clamp(F("solver", "restitution_threshold", restitutionThreshold), 0f, 20f);
            staticFriction = Mathf.Clamp(F("solver", "static_friction", staticFriction), 0f, 2f);
            dynamicFriction = Mathf.Clamp(F("solver", "dynamic_friction", dynamicFriction), 0f, 2f);
            staticRestitution = Mathf.Clamp(F("solver", "static_restitution", staticRestitution), 0f, 1f);
            frictionCombine = E("solver", "friction_combine", frictionCombine);
            restitutionCombine = E("solver", "restitution_combine", restitutionCombine);
            jointHertz = Mathf.Clamp(F("solver", "joint_hertz", jointHertz), 1f, 240f);
            jointDamping = Mathf.Clamp(F("solver", "joint_damping", jointDamping), 0f, 100f);

            floorMaxAngle = Mathf.Clamp(F("movers", "floor_max_angle", floorMaxAngle), 1f, 89f);
            maxSlides = Mathf.Clamp(I("movers", "max_slides", maxSlides), 1, 16);
            agentStepHeight = Mathf.Clamp(F("movers", "agent_step_height", agentStepHeight), 0f, 2f);
            agentSlopeLimit = Mathf.Clamp(F("movers", "agent_slope_limit", agentSlopeLimit), 1f, 89f);
            pushMass = Mathf.Clamp(F("movers", "push_mass", pushMass), 0f, 10000f);

            ApplyProfile(sections, "character", player);
            ApplyProfile(sections, "enemy_character", enemy);
        }

        private static void ApplyProfile(Dictionary<string, Dictionary<string, string>> sections, string sec, CharacterProfile p)
        {
            float F(string key, float cur) => Get(sections, sec, key, cur, v => float.Parse(v, CultureInfo.InvariantCulture));
            p.rideHeight = Mathf.Clamp(F("ride_height", p.rideHeight), 0.05f, 2f);
            p.rideFrequency = Mathf.Clamp(F("ride_frequency", p.rideFrequency), 0.5f, 30f);
            p.rideDamping = Mathf.Clamp(F("ride_damping", p.rideDamping), 0f, 5f);
            p.maxRideAcceleration = Mathf.Clamp(F("max_ride_acceleration", p.maxRideAcceleration), 1f, 1000f);
            p.groundTolerance = Mathf.Clamp(F("ground_tolerance", p.groundTolerance), 0f, 2f);
            p.maxSlopeAngle = Mathf.Clamp(F("max_slope", p.maxSlopeAngle), 1f, 89f);
            p.acceleration = Mathf.Clamp(F("acceleration", p.acceleration), 0f, 1000f);
            p.airAcceleration = Mathf.Clamp(F("air_acceleration", p.airAcceleration), 0f, 1000f);
            p.stopDeceleration = Mathf.Clamp(F("stop_deceleration", p.stopDeceleration), 0f, 1000f);
            p.jumpGrace = Mathf.Clamp(F("jump_grace", p.jumpGrace), 0f, 2f);
            p.mass = Mathf.Clamp(F("mass", p.mass), 0.1f, 10000f);
        }

        /// <summary>The cfg lines of every section, for saving (key, value, comment).</summary>
        public List<(string section, string key, string value, string comment)> Lines()
        {
            var l = new List<(string, string, string, string)>();
            string Fm(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            l.Add(("physics", "tick_rate", Fm(tickRate), "fixed physics steps per second (Time.fixedDeltaTime = 1 / tick_rate)"));
            l.Add(("physics", "max_steps_per_frame", maxStepsPerFrame.ToString(), "fixed steps one frame may run before the simulation falls behind real time"));
            l.Add(("physics", "max_frame_delta", Fm(maxFrameDelta), "seconds a single frame may simulate at most (hitches slow the game instead of skipping)"));
            l.Add(("physics", "gravity", $"{Fm(gravity.x)}, {Fm(gravity.y)}, {Fm(gravity.z)}", "m/s²"));
            l.Add(("collision", "contact_margin", Fm(contactMargin), "shapes closer than this count as touching (keeps resting contacts alive)"));
            l.Add(("collision", "skin", Fm(skin), "gap sweeps keep between a mover and what it stops at"));
            l.Add(("collision", "broadphase_cell", Fm(broadphaseCell), "static collider grid cell size (m)"));
            l.Add(("collision", "ccd_skin", Fm(ccdSkin), "stop distance of continuous collision sweeps"));
            l.Add(("collision", "max_body_speed", Fm(maxBodySpeed), "rigid bodies are clamped to this (m/s)"));
            l.Add(("collision", "max_body_angular_speed", Fm(maxBodyAngularSpeed), "rad/s"));
            l.Add(("collision", "sleep_threshold", Fm(sleepThreshold), "bodies slower than this (m/s, rad/s) for sleep_delay seconds fall asleep"));
            l.Add(("collision", "sleep_delay", Fm(sleepDelay), "seconds"));
            l.Add(("solver", "substeps", substeps.ToString(), "substeps per fixed step (more = stiffer stacks, slower)"));
            l.Add(("solver", "threads", threads.ToString(), "rigid solver worker threads: 0 = one per core (max 16), 1 = single-threaded"));
            l.Add(("solver", "velocity_iterations", velocityIterations.ToString(), "biased velocity passes per substep"));
            l.Add(("solver", "relax_iterations", relaxIterations.ToString(), "unbiased (relax) passes per substep; removes the energy the bias adds"));
            l.Add(("solver", "position_iterations", positionIterations.ToString(), "position passes per substep (NGS mode only)"));
            l.Add(("solver", "position_correction", positionCorrection.ToString(), "Soft (spring contacts, default) | Baumgarte (velocity bias) | NGS (position passes)"));
            l.Add(("solver", "warm_starting", warmStarting ? "true" : "false", "carry contact / joint impulses over from the previous step (false = cold start)"));
            l.Add(("solver", "contact_cache_lifetime", contactCacheLifetime.ToString(), "steps a manifold's impulses survive a brief separation"));
            l.Add(("solver", "friction_model", frictionModel.ToString(), "StaticDynamic (stick up to μs·N, slide at μd·N) | Coulomb (μd only)"));
            l.Add(("solver", "slip_speed", Fm(slipSpeed), "tangential speed (m/s) below which a contact sticks with static friction instead of sliding"));
            l.Add(("solver", "contact_hertz", Fm(contactHertz), "Soft mode: contact spring stiffness (capped at a quarter of the substep rate)"));
            l.Add(("solver", "contact_damping", Fm(contactDamping), "Soft mode: contact spring damping ratio"));
            l.Add(("solver", "baumgarte", Fm(baumgarte), "Baumgarte / NGS: fraction of the penetration corrected per substep"));
            l.Add(("solver", "linear_slop", Fm(linearSlop), "penetration tolerated without correction (m)"));
            l.Add(("solver", "angular_slop", Fm(angularSlop), "joint angle error tolerated without correction (degrees)"));
            l.Add(("solver", "max_linear_correction", Fm(maxLinearCorrection), "NGS: largest position push per pass (m)"));
            l.Add(("solver", "max_bias_velocity", Fm(maxBiasVelocity), "fastest penetration recovery (m/s)"));
            l.Add(("solver", "restitution_threshold", Fm(restitutionThreshold), "approach speed above which things bounce"));
            l.Add(("solver", "static_friction", Fm(staticFriction), "static friction of level geometry and movers (colliders without a material)"));
            l.Add(("solver", "dynamic_friction", Fm(dynamicFriction), "sliding friction of level geometry and movers"));
            l.Add(("solver", "static_restitution", Fm(staticRestitution), "bounciness of level geometry and movers"));
            l.Add(("solver", "friction_combine", frictionCombine.ToString(), "how two surfaces' friction combines when neither has a material: Average | Minimum | Maximum | Multiply"));
            l.Add(("solver", "restitution_combine", restitutionCombine.ToString(), "same for bounciness"));
            l.Add(("solver", "joint_hertz", Fm(jointHertz), "Soft mode: joint stiffness (ragdolls, hinges, welds)"));
            l.Add(("solver", "joint_damping", Fm(jointDamping), "Soft mode: joint damping ratio"));
            l.Add(("movers", "floor_max_angle", Fm(floorMaxAngle), "kinematic movers: steepest surface that is a floor (degrees)"));
            l.Add(("movers", "max_slides", maxSlides.ToString(), "kinematic movers: slide iterations per move"));
            l.Add(("movers", "agent_step_height", Fm(agentStepHeight), "kinematic nav agents: highest ledge stepped up"));
            l.Add(("movers", "agent_slope_limit", Fm(agentSlopeLimit), "kinematic nav agents: steepest walkable slope"));
            l.Add(("movers", "push_mass", Fm(pushMass), "kinematic characters push rigid bodies as if they weighed this much"));
            ProfileLines(l, "character", player);
            ProfileLines(l, "enemy_character", enemy);
            return l;
        }

        private static void ProfileLines(List<(string, string, string, string)> l, string sec, CharacterProfile p)
        {
            string Fm(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            l.Add((sec, "mass", Fm(p.mass), "kg"));
            l.Add((sec, "ride_height", Fm(p.rideHeight), "gap the capsule floats above the ground (ledges below it are climbed for free)"));
            l.Add((sec, "ride_frequency", Fm(p.rideFrequency), "ride spring stiffness (Hz)"));
            l.Add((sec, "ride_damping", Fm(p.rideDamping), "ride spring damping ratio (1 = critical)"));
            l.Add((sec, "max_ride_acceleration", Fm(p.maxRideAcceleration), "m/s² the spring may apply at most"));
            l.Add((sec, "ground_tolerance", Fm(p.groundTolerance), "how far below the ride height the ground still counts (stairs down)"));
            l.Add((sec, "max_slope", Fm(p.maxSlopeAngle), "steepest standable slope (degrees)"));
            l.Add((sec, "acceleration", Fm(p.acceleration), "ground acceleration towards the wanted velocity (m/s²)"));
            l.Add((sec, "air_acceleration", Fm(p.airAcceleration), "air control (m/s²)"));
            l.Add((sec, "stop_deceleration", Fm(p.stopDeceleration), "deceleration with no input (m/s²)"));
            l.Add((sec, "jump_grace", Fm(p.jumpGrace), "seconds the spring lets go after a jump"));
        }

        // ------------------------------------------------------------------ apply

        /// <summary>Pushes the values into the engine (time step, gravity, world, solver, defaults and the live characters).</summary>
        public void ApplyToWorld()
        {
            Current.CopyFrom(this);
            Time.fixedDeltaTime = 1f / Mathf.Clamp(tickRate, 10f, 1000f);
            Time.maximumDeltaTime = maxFrameDelta;
            Scene.MaxFixedSteps = maxStepsPerFrame;
            Physics.gravity = gravity;

            ContactGeneration.ContactMargin = contactMargin;
            Physics.defaultSkin = skin;
            Physics.World.CellSize = broadphaseCell;
            var rigid = Physics.World.Rigid;
            rigid.ccdSkin = ccdSkin;
            rigid.substeps = substeps; rigid.threads = threads; rigid.velocityIterations = velocityIterations; rigid.relaxIterations = relaxIterations; rigid.positionIterations = positionIterations;
            rigid.positionCorrection = positionCorrection; rigid.warmStarting = warmStarting; rigid.contactCacheLifetime = contactCacheLifetime; rigid.frictionModel = frictionModel; rigid.slipSpeed = slipSpeed;
            rigid.contactHertz = contactHertz; rigid.dampingRatio = contactDamping;
            rigid.baumgarte = baumgarte; rigid.linearSlop = linearSlop; rigid.angularSlop = angularSlop * Mathf.Deg2Rad; rigid.maxLinearCorrection = maxLinearCorrection;
            rigid.maxBiasVelocity = maxBiasVelocity; rigid.restitutionThreshold = restitutionThreshold;
            rigid.staticFriction = staticFriction; rigid.dynamicFriction = dynamicFriction; rigid.staticRestitution = staticRestitution;
            rigid.frictionCombine = frictionCombine; rigid.restitutionCombine = restitutionCombine;
            RigidBody.DefaultMaxSpeed = maxBodySpeed; RigidBody.DefaultMaxAngularSpeed = maxBodyAngularSpeed;
            RigidBody.DefaultSleepThreshold = sleepThreshold; RigidBody.DefaultSleepDelay = sleepDelay;
            foreach (var b in Physics.World.RigidBodies)
            {
                if (b.destroyed) continue;
                b.maxSpeed = maxBodySpeed; b.maxAngularSpeed = maxBodyAngularSpeed; b.sleepThreshold = sleepThreshold; b.sleepDelay = sleepDelay;
            }
            Constraint.DefaultHertz = jointHertz; Constraint.DefaultDampingRatio = jointDamping;
            foreach (var j in Physics.World.Constraints) { j.hertz = jointHertz; j.dampingRatio = jointDamping; }

            MoveSettings.DefaultFloorMaxAngle = floorMaxAngle; MoveSettings.DefaultMaxSlides = maxSlides;
            AI.NavMeshAgent.DefaultStepHeight = agentStepHeight; AI.NavMeshAgent.DefaultSlopeLimit = agentSlopeLimit;
            CharacterController.DefaultPushMass = pushMass;

            CharacterBody.PlayerProfile.CopyFrom(player);
            CharacterBody.EnemyProfile.CopyFrom(enemy);
            var scene = Scene.Current;
            if (scene != null)
            {
                foreach (var c in scene.FindObjectsOfType<CharacterBody>()) c.ApplyProfile();
                foreach (var a in scene.FindObjectsOfType<AI.NavMeshAgent>()) { a.stepHeight = agentStepHeight; a.slopeLimit = agentSlopeLimit; }
            }
            Applied?.Invoke();
        }

        // ------------------------------------------------------------------ helpers

        private static T Get<T>(Dictionary<string, Dictionary<string, string>> sections, string sec, string key, T cur, Func<string, T> parse)
        {
            if (sections == null || !sections.TryGetValue(sec, out var kv) || !kv.TryGetValue(key, out var v)) return cur;
            try { return parse(v); }
            catch (Exception) { Debug.LogWarning($"tunables.cfg: bad value for [{sec}] {key}: {v}"); return cur; }
        }

        private static bool ParseBool(string v)
        {
            v = v.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "on" || v == "yes") return true;
            if (v == "0" || v == "false" || v == "off" || v == "no") return false;
            throw new FormatException(v);
        }

        private static Vector3 ParseVector(string v)
        {
            var p = v.Split(',');
            float X(int i) => p.Length > i ? float.Parse(p[i].Trim(), CultureInfo.InvariantCulture) : 0f;
            return new Vector3(X(0), X(1), X(2));
        }
    }
}
