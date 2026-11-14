using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Input;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content;
using RipNRage.Game.Ecs.Combat;

namespace RipNRage.Game.Ecs
{
    public enum RageState : byte { Normal = 0, Enraged = 1, Depressed = 2 }

    /// <summary>
    /// The rage system, as data on the player entity: two meters the player fills by playing one way or the other,
    /// and the state one of them was spent on. <see cref="Fury"/> fills with aggression (hits, kills, headshots,
    /// kills up close), <see cref="Calm"/> with evasion under threat. Neither meter decays — what is earned is
    /// banked until it is spent.
    ///
    /// <para>The player activates a meter (partial is allowed: half a meter buys half a state). While a state runs,
    /// <b>neither meter gains</b> and the state cannot be left: the active meter drains, and reaching zero is the
    /// only way out. The draining meter <i>is</i> the remaining time, so a save needs no separate timer.</para>
    ///
    /// <para>Every number is in <see cref="RageDef"/> (tunables.cfg <c>[rage]</c>, live).</para>
    /// </summary>
    public struct Rage : IComponent
    {
        /// <summary>Aggression, 0..1.</summary>
        public float Fury;
        /// <summary>Evasion, 0..1.</summary>
        public float Calm;
        public RageState State;
        /// <summary>Something is hunting the player right now: what gates every Calm gain (written by
        /// <see cref="RageCalmSystem"/>, read by its gains and the scripts' probe).</summary>
        public bool Threat;
        /// <summary><see cref="PlayerMotor.Evasions"/> as of the last tick the Calm system looked; the difference is
        /// the evasive moves it has not paid for yet. The edge lives here, not in the system, so it is saved with
        /// the rest of the state and a reader can see it.</summary>
        public int EvasionsSeen;
    }

    /// <summary>What <see cref="Rages.Step"/> is told about this tick: meter gains and the activation presses.</summary>
    public struct RageInputs
    {
        public float FuryGain, CalmGain;
        public bool ActivateFury, ActivateCalm;
    }

    /// <summary>A meter gain posted by whatever earned it (the rocket blast, the evasion sources).</summary>
    public struct RageGainEvent : IPostProcessEvent
    {
        public float Fury, Calm;
    }

    /// <summary>The rules (a pure step) and the lookups the effects read. The numbers live in <see cref="RageDef"/>.</summary>
    public static class Rages
    {
        /// <summary>The live tuning table (tunables.cfg <c>[rage]</c>).</summary>
        public static RageDef Def => RageDef.Current;

        /// <summary>Movement multiplier for the state (DEPRESSED no longer slows the player: it is a defensive
        /// state, and slowing it would fight its own purpose).</summary>
        public static float SpeedMultiplier(in Rage r) => r.State == RageState.Enraged ? Def.enragedSpeed : 1f;

        public static float DamageDealt(in Rage r) => r.State == RageState.Enraged ? Def.enragedDamageDealt : 1f;
        public static float DamageTaken(in Rage r) => r.State == RageState.Depressed ? Def.depressedDamageTaken : 1f;
        public static float SightRangeScale(in Rage r) => r.State == RageState.Depressed ? Def.depressedSightRange : 1f;
        public static float MemoryScale(in Rage r) => r.State == RageState.Depressed ? Def.depressedMemory : 1f;
        public static float DashCooldownScale(in Rage r) => r.State == RageState.Depressed ? Def.depressedDashCooldown : 1f;
        public static int ExtraAirDashes(in Rage r) => r.State == RageState.Depressed ? Def.depressedAirDashes : 0;

        /// <summary>The multiplier an entity's state puts on a blow it struck (1 for anything with no rage).</summary>
        public static float DamageDealtBy(World w, Entity e) =>
            !e.IsNull && w.IsAlive(e) && w.Has<Rage>(e) ? DamageDealt(w.Get<Rage>(e)) : 1f;

        /// <summary>The multiplier an entity's state puts on a blow it is taking.</summary>
        public static float DamageTakenBy(World w, Entity e) =>
            !e.IsNull && w.IsAlive(e) && w.Has<Rage>(e) ? DamageTaken(w.Get<Rage>(e)) : 1f;

