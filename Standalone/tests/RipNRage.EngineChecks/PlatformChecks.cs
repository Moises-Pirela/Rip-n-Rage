using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;

/// <summary>
/// Moving ground: a deck doing chase speed under a character, and the things that ride it.
///
/// <para>The rule under test is <see cref="CharacterMotor.FrameVelocity"/>: every speed a character is given is
/// relative to what it stands on, and while it is in the air that frame is the one it jumped from. On ground that
/// does not move the frame is zero, so all of this is also a regression test that still ground behaves exactly as
/// it did — that case is checked first, and with the same numbers as the moving one.</para>
/// </summary>
internal static class PlatformChecks
{
    private const float Dt = 1f / 60f;
    /// <summary>Chase speed: a speedboat, and the deck of the ship.</summary>
    private const float DeckSpeed = 25f;

    public static void Run(Action<bool, string> True)
    {
        // the same measurements twice. The numbers that matter are not absolute — a character on still ground
        // drifts a little on its spring too — but that the deck at 25 m/s behaves like ground that is not moving.
        var still = FrameChecks(True, moving: false);
        var deck = FrameChecks(True, moving: true);
        True(Mathf.Abs(deck.Jump - still.Jump) < 0.1f,
             $"a jump on a deck at {DeckSpeed:0} m/s lands as true as one on still ground ({deck.Jump:0.000} m vs {still.Jump:0.000} m)");
        True(Mathf.Abs(deck.Walk - still.Walk) < 0.1f,
             $"walking on the deck is the walk it is on still ground ({deck.Walk:0.00} vs {still.Walk:0.00} m/s)");
        True(Mathf.Abs(deck.Dash - still.Dash) < 0.2f,
             $"a dash on the deck is the dash it is on still ground ({deck.Dash:0.00} vs {still.Dash:0.00} m/s)");
        CarryChecks(True);
    }

    /// <summary>What one run measured, all of it in the frame of whatever the character was standing on.</summary>
    private struct Measured { public float Jump, Walk, Dash; }

    /// <summary>
    /// The same five measurements on still ground and on a deck at 25 m/s. Every one of them is in the deck's
    /// frame, so both runs expect the same answer — that is the whole point.
    /// </summary>
    private static Measured FrameChecks(Action<bool, string> True, bool moving)
    {
        string what = moving ? $"on a deck at {DeckSpeed:0} m/s" : "on still ground";
        float speed = moving ? DeckSpeed : 0f;
        // each run gets its own patch of the shared physics world
        float x0 = moving ? 1200f : 1100f;

        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        w.FixedDeltaTime = Dt;

        // the deck: a kinematic body, driven by its velocity (never posed), so riders can read what it is doing
        var deck = w.CreateEntity();
        w.Add(deck, LocalTransform.At(new Vector3(x0, 0, 0)));
        w.Add(deck, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(40, 1, 12) });
        w.Add(deck, new BodyConfig { mass = 50000f, kind = BodyKind.Kinematic }.Bake());
        w.Add(deck, new BodyVelocity());
        w.Add(deck, new PlatformMotion { Velocity = new Vector3(speed, 0, 0), Direction = 1 });

        var ch = w.CreateEntity();
        w.Add(ch, LocalTransform.At(new Vector3(x0, 2f, 0)));
        w.Add(ch, new ColliderShape { Kind = ShapeKind.Capsule, Radius = 0.4f, Height = 1.3f });
        w.Add(ch, new BodyConfig { mass = 80f }.Bake());
        w.Add(ch, new BodyVelocity());
        w.Add(ch, new CharacterMotorConfig { rideHeight = 0.5f }.Bake());
        w.Sync();

        void Step(int n) { for (int i = 0; i < n; i++) w.Update(Dt); }
        Vector3 DeckPos() => w.Get<LocalTransform>(deck).Position;
        Vector3 Pos() => w.Get<LocalTransform>(ch).Position;
        // where the character stands on the deck, which is the only position that means anything here
        Vector3 OnDeck() => Pos() - DeckPos();

        Step(150);
        var m = w.Get<CharacterMotor>(ch);
        True(m.IsGrounded && m.Ground == deck, $"{what}: the character stands on the deck");
        True(Mathf.Abs(m.FrameVelocity.x - speed) < 0.2f,
             $"{what}: the frame is the deck's velocity (got {m.FrameVelocity.x:0.00})");

        // 1. standing still means standing still *on the deck*
        Vector3 before = OnDeck();
        Step(120);
        float drift = (OnDeck() - before).magnitude;
        True(drift < 0.1f, $"{what}: standing still, the character does not drift on the deck ({drift:0.000} m)");
        float relSpeed = w.Get<CharacterMotor>(ch).Relative(w.Get<BodyVelocity>(ch).Linear).magnitude;
        True(relSpeed < 0.3f, $"{what}: and its speed relative to the deck is zero ({relSpeed:0.000} m/s)");

