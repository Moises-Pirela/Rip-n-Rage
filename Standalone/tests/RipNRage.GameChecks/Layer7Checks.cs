using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Content;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.AI;
using RipNRage.Game.Ecs.Combat;

/// <summary>
/// Layer 7. 7.1: the rage system — two meters the player fills and spends. The checks pin the rules as a pure
/// function (<see cref="Rages.Step"/>): gains only in Normal and never a decay, activation with any amount in the
/// meter (partial buys a shorter state), no gain and no exit while a state runs, the active meter draining to zero
/// as the state's clock, and chaining straight into the other state once it does. Then the same rules over a
/// world: the system pays Fury for the player's hits, kills, headshots and kills up close, and a dead player
/// keeps nothing. 7.2: the Calm meter — evasion gated on threat (a hunting enemy or an attack aimed at the
/// player), the dodge edges, a bullet that passed close by, shaking a pursuer off, and the penalty for being hit.
/// 7.3: what the states are worth — the damage multipliers at both ends of a blow, ENRAGED lifesteal, DEPRESSED
/// armour regen and the shorter sight range / memory enemies get against a DEPRESSED player.
/// </summary>
internal static class Layer7Checks
{
    /// <summary>The checks pin their own table: tuning tunables.cfg [rage] must not move an assertion.</summary>
    private static readonly RageDef Def = new RageDef();

    public static void Run(Action<bool, string> True, Action<float, float, string> Equal)
    {
        // the attack entities the dodge watches are stepped by EnemyAttackSystem, which reads the definition table
        var saved = Enemies.All;
        Enemies.All = new[] { new EnemyData { name = "TestGrunt", burstInterval = 0.1f, fireRate = 1f } };
        try { Checks(True, Equal); }
        finally { Enemies.All = saved; }
    }

    private static void Checks(Action<bool, string> True, Action<float, float, string> Equal)
    {
        Meters(True, Equal);
        Activation(True, Equal);
        InAState(True, Equal);
        Effects(Equal);
        FuryFromCombat(True, Equal);
        NearMissBand(Equal);
        CalmFromEvasion(True, Equal);
        StateEffects(True, Equal);
    }

    private const float Dt = 1f / 60f;

    private static RageInputs Gain(float fury = 0f, float calm = 0f) => new RageInputs { FuryGain = fury, CalmGain = calm };
    private static RageInputs Press(bool fury = false, bool calm = false) => new RageInputs { ActivateFury = fury, ActivateCalm = calm };

    // ---------------------------------------------------------------- the meters

    private static void Meters(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var r = new Rage();
        r = Rages.Step(in r, Gain(fury: 0.2f), Dt, Def);
        Equal(0.2f, r.Fury, "a gain fills the meter");
        Equal(0f, r.Calm, "the other meter is untouched");

        // no decay anywhere: what is earned is banked until it is spent
        var idle = r;
        for (int i = 0; i < 600; i++) idle = Rages.Step(in idle, default, Dt, Def);
        Equal(0.2f, idle.Fury, "ten seconds idle: the meter does not decay");

        var full = new Rage { Fury = 0.95f };
        full = Rages.Step(in full, Gain(fury: 0.5f), Dt, Def);
        Equal(1f, full.Fury, "a meter fills to one and no further");

        var both = Rages.Step(in r, Gain(fury: 0.1f, calm: 0.3f), Dt, Def);
        Equal(0.3f, both.Fury, "both meters can fill at once: Fury");
        Equal(0.3f, both.Calm, "both meters can fill at once: Calm");
        True(both.State == RageState.Normal, "filling a meter does not enter a state: the player chooses");
    }

    // ---------------------------------------------------------------- activation

