using System;
using System.Collections.Generic;
using System.IO;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;

// ---- test components / configs for the prefab trees (reuse Health / Position from EcsChecks)
public struct Link : IComponent { public Entity Target, Other; }
public sealed class LinkConfig : ComponentConfig<Link>
{
    public EntityRef target, other;
    public override Link Bake() => default;
    public override Link Bake(in BakeContext ctx) => new Link { Target = target.Resolve(ctx), Other = other.Resolve(ctx) };
}
[NotInPreview] public struct Motorised : IComponent { public float Speed; }
public sealed class MotorisedConfig : ComponentConfig<Motorised> { public float speed = 1f; public override Motorised Bake() => new Motorised { Speed = speed }; }
public struct Seeded : IComponent { public int Seed; public bool Posed; }
public sealed class SeededConfig : ComponentConfig<Seeded>
{
    public override Seeded Bake() => default;
    public override Seeded Bake(in BakeContext ctx) => new Seeded { Seed = (int)MathF.Round(ctx.Position.x * 100f), Posed = ctx.HasEntity };
}
public enum Flavour { Plain, Spicy }
public struct Flavoured : IComponent { public Flavour F; public bool On; public Vector3 V; public float Optional; }
public sealed class FlavouredConfig : ComponentConfig<Flavoured>
{
    public Flavour flavour; public bool on; public float[] v = { 0, 0, 0 }; public float? optional;
    public override Flavoured Bake() => new Flavoured { F = flavour, On = on, V = new Vector3(v[0], v[1], v[2]), Optional = optional ?? -1f };
}

/// <summary>Layer 10.1: prefab trees, references by name, overrides and aliases, the preview strip, the buffered spawn.</summary>
internal static class Layer10Checks
{
    private static KeyValuePair<string, string> KV(string k, string v) => new KeyValuePair<string, string>(k, v);