        // 2. a standing jump lands where it took off from — the heart of it
        before = OnDeck();
        w.Get<CharacterMotor>(ch).PendingJump = 6f;
        Step(5);
        True(!w.Get<CharacterMotor>(ch).IsGrounded, $"{what}: the jump leaves the deck");
        True(Mathf.Abs(w.Get<CharacterMotor>(ch).FrameVelocity.x - speed) < 0.2f,
             $"{what}: in the air the frame is frozen at the deck's velocity");
        for (int i = 0; i < 240 && !w.Get<CharacterMotor>(ch).IsGrounded; i++) Step(1);
        True(w.Get<CharacterMotor>(ch).IsGrounded, $"{what}: and it lands again");
        float landed = (OnDeck() - before).magnitude;
        True(landed < 0.35f, $"{what}: a standing jump lands where it took off on the deck ({landed:0.000} m)");

        // 3. walking is at the asked-for speed relative to the deck
        for (int i = 0; i < 90; i++) { w.Get<CharacterMotor>(ch).Desired = new Vector3(0, 0, 4f); Step(1); }
        float walk = w.Get<CharacterMotor>(ch).Relative(w.Get<BodyVelocity>(ch).Linear).z;
        True(Mathf.Abs(walk - 4f) < 0.4f, $"{what}: walking is 4 m/s across the deck (got {walk:0.00})");

        // 4. and letting go stops on the deck, rather than braking towards a standstill the sea is in
        for (int i = 0; i < 90; i++) { w.Get<CharacterMotor>(ch).Desired = Vector3.zero; Step(1); }
        float stopped = w.Get<CharacterMotor>(ch).Relative(w.Get<BodyVelocity>(ch).Linear).magnitude;
        True(stopped < 0.3f, $"{what}: letting go stops on the deck ({stopped:0.000} m/s)");

        // 5. a dash is a dash across the deck, not a dash through the world
        before = OnDeck();
        w.Get<CharacterMotor>(ch).PendingDash = new Vector3(0, 0, 20f);
        w.Get<CharacterMotor>(ch).PendingDashTime = 0.2f;
        Step(1);
        float dash = w.Get<CharacterMotor>(ch).Relative(w.Get<BodyVelocity>(ch).Linear).z;
        True(Mathf.Abs(dash - 20f) < 0.5f, $"{what}: a dash is 20 m/s across the deck (got {dash:0.00})");
        Step(11);
        float moved = (OnDeck() - before).z;
        True(moved > 2.5f, $"{what}: and it carries the character across the deck ({moved:0.00} m)");

        return new Measured { Jump = landed, Walk = walk, Dash = dash };
    }

    /// <summary>Things that ride: a crate lashed to the deck, and a trigger aboard.</summary>
    private static void CarryChecks(Action<bool, string> True)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        w.FixedDeltaTime = Dt;

        var deck = w.CreateEntity();
        w.Add(deck, LocalTransform.At(new Vector3(1300, 0, 0)));
        w.Add(deck, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(40, 1, 12) });
        w.Add(deck, new BodyConfig { mass = 50000f, kind = BodyKind.Kinematic }.Bake());
        w.Add(deck, new BodyVelocity());
        w.Add(deck, new PlatformMotion { Velocity = new Vector3(DeckSpeed, 0, 0), YawRate = 10f, Direction = 1 });

        // a kinematic crate welded to the deck: the kind of thing that must be stood on
        var crate = w.CreateEntity();
        w.Add(crate, LocalTransform.At(new Vector3(1304, 1f, 2f)));
        w.Add(crate, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(1, 1, 1) });
        w.Add(crate, new BodyConfig { mass = 200f, kind = BodyKind.Kinematic }.Bake());
        w.Add(crate, new BodyVelocity());
        w.Add(crate, new CarriedBy { Carrier = deck });

        // a trigger volume aboard: a static collider, which is all a trigger ever is
        var trigger = w.CreateEntity();
        w.Add(trigger, LocalTransform.At(new Vector3(1296, 1f, -3f)));
        w.Add(trigger, new ColliderShape { Kind = ShapeKind.Box, Size = new Vector3(4, 4, 4), IsStatic = true });
        w.Add(trigger, new Trigger());
        w.Add(trigger, new CarriedBy { Carrier = deck });
        w.Sync();

        void Step(int n) { for (int i = 0; i < n; i++) w.Update(Dt); }
        Step(2);
        Vector3 deck0 = w.Get<LocalTransform>(deck).Position;
        Vector3 crateLocal = w.Get<LocalTransform>(crate).Position - deck0;
        Vector3 triggerLocal = w.Get<LocalTransform>(trigger).Position - deck0;

        Step(180);
        Vector3 deck1 = w.Get<LocalTransform>(deck).Position;
        True((deck1 - deck0).magnitude > 50f, "the deck has sailed");

        var rotation = w.Get<LocalTransform>(deck).Rotation;
        float crateErr = (w.Get<LocalTransform>(crate).Position - (deck1 + rotation * crateLocal)).magnitude;
        float triggerErr = (w.Get<LocalTransform>(trigger).Position - (deck1 + rotation * triggerLocal)).magnitude;
        True(crateErr < 0.05f, $"a carried body holds its place on the deck through a turn ({crateErr:0.000} m)");
        True(triggerErr < 0.05f, $"a carried trigger holds its place too ({triggerErr:0.000} m)");

        // and the carried body carries in turn: its velocity is the deck's at its own position
        var cv = w.Get<BodyVelocity>(crate).Linear;
        True(Mathf.Abs(cv.magnitude - DeckSpeed) < 2f,
             $"a carried body is given the deck's velocity, so it can be stood on ({cv.magnitude:0.00} m/s)");
    }
}