    private static void Activation(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var empty = new Rage();
        empty = Rages.Step(in empty, Press(fury: true), Dt, Def);
        True(empty.State == RageState.Normal, "an empty meter cannot be activated");

        var partial = new Rage { Fury = 0.5f };
        partial = Rages.Step(in partial, Press(fury: true), Dt, Def);
        True(partial.State == RageState.Enraged, "a partly filled meter can be activated");

        var calm = new Rage { Calm = 1f };
        calm = Rages.Step(in calm, Press(calm: true), Dt, Def);
        True(calm.State == RageState.Depressed, "the Calm meter activates DEPRESSED");

        var ambiguous = new Rage { Fury = 1f, Calm = 1f };
        ambiguous = Rages.Step(in ambiguous, new RageInputs { ActivateFury = true, ActivateCalm = true }, Dt, Def);
        True(ambiguous.State == RageState.Enraged, "both pressed in one tick: Fury wins");

        // partial = proportionally shorter: half a meter is half the state
        var half = new Rage { Fury = 0.5f, State = RageState.Enraged };
        Lasts(True, Def.enragedDuration * 0.5f, Drain(ref half), "half a Fury meter buys half of ENRAGED");
        var whole = new Rage { Calm = 1f, State = RageState.Depressed };
        Lasts(True, Def.depressedDuration, Drain(ref whole), "a full Calm meter buys the whole of DEPRESSED");
    }

    /// <summary>A duration in whole ticks: the state ends on the tick that empties the meter, never before it.</summary>
    private static void Lasts(Action<bool, string> True, float expected, float actual, string test) =>
        True(actual >= expected && actual - expected <= Dt + 1e-4f, $"{test} (expected {expected}s, lasted {actual}s)");

    /// <summary>Steps until the state ends; returns how long that took.</summary>
    private static float Drain(ref Rage r)
    {
        float t = 0f;
        for (int i = 0; i < 100_000 && r.State != RageState.Normal; i++) { r = Rages.Step(in r, default, Dt, Def); t += Dt; }
        return t;
    }

    // ---------------------------------------------------------------- inside a state

    private static void InAState(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var r = new Rage { Fury = 1f, Calm = 0.4f, State = RageState.Enraged };

        var gained = Rages.Step(in r, Gain(fury: 0.5f, calm: 0.5f), Dt, Def);
        Equal(1f - Dt / Def.enragedDuration, gained.Fury, "inside a state the active meter only drains: no Fury gain");
        Equal(0.4f, gained.Calm, "inside a state the other meter is frozen: no Calm gain");

        var pressed = Rages.Step(in r, new RageInputs { ActivateCalm = true, ActivateFury = true }, Dt, Def);
        True(pressed.State == RageState.Enraged, "a state cannot be left by pressing: only the drain ends it");

        // the drain is the clock, and it empties the meter it spent
        var spent = r;
        float took = Drain(ref spent);
        Lasts(True, Def.enragedDuration, took, "a full Fury meter runs ENRAGED for its duration");
        Equal(0f, spent.Fury, "the spent meter is empty");
        Equal(0.4f, spent.Calm, "the other meter kept everything it had");

        // chaining: the moment the first state ends, a meter filled earlier can be spent
        var chained = Rages.Step(in spent, Press(calm: true), Dt, Def);
        True(chained.State == RageState.Depressed, "ENRAGED can be chained straight into DEPRESSED");
        Lasts(True, Def.depressedDuration * 0.4f, Drain(ref chained), "the chained state lasts what its meter held");
    }

    private static void Effects(Action<float, float, string> Equal)
    {
        Equal(1f, Rages.SpeedMultiplier(new Rage()), "Normal does not change the player's speed");
        Equal(Def.enragedSpeed, Rages.SpeedMultiplier(new Rage { State = RageState.Enraged }), "ENRAGED is faster");
        Equal(1f, Rages.SpeedMultiplier(new Rage { State = RageState.Depressed }), "DEPRESSED does not slow the player down");
    }

    // ---------------------------------------------------------------- the system over a world

