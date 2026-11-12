using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Effects;
using RipNRage.Engine.Ecs.Transforms;

/// <summary>Layer 5 engine pieces: the Lifetime countdown (projectile range, one-shot effects).</summary>
internal static class Layer5Checks
{
    public static void Run(Action<bool, string> True)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        w.FixedDeltaTime = 1f / 60f;

        var e = w.CreateEntity();
        w.Add(e, LocalTransform.At(new Vector3(500f, 0f, 0f)));
        w.Add(e, new Lifetime { Left = 3.5f / 60f });
        var child = w.CreateEntity();
        w.Add(child, LocalTransform.Identity);
        w.Add(child, new Parent { Value = e });

        for (int i = 0; i < 3; i++) w.Update(1f / 60f);
        True(w.IsAlive(e), "an entity lives while its Lifetime has time left");
        w.Update(1f / 60f);
        w.Update(1f / 60f); // destruction is a command: played back at the sync after the tick that saw zero
        True(!w.IsAlive(e), "and is destroyed the frame the countdown ends");
        True(!w.IsAlive(child), "children parented to it go with it");
    }
}
