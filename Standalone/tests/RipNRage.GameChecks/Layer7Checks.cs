using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
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
/// </summary>
internal static class Layer7Checks
{
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
    }

    private const float Dt = 1f / 60f;

    private static RageInputs Gain(float fury = 0f, float calm = 0f) => new RageInputs { FuryGain = fury, CalmGain = calm };
    private static RageInputs Press(bool fury = false, bool calm = false) => new RageInputs { ActivateFury = fury, ActivateCalm = calm };

    // ---------------------------------------------------------------- the meters

    private static void Meters(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var r = new Rage();
        r = Rages.Step(in r, Gain(fury: 0.2f), Dt);
        Equal(0.2f, r.Fury, "a gain fills the meter");
        Equal(0f, r.Calm, "the other meter is untouched");

        // no decay anywhere: what is earned is banked until it is spent
        var idle = r;
        for (int i = 0; i < 600; i++) idle = Rages.Step(in idle, default, Dt);
        Equal(0.2f, idle.Fury, "ten seconds idle: the meter does not decay");

        var full = new Rage { Fury = 0.95f };
        full = Rages.Step(in full, Gain(fury: 0.5f), Dt);
        Equal(1f, full.Fury, "a meter fills to one and no further");

        var both = Rages.Step(in r, Gain(fury: 0.1f, calm: 0.3f), Dt);
        Equal(0.3f, both.Fury, "both meters can fill at once: Fury");
        Equal(0.3f, both.Calm, "both meters can fill at once: Calm");
        True(both.State == RageState.Normal, "filling a meter does not enter a state: the player chooses");
    }

    // ---------------------------------------------------------------- activation

    private static void Activation(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var empty = new Rage();
        empty = Rages.Step(in empty, Press(fury: true), Dt);
        True(empty.State == RageState.Normal, "an empty meter cannot be activated");

        var partial = new Rage { Fury = 0.5f };
        partial = Rages.Step(in partial, Press(fury: true), Dt);
        True(partial.State == RageState.Enraged, "a partly filled meter can be activated");

        var calm = new Rage { Calm = 1f };
        calm = Rages.Step(in calm, Press(calm: true), Dt);
        True(calm.State == RageState.Depressed, "the Calm meter activates DEPRESSED");

        var ambiguous = new Rage { Fury = 1f, Calm = 1f };
        ambiguous = Rages.Step(in ambiguous, new RageInputs { ActivateFury = true, ActivateCalm = true }, Dt);
        True(ambiguous.State == RageState.Enraged, "both pressed in one tick: Fury wins");

        // partial = proportionally shorter: half a meter is half the state
        var half = new Rage { Fury = 0.5f, State = RageState.Enraged };
        Lasts(True, Rages.EnragedDuration * 0.5f, Drain(ref half), "half a Fury meter buys half of ENRAGED");
        var whole = new Rage { Calm = 1f, State = RageState.Depressed };
        Lasts(True, Rages.DepressedDuration, Drain(ref whole), "a full Calm meter buys the whole of DEPRESSED");
    }

    /// <summary>A duration in whole ticks: the state ends on the tick that empties the meter, never before it.</summary>
    private static void Lasts(Action<bool, string> True, float expected, float actual, string test) =>
        True(actual >= expected && actual - expected <= Dt + 1e-4f, $"{test} (expected {expected}s, lasted {actual}s)");

    /// <summary>Steps until the state ends; returns how long that took.</summary>
    private static float Drain(ref Rage r)
    {
        float t = 0f;
        for (int i = 0; i < 100_000 && r.State != RageState.Normal; i++) { r = Rages.Step(in r, default, Dt); t += Dt; }
        return t;
    }

    // ---------------------------------------------------------------- inside a state

    private static void InAState(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var r = new Rage { Fury = 1f, Calm = 0.4f, State = RageState.Enraged };

        var gained = Rages.Step(in r, Gain(fury: 0.5f, calm: 0.5f), Dt);
        Equal(1f - Dt / Rages.EnragedDuration, gained.Fury, "inside a state the active meter only drains: no Fury gain");
        Equal(0.4f, gained.Calm, "inside a state the other meter is frozen: no Calm gain");

        var pressed = Rages.Step(in r, new RageInputs { ActivateCalm = true, ActivateFury = true }, Dt);
        True(pressed.State == RageState.Enraged, "a state cannot be left by pressing: only the drain ends it");

        // the drain is the clock, and it empties the meter it spent
        var spent = r;
        float took = Drain(ref spent);
        Lasts(True, Rages.EnragedDuration, took, "a full Fury meter runs ENRAGED for its duration");
        Equal(0f, spent.Fury, "the spent meter is empty");
        Equal(0.4f, spent.Calm, "the other meter kept everything it had");

        // chaining: the moment the first state ends, a meter filled earlier can be spent
        var chained = Rages.Step(in spent, Press(calm: true), Dt);
        True(chained.State == RageState.Depressed, "ENRAGED can be chained straight into DEPRESSED");
        Lasts(True, Rages.DepressedDuration * 0.4f, Drain(ref chained), "the chained state lasts what its meter held");
    }

    private static void Effects(Action<float, float, string> Equal)
    {
        Equal(1f, Rages.SpeedMultiplier(new Rage()), "Normal does not change the player's speed");
        Equal(Rages.EnragedSpeed, Rages.SpeedMultiplier(new Rage { State = RageState.Enraged }), "ENRAGED is faster");
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
        Equal(Rages.HitFury, w.Get<Rage>(player).Fury, "a hit on an enemy pays the hit gain");

        // a headshot hit pays more
        Hit(w, enemy, new Vector3(0f, 0f, 20f), headshot: true, killed: false);
        Tick(w, 2);
        Equal(Rages.HitFury * 2f + Rages.HeadshotHitFury, w.Get<Rage>(player).Fury, "a headshot pays the extra");
        Close(w);

        // a kill far away: hit + kill, no close-range bonus
        w = NewWorld();
        player = Player(w, Vector3.zero);
        enemy = Enemy(w, new Vector3(0f, 0f, 20f));
        Hit(w, enemy, new Vector3(0f, 0f, 20f), headshot: false, killed: true);
        Tick(w, 2);
        Equal(Rages.HitFury + Rages.KillFury, w.Get<Rage>(player).Fury, "a kill out of range pays hit + kill");
        Close(w);

        // the same kill up close
        w = NewWorld();
        player = Player(w, Vector3.zero);
        enemy = Enemy(w, new Vector3(0f, 0f, 3f));
        Hit(w, enemy, new Vector3(0f, 0f, 3f), headshot: true, killed: true);
        Tick(w, 2);
        Equal(Rages.HitFury + Rages.HeadshotHitFury + Rages.KillFury + Rages.HeadshotKillFury + Rages.CloseKillFury,
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
        w.Post(new RageGainEvent { Fury = Rages.SplashFury, Calm = 0.25f });
        Tick(w, 2);
        Equal(Rages.SplashFury, w.Get<Rage>(player).Fury, "a posted gain pays Fury");
        Equal(0.25f, w.Get<Rage>(player).Calm, "a posted gain pays Calm");

        // death clears both meters and the state
        w.Get<Rage>(player).State = RageState.Enraged;
        w.Post(new DeathEvent { Entity = player });
        Tick(w, 2);
        var dead = w.Get<Rage>(player);
        True(dead.Fury == 0f && dead.Calm == 0f && dead.State == RageState.Normal, "a dead player keeps no meters and no state");
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
        True(MathF.Abs(perSecond - Calms.WallRunPerSecond) < Calms.WallRunPerSecond * 0.2f,
             $"a second of wall running under threat pays about {Calms.WallRunPerSecond} (paid {perSecond})");

        // ---- sliding / airborne pays the smaller rate ----
        SetMotor(w, player, wallRunning: false, sliding: true);
        before = w.Get<Rage>(player).Calm;
        Tick(w, 60);
        float slide = w.Get<Rage>(player).Calm - before;
        True(MathF.Abs(slide - Calms.EvadePerSecond) < Calms.EvadePerSecond * 0.25f,
             $"a second of sliding under threat pays about {Calms.EvadePerSecond} (paid {slide})");
        Close(w);

        // ---- the dodge needs a live attack, not just a threat ----
        w = NewWorld();
        player = Mover(w, Vector3.zero);
        hunter = Hunter(w, new Vector3(0f, 0f, 6f), player);
        before = w.Get<Rage>(player).Calm;
        SetMotor(w, player, dashing: true); // the edge: not dashing -> dashing
        Tick(w, 2);
        // dashing under threat still earns the per-second evade rate; what it must not earn is the dodge
        True(w.Get<Rage>(player).Calm - before < Calms.Dodge * 0.5f, "a dash with nobody shooting is not a dodge");
        SetMotor(w, player, dashing: false);
        Tick(w, 2);

        Attack(w, hunter, windup: 0.3f);
        before = w.Get<Rage>(player).Calm;
        SetMotor(w, player, dashing: true);
        Tick(w, 2);
        True(w.Get<Rage>(player).Calm - before >= Calms.Dodge, "a dash while an attack is winding up is a dodge");
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
        Equal(Calms.NearMiss, w.Get<Rage>(player).Calm, "a bullet that passed inside the band pays the near miss");
        Move(w, bullet, new Vector3(1f, 0.9f, -15f));
        Tick(w, 4); // the transform updates in LateTick and the gain lands a tick later
        Equal(Calms.NearMiss, w.Get<Rage>(player).Calm, "the same bullet pays only once");

        var far = Bullet(w, hunter, new Vector3(8f, 0.9f, 5f));
        Tick(w, 4); // the transform updates in LateTick and the gain lands a tick later
        Move(w, far, new Vector3(8f, 0.9f, -5f));
        Tick(w, 4); // the transform updates in LateTick and the gain lands a tick later
        Equal(Calms.NearMiss, w.Get<Rage>(player).Calm, "a bullet that passed eight metres away is not a near miss");
        Close(w);

        // ---- shaking a pursuer off, and the penalty for being hit ----
        w = NewWorld();
        player = Mover(w, Vector3.zero);
        hunter = Hunter(w, new Vector3(0f, 0f, 6f), player);
        SetMotor(w, player, speed: 8f);
        Tick(w, 2);
        before = w.Get<Rage>(player).Calm;
        w.Get<Perception>(hunter).Seen = false; // the enemy lost sight of a moving player
        Tick(w, 2);
        Equal(before + Calms.BreakLineOfSight, w.Get<Rage>(player).Calm, "losing a pursuer while moving pays");

        w.Get<Rage>(player).Calm = 0.5f;
        SetMotor(w, player); // standing still: nothing else moves the meter
        Tick(w, 2);
        before = w.Get<Rage>(player).Calm;
        w.Post(new HitEvent { Target = player, Point = Vector3.zero });
        Tick(w, 2);
        Equal(before - Calms.DamagePenalty, w.Get<Rage>(player).Calm, "taking a blow costs Calm");

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

    private static void SetMotor(World w, Entity player, bool wallRunning = false, bool sliding = false,
                                 bool dashing = false, float speed = 0f)
    {
        ref var p = ref w.Get<PlayerMotor>(player);
        p.IsWallRunning = wallRunning; p.IsSliding = sliding; p.IsDashing = dashing; p.CurrentSpeed = speed;
        w.Get<CharacterMotor>(player).IsGrounded = true; // only the flags above should pay
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

    private static void Hit(World w, Entity target, Vector3 point, bool headshot, bool killed) =>
        w.Post(new HitEvent { Target = target, Point = point, Headshot = headshot, Killed = killed });

    private static void Tick(World w, int n) { for (int i = 0; i < n; i++) w.Update(Dt); }

    private static void Close(World w) { w.DestroyAllEntities(); w.Update(Dt); w.Dispose(); }
}