    private static void FuryFromCombat(Action<bool, string> True, Action<float, float, string> Equal)
    {
        // a hit on an enemy
        var w = NewWorld();
        var player = Player(w, Vector3.zero);
        var enemy = Enemy(w, new Vector3(0f, 0f, 20f));
        Hit(w, enemy, new Vector3(0f, 0f, 20f), headshot: false, killed: false);
        Tick(w, 2);
        Equal(Def.hitFury, w.Get<Rage>(player).Fury, "a hit on an enemy pays the hit gain");

        // a headshot hit pays more
        Hit(w, enemy, new Vector3(0f, 0f, 20f), headshot: true, killed: false);
        Tick(w, 2);
        Equal(Def.hitFury * 2f + Def.headshotHitFury, w.Get<Rage>(player).Fury, "a headshot pays the extra");
        Close(w);

        // a kill far away: hit + kill, no close-range bonus
        w = NewWorld();
        player = Player(w, Vector3.zero);
        enemy = Enemy(w, new Vector3(0f, 0f, 20f));
        Hit(w, enemy, new Vector3(0f, 0f, 20f), headshot: false, killed: true);
        Tick(w, 2);
        Equal(Def.hitFury + Def.killFury, w.Get<Rage>(player).Fury, "a kill out of range pays hit + kill");
        Close(w);

        // the same kill up close
        w = NewWorld();
        player = Player(w, Vector3.zero);
        enemy = Enemy(w, new Vector3(0f, 0f, 3f));
        Hit(w, enemy, new Vector3(0f, 0f, 3f), headshot: true, killed: true);
        Tick(w, 2);
        Equal(Def.hitFury + Def.headshotHitFury + Def.killFury + Def.headshotKillFury + Def.closeKillFury,
              w.Get<Rage>(player).Fury, "a headshot kill inside the close range pays every bonus");

        // a hit on something that is not an enemy pays nothing (enemy fire on the player)
        float before = w.Get<Rage>(player).Fury;
        Hit(w, player, Vector3.zero, headshot: false, killed: false);
        Tick(w, 2);
        Equal(before, w.Get<Rage>(player).Fury, "a blow on the player is not aggression");
        Close(w);

        // the posted gain event (the rocket blast, and 7.2's evasion sources)
        w = NewWorld();
        player = Player(w, Vector3.zero);
        w.Post(new RageGainEvent { Fury = Def.splashFury, Calm = 0.25f });
        Tick(w, 2);
        Equal(Def.splashFury, w.Get<Rage>(player).Fury, "a posted gain pays Fury");
        Equal(0.25f, w.Get<Rage>(player).Calm, "a posted gain pays Calm");

        // death clears both meters and the state (the Dead tag HealthSystem latches on a killing blow)
        Enrage(w, player);
        w.Add(player, new Dead());
        Tick(w, 2);
        var dead = w.Get<Rage>(player);
        True(dead.Fury == 0f && dead.Calm == 0f && dead.State == RageState.Normal, "a dead player keeps no meters and no state");
        Close(w);

        // the fall kill zeroes the player's health without any DamageEvent, so no DeathEvent is ever posted:
        // empty health has to be enough on its own, or a corpse keeps spending a state (armour regen and all)
        w = NewWorld();
        player = Player(w, Vector3.zero);
        w.Add(player, new Health { Current = 100f, Max = 100f });
        Depress(w, player);
        Tick(w, 2);
        True(w.Get<Rage>(player).State == RageState.Depressed, "the state runs while the player lives");
        w.Get<Health>(player).Current = 0f; // PlayerState.Die(), as FallRespawn calls it
        Tick(w, 2);
        var fell = w.Get<Rage>(player);
        True(fell.State == RageState.Normal && fell.Calm == 0f, "zeroed health ends the state, with no DeathEvent");
        Close(w);
    }


    // ---------------------------------------------------------------- 7.2: the Calm meter

    private static void NearMissBand(Action<float, float, string> Equal)
    {
        // the distance a bullet passed at is measured against the segment it flew, not where it ended up
        Equal(1f, Calms.DistanceToSegment(new Vector3(0f, 1f, 0f), new Vector3(-5f, 0f, 0f), new Vector3(5f, 0f, 0f)),
              "a bullet passing overhead: the distance is to the segment");
        Equal(5f, Calms.DistanceToSegment(Vector3.zero, new Vector3(5f, 0f, 0f), new Vector3(9f, 0f, 0f)),
              "a segment that never comes near: the distance is to its closest end");
    }

