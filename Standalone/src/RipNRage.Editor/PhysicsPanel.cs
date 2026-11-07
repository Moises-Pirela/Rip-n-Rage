using System;
using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Graphics;
using RipNRage.Game.Content;
using RipNRage.ImGuiIntegration;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace RipNRage.Editor
{
    /// <summary>
    /// The level editor's physics settings panel (F9): every global knob of the simulation — the fixed step and
    /// gravity, collision, the rigid-body solver, the kinematic movers, the player's and the enemies' character
    /// motors, and the player's movement (speeds, jump, slide, wall run, dash). Values are edited with drag cells
    /// (Shift ×10, Ctrl ÷10, wheel, right click resets), applied to this editor's world as you drag, and written
    /// to tunables.cfg with SAVE — which a running game picks up within half a second.
    /// </summary>
    public sealed class PhysicsPanel
    {
        public bool Open;
        private readonly PhysicsSettings _phys = new PhysicsSettings();
        private readonly MovementSettings _move = new MovementSettings();
        private static readonly Vec4 Accent = new Vec4(1f, 0.78f, 0.25f, 1f);
        private static readonly Vec4 Dim = new Vec4(0.55f, 0.58f, 0.63f, 1f);
        private int _tab; // 0 physics, 1 characters, 2 player movement
        private bool _dirty;
        private string _status = ""; private float _statusUntil;

        public PhysicsPanel() { Revert(); }

        /// <summary>Loads the current engine values (the file as last applied).</summary>
        public void Revert()
        {
            _phys.CopyFrom(PhysicsSettings.Current);
            _move.CopyFrom(MovementSettings.Current);
            _dirty = false;
        }

        public void Defaults()
        {
            _phys.CopyFrom(new PhysicsSettings());
            _move.CopyFrom(new MovementSettings());
            Apply();
            _dirty = true;
            Status("DEFAULTS (NOT SAVED)");
        }

        /// <summary>Pushes the panel's values into the running engine (this editor; the game only sees saved files).</summary>
        private void Apply()
        {
            _phys.ApplyToWorld();
            _move.Commit();
        }

        public void Save()
        {
            var t = Application.Current?.Tunables;
            if (t == null) { Status("NO tunables.cfg FOUND (START THE EDITOR FROM THE Standalone FOLDER)"); return; }
            try
            {
                var lines = new List<(string, string, string, string)>();
                lines.AddRange(_phys.Lines());
                lines.AddRange(_move.Lines());
                t.Update(lines);
                Apply();
                _dirty = false;
                Status("SAVED " + t.Path + " (A RUNNING GAME RELOADS IT)");
            }
            catch (Exception e) { Status("SAVE FAILED: " + e.Message); }
        }

        private void Status(string s) { _status = s; _statusUntil = Time.unscaledTime + 4f; }

        public void HandleKeys()
        {
            if (!Open) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F9)) Open = false;
            if (ctrl && Input.GetKeyDown(KeyCode.S)) Save();
            if (Input.GetKeyDown(KeyCode.Tab)) _tab = (_tab + 1) % 3;
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>
        /// The settings form, as a Dear ImGui window. The rows below are the same list of knobs as before; only the
        /// widgets changed, so a value still applies to this editor's world the moment it is dragged.
        /// </summary>
        public void Draw(ImGuiController imgui)
        {
            if (!Open) return;
            var centre = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(centre, ImGuiCond.Appearing, new Vec2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vec2(900f, 720f), ImGuiCond.Appearing);
            bool open = Open;
            if (!ImGui.Begin("Physics settings (F9)", ref open)) { Open = open; ImGui.End(); return; }
            Open = open;

            if (ImGui.Button(_dirty ? "Save to tunables.cfg *" : "Save to tunables.cfg")) Save();
            ImGui.SameLine();
            if (ImGui.Button("Revert to file")) { Revert(); Apply(); Status("Reverted"); }
            ImGui.SameLine();
            if (ImGui.Button("Engine defaults")) Defaults();
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"step {1000f / MathF.Max(_phys.tickRate, 1f):0.0} ms   substep {1000f / MathF.Max(_phys.tickRate * _phys.substeps, 1f):0.00} ms");

            ImGui.TextColored(Time.unscaledTime < _statusUntil ? Accent : Dim,
                Time.unscaledTime < _statusUntil ? _status
                : "Drag a value to change it; right-click resets it to the default. Changes apply here at once; Save writes tunables.cfg for the game.");

            bool changed = false;
            int col = 0;          // the form is two columns wide
            float half = 0f;

            void Header(string text)
            {
                col = 0;
                ImGui.SeparatorText(text);
            }
            void NextCell()
            {
                half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
                if (col == 1) ImGui.SameLine();
                ImGui.SetNextItemWidth(half * 0.45f);
                col = (col + 1) % 2;
            }
            float Value(string label, float v, float step, float min, float max, float reset, string fmt = "0.00")
            {
                NextCell();
                float nv = v;
                string f = fmt == "0" ? "%.0f" : fmt == "0.0" ? "%.1f" : fmt == "0.000" ? "%.3f" : "%.2f";
                if (ImGui.DragFloat(label, ref nv, step, min, max, f)) changed = true;
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) { nv = reset; changed = true; }
                return Mathf.Clamp(nv, min, max);
            }
            int IntValue(string label, int v, int min, int max, int reset)
            {
                NextCell();
                int nv = v;
                if (ImGui.DragInt(label, ref nv, 0.2f, min, max)) changed = true;
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) { nv = reset; changed = true; }
                return Mathf.Clamp(nv, min, max);
            }
            bool Flag(string label, bool v)
            {
                NextCell();
                bool nv = v;
                if (ImGui.Checkbox(label, ref nv)) changed = true;
                return nv;
            }
            TEnum Choice<TEnum>(string label, TEnum v, TEnum reset) where TEnum : struct, Enum
            {
                NextCell();
                var values = (TEnum[])Enum.GetValues(typeof(TEnum));
                var names = Enum.GetNames(typeof(TEnum));
                int i = Math.Max(0, Array.IndexOf(values, v));
                TEnum nv = v;
                if (ImGui.Combo(label, ref i, names, names.Length)) { nv = values[i]; changed = true; }
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) { nv = reset; changed = true; }
                return nv;
            }

            if (ImGui.BeginTabBar("##phystabs"))
            {
                if (ImGui.BeginTabItem("Step / collision / solver")) { _tab = 0; ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Characters / movers")) { _tab = 1; ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Player movement")) { _tab = 2; ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
            ImGui.BeginChild("##settings", new Vec2(-1f, -1f));

            if (_tab == 0)
            {
                var p = _phys;
                Header("FIXED STEP");
                p.tickRate = Value("TICK RATE (HZ)", p.tickRate, 1f, 10f, 1000f, 50f, "0");
                p.maxStepsPerFrame = IntValue("MAX STEPS / FRAME", p.maxStepsPerFrame, 1, 64, 8);
                p.maxFrameDelta = Value("MAX FRAME DELTA (S)", p.maxFrameDelta, 0.01f, 0.01f, 1f, 0.1f);
                Header("GRAVITY (M/S²)");
                p.gravity.x = Value("GRAVITY X", p.gravity.x, 0.1f, -100f, 100f, 0f);
                p.gravity.y = Value("GRAVITY Y", p.gravity.y, 0.1f, -100f, 100f, -9.81f);
                p.gravity.z = Value("GRAVITY Z", p.gravity.z, 0.1f, -100f, 100f, 0f);
                Header("COLLISION");
                p.contactMargin = Value("CONTACT MARGIN (M)", p.contactMargin, 0.001f, 0.001f, 0.2f, 0.01f, "0.000");
                p.skin = Value("SWEEP SKIN (M)", p.skin, 0.001f, 0.001f, 0.1f, 0.01f, "0.000");
                p.broadphaseCell = Value("BROADPHASE CELL (M)", p.broadphaseCell, 1f, 1f, 64f, 8f, "0");
                p.ccdSkin = Value("CCD SKIN (M)", p.ccdSkin, 0.0005f, 0.0005f, 0.05f, 0.003f, "0.0000");
                p.maxBodySpeed = Value("MAX BODY SPEED", p.maxBodySpeed, 1f, 1f, 1000f, 80f, "0");
                p.maxBodyAngularSpeed = Value("MAX ANGULAR SPEED", p.maxBodyAngularSpeed, 1f, 1f, 500f, 40f, "0");
                p.sleepThreshold = Value("SLEEP THRESHOLD", p.sleepThreshold, 0.01f, 0f, 5f, 0.08f);
                p.sleepDelay = Value("SLEEP DELAY (S)", p.sleepDelay, 0.05f, 0f, 30f, 0.6f);
                Header("RIGID-BODY SOLVER");
                p.substeps = IntValue("SUBSTEPS", p.substeps, 1, 16, 4);
                p.threads = IntValue("SOLVER THREADS (0 = AUTO)", p.threads, 0, 64, 0);
                p.positionCorrection = Choice("POSITION CORRECTION", p.positionCorrection, PositionCorrectionMode.Soft);
                p.velocityIterations = IntValue("VELOCITY ITERATIONS", p.velocityIterations, 1, 16, 1);
                p.relaxIterations = IntValue("RELAX ITERATIONS", p.relaxIterations, 0, 16, 1);
                p.positionIterations = IntValue("POSITION ITERATIONS (NGS)", p.positionIterations, 1, 16, 2);
                p.warmStarting = Flag("WARM STARTING", p.warmStarting);
                p.contactCacheLifetime = IntValue("CONTACT CACHE LIFETIME", p.contactCacheLifetime, 0, 60, 3);
                p.frictionModel = Choice("FRICTION MODEL", p.frictionModel, FrictionModel.StaticDynamic);
                p.slipSpeed = Value("SLIP SPEED (M/S)", p.slipSpeed, 0.01f, 0f, 5f, 0.05f);
                Header("POSITION CORRECTION");
                p.contactHertz = Value("CONTACT HERTZ (SOFT)", p.contactHertz, 1f, 1f, 240f, 30f, "0");
                p.contactDamping = Value("CONTACT DAMPING (SOFT)", p.contactDamping, 0.5f, 0f, 100f, 10f, "0.0");
                p.baumgarte = Value("BAUMGARTE FACTOR", p.baumgarte, 0.01f, 0.01f, 1f, 0.2f);
                p.maxBiasVelocity = Value("MAX BIAS VELOCITY", p.maxBiasVelocity, 0.1f, 0.1f, 50f, 4f, "0.0");
                p.linearSlop = Value("LINEAR SLOP (M)", p.linearSlop, 0.001f, 0f, 0.1f, 0.005f, "0.000");
                p.angularSlop = Value("ANGULAR SLOP (DEG)", p.angularSlop, 0.5f, 0f, 45f, 2f, "0.0");
                p.maxLinearCorrection = Value("MAX NGS CORRECTION (M)", p.maxLinearCorrection, 0.01f, 0.01f, 2f, 0.2f);
                p.restitutionThreshold = Value("RESTITUTION THRESHOLD", p.restitutionThreshold, 0.1f, 0f, 20f, 1f, "0.0");
                Header("LEVEL SURFACES (COLLIDERS WITHOUT A MATERIAL)");
                p.staticFriction = Value("STATIC FRICTION", p.staticFriction, 0.05f, 0f, 2f, 0.6f);
                p.dynamicFriction = Value("DYNAMIC FRICTION", p.dynamicFriction, 0.05f, 0f, 2f, 0.5f);
                p.staticRestitution = Value("RESTITUTION", p.staticRestitution, 0.05f, 0f, 1f, 0f);
                p.frictionCombine = Choice("FRICTION COMBINE", p.frictionCombine, CombineMode.Average);
                p.restitutionCombine = Choice("RESTITUTION COMBINE", p.restitutionCombine, CombineMode.Maximum);
                Header("JOINTS");
                p.jointHertz = Value("JOINT HERTZ (SOFT)", p.jointHertz, 1f, 1f, 240f, 60f, "0");
                p.jointDamping = Value("JOINT DAMPING (SOFT)", p.jointDamping, 0.1f, 0f, 100f, 2f, "0.0");
            }
            else if (_tab == 1)
            {
                var p = _phys;
                Header("PLAYER CHARACTER BODY ([character])");
                DrawProfile(p.player, true, Value);
                Header("ENEMY CHARACTER BODY ([enemy_character])");
                DrawProfile(p.enemy, false, Value);
                Header("KINEMATIC MOVERS (PROJECTILES, LEGACY CONTROLLERS)");
                p.floorMaxAngle = Value("FLOOR MAX ANGLE", p.floorMaxAngle, 1f, 1f, 89f, 46f, "0");
                p.maxSlides = IntValue("MAX SLIDES", p.maxSlides, 1, 16, 5);
                p.agentStepHeight = Value("AGENT STEP HEIGHT", p.agentStepHeight, 0.05f, 0f, 2f, 0.4f);
                p.agentSlopeLimit = Value("AGENT SLOPE LIMIT", p.agentSlopeLimit, 1f, 1f, 89f, 50f, "0");
                p.pushMass = Value("PUSH MASS (KG)", p.pushMass, 5f, 0f, 10000f, 80f, "0");
            }
            else
            {
                var m = _move;
                Header("SPEEDS");
                m.walkSpeed = Value("WALK SPEED", m.walkSpeed, 0.5f, 0f, 100f, 12f, "0.0");
                m.runSpeed = Value("RUN SPEED", m.runSpeed, 0.5f, 0f, 100f, 16f, "0.0");
                m.bunnyHop = Flag("BUNNY HOP", m.bunnyHop);
                m.bunnyHopSpeed = Value("BUNNY HOP SPEED", m.bunnyHopSpeed, 0.5f, 0f, 100f, 30f, "0.0");
                m.backwardsPercent = Value("BACKWARDS %", m.backwardsPercent, 0.05f, 0f, 1f, 1f);
                m.sidePercent = Value("STRAFE %", m.sidePercent, 0.05f, 0f, 1f, 1f);
                Header("JUMP / GRAVITY");
                m.jumpSpeed = Value("JUMP SPEED", m.jumpSpeed, 0.5f, 0f, 100f, 20f, "0.0");
                m.gravityMultiplier = Value("GRAVITY MULTIPLIER", m.gravityMultiplier, 0.1f, 0f, 10f, 2.5f, "0.0");
                Header("SMOOTHING");
                m.smoothInput = Value("INPUT", m.smoothInput, 0.5f, 1f, 100f, 5f, "0.0");
                m.smoothVelocity = Value("VELOCITY", m.smoothVelocity, 0.5f, 1f, 100f, 5f, "0.0");
                m.smoothDirection = Value("DIRECTION", m.smoothDirection, 0.5f, 1f, 100f, 5f, "0.0");
                Header("SLIDE");
                m.slide = Flag("SLIDE", m.slide);
                m.slideBoost = Value("BOOST", m.slideBoost, 0.5f, 0f, 50f, 7f, "0.0");
                m.slideFriction = Value("FRICTION", m.slideFriction, 0.5f, 0f, 50f, 7f, "0.0");
                m.slideMinSpeed = Value("MIN SPEED", m.slideMinSpeed, 0.5f, 0f, 50f, 5f, "0.0");
                m.slideStartSpeed = Value("START SPEED", m.slideStartSpeed, 0.5f, 0f, 50f, 6f, "0.0");
                m.slideHeight = Value("CAPSULE HEIGHT", m.slideHeight, 0.05f, 0.5f, 2f, 1f);
                m.slideCameraDrop = Value("CAMERA DROP", m.slideCameraDrop, 0.05f, 0f, 1.5f, 0.55f);
                m.slideCooldown = Value("COOLDOWN (S)", m.slideCooldown, 0.05f, 0f, 5f, 0.35f);
                m.slideSteer = Value("STEER", m.slideSteer, 0.1f, 0f, 10f, 1.5f, "0.0");
                Header("WALL RUN");
                m.wallRun = Flag("WALL RUN", m.wallRun);
                m.wallRunMaxTime = Value("MAX TIME (S)", m.wallRunMaxTime, 0.1f, 0f, 10f, 1.6f, "0.0");
                m.wallRunGravity = Value("GRAVITY FRACTION", m.wallRunGravity, 0.05f, 0f, 2f, 0.2f);
                m.wallRunSpeed = Value("SPEED", m.wallRunSpeed, 0.5f, 0f, 60f, 13f, "0.0");
                m.wallCheckDistance = Value("WALL CHECK DISTANCE", m.wallCheckDistance, 0.05f, 0f, 3f, 0.75f);
                m.wallJumpUp = Value("WALL JUMP UP", m.wallJumpUp, 0.5f, 0f, 60f, 15f, "0.0");
                m.wallJumpAway = Value("WALL JUMP AWAY", m.wallJumpAway, 0.5f, 0f, 60f, 11f, "0.0");
                m.wallRunTilt = Value("CAMERA TILT (DEG)", m.wallRunTilt, 1f, 0f, 45f, 14f, "0");
                m.wallRunEntryBoost = Value("ENTRY BOOST", m.wallRunEntryBoost, 0.5f, 0f, 20f, 3f, "0.0");
                Header("DASH");
                m.dash = Flag("DASH", m.dash);
                m.dashSpeed = Value("SPEED", m.dashSpeed, 1f, 0f, 100f, 26f, "0");
                m.dashDuration = Value("DURATION (S)", m.dashDuration, 0.01f, 0.02f, 2f, 0.16f);
                m.dashCooldown = Value("COOLDOWN (S)", m.dashCooldown, 0.05f, 0f, 10f, 0.7f);
                m.airDashes = IntValue("AIR DASHES", m.airDashes, 0, 10, 1);
            }
            ImGui.EndChild();
            ImGui.End();

            if (changed) { _dirty = true; Apply(); }
        }

        private static void DrawProfile(CharacterProfile p, bool player, Func<string, float, float, float, float, float, string, float> value)
        {
            p.mass = value("MASS (KG)", p.mass, 1f, 0.1f, 10000f, player ? 80f : 85f, "0");
            p.rideHeight = value("RIDE HEIGHT (M)", p.rideHeight, 0.01f, 0.05f, 2f, player ? 0.5f : 0.35f, "0.00");
            p.rideFrequency = value("RIDE FREQUENCY (HZ)", p.rideFrequency, 0.25f, 0.5f, 30f, 6f, "0.00");
            p.rideDamping = value("RIDE DAMPING RATIO", p.rideDamping, 0.05f, 0f, 5f, 0.8f, "0.00");
            p.maxRideAcceleration = value("MAX RIDE ACCEL", p.maxRideAcceleration, 5f, 1f, 1000f, 90f, "0");
            p.groundTolerance = value("GROUND TOLERANCE", p.groundTolerance, 0.01f, 0f, 2f, 0.25f, "0.00");
            p.maxSlopeAngle = value("MAX SLOPE (DEG)", p.maxSlopeAngle, 1f, 1f, 89f, 50f, "0");
            p.acceleration = value("ACCELERATION", p.acceleration, 1f, 0f, 1000f, player ? 70f : 40f, "0");
            p.airAcceleration = value("AIR ACCELERATION", p.airAcceleration, 1f, 0f, 1000f, player ? 20f : 8f, "0");
            p.stopDeceleration = value("STOP DECELERATION", p.stopDeceleration, 1f, 0f, 1000f, player ? 60f : 40f, "0");
            p.jumpGrace = value("JUMP GRACE (S)", p.jumpGrace, 0.01f, 0f, 2f, 0.25f, "0.00");
        }
    }
}
