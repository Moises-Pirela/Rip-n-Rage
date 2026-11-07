using System;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;

internal static class TransformChecks
{
    private static bool Near(Vector3 a, Vector3 b, float eps = 1e-3f) => (a - b).magnitude < eps;
    private static bool Near(Quaternion a, Quaternion b) => Math.Abs(Math.Abs(Quaternion.Dot(a, b)) - 1f) < 1e-4f;

    public static void Run(Action<bool, string> True)
    {
        using var w = new World(4);
        w.AddSystemsFromAssemblies(typeof(World).Assembly);
        var rng = new System.Random(1234);
        Vector3 V(float s) => new Vector3((float)rng.NextDouble() * s - s / 2, (float)rng.NextDouble() * s - s / 2, (float)rng.NextDouble() * s - s / 2);
        Quaternion R() => Quaternion.Euler((float)rng.NextDouble() * 360, (float)rng.NextDouble() * 360, (float)rng.NextDouble() * 360);

        // ---- hierarchy matches the legacy Transform maths (3-level chains, random poses incl. non-uniform scale)
        const int Chains = 200;
        var ecs = new Entity[Chains * 3]; var legacy = new Transform[Chains * 3];
        for (int c = 0; c < Chains; c++)
        {
            for (int d = 0; d < 3; d++)
            {
                var lt = new LocalTransform { Position = V(10), Rotation = R(), Scale = new Vector3(0.5f + (float)rng.NextDouble(), 0.5f + (float)rng.NextDouble(), 0.5f + (float)rng.NextDouble()) };
                var e = w.CreateEntity(); w.Add(e, lt);
                var go = new GameObject("t"); go.transform.localPosition = lt.Position; go.transform.localRotation = lt.Rotation; go.transform.localScale = lt.Scale;
                if (d > 0)
                {
                    w.Add(e, new Parent { Value = ecs[c * 3 + d - 1] });
                    go.transform.SetParent(legacy[c * 3 + d - 1], false);
                }
                ecs[c * 3 + d] = e; legacy[c * 3 + d] = go.transform;
            }
        }
        w.Update(0.016f);
        True(w.Has<LocalToWorld>(ecs[0]), "LocalToWorld added automatically to entities with LocalTransform");
        bool posOk = true, rotOk = true, scaleOk = true, matOk = true;
        for (int i = 0; i < ecs.Length; i++)
        {
            var l = w.Get<LocalToWorld>(ecs[i]); var t = legacy[i];
            posOk &= Near(l.Position, t.position); rotOk &= Near(l.Rotation, t.rotation); scaleOk &= Near(l.Scale, t.lossyScale);
            matOk &= Near(l.Value.MultiplyPoint3x4(Vector3.one), t.localToWorldMatrix.MultiplyPoint3x4(Vector3.one));
        }
        True(posOk, "world position matches legacy Transform");
        True(rotOk, "world rotation matches legacy Transform");
        True(scaleOk, "lossy scale matches legacy Transform");
        True(matOk, "world matrix matches legacy Transform");

        // ---- moving a parent moves the children next pass
        var root = ecs[0];
        ref var rootLocal = ref w.Get<LocalTransform>(root);
        rootLocal.Position += new Vector3(100, 0, 0);
        legacy[0].localPosition += new Vector3(100, 0, 0);
        w.Update(0.016f);
        True(Near(w.Get<LocalToWorld>(ecs[2]).Position, legacy[2].position), "grandchild follows its moved root");

        // ---- re-parenting and dangling parents
        w.Remove<Parent>(ecs[5]);
        w.Update(0.016f);
        var l5 = w.Get<LocalTransform>(ecs[5]);
        True(Near(w.Get<LocalToWorld>(ecs[5]).Position, l5.Position), "removing Parent makes the entity a root");

        // ---- destroying a parent destroys the subtree
        var a = w.CreateEntity(); w.Add(a, LocalTransform.Identity);
        var b = w.CreateEntity(); w.Add(b, LocalTransform.At(Vector3.one)); w.Add(b, new Parent { Value = a });
        var c2 = w.CreateEntity(); w.Add(c2, LocalTransform.At(Vector3.one)); w.Add(c2, new Parent { Value = b });
        w.Update(0.016f);
        w.DestroyEntity(a);
        True(!w.IsAlive(b) && !w.IsAlive(c2), "destroying a parent destroys children and grandchildren");

        // ---- many roots through the parallel job
        var roots = new Entity[50_000];
        for (int i = 0; i < roots.Length; i++) { roots[i] = w.CreateEntity(); w.Add(roots[i], LocalTransform.At(new Vector3(i, 0, 0))); }
        w.Update(0.016f);
        bool allRoots = true;
        for (int i = 0; i < roots.Length; i += 997) allRoots &= w.Get<LocalToWorld>(roots[i]).Position.x == i;
        True(allRoots, "parallel root pass computed every root");
    }
}