    private static bool Throws<T>(Action a, string contains = null) where T : Exception
    {
        try { a(); return false; }
        catch (T e) { return contains == null || e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase); }
    }

    public static void Run(Action<bool, string> True)
    {
        using var w = new World(0);
        w.AddSystemsFromAssemblies(typeof(World).Assembly); // the transform system: LocalToWorld for the tree
        var lib = new PrefabLibrary();
        lib.AddJson(@"{ ""name"": ""rig"", ""description"": ""a rig"", ""keys"": { ""hp"": ""Health.max"", ""tipx"": ""arm/tip/Position.x"" },
            ""components"": { ""LocalTransform"": { ""position"": [0, 1, 0] }, ""Health"": { ""max"": 50 }, ""Link"": { ""target"": ""tip"" }, ""Motorised"": { ""speed"": 2 } },
            ""children"": { ""arm"": { ""components"": { ""LocalTransform"": { ""position"": [1, 0, 0] }, ""Health"": { ""max"": 10 } },
                                      ""children"": { ""tip"": { ""components"": { ""Position"": { ""x"": 5 }, ""Link"": { ""target"": ""root"", ""other"": ""hook"" } } } } } } }");
        var hook = w.CreateEntity();
        Entity Named(string n) => n == "hook" ? hook : Entity.Null;

        // ---- a tree spawned immediately, at a pose
        var rot = Quaternion.Euler(0f, 90f, 0f);
        var inst = lib.Get("rig").Spawn(w, new Vector3(10f, 0f, 0f), rot, Named);
        True(inst.All.Count == 3 && w.IsAlive(inst.Root) && w.IsAlive(inst["arm"]) && w.IsAlive(inst["tip"]), "a prefab tree spawns root + arm + tip, alive at once");
        True(inst.Named("root") == inst.Root && inst.Named("nobody").IsNull, "the instance names its root and answers Null for an unknown name");
        True(w.Get<Parent>(inst["arm"]).Value == inst.Root && w.Get<Parent>(inst["tip"]).Value == inst["arm"], "children are parented to the node above");
        var rootT = w.Get<LocalTransform>(inst.Root);
        True((rootT.Position - new Vector3(10f, 1f, 0f)).magnitude < 1e-4f && Quaternion.Angle(rootT.Rotation, rot) < 1e-3f, "the root's configured transform composes with the placed pose (offset above the origin, rotation added)");
        True(w.Get<LocalTransform>(inst["tip"]).Position == Vector3.zero && w.Get<LocalTransform>(inst["tip"]).Scale == Vector3.one, "a child without a transform gets the identity");
        w.Update(1f / 60f);
        var armWorld = w.Get<LocalToWorld>(inst["arm"]).Position;
        var expected = new Vector3(10f, 1f, 0f) + rot * new Vector3(1f, 0f, 0f);
        True((armWorld - expected).magnitude < 1e-3f, $"the hierarchy resolves the child's world pose through the rotated root ({armWorld} vs {expected})");
        True(w.Get<Link>(inst.Root).Target == inst["tip"], "a config field names a grandchild by name");
        True(w.Get<Link>(inst["tip"]).Target == inst.Root && w.Get<Link>(inst["tip"]).Other == hook, "'root' and the spawner's own names resolve too");
        True(w.Get<Health>(inst.Root).Max == 50 && w.Get<Health>(inst["arm"]).Max == 10 && w.Get<Position>(inst["tip"]).X == 5, "every node bakes its own components");
        True(lib.Get("rig").Description == "a rig" && lib.Get("rig").Child("arm").Child("tip") != null, "description and the child tree are on the prefab");

        // ---- references that cannot resolve
        lib.AddJson(@"{ ""name"": ""dangling"", ""components"": { ""Link"": { ""target"": ""nobody"" } } }");
        True(Throws<KeyNotFoundException>(() => lib.Get("dangling").Spawn(w), "nobody"), "an unknown entity name throws, naming it");
        lib.AddJson(@"{ ""name"": ""loose"", ""components"": { ""Link"": { } } }");
        var loose = lib.Get("loose").Spawn(w);
        True(w.Get<Link>(loose.Root).Target.IsNull && w.Get<Link>(loose.Root).Other.IsNull, "an empty reference is Null, not an error");

        // ---- inheritance merges trees: children by name, fields by name, new children added
        lib.AddJson(@"{ ""name"": ""rig2"", ""base"": ""rig"", ""components"": { ""Health"": { ""max"": 60 } },
            ""children"": { ""arm"": { ""components"": { ""Health"": { ""max"": 20 } }, ""children"": { ""tip"": { ""components"": { ""Position"": { ""y"": 7 } } } } },
                            ""extra"": { ""components"": { ""Health"": { ""max"": 1 } } } } }");
        var r2 = lib.Get("rig2").Spawn(w, Vector3.zero, Quaternion.identity, Named);
        True(r2.All.Count == 4 && w.IsAlive(r2["extra"]), "a derived prefab adds a child");
        True(w.Get<Health>(r2.Root).Max == 60 && w.Get<Link>(r2.Root).Target == r2["tip"], "the derived root overrides a field and keeps the rest");
        True(w.Get<Health>(r2["arm"]).Max == 20 && w.Get<LocalTransform>(r2["arm"]).Position == new Vector3(1f, 0f, 0f), "a child merges by name: its field overridden, its transform kept");
        True(w.Get<Position>(r2["tip"]).X == 5 && w.Get<Position>(r2["tip"]).Y == 7, "a grandchild merges field by field");
        True(lib.ResolveKey("rig2", "hp") == "Health.max", "aliases are inherited");

        // ---- overrides: aliases and paths, typed from text, applied last
        var ov = lib.Get("rig", new[] { KV("hp", "200"), KV("tipx", "3"), KV("Motorised.speed", "4.5"), KV("arm/Health.max", "11") }).Spawn(w, Vector3.zero, Quaternion.identity, Named);
        True(w.Get<Health>(ov.Root).Max == 200 && w.Get<Position>(ov["tip"]).X == 3, "an alias reaches a root field and a grandchild's field");
        True(w.Get<Motorised>(ov.Root).Speed == 4.5f && w.Get<Health>(ov["arm"]).Max == 11, "a path reaches a root field and a child's field");
        True(w.Get<Health>(lib.Get("rig").Spawn(w, Vector3.zero, Quaternion.identity, Named).Root).Max == 50, "overrides do not touch the cached prefab");
        True(lib.ResolveKey("rig", "hp") == "Health.max" && lib.ResolveKey("rig", "Health.max") == "Health.max" && lib.ResolveKey("rig", "origin") == null,
            "ResolveKey: an alias, a path, and a key that is neither");
        True(Throws<InvalidDataException>(() => lib.Get("rig", new[] { KV("Health.nope", "1") }), "nope"), "an unknown field throws, naming it");
        True(Throws<InvalidDataException>(() => lib.Get("rig", new[] { KV("Nope.x", "1") }), "Nope"), "an unknown config throws");
        True(Throws<InvalidDataException>(() => lib.Get("rig", new[] { KV("leg/Health.max", "1") }), "leg"), "an unknown child throws");
        True(Throws<InvalidDataException>(() => lib.Get("rig", new[] { KV("hp", "abc") }), "abc"), "a value of the wrong type throws, quoting it");
        True(Throws<InvalidDataException>(() => lib.Get("rig", new[] { KV("plain", "1") }), "plain"), "a key that is neither alias nor path throws");

        lib.AddJson(@"{ ""name"": ""fl"", ""components"": { ""Flavoured"": { ""optional"": 9 } } }");
        var fl = lib.Get("fl", new[] { KV("Flavoured.flavour", "spicy"), KV("Flavoured.on", "1"), KV("Flavoured.v", "1 2 3"), KV("Flavoured.optional", "2.5"), KV("Health.max", "9") }).Spawn(w);
        var f = w.Get<Flavoured>(fl.Root);
        True(f.F == Flavour.Spicy && f.On && f.V == new Vector3(1f, 2f, 3f) && f.Optional == 2.5f, "text converts by field type: enum by name, bool from 1, a vector from \"1 2 3\", a nullable number");
        True(w.Get<Health>(fl.Root).Max == 9, "overriding a component the prefab lacks adds it");
        True(w.Get<Flavoured>(lib.Get("fl", new[] { KV("Flavoured.optional", "null"), KV("Flavoured.on", "false") }).Spawn(w).Root).Optional == -1f, "\"null\" clears a nullable; \"false\" is a bool");

        // ---- the preview strips what simulates
        var pv = lib.Get("rig").SpawnPreview(w, Vector3.zero, Quaternion.identity, Named);
        True(!w.Has<Motorised>(pv.Root) && w.Has<Health>(pv.Root) && pv.All.Count == 3 && w.Get<Link>(pv.Root).Target == pv["tip"],
            "the preview leaves out [NotInPreview] components and keeps the rest of the tree");

        // ---- the buffered spawn equals the immediate one once played back
        var buffered = lib.Get("rig").Instantiate(w.Commands, w, new Vector3(10f, 0f, 0f), rot, Named);
        True(!w.IsAlive(buffered.Root) && !w.IsAlive(buffered["tip"]), "through a command buffer the entities are reserved, not alive");
        w.Sync();
        True(w.IsAlive(buffered.Root) && w.Get<Parent>(buffered["tip"]).Value == buffered["arm"] && w.Get<Link>(buffered.Root).Target == buffered["tip"]
             && w.Get<Link>(buffered["tip"]).Other == hook && (w.Get<LocalTransform>(buffered.Root).Position - rootT.Position).magnitude < 1e-4f
             && w.Get<Health>(buffered["arm"]).Max == 10,
            "after playback the buffered tree is the immediate tree: parents, references, pose, values");

        // ---- the context: the instance pose reaches a config; a bare Bake has none
        lib.AddJson(@"{ ""name"": ""seed"", ""components"": { ""Seeded"": { } } }");
        var seeded = w.Get<Seeded>(lib.Get("seed").Spawn(w, new Vector3(3f, 0f, 0f), Quaternion.identity).Root);
        True(seeded.Seed == 300 && seeded.Posed, "a config bakes from where the prefab was placed");
        True(new SeededConfig().Bake().Seed == 0 && !new SeededConfig().Bake().Posed, "a bare Bake() has no context");

        // ---- the layer-0 API still holds
        lib.AddJson(@"{ ""name"": ""plain"", ""components"": { ""LocalTransform"": { ""position"": [0, 1, 0] }, ""Health"": { ""max"": 50 } } }");
        var old = lib.Get("plain").Instantiate(w);
        w.Sync();
        True(w.IsAlive(old) && w.Get<Health>(old).Max == 50 && w.Get<LocalTransform>(old).Position == new Vector3(0f, 1f, 0f), "Instantiate(world) still returns the root, at the prefab's own pose");
        True(w.Get<LocalTransform>(lib.Get("plain").Spawn(w).Root).Position == new Vector3(0f, 1f, 0f), "Spawn(world) without a pose leaves the configured transform alone");
        True(!w.Has<LocalTransform>(lib.Get("loose").Spawn(w).Root), "a poseless spawn adds no transform to a prefab that has none");
        True(w.Has<LocalTransform>(lib.Get("loose").Spawn(w, Vector3.one, Quaternion.identity).Root), "a posed spawn gives the root a transform");

        // ---- malformed trees
        lib.AddJson(@"{ ""name"": ""badroot"", ""children"": { ""root"": { } } }");
        True(Throws<InvalidDataException>(() => lib.Get("badroot").Spawn(w), "reserved"), "a child named 'root' is refused");
        lib.AddJson(@"{ ""name"": ""dup"", ""children"": { ""arm"": { ""children"": { ""arm"": { } } } } }");
        True(Throws<InvalidDataException>(() => lib.Get("dup").Spawn(w), "two children"), "two nodes with one name are refused");
        lib.AddJson(@"{ ""name"": ""twice"", ""components"": { } }", null, "a.json");
        True(Throws<InvalidDataException>(() => lib.AddJson(@"{ ""name"": ""twice"", ""components"": { } }", null, "b.json"), "defined twice"), "the same name in two files is refused");
        lib.AddJson(@"{ ""name"": ""twice"", ""components"": { } }", null, "a.json");
        True(lib.Contains("twice"), "the same file may redefine its own prefab (a reload)");
    }
}