    private static void CalmFromEvasion(Action<bool, string> True, Action<float, float, string> Equal)
    {
        // ---- no threat: evasion pays nothing ----
        var w = NewWorld();
        var player = Mover(w, Vector3.zero);
        SetMotor(w, player, wallRunning: true);
        Tick(w, 4);
        Equal(0f, w.Get<Rage>(player).Calm, "wall running with nothing hunting the player pays nothing");
        True(!w.Get<Rage>(player).Threat, "no enemy, no threat");
        Close(w);

        // ---- enemies busy with each other are nobody's threat (the index is keyed by target, not by "is it
        // the player?", so infighting is just more entries) ----
        w = NewWorld();
        player = Mover(w, Vector3.zero);
        SetMotor(w, player, wallRunning: true);
        var brawlerA = Enemy(w, new Vector3(0f, 0f, 6f));
        var brawlerB = Enemy(w, new Vector3(0f, 0f, 9f));
        w.Add(brawlerA, new Perception { Target = brawlerB, Seen = true });
        w.Add(brawlerB, new Perception { Target = brawlerA, Seen = true });
        w.Add(brawlerA, new EnemyWeapon());
        Attack(w, brawlerA, windup: 0.3f); // and shooting at each other
        Evade(w, player);
        Tick(w, 4);
        True(!w.Get<Rage>(player).Threat, "two enemies fighting each other do not threaten the player");
        Equal(0f, w.Get<Rage>(player).Calm, "and their fight pays the player nothing");
        Close(w);

        // ---- under threat: wall running pays per second ----
        w = NewWorld();
        player = Mover(w, Vector3.zero);
        var hunter = Hunter(w, new Vector3(0f, 0f, 6f), player);
        SetMotor(w, player, wallRunning: true);
        Tick(w, 2);
        True(w.Get<Rage>(player).Threat, "an enemy that has the player and sees them is a threat");
        float before = w.Get<Rage>(player).Calm;
        Tick(w, 60);
        float perSecond = w.Get<Rage>(player).Calm - before;
        True(MathF.Abs(perSecond - Def.wallRunPerSecond) < Def.wallRunPerSecond * 0.2f,
             $"a second of wall running under threat pays about {Def.wallRunPerSecond} (paid {perSecond})");

        // ---- sliding / airborne pays the smaller rate ----
        SetMotor(w, player, wallRunning: false, sliding: true);
        before = w.Get<Rage>(player).Calm;
        Tick(w, 60);
        float slide = w.Get<Rage>(player).Calm - before;
        True(MathF.Abs(slide - Def.evadePerSecond) < Def.evadePerSecond * 0.25f,
             $"a second of sliding under threat pays about {Def.evadePerSecond} (paid {slide})");
        Close(w);

        // ---- the dodge needs a live attack, not just a threat ----
        w = NewWorld();
        player = Mover(w, Vector3.zero);
        hunter = Hunter(w, new Vector3(0f, 0f, 6f), player);
        before = w.Get<Rage>(player).Calm;
        SetMotor(w, player, dashing: true);
        Evade(w, player); // the motor counts the move; nothing is shooting
        Tick(w, 2);
        // dashing under threat still earns the per-second evade rate; what it must not earn is the dodge
        True(w.Get<Rage>(player).Calm - before < Def.dodge * 0.5f, "a dash with nobody shooting is not a dodge");
        SetMotor(w, player, dashing: false);
        Tick(w, 2);

        Attack(w, hunter, windup: 0.3f);
        Tick(w, 2); // the attack entity has to exist before the move counts as a dodge
        before = w.Get<Rage>(player).Calm;
        SetMotor(w, player, dashing: true);
        Evade(w, player);
        Tick(w, 2);
        True(w.Get<Rage>(player).Calm - before >= Def.dodge, "a dash while an attack is winding up is a dodge");

        // two moves in one tick are two dodges: the count cannot lose one
        before = w.Get<Rage>(player).Calm;
        Evade(w, player); Evade(w, player);
        Tick(w, 2);
        True(w.Get<Rage>(player).Calm - before >= 2f * Def.dodge, "every evasive move counted is paid");
        Close(w);

        // ---- a near miss pays once; a bullet far away pays nothing ----
        w = NewWorld();
        player = Mover(w, Vector3.zero);
        SetMotor(w, player); // grounded and still: only the bullets should pay
        hunter = Hunter(w, new Vector3(0f, 0f, 20f), player);
        var bullet = Bullet(w, hunter, new Vector3(1f, 0.9f, 5f));
        Tick(w, 4); // the transform updates in LateTick and the gain lands a tick later
        Equal(0f, w.Get<Rage>(player).Calm, "a bullet's first tick has no segment to measure");
        Move(w, bullet, new Vector3(1f, 0.9f, -5f)); // flies past, one metre to the side
        Tick(w, 4); // the transform updates in LateTick and the gain lands a tick later
        Equal(Def.nearMiss, w.Get<Rage>(player).Calm, "a bullet that passed inside the band pays the near miss");
        Move(w, bullet, new Vector3(1f, 0.9f, -15f));
        Tick(w, 4); // the transform updates in LateTick and the gain lands a tick later
        Equal(Def.nearMiss, w.Get<Rage>(player).Calm, "the same bullet pays only once");

        var far = Bullet(w, hunter, new Vector3(8f, 0.9f, 5f));
        Tick(w, 4); // the transform updates in LateTick and the gain lands a tick later
        Move(w, far, new Vector3(8f, 0.9f, -5f));
        Tick(w, 4); // the transform updates in LateTick and the gain lands a tick later
        Equal(Def.nearMiss, w.Get<Rage>(player).Calm, "a bullet that passed eight metres away is not a near miss");
        Close(w);

        // ---- shaking a pursuer off: driven by the real SightSystem, which flags the edge ----
        w = NewWorld();
        player = Mover(w, new Vector3(500f, 1f, 0f));
        var watcher = Watcher(w, new Vector3(500f, 1f, 6f)); // 6 m ahead of the player, facing them
        SetMotor(w, player, speed: 8f);
        Tick(w, 3);
        True(w.Get<Perception>(watcher).Target == player && w.Get<Rage>(player).Threat, "a watching enemy is a threat");

        before = w.Get<Rage>(player).Calm;
        w.Get<LocalTransform>(player).Position = new Vector3(540f, 1f, 0f); // gone, well past the sight range
        Tick(w, 3);
        Equal(before + Def.breakLineOfSight, w.Get<Rage>(player).Calm, "losing a pursuer while moving pays, once");
        True(!w.Get<Perception>(watcher).LostSight, "the flag is the tick's edge, not a state");

        before = w.Get<Rage>(player).Calm;
        Tick(w, 10);
        Equal(before, w.Get<Rage>(player).Calm, "staying lost pays nothing further");
        Close(w);

        // ---- the penalty for being hit ----
        w = NewWorld();
        player = Mover(w, Vector3.zero);
        hunter = Hunter(w, new Vector3(0f, 0f, 6f), player);

        w.Get<Rage>(player).Calm = 0.5f;
        SetMotor(w, player); // standing still: nothing else moves the meter
        Tick(w, 2);
        before = w.Get<Rage>(player).Calm;
        w.Post(new HitEvent { Target = player, Point = Vector3.zero });
        Tick(w, 2);
        Equal(before - Def.damagePenalty, w.Get<Rage>(player).Calm, "taking a blow costs Calm");

        // and it cannot go below empty
        w.Get<Rage>(player).Calm = 0.05f;
        w.Post(new HitEvent { Target = player, Point = Vector3.zero });
        Tick(w, 2);
        Equal(0f, w.Get<Rage>(player).Calm, "the penalty cannot take the meter below empty");
        Close(w);
    }

