using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.AI;
using RipNRage.Game.Ecs.Combat;

/// <summary>
/// Layer 6 (enemies, AI). 6.2: the behaviour tree became a state tree — a value (<see cref="AiBrain"/>) stepped by
/// pure functions, writing an <see cref="AiIntent"/>. The checks pin the tree's shape: the interrupt order (the
/// target branch is decided before the leaf runs), the wander dwell, and that a tick is a function of its inputs.
/// 6.3: the senses — sight as range → cone → line of sight with a memory, and sound as a <see cref="Noise"/> entity
/// whose front crosses the level at <see cref="Noises.Speed"/> and is muffled by walls. 6.4: steering — the intent
/// into the motor and the <see cref="Heading"/>, arrival, the ledge guard, a wander that gives up.
/// </summary>
internal static class Layer6Checks
{
    public static void Run(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var saved = Enemies.All;
        Enemies.All = new[] { new EnemyData { name = "TestGrunt", attackRange = 10f, wanderWait = 1f, wanderRadius = 15f, wanderMinDistance = 10f } };
        try
        {
            StateTree(True, Equal);
            BrainSystem(True);
            Sight(True);
            Hearing(True);
            Steer(True, Equal);
        }
        finally { Enemies.All = saved; }
    }

    /// <summary>The pure step, driven by hand.</summary>
    private static void StateTree(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var def = Enemies.All[0];
        var self = new Vector3(300f, 1f, 0f);
        var b = new AiBrain(); var intent = new AiIntent();

        // unaware: idle for wanderWait, then one wander tick that moves and returns to idle
        var nobody = new Perception();
        for (int i = 0; i < 60; i++) AiBrains.Step(ref b, ref intent, def, self, in nobody, 1f / 60f);
        True(b.State == AiState.Idle && !intent.Move && !intent.Attack, "a fresh brain idles quietly through the dwell");
        int moves = 0; Vector3 point = default;
        for (int i = 0; i < 120 && moves == 0; i++)
        {
            AiBrains.Step(ref b, ref intent, def, self, in nobody, 1f / 60f);
            if (intent.Move) { moves++; point = intent.MoveTo; }
        }
        True(moves == 1 && b.State == AiState.Wander, "after the dwell the brain wanders: a move intent towards its point");
        float d = Vector3.Distance(point, self);
        True(d > def.wanderMinDistance && d <= def.wanderRadius && point.y == self.y, $"the wander point is {d:0.0} m away on the ground plane: beyond the minimum, inside the radius");
        AiBrains.Step(ref b, ref intent, def, self, in nobody, 1f / 60f);
        True(intent.Move && intent.MoveTo == point, "the wander holds its point from tick to tick");
        // standing still (nothing moves the body in vitro), the wander gives up after its timeout
        for (int i = 0; i < 3 * 60; i++) AiBrains.Step(ref b, ref intent, def, self, in nobody, 1f / 60f);
        True(b.State == AiState.Idle && !intent.Move, "a wander that gets nowhere in three seconds is given up");
        // and one that arrives ends at once
        b = new AiBrain { State = AiState.Wander, StateTime = 0.5f, MoveTo = self + new Vector3(0f, 0f, 12f) };
        AiBrains.Step(ref b, ref intent, def, self + new Vector3(0f, 0f, 8f), in nobody, 1f / 60f);
        True(b.State == AiState.Idle && !intent.Move, "within the 5 m arrive distance of its point the wander is done");

        // engaged: the target branch outranks the leaf
        var target = new Entity(7, 1);
        var far = self + new Vector3(0f, 0f, 20f);
        var seenFar = new Perception { Target = target, LastKnown = far, Seen = true };
        AiBrains.Step(ref b, ref intent, def, self, in seenFar, 1f / 60f);
        True(b.State == AiState.Chase && intent.Move && intent.MoveTo == far && !intent.Attack, "a target seen 20 m away: chase towards it");
        var near = self + new Vector3(0f, 0f, 5f);
        var seenNear = new Perception { Target = target, LastKnown = near, Seen = true };
        AiBrains.Step(ref b, ref intent, def, self, in seenNear, 1f / 60f);
        True(b.State == AiState.Attack && intent.Attack && intent.FacePoint == near && !intent.Move, "the target seen 5 m away: attack, aimed at it, standing still");
        Equal(0f, b.StateTime, "entering a leaf resets its clock");
        AiBrains.Step(ref b, ref intent, def, self, in seenFar, 1f / 60f);
        True(b.State == AiState.Chase, "the target backing out of range drops to chase again");
        var heardNear = new Perception { Target = target, LastKnown = near, Seen = false };
        AiBrains.Step(ref b, ref intent, def, self, in heardNear, 1f / 60f);
        True(b.State == AiState.Chase && !intent.Move && !intent.Attack, "a target only heard 5 m away: chase, but already within arrive distance — stand and look, do not shoot");
        var heardFar = new Perception { Target = target, LastKnown = far, Seen = false };
        AiBrains.Step(ref b, ref intent, def, self, in heardFar, 1f / 60f);
        True(b.State == AiState.Chase && intent.Move && intent.MoveTo == far, "heard 20 m away: walk to where it was heard");

        // the target is lost (dead, gone, or forgotten): back to the unaware half, quietly
        AiBrains.Step(ref b, ref intent, def, self, in nobody, 1f / 60f);
        True(b.State == AiState.Idle && !intent.Move && !intent.Attack, "a lost target drops the brain to idle");

        // purity: the same inputs give the same outputs (wander rolls the dice, so pin it on the deterministic leaves)
        var b1 = new AiBrain { State = AiState.Chase, StateTime = 3f }; var i1 = new AiIntent();
        var b2 = b1; var i2 = new AiIntent();
        AiBrains.Step(ref b1, ref i1, def, self, in seenFar, 0.5f);
        AiBrains.Step(ref b2, ref i2, def, self, in seenFar, 0.5f);
        True(b1.State == b2.State && b1.StateTime == b2.StateTime && b1.MoveTo == b2.MoveTo && i1.Move == i2.Move && i1.MoveTo == i2.MoveTo,
             "a step is a function of its inputs");
        Equal(3.5f, b1.StateTime, "the leaf clock advances by dt");
    }