        /// <summary>
        /// One tick of the rules, as a pure function of the state, this tick's inputs and the tuning table:
        /// <list type="number">
        /// <item>in a state, every gain is dropped and the active meter drains; at zero the state ends;</item>
        /// <item>in Normal, gains are added (clamped to a full meter) and never decay;</item>
        /// <item>an activation press with anything in that meter enters its state (partial = shorter);</item>
        /// <item>both pressed in one tick: Fury wins (the offensive read of an ambiguous press).</item>
        /// </list>
        /// </summary>
        public static Rage Step(in Rage rage, in RageInputs inputs, float dt, RageDef def)
        {
            var r = rage;
            if (r.State != RageState.Normal)
            {
                float duration = r.State == RageState.Enraged ? def.enragedDuration : def.depressedDuration;
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

        /// <summary>The same step against the live table (what the system runs).</summary>
        public static Rage Step(in Rage rage, in RageInputs inputs, float dt) => Step(in rage, in inputs, dt, Def);

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
    /// A dead player keeps nothing.
    /// </summary>
    [Reads(typeof(InputState), typeof(PlayerTag), typeof(LocalToWorld), typeof(Dead))] // the presses; who struck a blow; how close a kill was
    [Writes(typeof(Rage), typeof(Health), typeof(Armor))] // the meters, and ENRAGED lifesteal / DEPRESSED armour regen
    public sealed class RageSystem : SystemBase, ITick, IConsumes<HitEvent>, IConsumes<RageGainEvent>
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
            var w = ctx.World;
            foreach (var chunk in _players)
            {
                var rages = chunk.Get<Rage>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    // a corpse has no rage. The Dead tag alone is not enough: the fall kill calls PlayerState.Die()
                    // straight out, so no damage and no DeathEvent ever reach the ECS — empty health is the signal
                    if (IsDead(w, ents[i])) { rages[i] = default; continue; }
                    rages[i] = Rages.Step(in rages[i], in inputs, dt);
                    if (rages[i].State == RageState.Depressed) Regenerate(w, ents[i], dt);
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
            a.Current = Mathf.Min(a.Max, a.Current + Rages.Def.armorRegenPerSecond * dt);
        }

        /// <summary>ENRAGED: the player's blows feed them.</summary>
        private static void Lifesteal(World w, Entity player, bool killed)
        {
            if (!w.Has<Health>(player)) return;
            ref var h = ref w.Get<Health>(player);
            if (h.Current <= 0f) return;
            h.Current = Mathf.Min(h.Max, h.Current + (killed ? Rages.Def.lifestealPerKill : Rages.Def.lifestealPerHit));
        }

        /// <summary>
        /// Aggression for the blows the player struck, and the Calm penalty for the ones they took — both ends read
        /// off the event. A blow the player dealt to themselves (the rocket jump) is neither: mobility is not
        /// carelessness, and it is not aggression either.
        /// </summary>
        public void Consume(SystemContext ctx, System.ReadOnlySpan<HitEvent> events)
        {
            var w = ctx.World;
            var def = Rages.Def;
            foreach (ref readonly var e in events)
            {
                bool byPlayer = !e.Instigator.IsNull && w.IsAlive(e.Instigator) && w.Has<PlayerTag>(e.Instigator);
                if (w.IsAlive(e.Target) && w.Has<PlayerTag>(e.Target))
                {
                    if (!byPlayer) _pendingCalm -= def.damagePenalty;
                    continue;
                }
                if (!byPlayer) continue;

                float fury = def.hitFury + (e.Headshot ? def.headshotHitFury : 0f);
                if (e.Killed)
                {
                    fury += def.killFury + (e.Headshot ? def.headshotKillFury : 0f);
                    if (TryPlayerPosition(out var player) && (e.Point - player).magnitude <= def.closeRange)
                        fury += def.closeKillFury;
                }
                _pendingFury += fury;

                // ENRAGED turns those blows into health
                if (w.Has<Rage>(e.Instigator) && w.Get<Rage>(e.Instigator).State == RageState.Enraged)
                    Lifesteal(w, e.Instigator, e.Killed);
            }
        }

        public void Consume(SystemContext ctx, System.ReadOnlySpan<RageGainEvent> events)
        {
            foreach (ref readonly var e in events) { _pendingFury += e.Fury; _pendingCalm += e.Calm; }
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