    /// <summary>A player with the movement components the evasion gains read.</summary>
    private static Entity Mover(World w, Vector3 at)
    {
        var e = Player(w, at);
        w.Add(e, new PlayerMotor());
        w.Add(e, new CharacterMotor());
        return e;
    }

    /// <summary>An evasive move, as the motor records one (a dash, a slide start or a wall jump).</summary>
    private static void Evade(World w, Entity player) => w.Get<PlayerMotor>(player).Evasions++;

    private static void SetMotor(World w, Entity player, bool wallRunning = false, bool sliding = false,
                                 bool dashing = false, float speed = 0f)
    {
        ref var p = ref w.Get<PlayerMotor>(player);
        p.IsWallRunning = wallRunning; p.IsSliding = sliding; p.IsDashing = dashing; p.CurrentSpeed = speed;
        w.Get<CharacterMotor>(player).IsGrounded = true; // only the flags above should pay
    }

    /// <summary>A whole enemy as the AI builds one: SightSystem drives its perception (needs the heading).</summary>
    private static Entity Watcher(World w, Vector3 at)
    {
        var e = Enemy(w, at);
        w.Add(e, new Heading { Yaw = 180f }); // faces -z, down the player's way
        w.Add(e, new Perception());
        w.Add(e, new EnemyWeapon());
        return e;
    }