    private static World NewWorld()
    {
        var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(RipNRage.Game.Ecs.PlayerLink).Assembly);
        w.FixedDeltaTime = 1f / 60f;
        return w;
    }

    /// <summary>An enemy entity as AIState builds it, minus the body: identity, senses, mind.</summary>
    private static Entity Grunt(World w, Vector3 at, Quaternion facing)
    {
        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(at, facing));
        w.Add(e, new Enemy { Kind = 0 });
        w.Add(e, new Heading { Yaw = facing.eulerAngles.y });
        w.Add(e, new Perception());
        w.Add(e, new AiBrain());
        w.Add(e, new AiIntent());
        return e;
    }

    private static Entity Player(World w, Vector3 at, bool tagged = true)
    {
        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(at));
        w.Add(e, new Health { Current = 100f, Max = 100f });
        if (tagged) w.Add(e, new RipNRage.Game.Ecs.PlayerTag());
        return e;
    }

    private static void Close(World w) { w.DestroyAllEntities(); w.Update(1f / 60f); w.Dispose(); }

    /// <summary>Ticks; a direct LocalTransform write is one LateTick away from the LocalToWorld the senses read.</summary>
    private static void Tick(World w, int n) { for (int i = 0; i < n; i++) w.Update(1f / 60f); }

    /// <summary>The system over a world: sight finds the target, the brain acts, and a dead enemy's brain stops.</summary>
    private static void BrainSystem(Action<bool, string> True)
    {
        var w = NewWorld();
        var enemy = Grunt(w, new Vector3(400f, 1f, 0f), Quaternion.identity);
        var player = Player(w, new Vector3(400f, 1f, 6f));
        w.Update(1f / 60f);
        w.Update(1f / 60f);
        True(w.Get<AiBrain>(enemy).State == AiState.Attack && w.Get<AiIntent>(enemy).Attack, "sight → perception → brain: the player 6 m ahead is attacked");

        w.Add(player, new Dead());
        w.Update(1f / 60f);
        True(w.Get<AiBrain>(enemy).State == AiState.Idle && w.Get<Perception>(enemy).Target.IsNull, "a dead target is dropped");

        w.Remove<Dead>(player);
        w.Add(enemy, new Dead());
        var before = w.Get<AiBrain>(enemy);
        w.Update(1f / 60f);
        var after = w.Get<AiBrain>(enemy);
        True(after.State == before.State && after.StateTime == before.StateTime && !w.Get<AiIntent>(enemy).Attack && !w.Get<AiIntent>(enemy).Move,
             "a dead enemy's brain stops and its intent reads quiet");
        Close(w);
    }

    /// <summary>Range, cone, line of sight, memory.</summary>
    private static void Sight(Action<bool, string> True)
    {
        var w = NewWorld();
        var self = new Vector3(500f, 1f, 0f);
        var enemy = Grunt(w, self, Quaternion.identity); // faces +z
        var player = Player(w, self + new Vector3(0f, 0f, 6f));
        Tick(w, 2);
        var p = w.Get<Perception>(enemy);
        True(p.Seen && p.Target == player && p.LastKnown == self + new Vector3(0f, 0f, 6f), "6 m ahead, in the open: seen, and the last known position is the live one");

        w.Get<LocalTransform>(player).Position = self + new Vector3(0f, 0f, 12f);
        Tick(w, 2);
        p = w.Get<Perception>(enemy);
        True(!p.Seen && p.Target == player, "12 m ahead is past the 10 m range: out of sight, but remembered");

        w.Get<LocalTransform>(player).Position = self + new Vector3(0f, 0f, -6f);
        Tick(w, 2);
        True(!w.Get<Perception>(enemy).Seen, "6 m behind is outside the 180° cone");
        w.Get<Heading>(enemy).Yaw = 180f;
        Tick(w, 1);
        True(w.Get<Perception>(enemy).Seen, "the cone follows the heading: turned round, the same spot is in sight");
        w.Get<Heading>(enemy).Yaw = 0f;
        Tick(w, 1);

        w.Get<LocalTransform>(player).Position = self + new Vector3(6f, 0f, 0.5f);
        Tick(w, 2);
        True(w.Get<Perception>(enemy).Seen, "just inside the cone's edge (6 m to the side, half a metre forward): seen");

        w.Get<LocalTransform>(player).Position = self + new Vector3(0f, 0f, 8f);
        var wall = RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(w, self + new Vector3(0f, 1f, 4f), Quaternion.identity, new Vector3(4f, 4f, 0.2f), Layers.Wall);
        Tick(w, 3); // the wall registers at the next fixed step
        True(!w.Get<Perception>(enemy).Seen, "8 m ahead behind a wall: not seen");
        w.DestroyEntity(wall);
        Tick(w, 3);
        True(w.Get<Perception>(enemy).Seen, "the wall gone: seen again");

        // memory: five seconds out of sight and the target is forgotten
        w.Get<LocalTransform>(player).Position = self + new Vector3(0f, 0f, -20f);
        Tick(w, 4 * 60);
        True(w.Get<Perception>(enemy).Target == player && w.Get<AiBrain>(enemy).State == AiState.Chase, "four seconds unseen: still remembered, still chased");
        Tick(w, 2 * 60);
        True(w.Get<Perception>(enemy).Target.IsNull && w.Get<AiBrain>(enemy).State == AiState.Idle, "six seconds unseen: forgotten, back to idle");
        Close(w);
    }

    /// <summary>A noise is an entity: its front crosses the level, walls muffle it, and it dies spent.</summary>
    private static void Hearing(Action<bool, string> True)
    {
        var w = NewWorld();
        var origin = new Vector3(600f, 1f, 0f);
        var player = Player(w, origin, tagged: false); // never seen: only heard
        var near = Grunt(w, origin + new Vector3(0f, 0f, 20f), Quaternion.identity);
        var far = Grunt(w, origin + new Vector3(0f, 0f, 40f), Quaternion.identity);
        var noises = w.Query().With<Noise>().Build();
        Tick(w, 2);

        Noises.Emit(w.Commands, origin, player, 30f);
        w.Update(1f / 60f); // the entity materialises
        True(noises.Count() == 1, "the sound is an entity");
        // the front moves 80 m/s: 20 m takes a quarter of a second
        for (int i = 0; i < 10; i++) w.Update(1f / 60f);
        True(w.Get<Perception>(near).Target.IsNull, "a sixth of a second in, the front (13 m) has not reached the ear 20 m away");
        for (int i = 0; i < 6; i++) w.Update(1f / 60f);
        var p = w.Get<Perception>(near);
        True(p.Target == player && p.LastKnown == origin && !p.Seen, "a quarter of a second in it has: the listener turns on the source, knows where it came from, has not seen it");
        True(w.Get<AiBrain>(near).State == AiState.Chase && w.Get<AiIntent>(near).MoveTo == origin, "and the brain chases the sound");
        for (int i = 0; i < 10; i++) w.Update(1f / 60f);
        True(noises.Count() == 0, "past 30 m the sound is spent and gone");
        True(w.Get<Perception>(far).Target.IsNull, "the ear 40 m away never heard a 30 m sound");

        // a wall halves how far it carries
        var wall = RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(w, origin + new Vector3(0f, 1f, 10f), Quaternion.identity, new Vector3(6f, 4f, 0.2f), Layers.Wall);
        w.Get<Perception>(near) = default; w.Get<AiBrain>(near) = default;
        for (int i = 0; i < 3; i++) w.Update(1f / 60f);
        Noises.Emit(w.Commands, origin, player, 30f);
        for (int i = 0; i < 30; i++) w.Update(1f / 60f);
        True(w.Get<Perception>(near).Target.IsNull, "20 m through a wall: a 30 m sound carries only 15 — not heard");
        Noises.Emit(w.Commands, origin, player, 50f);
        for (int i = 0; i < 45; i++) w.Update(1f / 60f);
        True(w.Get<Perception>(near).Target == player, "a 50 m sound carries 25 through the wall — heard");
        w.DestroyEntity(wall);

        // an enemy's own shot passes on its target: the others learn whom it is shooting at
        w.Get<Perception>(far) = default;
        w.Get<Perception>(near) = new Perception { Target = player, LastKnown = origin };
        Noises.Emit(w.Commands, origin + new Vector3(0f, 0f, 20f), near, 30f);
        for (int i = 0; i < 25; i++) w.Update(1f / 60f);
        True(w.Get<Perception>(far).Target == player && w.Get<Perception>(far).LastKnown == origin + new Vector3(0f, 0f, 20f),
             "the ear 20 m from the shooter turns on the shooter's target, towards the shot");
        True(w.Get<Perception>(near).Target == player, "the shooter does not hear itself into a new target");
        Close(w);
    }

    /// <summary>The intent into the motor and the heading; the ledge guard; a dead body braked.</summary>
    private static void Steer(Action<bool, string> True, Action<float, float, string> Equal)
    {
        var def = Enemies.All[0];
        def.movementSpeed = 11f; def.turnSpeed = 3600f;
        var self = new Vector3(700f, 1f, 0f);
        var motor = new CharacterMotor(); var heading = new Heading { Yaw = 0f };

        var go = new AiIntent { Move = true, MoveTo = self + new Vector3(10f, 0f, 0f) };
        Steering.Step(ref motor, ref heading, in go, def, self, groundAhead: true, 1f / 60f);
        True(motor.Desired == new Vector3(11f, 0f, 0f), "a move intent east: the motor wants 11 m/s east");
        Equal(60f, heading.Yaw, "and the heading swings east at the turn speed: 3600°/s is 60° this tick");
        Steering.Step(ref motor, ref heading, in go, def, self, groundAhead: true, 1f / 60f);
        Equal(90f, heading.Yaw, "and faces east the next");

        Steering.Step(ref motor, ref heading, in go, def, self, groundAhead: false, 1f / 60f);
        True(motor.Desired == Vector3.zero, "no ground ahead: the motor brakes");
        Equal(90f, heading.Yaw, "and the heading still looks where it wanted to go");

        def.turnSpeed = 900f;
        var aim = new AiIntent { Attack = true, FacePoint = self + new Vector3(0f, 0f, -5f) };
        Steering.Step(ref motor, ref heading, in aim, def, self, groundAhead: true, 1f / 60f);
        True(motor.Desired == Vector3.zero, "an attack intent stands still");
        Equal(105f, heading.Yaw, "and turns towards the aim at the turn speed: 15° this tick");
        for (int i = 0; i < 10; i++) Steering.Step(ref motor, ref heading, in aim, def, self, groundAhead: true, 1f / 60f);
        Equal(180f, heading.Yaw, "until it faces it");

        var quiet = new AiIntent();
        Steering.Step(ref motor, ref heading, in quiet, def, self, groundAhead: true, 1f / 60f);
        True(motor.Desired == Vector3.zero, "no intent: stand");
        Equal(180f, heading.Yaw, "and keep facing");

        // in a world: the body is braked the tick its owner dies, and the sheet follows the motion
        var w = NewWorld();
        var enemy = Grunt(w, self, Quaternion.identity);
        w.Add(enemy, new CharacterMotorConfig { profile = CharacterProfileKind.Enemy, rideHeight = 0.35f }.Bake());
        w.Add(enemy, new EnemyVisual { DeadTime = -1f });
        var quarry = Player(w, self + new Vector3(0f, 0f, 30f), tagged: false); // alive, out of sight: remembered, chased
        w.Get<AiBrain>(enemy) = new AiBrain { State = AiState.Chase, MoveTo = self + new Vector3(0f, 0f, 30f) };
        w.Get<Perception>(enemy) = new Perception { Target = quarry, LastKnown = self + new Vector3(0f, 0f, 30f) };
        Tick(w, 2);
        True(w.Get<CharacterMotor>(enemy).Desired == Vector3.zero, "no ground under the test world: the ledge guard holds the body");
        var floor = PhysicsEntities.StaticBox(w, self + new Vector3(0f, -1.6f, 15f), Quaternion.identity, new Vector3(10f, 0.2f, 40f), Layers.Walkable);
        Tick(w, 3);
        True(w.Get<CharacterMotor>(enemy).Desired == new Vector3(0f, 0f, 11f) && w.Get<EnemyVisual>(enemy).State == AnimationState.MOVING,
             "a floor laid down: the body walks north and the sheet shows it");
        w.Add(enemy, new Dead());
        Tick(w, 1);
        True(w.Get<CharacterMotor>(enemy).Desired == Vector3.zero, "dead: braked");
        w.DestroyEntity(floor);
        Close(w);
    }
}
