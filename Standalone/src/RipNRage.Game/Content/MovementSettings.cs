using System;
using System.Collections.Generic;
using System.Globalization;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// The player's locomotion tunables (the FPSController's public fields) as a settings block: read from
    /// tunables.cfg <c>[movement]</c> (the prefab values are the defaults), applied to the live controller whenever
    /// the file or the editor's physics panel changes them.
    /// </summary>
    public sealed class MovementSettings
    {
        public bool bunnyHop = true;
        public float walkSpeed = 12f, runSpeed = 16f, bunnyHopSpeed = 30f, jumpSpeed = 20f;
        public float backwardsPercent = 1f, sidePercent = 1f;
        public float gravityMultiplier = 2.5f;
        public float smoothInput = 5f, smoothVelocity = 5f, smoothDirection = 5f;
        public bool slide = true;
        public float slideBoost = 7f, slideFriction = 7f, slideMinSpeed = 5f, slideStartSpeed = 6f, slideHeight = 1.0f, slideCameraDrop = 0.55f, slideCooldown = 0.35f, slideSteer = 1.5f;
        public bool wallRun = true;
        public float wallRunMaxTime = 1.6f, wallRunGravity = 0.2f, wallRunSpeed = 13f, wallCheckDistance = 0.75f, wallJumpUp = 15f, wallJumpAway = 11f, wallRunTilt = 14f, wallRunEntryBoost = 3f;
        public bool dash = true;
        public float dashSpeed = 26f, dashDuration = 0.16f, dashCooldown = 0.7f;
        public int airDashes = 1;

        public static MovementSettings Current { get; } = new MovementSettings();
        /// <summary>Bumped on every change; controllers re-apply when it moves.</summary>
        public static int Version { get; private set; }
        public static event Action Changed;

        public MovementSettings Clone() => (MovementSettings)MemberwiseClone();

        public void CopyFrom(MovementSettings o)
        {
            bunnyHop = o.bunnyHop; walkSpeed = o.walkSpeed; runSpeed = o.runSpeed; bunnyHopSpeed = o.bunnyHopSpeed; jumpSpeed = o.jumpSpeed;
            backwardsPercent = o.backwardsPercent; sidePercent = o.sidePercent; gravityMultiplier = o.gravityMultiplier;
            smoothInput = o.smoothInput; smoothVelocity = o.smoothVelocity; smoothDirection = o.smoothDirection;
            slide = o.slide; slideBoost = o.slideBoost; slideFriction = o.slideFriction; slideMinSpeed = o.slideMinSpeed; slideStartSpeed = o.slideStartSpeed;
            slideHeight = o.slideHeight; slideCameraDrop = o.slideCameraDrop; slideCooldown = o.slideCooldown; slideSteer = o.slideSteer;
            wallRun = o.wallRun; wallRunMaxTime = o.wallRunMaxTime; wallRunGravity = o.wallRunGravity; wallRunSpeed = o.wallRunSpeed; wallCheckDistance = o.wallCheckDistance;
            wallJumpUp = o.wallJumpUp; wallJumpAway = o.wallJumpAway; wallRunTilt = o.wallRunTilt; wallRunEntryBoost = o.wallRunEntryBoost;
            dash = o.dash; dashSpeed = o.dashSpeed; dashDuration = o.dashDuration; dashCooldown = o.dashCooldown; airDashes = o.airDashes;
        }

        /// <summary>Makes these the live settings: every FPSController picks them up on its next Update.</summary>
        public void Commit()
        {
            if (!ReferenceEquals(this, Current)) Current.CopyFrom(this);
            Version++;
            Changed?.Invoke();
        }

        public void Apply(Dictionary<string, Dictionary<string, string>> sections)
        {
            float F(string key, float cur) => Get(sections, key, cur, v => float.Parse(v, CultureInfo.InvariantCulture));
            int I(string key, int cur) => Get(sections, key, cur, v => int.Parse(v, CultureInfo.InvariantCulture));
            bool B(string key, bool cur) => Get(sections, key, cur, v => v == "true" || v == "1" || v == "yes" || v == "on");
            bunnyHop = B("bunny_hop", bunnyHop);
            walkSpeed = F("walk_speed", walkSpeed); runSpeed = F("run_speed", runSpeed); bunnyHopSpeed = F("bunny_hop_speed", bunnyHopSpeed); jumpSpeed = F("jump_speed", jumpSpeed);
            backwardsPercent = Mathf.Clamp01(F("backwards_percent", backwardsPercent)); sidePercent = Mathf.Clamp01(F("side_percent", sidePercent));
            gravityMultiplier = F("gravity_multiplier", gravityMultiplier);
            smoothInput = F("smooth_input", smoothInput); smoothVelocity = F("smooth_velocity", smoothVelocity); smoothDirection = F("smooth_direction", smoothDirection);
            slide = B("slide", slide); slideBoost = F("slide_boost", slideBoost); slideFriction = F("slide_friction", slideFriction); slideMinSpeed = F("slide_min_speed", slideMinSpeed);
            slideStartSpeed = F("slide_start_speed", slideStartSpeed); slideHeight = F("slide_height", slideHeight); slideCameraDrop = F("slide_camera_drop", slideCameraDrop);
            slideCooldown = F("slide_cooldown", slideCooldown); slideSteer = F("slide_steer", slideSteer);
            wallRun = B("wall_run", wallRun); wallRunMaxTime = F("wall_run_time", wallRunMaxTime); wallRunGravity = F("wall_run_gravity", wallRunGravity); wallRunSpeed = F("wall_run_speed", wallRunSpeed);
            wallCheckDistance = F("wall_check_distance", wallCheckDistance); wallJumpUp = F("wall_jump_up", wallJumpUp); wallJumpAway = F("wall_jump_away", wallJumpAway);
            wallRunTilt = F("wall_run_tilt", wallRunTilt); wallRunEntryBoost = F("wall_run_entry_boost", wallRunEntryBoost);
            dash = B("dash", dash); dashSpeed = F("dash_speed", dashSpeed); dashDuration = F("dash_duration", dashDuration); dashCooldown = F("dash_cooldown", dashCooldown);
            airDashes = Mathf.Clamp(I("air_dashes", airDashes), 0, 10);
        }

        private static T Get<T>(Dictionary<string, Dictionary<string, string>> sections, string key, T cur, Func<string, T> parse)
        {
            if (sections == null || !sections.TryGetValue("movement", out var kv) || !kv.TryGetValue(key, out var v)) return cur;
            try { return parse(v); }
            catch (Exception) { Debug.LogWarning($"tunables.cfg: bad value for [movement] {key}: {v}"); return cur; }
        }

        /// <summary>The cfg lines (section, key, value, comment).</summary>
        public List<(string section, string key, string value, string comment)> Lines()
        {
            string Fm(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            string Bm(bool b) => b ? "true" : "false";
            const string s = "movement";
            return new List<(string, string, string, string)>
            {
                (s, "walk_speed", Fm(walkSpeed), "m/s"), (s, "run_speed", Fm(runSpeed), "m/s (the default ground speed)"),
                (s, "bunny_hop", Bm(bunnyHop), "airborne speed switches to bunny_hop_speed"), (s, "bunny_hop_speed", Fm(bunnyHopSpeed), "m/s while airborne"),
                (s, "jump_speed", Fm(jumpSpeed), "m/s upwards"), (s, "gravity_multiplier", Fm(gravityMultiplier), "player gravity relative to [physics] gravity"),
                (s, "backwards_percent", Fm(backwardsPercent), "speed when moving backwards (0..1)"), (s, "side_percent", Fm(sidePercent), "speed when strafing (0..1)"),
                (s, "smooth_input", Fm(smoothInput), "input smoothing (higher = snappier)"), (s, "smooth_velocity", Fm(smoothVelocity), "speed smoothing"), (s, "smooth_direction", Fm(smoothDirection), "direction smoothing"),
                (s, "slide", Bm(slide), "crouch while running to slide"), (s, "slide_boost", Fm(slideBoost), "m/s added when a slide starts"), (s, "slide_friction", Fm(slideFriction), "m/s² lost while sliding"),
                (s, "slide_min_speed", Fm(slideMinSpeed), "slide ends below this"), (s, "slide_start_speed", Fm(slideStartSpeed), "speed needed to start"), (s, "slide_height", Fm(slideHeight), "capsule height while sliding"),
                (s, "slide_camera_drop", Fm(slideCameraDrop), "camera drop while sliding"), (s, "slide_cooldown", Fm(slideCooldown), "seconds"), (s, "slide_steer", Fm(slideSteer), "how much left/right bends the slide"),
                (s, "wall_run", Bm(wallRun), "run along walls while airborne"), (s, "wall_run_time", Fm(wallRunMaxTime), "seconds"), (s, "wall_run_gravity", Fm(wallRunGravity), "gravity fraction on the wall"),
                (s, "wall_run_speed", Fm(wallRunSpeed), "m/s"), (s, "wall_check_distance", Fm(wallCheckDistance), "wall probe beyond the capsule radius"),
                (s, "wall_jump_up", Fm(wallJumpUp), "m/s"), (s, "wall_jump_away", Fm(wallJumpAway), "m/s"), (s, "wall_run_tilt", Fm(wallRunTilt), "camera roll degrees"), (s, "wall_run_entry_boost", Fm(wallRunEntryBoost), "upward nudge when catching a wall"),
                (s, "dash", Bm(dash), "dash on the dash key"), (s, "dash_speed", Fm(dashSpeed), "m/s"), (s, "dash_duration", Fm(dashDuration), "seconds"), (s, "dash_cooldown", Fm(dashCooldown), "seconds"), (s, "air_dashes", airDashes.ToString(), "dashes allowed per jump"),
            };
        }

        /// <summary>Copies the settings into a controller.</summary>
        public void ApplyTo(FPSController fps)
        {
            if (fps == null) return;
            fps.bEnableBunnyHop = bunnyHop; fps.walkSpeed = walkSpeed; fps.runSpeed = runSpeed; fps.bunnyHopSpeed = bunnyHopSpeed; fps.jumpSpeed = jumpSpeed;
            fps.moveBackwardsSpeedPercent = backwardsPercent; fps.moveSideSpeedPercent = sidePercent; fps.gravityMultiplier = gravityMultiplier;
            fps.smoothInputSpeed = smoothInput; fps.smoothVelocitySpeed = smoothVelocity; fps.smoothFinalDirectionSpeed = smoothDirection;
            fps.enableSlide = slide; fps.slideSpeedBoost = slideBoost; fps.slideFriction = slideFriction; fps.slideMinSpeed = slideMinSpeed; fps.slideStartSpeed = slideStartSpeed;
            fps.slideHeight = slideHeight; fps.slideCameraDrop = slideCameraDrop; fps.slideCooldown = slideCooldown; fps.slideSteer = slideSteer;
            fps.enableWallRun = wallRun; fps.wallRunMaxTime = wallRunMaxTime; fps.wallRunGravity = wallRunGravity; fps.wallRunSpeed = wallRunSpeed; fps.wallCheckDistance = wallCheckDistance;
            fps.wallJumpUp = wallJumpUp; fps.wallJumpAway = wallJumpAway; fps.wallRunTilt = wallRunTilt; fps.wallRunEntryBoost = wallRunEntryBoost;
            fps.enableDash = dash; fps.dashSpeed = dashSpeed; fps.dashDuration = dashDuration; fps.dashCooldown = dashCooldown; fps.airDashes = airDashes;
        }

        /// <summary>Hooks the settings to the application's tunables file (initial values + live reload).</summary>
        public static void Attach(Application app)
        {
            if (app?.Tunables == null) return;
            Current.Apply(app.Tunables.LastSections);
            Current.Commit();
            app.Tunables.Reloaded += _ => { Current.Apply(app.Tunables.LastSections); Current.Commit(); };
        }
    }
}
