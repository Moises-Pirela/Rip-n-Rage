using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs;
using RipNRage.Game.Ecs.AI;

/// <summary>
/// The gameplay side of moving ground and open water: the sea that has no edges, and what an enemy does when the
/// floor it stands on is not in the navigation grid because it is sailing over it.
/// </summary>
internal static class VesselChecks
{
    public static void Run(Action<bool, string> True)
    {
        Ocean(True);
        MovingGroundAi(True);
    }

    /// <summary>An infinite volume is everything below its surface; a harbour is still a box.</summary>
    private static void Ocean(Action<bool, string> True)
    {
        var harbour = new WaterVolume { Min = new Vector3(-100, -100, -100), Max = new Vector3(100, -0.5f, 100) };
        True(harbour.Contains(new Vector3(0, -2, 0)), "a water box holds what is inside it");
        True(!harbour.Contains(new Vector3(4000, -2, 0)), "and not what is beyond its edge");
        True(!harbour.Contains(new Vector3(0, 3, 0)), "nor what is above its surface");

        var ocean = new WaterVolume { Min = new Vector3(-100, -100, -100), Max = new Vector3(100, -0.5f, 100), Infinite = true };
        True(ocean.Contains(new Vector3(0, -2, 0)), "the open sea holds what is under it");
        True(ocean.Contains(new Vector3(40000, -2, -9000)), "and goes on as far as the level does — a chase runs for kilometres");
        True(!ocean.Contains(new Vector3(40000, 3, -9000)), "but stops at the surface, wherever you are");
        True(Mathf.Abs(ocean.SurfaceY - (-0.5f)) < 1e-6f, "its surface is the top of the volume");

        // the key a map writes, and what it bakes to
        var baked = new RipNRage.Game.Ecs.WaterVolumeConfig { size = new[] { 200f, 200f }, depth = 100f, infinite = true }.Bake();
        True(baked.Infinite, "water_plane's \"infinite\" key makes the open sea");
        True(!new RipNRage.Game.Ecs.WaterVolumeConfig { size = new[] { 200f, 200f } }.Bake().Infinite,
             "and a water plane is a box unless it says otherwise");
    }

    /// <summary>
    /// An enemy on a deck under way walks straight at its goal: the grid is baked for the world, and the sea the
    /// deck sails over has no spans in it. On ground that does not move, nothing changes — it paths as it always did.
    /// </summary>
    private static void MovingGroundAi(Action<bool, string> True)
    {
        var still = new CharacterMotor();
        True(!still.OnMovingFrame, "still ground is not a moving frame");

        var deck = new CharacterMotor { FrameVelocity = new Vector3(25f, 0f, 0f) };
        True(deck.OnMovingFrame, "a deck at 25 m/s is");

        var drift = new CharacterMotor { FrameVelocity = new Vector3(0.05f, 0f, 0f) };
        True(!drift.OnMovingFrame, "a millimetre of jitter is not: the threshold is real motion, not noise");

        True(Mathf.Abs(deck.Relative(new Vector3(27f, 0f, 0f)).x - 2f) < 1e-5f,
             "walking at 27 through the world is walking at 2 across the deck");

        // the system's own query, end to end: an enemy standing on a moving platform is put on the direct line
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly, typeof(PlayerLink).Assembly);
        w.FixedDeltaTime = 1f / 60f;

        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(new Vector3(0, 1, 0)));
        w.Add(e, new Enemy { Kind = 0 });
        w.Add(e, new AiIntent { Move = true, MoveTo = new Vector3(10, 1, 0) });
        w.Add(e, new PathFollow { Count = 3, Direct = false });
        w.Add(e, new PathRequest());
        w.Add(e, new CharacterMotor { FrameVelocity = new Vector3(25f, 0f, 0f) });
        w.Sync();
        w.Update(1f / 60f);

        var follow = w.Get<PathFollow>(e);
        True(follow.Direct && follow.Count == 0,
             "an enemy on a deck under way drops its path and walks straight at the goal");
    }
}