    /// <summary>An enemy that has the player and can see them (with the gun its attack entity is stepped against).</summary>
    private static Entity Hunter(World w, Vector3 at, Entity player)
    {
        var e = Enemy(w, at);
        w.Add(e, new Perception { Target = player, Seen = true, LastKnown = Vector3.zero });
        w.Add(e, new EnemyWeapon());
        return e;
    }

    /// <summary>An attack entity parented to its enemy, as EnemyAttackSystem spawns it.</summary>
    private static Entity Attack(World w, Entity enemy, float windup)
    {
        var e = w.CreateEntity();
        w.Add(e, new EnemyAttack { WindupLeft = windup, ShotsLeft = 3 });
        w.Add(e, new Parent { Value = enemy });
        return e;
    }

    private static Entity Bullet(World w, Entity shooter, Vector3 at)
    {
        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(at));
        w.Add(e, new Projectile { Kind = 1, Instigator = shooter });
        return e;
    }

    private static void Move(World w, Entity e, Vector3 to) => w.Get<LocalTransform>(e).Position = to;


    // ---------------------------------------------------------------- 7.3: what the states do

    private static void StateEffects(Action<bool, string> True, Action<float, float, string> Equal)
    {
        // ---- the multipliers are the state's, and Normal changes nothing ----
        Equal(1f, Rages.DamageDealt(new Rage()), "Normal deals its own damage");
        Equal(Def.enragedDamageDealt, Rages.DamageDealt(new Rage { State = RageState.Enraged }), "ENRAGED deals more");
        Equal(1f, Rages.DamageDealt(new Rage { State = RageState.Depressed }), "DEPRESSED deals its own damage");
        Equal(1f, Rages.DamageTaken(new Rage()), "Normal takes its own damage");
        Equal(Def.depressedDamageTaken, Rages.DamageTaken(new Rage { State = RageState.Depressed }), "DEPRESSED takes less");
        Equal(1f, Rages.DamageTaken(new Rage { State = RageState.Enraged }), "ENRAGED takes its own damage");
        Equal(1f, Rages.SightRangeScale(new Rage()), "Normal is seen at the usual range");
        Equal(Def.depressedSightRange, Rages.SightRangeScale(new Rage { State = RageState.Depressed }), "DEPRESSED is harder to see");
        Equal(0, Rages.ExtraAirDashes(new Rage()), "Normal gets the usual air dashes");
        Equal(Def.depressedAirDashes, Rages.ExtraAirDashes(new Rage { State = RageState.Depressed }), "DEPRESSED gets one more air dash");

        // ---- a blow, over a world: dealt by an ENRAGED player ----
        var w = NewWorld();
        var player = Player(w, Vector3.zero);
        var enemy = Hurtable(w, new Vector3(0f, 0f, 10f), health: 100f);
        w.Post(new DamageEvent { Instigator = player, Target = enemy, Amount = 20f });
        Tick(w, 2);
        Equal(80f, w.Get<Health>(enemy).Current, "a blow from a Normal player lands as it is");

        Enrage(w, player);
        w.Post(new DamageEvent { Instigator = player, Target = enemy, Amount = 20f });
        Tick(w, 2);
        Equal(50f, w.Get<Health>(enemy).Current, "ENRAGED multiplies the blow the player struck");

        // a blow with no author is nobody's doing
        w.Post(new DamageEvent { Target = enemy, Amount = 10f });
        Tick(w, 2);
        Equal(40f, w.Get<Health>(enemy).Current, "a blow with no instigator is not multiplied");
        Close(w);

        // ---- taken by a DEPRESSED player ----
        w = NewWorld();
        player = Player(w, Vector3.zero);
        w.Add(player, new Health { Current = 100f, Max = 100f });
        w.Post(new DamageEvent { Target = player, Amount = 20f });
        Tick(w, 2);
        Equal(80f, w.Get<Health>(player).Current, "a Normal player takes the whole blow");

        Depress(w, player);
        w.Post(new DamageEvent { Target = player, Amount = 20f });
        Tick(w, 2);
        Equal(70f, w.Get<Health>(player).Current, "DEPRESSED halves what the player takes");
        Close(w);

        // ---- ENRAGED lifesteal ----
        w = NewWorld();
        player = Player(w, Vector3.zero);
        w.Add(player, new Health { Current = 50f, Max = 100f });
        enemy = Enemy(w, new Vector3(0f, 0f, 10f));
        Enrage(w, player);
        Hit(w, enemy, new Vector3(0f, 0f, 10f), headshot: false, killed: false);
        Tick(w, 2);
        Equal(50f + Def.lifestealPerHit, w.Get<Health>(player).Current, "ENRAGED: a hit feeds the player");
        Hit(w, enemy, new Vector3(0f, 0f, 10f), headshot: false, killed: true);
        Tick(w, 2);
        Equal(50f + Def.lifestealPerHit + Def.lifestealPerKill, w.Get<Health>(player).Current, "ENRAGED: a kill feeds more");

        w.Get<Health>(player).Current = 99f;
        Hit(w, enemy, new Vector3(0f, 0f, 10f), headshot: false, killed: true);
        Tick(w, 2);
        Equal(100f, w.Get<Health>(player).Current, "lifesteal stops at full health");

        // and nothing of it outside the state
        Calm(w, player);
        w.Get<Health>(player).Current = 50f;
        Hit(w, enemy, new Vector3(0f, 0f, 10f), headshot: false, killed: true);
        Tick(w, 2);
        Equal(50f, w.Get<Health>(player).Current, "a Normal player steals no life");
        Close(w);

        // ---- DEPRESSED armour regen ----
        w = NewWorld();
        player = Player(w, Vector3.zero);
        w.Add(player, new Armor { Current = 0f, Max = 100f, Resistance = 1f });
        Depress(w, player);
        Tick(w, 60);
        float armor = w.Get<Armor>(player).Current;
        True(MathF.Abs(armor - Def.armorRegenPerSecond) < Def.armorRegenPerSecond * 0.2f,
             $"DEPRESSED regrows about {Def.armorRegenPerSecond} armour a second (grew {armor})");

        Calm(w, player);
        armor = w.Get<Armor>(player).Current;
        Tick(w, 60);
        Equal(armor, w.Get<Armor>(player).Current, "a Normal player regrows no armour");
        Close(w);
    }

    /// <summary>Forces a state with a full meter: a state entered with an empty meter ends on the next tick.</summary>
    private static void Enrage(World w, Entity player) { ref var r = ref w.Get<Rage>(player); r.Fury = 1f; r.State = RageState.Enraged; }
    private static void Depress(World w, Entity player) { ref var r = ref w.Get<Rage>(player); r.Calm = 1f; r.State = RageState.Depressed; }
    private static void Calm(World w, Entity player) { ref var r = ref w.Get<Rage>(player); r.State = RageState.Normal; }

    /// <summary>Something with health that is not an enemy or a player: a blow lands on it unscaled.</summary>
    private static Entity Hurtable(World w, Vector3 at, float health)
    {
        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(at));
        w.Add(e, new Health { Current = health, Max = health });
        return e;
    }

    private static World NewWorld()
    {
        var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(PlayerLink).Assembly);
        w.FixedDeltaTime = Dt;
        return w;
    }

    private static Entity Player(World w, Vector3 at)
    {
        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(at));
        w.Add(e, new PlayerTag());
        w.Add(e, new Rage());
        return e;
    }

    private static Entity Enemy(World w, Vector3 at)
    {
        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(at));
        w.Add(e, new Enemy { Kind = 0 });
        return e;
    }

    /// <summary>A blow the player struck (the instigator is what makes it aggression).</summary>
    private static void Hit(World w, Entity target, Vector3 point, bool headshot, bool killed, Entity by = default) =>
        w.Post(new HitEvent { Target = target, Instigator = by.IsNull ? Player(w) : by, Point = point, Headshot = headshot, Killed = killed });

    /// <summary>The one player in the world (the checks never build two).</summary>
    private static Entity Player(World w)
    {
        foreach (var chunk in w.Query().WithReadOnly<PlayerTag>().Build())
            for (int i = 0; i < chunk.Length; i++)
                if (chunk.Includes(i)) return chunk.Entities[i];
        return Entity.Null;
    }

    private static void Tick(World w, int n) { for (int i = 0; i < n; i++) w.Update(Dt); }

    private static void Close(World w) { w.DestroyAllEntities(); w.Update(Dt); w.Dispose(); }
}

