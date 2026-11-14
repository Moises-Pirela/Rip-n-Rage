using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.AI;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs
{
    public enum RageState : byte { Normal = 0, Enraged = 1, Depressed = 2 }

    /// <summary>
    /// The rage system, as data on the player entity: two meters the player fills by playing one way or the other,
    /// and the state one of them was spent on. <see cref="Fury"/> fills with aggression (hits, kills, headshots,
    /// kills up close), <see cref="Calm"/> with evasion under threat (7.2). Neither meter decays — what is earned is
    /// banked until it is spent.
    ///
    /// <para>The player activates a meter (partial is allowed: half a meter buys half a state). While a state runs,
    /// <b>neither meter gains</b> and the state cannot be left: the active meter drains, and reaching zero is the
    /// only way out. The draining meter <i>is</i> the remaining time, so a save needs no separate timer.</para>
    ///
    /// <para>Replaces the 0..1 rage value, <c>PlayerRageState</c>, <c>PlayerCombatState</c> and the style meter.</para>
    /// </summary>
    public struct Rage : IComponent
    {
        /// <summary>Aggression, 0..1.</summary>
        public float Fury;
        /// <summary>Evasion, 0..1 (filled in 7.2).</summary>
        public float Calm;
        public RageState State;
        /// <summary>Something is hunting the player right now: what gates every Calm gain (written by
        /// <see cref="RageCalmSystem"/>, read by its gains and the scripts' probe).</summary>
        public bool Threat;
    }

    /// <summary>What <see cref="Rages.Step"/> is told about this tick: meter gains and the activation presses.</summary>
    public struct RageInputs
    {
        public float FuryGain, CalmGain;
        public bool ActivateFury, ActivateCalm;
    }

    /// <summary>A meter gain posted by whatever earned it (the rocket blast, 7.2's evasion sources).</summary>
    public struct RageGainEvent : IPostProcessEvent
    {
        public float Fury, Calm;
    }

    /// <summary>The rage definition (flat constants, one table) and the pure step the system runs.</summary>
    public static class Rages
    {
        // ---- Fury: aggression ----
        public const float HitFury = 0.02f;
        public const float KillFury = 0.15f;
        /// <summary>Extra, on top of the hit / kill gain.</summary>
        public const float HeadshotHitFury = 0.03f, HeadshotKillFury = 0.10f;
        /// <summary>Extra for a kill inside <see cref="CloseRange"/>: rewards pushing in.</summary>
        public const float CloseKillFury = 0.10f, CloseRange = 6f;
        /// <summary>An enemy caught in a rocket blast.</summary>
        public const float SplashFury = 0.02f;

        // ---- states: a full meter lasts this long, so the drain is 1/duration per second ----
        public const float EnragedDuration = 8f, DepressedDuration = 10f;

        // ---- effects: ENRAGED presses the attack, DEPRESSED survives and slips away ----
        public const float EnragedSpeed = 1.2f;
        /// <summary>ENRAGED deals more; DEPRESSED takes less.</summary>
        public const float EnragedDamageDealt = 1.5f, DepressedDamageTaken = 0.5f;
        /// <summary>ENRAGED lifesteal: health per hit on an enemy, and per kill.</summary>
        public const float LifestealPerHit = 2f, LifestealPerKill = 15f;
        /// <summary>DEPRESSED: armour points per second.</summary>
        public const float ArmorRegenPerSecond = 5f;
        /// <summary>DEPRESSED: the dash comes back sooner and there is one more of them in the air.</summary>
        public const float DepressedDashCooldown = 0.5f;
        public const int DepressedAirDashes = 1;
        /// <summary>DEPRESSED: enemies see less far and forget sooner — the state to break away in.</summary>
        public const float DepressedSightRange = 0.6f, DepressedMemory = 0.5f;

        /// <summary>Movement multiplier for the state (DEPRESSED no longer slows the player: it is a defensive
        /// state, and slowing it would fight its own purpose).</summary>
        public static float SpeedMultiplier(in Rage r) => r.State == RageState.Enraged ? EnragedSpeed : 1f;

        public static float DamageDealt(in Rage r) => r.State == RageState.Enraged ? EnragedDamageDealt : 1f;
        public static float DamageTaken(in Rage r) => r.State == RageState.Depressed ? DepressedDamageTaken : 1f;
        public static float SightRangeScale(in Rage r) => r.State == RageState.Depressed ? DepressedSightRange : 1f;
        public static float MemoryScale(in Rage r) => r.State == RageState.Depressed ? DepressedMemory : 1f;
        public static float DashCooldownScale(in Rage r) => r.State == RageState.Depressed ? DepressedDashCooldown : 1f;
        public static int ExtraAirDashes(in Rage r) => r.State == RageState.Depressed ? DepressedAirDashes : 0;

        /// <summary>The multiplier an entity's state puts on a blow it struck (1 for anything with no rage).</summary>
        public static float DamageDealtBy(World w, Entity e) =>
            !e.IsNull && w.IsAlive(e) && w.Has<Rage>(e) ? DamageDealt(w.Get<Rage>(e)) : 1f;

        /// <summary>The multiplier an entity's state puts on a blow it is taking.</summary>
        public static float DamageTakenBy(World w, Entity e) =>
            !e.IsNull && w.IsAlive(e) && w.Has<Rage>(e) ? DamageTaken(w.Get<Rage>(e)) : 1f;

        /// <summary>The player's rage as the systems outside the ECS see it (null-safe, Normal if there is none).</summary>
        public static Rage Current() => TryGet(out var r) ? r : default;

        /// <summary>
        /// One tick of the rules, as a pure function of the state and this tick's inputs:
        /// <list type="number">
        /// <item>in a state, every gain is dropped and the active meter drains; at zero the state ends;</item>
        /// <item>in Normal, gains are added (clamped to a full meter) and never decay;</item>
        /// <item>an activation press with anything in that meter enters its state (partial = shorter);</item>
        /// <item>both pressed in one tick: Fury wins (the offensive read of an ambiguous press).</item>
        /// </list>
        /// </summary>
        public static Rage Step(in Rage rage, in RageInputs inputs, float dt)
        {
            var r = rage;
            if (r.State != RageState.Normal)
            {
                float duration = r.State == RageState.Enraged ? EnragedDuration : DepressedDuration;
                ref float active = ref (r.State == RageState.Enraged ? ref r.Fury : ref r.Calm);
                active -= dt / duration;
                if (active <= 0f) { active = 0f; r.State = RageState.Normal; }
                return r;
            }

            r.Fury = Mathf.Clamp01(r.Fury + inputs.FuryGain);
            r.Calm = Mathf.Clamp01(r.Calm + inputs.CalmGain);

            if (inputs.ActivateFury && r.Fury > 0f) r.State = RageState.Enraged;
            else if (inputs.ActivateCalm && r.Calm > 0f) r.State = RageState.Depressed;
            return r;
        }

        /// <summary>The player's rage, for the readers outside the ECS (HUD, pause menu, save, the scripts).</summary>
        public static bool TryGet(out Rage rage)
        {
            var w = Application.Current?.World;
            var e = PlayerLink.PlayerEntity;
            if (w != null && !e.IsNull && w.IsAlive(e) && w.Has<Rage>(e)) { rage = w.Get<Rage>(e); return true; }
            rage = default;
            return false;
        }

        /// <summary>Save restore: the meters and the state come back exactly (the active meter is the time left).</summary>
        public static void Restore(float fury, float calm, RageState state)
        {
            var w = Application.Current?.World;
            var e = PlayerLink.PlayerEntity;
            if (w == null || e.IsNull || !w.IsAlive(e) || !w.Has<Rage>(e)) return;
            ref var r = ref w.Get<Rage>(e);
            r.Fury = Mathf.Clamp01(fury);
            r.Calm = Mathf.Clamp01(calm);
            r.State = state;
        }
    }

    /// <summary>
    /// Steps the player's rage: collects this tick's gains (aggression off <see cref="HitEvent"/>, anything else off
    /// <see cref="RageGainEvent"/>), reads the two activation presses, and hands both to <see cref="Rages.Step"/>.
    /// Death clears everything.
    /// </summary>
    [Reads(typeof(InputState), typeof(Enemy), typeof(PlayerTag), typeof(LocalToWorld), typeof(Dead))] // the activation presses; the hit consumer asks whether the target was an enemy and how close it died
    [Writes(typeof(Rage), typeof(Health), typeof(Armor))] // the meter, and ENRAGED lifesteal / DEPRESSED armour regen
    public sealed class RageSystem : SystemBase, ITick, IConsumes<HitEvent>, IConsumes<RageGainEvent>, IConsumes<DeathEvent>
    {
        private Query _players;
        /// <summary>Gains earned since the last tick (the events are consumed at the end of the frame).</summary>
        private float _pendingFury, _pendingCalm;

        public override void OnCreate()
        {
            _players = World.Query().With<Rage>().WithReadOnly<PlayerTag>().WithReadOnly<LocalToWorld>().Build();
        }

        public void Tick(SystemContext ctx)
        {
            var input = World.Singleton<InputState>();
            var inputs = new RageInputs
            {
                FuryGain = _pendingFury,
                CalmGain = _pendingCalm,
                ActivateFury = input.FuryPressed,
                ActivateCalm = input.CalmPressed,
            };
            _pendingFury = _pendingCalm = 0f;

            float dt = ctx.DeltaTime;
            foreach (var chunk in _players)
            {
                var rages = chunk.Get<Rage>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    // a corpse has no rage. The Dead tag is not enough: the fall kill calls PlayerState.Die()
                    // straight out, so no damage and no DeathEvent ever reach the ECS — empty health is the signal
                    if (IsDead(ctx.World, ents[i])) { rages[i] = default; continue; }
                    rages[i] = Rages.Step(in rages[i], in inputs, dt);
                    if (rages[i].State == RageState.Depressed) Regenerate(ctx.World, ents[i], dt);
                }
            }
        }

        private static bool IsDead(World w, Entity e) =>
            w.Has<Dead>(e) || (w.Has<Health>(e) && w.Get<Health>(e).Current <= 0f);

        /// <summary>DEPRESSED: armour comes back while the player keeps out of trouble.</summary>
        private static void Regenerate(World w, Entity player, float dt)
        {
            if (!w.Has<Armor>(player)) return;
            ref var a = ref w.Get<Armor>(player);
            a.Current = Mathf.Min(a.Max, a.Current + Rages.ArmorRegenPerSecond * dt);
        }

        /// <summary>ENRAGED: the player's blows feed them.</summary>
        private static void Lifesteal(World w, Entity player, bool killed)
        {
            if (!w.Has<Health>(player)) return;
            ref var h = ref w.Get<Health>(player);
            if (h.Current <= 0f) return;
            h.Current = Mathf.Min(h.Max, h.Current + (killed ? Rages.LifestealPerKill : Rages.LifestealPerHit));
        }

        /// <summary>Aggression: the player's blows on an enemy. (As the combat bridge does, "the target is an enemy"
        /// is what makes it the player's hit — enemy fire never targets enemies.)</summary>
        public void Consume(SystemContext ctx, System.ReadOnlySpan<HitEvent> events)
        {
            var w = ctx.World;
            foreach (ref readonly var e in events)
            {
                if (w.IsAlive(e.Target) && w.Has<PlayerTag>(e.Target)) { _pendingCalm -= Calms.DamagePenalty; continue; } // taking a blow is the opposite of evading
                if (!w.IsAlive(e.Target) || !w.Has<Enemy>(e.Target)) continue;
                float fury = Rages.HitFury + (e.Headshot ? Rages.HeadshotHitFury : 0f);
                if (e.Killed)
                {
                    fury += Rages.KillFury + (e.Headshot ? Rages.HeadshotKillFury : 0f);
                    if (TryPlayerPosition(out var player) && (e.Point - player).magnitude <= Rages.CloseRange)
                        fury += Rages.CloseKillFury;
                }
                _pendingFury += fury;

                // ENRAGED turns those blows into health
                foreach (var chunk in _players)
                {
                    var rages = chunk.Get<Rage>(); var ents = chunk.Entities;
                    for (int j = 0; j < chunk.Length; j++)
                        if (chunk.Includes(j) && rages[j].State == RageState.Enraged) Lifesteal(w, ents[j], e.Killed);
                }
            }
        }

        public void Consume(SystemContext ctx, System.ReadOnlySpan<RageGainEvent> events)
        {
            foreach (ref readonly var e in events) { _pendingFury += e.Fury; _pendingCalm += e.Calm; }
        }

        /// <summary>A dead player keeps nothing: both meters and the state go.</summary>
        public void Consume(SystemContext ctx, System.ReadOnlySpan<DeathEvent> events)
        {
            var w = ctx.World;
            foreach (ref readonly var e in events)
            {
                if (!w.IsAlive(e.Entity) || !w.Has<Rage>(e.Entity)) continue;
                ref var r = ref w.Get<Rage>(e.Entity);
                r = default;
                _pendingFury = _pendingCalm = 0f;
            }
        }

        /// <summary>The player's position, off the same query (no PlayerLink static: the query is the declaration).</summary>
        private bool TryPlayerPosition(out Vector3 position)
        {
            foreach (var chunk in _players)
            {
                var ltw = chunk.Get<LocalToWorld>();
                for (int i = 0; i < chunk.Length; i++)
                    if (chunk.Includes(i)) { position = ltw[i].Position; return true; }
            }
            position = default;
            return false;
        }
    }
}
