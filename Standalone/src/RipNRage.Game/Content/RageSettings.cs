using System;
using System.Collections.Generic;
using System.Globalization;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Every number the rage system is tuned by, as one table read from tunables.cfg <c>[rage]</c> — the fields here
    /// are the defaults. Tuning happens by playing, so the file is watched: save it and the next tick uses the new
    /// values (the same deal <see cref="MovementSettings"/> has for locomotion).
    ///
    /// <para>The meters and the states are in <c>Ecs/Rage.cs</c>; the evasion sources in <c>Ecs/RageCalm.cs</c>.
    /// Both read <see cref="Current"/>, and the pure step is handed the table so a check can pin its own.</para>
    /// </summary>
    public sealed class RageDef
    {
        // ---- Fury: aggression ----
        public float hitFury = 0.02f, killFury = 0.15f;
        public float headshotHitFury = 0.03f, headshotKillFury = 0.10f;
        public float closeKillFury = 0.10f, closeRange = 6f;
        public float splashFury = 0.02f;

        // ---- Calm: evasion under threat ----
        public float wallRunPerSecond = 0.06f, evadePerSecond = 0.03f;
        public float dodge = 0.12f, breakLineOfSight = 0.05f, nearMiss = 0.08f;
        public float damagePenalty = 0.10f;
        /// <summary>The near-miss band: inside the first it would have hit, past the second it was never a threat.</summary>
        public float nearMissMin = 0.6f, nearMissMax = 1.5f;
        /// <summary>The player counts as moving (so a break of line of sight was earned) above this speed.</summary>
        public float movingSpeed = 1f;

        // ---- the states: a full meter lasts this long, so the drain is 1/duration per second ----
        public float enragedDuration = 8f, depressedDuration = 10f;

        // ---- what the states are worth ----
        public float enragedSpeed = 1.2f;
        public float enragedDamageDealt = 1.5f, enragedMeleeDamage = 2.5f, depressedDamageTaken = 0.5f;
        public float lifestealPerHit = 2f, lifestealPerKill = 15f;
        public float armorRegenPerSecond = 5f;
        public float depressedDashCooldown = 0.5f;
        public int depressedAirDashes = 1;
        public float depressedSightRange = 0.6f, depressedMemory = 0.5f;

        /// <summary>The live table. Replaced wholesale on a reload, so a tick never sees half a file.</summary>
        public static RageDef Current { get; private set; } = new RageDef();

        public RageDef Clone() => (RageDef)MemberwiseClone();

        /// <summary>Reads tunables.cfg <c>[rage]</c> over a copy of the defaults and makes it live.</summary>
        public static void Apply(Dictionary<string, Dictionary<string, string>> sections)
        {
            var d = new RageDef();
            float F(string key, float cur) => Get(sections, key, cur, v => float.Parse(v, CultureInfo.InvariantCulture));
            int I(string key, int cur) => Get(sections, key, cur, v => int.Parse(v, CultureInfo.InvariantCulture));

            d.hitFury = F("hit_fury", d.hitFury); d.killFury = F("kill_fury", d.killFury);
            d.headshotHitFury = F("headshot_hit_fury", d.headshotHitFury); d.headshotKillFury = F("headshot_kill_fury", d.headshotKillFury);
            d.closeKillFury = F("close_kill_fury", d.closeKillFury); d.closeRange = F("close_range", d.closeRange);
            d.splashFury = F("splash_fury", d.splashFury);

            d.wallRunPerSecond = F("wall_run_calm", d.wallRunPerSecond); d.evadePerSecond = F("evade_calm", d.evadePerSecond);
            d.dodge = F("dodge_calm", d.dodge); d.breakLineOfSight = F("break_los_calm", d.breakLineOfSight);
            d.nearMiss = F("near_miss_calm", d.nearMiss); d.damagePenalty = F("damage_penalty", d.damagePenalty);
            d.nearMissMin = F("near_miss_min", d.nearMissMin); d.nearMissMax = F("near_miss_max", d.nearMissMax);
            d.movingSpeed = F("moving_speed", d.movingSpeed);

            d.enragedDuration = Mathf.Max(0.1f, F("enraged_duration", d.enragedDuration));
            d.depressedDuration = Mathf.Max(0.1f, F("depressed_duration", d.depressedDuration));

            d.enragedSpeed = F("enraged_speed", d.enragedSpeed);
            d.enragedDamageDealt = F("enraged_damage_dealt", d.enragedDamageDealt);
            d.enragedMeleeDamage = F("enraged_melee_damage", d.enragedMeleeDamage);
            d.depressedDamageTaken = F("depressed_damage_taken", d.depressedDamageTaken);
            d.lifestealPerHit = F("lifesteal_per_hit", d.lifestealPerHit); d.lifestealPerKill = F("lifesteal_per_kill", d.lifestealPerKill);
            d.armorRegenPerSecond = F("armor_regen", d.armorRegenPerSecond);
            d.depressedDashCooldown = F("depressed_dash_cooldown", d.depressedDashCooldown);
            d.depressedAirDashes = Mathf.Clamp(I("depressed_air_dashes", d.depressedAirDashes), 0, 10);
            d.depressedSightRange = F("depressed_sight_range", d.depressedSightRange);
            d.depressedMemory = F("depressed_memory", d.depressedMemory);

            Current = d;
        }

        private static T Get<T>(Dictionary<string, Dictionary<string, string>> sections, string key, T cur, Func<string, T> parse)
        {
            if (sections == null || !sections.TryGetValue("rage", out var kv) || !kv.TryGetValue(key, out var v)) return cur;
            try { return parse(v); }
            catch (Exception) { Debug.LogWarning($"tunables.cfg: bad value for [rage] {key}: {v}"); return cur; }
        }

        /// <summary>The cfg lines (section, key, value, comment) — the editor's writer uses them.</summary>
        public List<(string section, string key, string value, string comment)> Lines()
        {
            string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            const string s = "rage";
            return new List<(string, string, string, string)>
            {
                (s, "hit_fury", F(hitFury), "FURY for a hit on an enemy (meters are 0..1)"),
                (s, "kill_fury", F(killFury), "FURY for a kill"),
                (s, "headshot_hit_fury", F(headshotHitFury), "extra for a headshot hit"),
                (s, "headshot_kill_fury", F(headshotKillFury), "extra for a headshot kill"),
                (s, "close_kill_fury", F(closeKillFury), "extra for a kill inside close_range"),
                (s, "close_range", F(closeRange), "metres"),
                (s, "splash_fury", F(splashFury), "per enemy caught in a rocket blast"),
                (s, "wall_run_calm", F(wallRunPerSecond), "CALM per second wall running under threat"),
                (s, "evade_calm", F(evadePerSecond), "CALM per second sliding / dashing / airborne under threat"),
                (s, "dodge_calm", F(dodge), "CALM for an evasive move while an attack is live"),
                (s, "break_los_calm", F(breakLineOfSight), "CALM for losing a pursuer while moving"),
                (s, "near_miss_calm", F(nearMiss), "CALM for an enemy bullet that passed by"),
                (s, "near_miss_min", F(nearMissMin), "metres: closer than this it would have hit"),
                (s, "near_miss_max", F(nearMissMax), "metres: further than this it was never close"),
                (s, "damage_penalty", F(damagePenalty), "CALM lost when a blow lands on the player"),
                (s, "moving_speed", F(movingSpeed), "m/s the player must move for a lost pursuer to count"),
                (s, "enraged_duration", F(enragedDuration), "seconds a full FURY meter lasts"),
                (s, "depressed_duration", F(depressedDuration), "seconds a full CALM meter lasts"),
                (s, "enraged_speed", F(enragedSpeed), "movement multiplier while ENRAGED"),
                (s, "enraged_damage_dealt", F(enragedDamageDealt), "damage multiplier for blows the player strikes"),
                (s, "enraged_melee_damage", F(enragedMeleeDamage), "damage multiplier for melee blows the player strikes while ENRAGED (the fist combo)"),
                (s, "depressed_damage_taken", F(depressedDamageTaken), "damage multiplier for blows the player takes"),
                (s, "lifesteal_per_hit", F(lifestealPerHit), "health per hit while ENRAGED"),
                (s, "lifesteal_per_kill", F(lifestealPerKill), "health per kill while ENRAGED"),
                (s, "armor_regen", F(armorRegenPerSecond), "armour per second while DEPRESSED"),
                (s, "depressed_dash_cooldown", F(depressedDashCooldown), "dash cooldown multiplier while DEPRESSED"),
                (s, "depressed_air_dashes", depressedAirDashes.ToString(), "extra air dashes while DEPRESSED"),
                (s, "depressed_sight_range", F(depressedSightRange), "enemy sight range multiplier against a DEPRESSED player"),
                (s, "depressed_memory", F(depressedMemory), "enemy memory multiplier against a DEPRESSED player"),
            };
        }

        /// <summary>Reads the file now and on every reload (Program wires this at start-up).</summary>
        public static void Attach(Application app)
        {
            if (app?.Tunables == null) return;
            Apply(app.Tunables.LastSections);
            app.Tunables.Reloaded += _ => Apply(app.Tunables.LastSections);
        }
    }
}
