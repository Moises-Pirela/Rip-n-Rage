# Using the engine

How to build things with Rip-n-Rage's engine as it stands after the ECS migration (layers 0–10): the model, the
recipes for the things you will actually do, and the rules that keep a change from breaking something far away.
The [README](../README.md) describes what the engine *is* (renderer, physics, audio, tools); this is how to work
in it. The migration's own history and reasoning live in [ECS_ROADMAP.md](ECS_ROADMAP.md).

---

## 1. The model in one page

There is **one `World`**. Everything in the game is an **entity**: a number with a generation. An entity owns
**components** — plain structs, no behaviour, no references to objects. All behaviour lives in **systems**, which
run in phases over queries. Nothing inherits from anything.

```
data        a struct : IComponent                     Health { Current, Max }
authoring   one class : ComponentConfig<T>            HealthConfig { max = 100 } -> Bake() -> Health
behaviour   a class : SystemBase, ITick/IFixedTick…   HealthSystem consumes DamageEvent, posts DeathEvent
a thing     a prefab: a tree of nodes of components   prefabs/level/monster_rifleman.json
one of them an entity, spawned from a prefab          the four riflemen in dock.map
```

**Phases.** `Tick` (once a frame) → the fixed steps that fit (`FixedTick`, 50 Hz: physics, movement) → `LateTick`
(end of frame: interpolation, transforms, rods) → the frame's posted events are delivered to `IConsumes<T>`
systems. Lifecycle events (`IOnEntityCreated`, `IOnEntityDestroyed`) fire at **sync points**, where command
buffers play back — which is also where an entity created during a build becomes alive.

**Declarations.** A system declares what it touches with `[Reads(...)]` / `[Writes(...)]` (queries built in
`OnCreate` declare themselves). The scheduler orders systems by `[UpdateAfter]` / `[UpdateBefore]` and packs
non-conflicting ones into parallel batches; a system that declares nothing is `[Exclusive]` and runs alone.
Undeclared access throws when `--validate-access` is on (the gates always run with it).

**Singletons** are components on one carrier entity: `World.Singleton<Flow>()` by reference,
`GetSingleton<T>()` for a snapshot. A writer claims its carrier in `OnCreate` (`SingletonEntity<T>()`), because
creating one later, inside a phase, would be a structural change.

**Events** are structs: `ctx.Post(new DamageEvent { … })` during the frame, delivered at the end of it to every
`IConsumes<DamageEvent>` system, then cleared.

**Assets** are handles, not references: `AssetHandle<Mesh>` into an `AssetRegistry<Mesh>`. Registries are
**append-only** and intern by name — the same name always gives the same handle, which is what lets a level be
rebuilt a hundred times without leaking meshes.

---

## 2. Recipes

### 2.1 Add a component

```csharp
// src/RipNRage.Game/Ecs/Doors.cs
public struct AutoClose : IComponent { public float Delay, Left; }

public sealed class AutoCloseConfig : ComponentConfig<AutoClose>
{
    [InspectRange(0, 60, 0.1f), InspectTip("seconds after it is opened before it swings back")]
    public float delay = 3f;
    public override AutoClose Bake() => new AutoClose { Delay = delay, Left = delay };
}
```

That is all a prefab, a map key and the editor's inspector need: components and configs are found by reflection.
Rules that matter:

* **exactly one config per component**, and the config's field names are the JSON field names;
* mark tags and frequently toggled state `[SparseComponent]` (adding or removing one never moves the entity
  between archetype tables);
* mark a component the editor should not spawn into a preview `[NotInPreview]` (bodies, joints, brains, triggers);
* a component that only systems write needs no config at all.

If the component needs the spawn context — where it was placed, which entity it belongs to, a reference to a
sibling — override the other bake:

```csharp
public override AutoClose Bake(in BakeContext ctx) =>
    new AutoClose { Delay = delay, Left = ctx.Position.y > 10f ? delay * 2f : delay };
```

`BakeContext` carries `Self`, `Root`, the instance's `Position` / `Rotation`, and `Resolve(name)` for an
`EntityRef` field (a child's name, `root`, or a level `targetname`).

### 2.2 Add a system

```csharp
[UpdateAfter(typeof(JointSystem))]
[Reads(typeof(AutoClose)), Writes(typeof(Joint))]
public sealed class AutoCloseSystem : SystemBase, IFixedTick
{
    private Query _q;
    public override void OnCreate() => _q = World.Query().With<AutoClose>().With<Joint>().Build();

    public void FixedTick(SystemContext ctx)
    {
        foreach (var chunk in _q)
        {
            var doors = chunk.Get<AutoClose>(); var joints = chunk.Get<Joint>();
            for (int i = 0; i < chunk.Length; i++)
            {
                if (!chunk.Includes(i)) continue;
                doors[i].Left -= ctx.DeltaTime;
                if (doors[i].Left <= 0f) joints[i].SpringTarget = 0f;
            }
        }
    }
}
```

Systems are discovered by reflection (`AddSystemsFromAssemblies`); `[DisableAutoCreation]` keeps one out.
**Never change structure inside a system** — no `World.CreateEntity` / `Add` / `Destroy`. Use `ctx.Commands`
(or `ctx.CommandsFor(worker)` inside a chunk job); it plays back at the next sync point.

Adding a system changes the schedule, and the schedule is pinned: `tests/RipNRage.GameChecks/ScheduleChecks.cs`
fails with both layouts printed. Run it, paste the new layout in, and say why in the commit.

### 2.3 Work at creation time

Behaviour that has to happen once, as a thing is built, is an `IOnEntityCreated` system with an
`[EntityFilter(...)]`. This is how authored data becomes a finished entity:

```csharp
[EntityFilter(typeof(StandOnBase), typeof(LocalTransform))]
[Reads(typeof(StandOnBase)), Writes(typeof(LocalTransform))]
[UpdateBefore(typeof(TransformSystem))]      // or the world pose is read before you moved it
public sealed class StandOnBaseSystem : SystemBase, IOnEntityCreated { … }
```

Two rules learned the hard way: a system that **moves** an entity at creation must run before `TransformSystem`
(which initialises `LocalToWorld` the same way), and anything structural still goes through `ctx.Commands`.

### 2.4 Author a prefab

A prefab is a JSON file under `prefabs/` (`prefabs/level/` holds the classes a `.map` can place). It is a tree of
nodes; each node is a flat list of components. There is no class hierarchy — a door is not a kind of prop, it is
a composition that happens to include a joint.

```jsonc
{ "name": "phys_door",
  "description": "Hinged door (size w h thick; hinge left|right|top|bottom; limits; spring; motor)",
  "keys": { "size": ["BoxMesh.size", "ShapeSize.size"],      // one key may drive several fields
            "mass": "Body.mass",
            "hinge": "HingeOnEdge.edge" },
  "editor": { "gizmoColor": [1, 0.6, 0.2, 1], "pickExtents": [0.6, 1.2, 0.2],
              "defaults": { "size": "1.2 2.4 0.12", "mass": "25" },
              "numeric": [ { "key": "spring", "step": 5 } ] },
  "components": {
    "BoxMesh":   { "size": [1.2, 2.4, 0.12], "fitUvToWidth": true },
    "MeshRender": {},
    "LevelTextured": { "texture": "planks" },
    "ColliderShape": { "kind": "Box" },
    "ShapeSize": { "size": [1.2, 2.4, 0.12] },
    "Body": { "mass": 25, "collisionDetection": "Continuous" },
    "BodyVelocity": {},
    "StandOnBase": { "clearance": 0.01 },
    "Joint": { "kind": "Hinge", "limits": [-110, 110] },
    "HingeOnEdge": { "edge": "Left" }
  },
  "children": {
    "handle": { "components": { "LocalTransform": { "position": [0.5, 0, 0.1] }, "MeshRender": { "mesh": "box" } } }
  } }
```

| Key | Meaning |
|---|---|
| `base` | inherit another prefab: components merge field by field, children by name, the child wins |
| `keys` | the short names a `.map` may write, each mapping to one or more `Config.field` paths (`child/Config.field` for a child's) |
| `editor` | what the editor needs: gizmo colour, pick extents, the keys written when one is placed, the `+` / `-` steps |
| `components` | keyed by component or config name; the fields are the config's |
| `children` | named sub-entities, parented to the node above |
| `detached` | a child spawned with the instance but **not** parented (posed like the root) — a collider must be a root |
| `previewOnly` / `gameOnly` | a node only the editor sees (a pad, a ghost) / only the game sees (a collider) |

Spawn one in code:

```csharp
var inst = GamePrefabs.Library.Get("phys_door").Spawn(world, position, Quaternion.Euler(0, yaw, 0));
var handle = inst["handle"];           // named children; inst.Root is the root; inst.All is everything
```

Inside a system, use `prefab.Instantiate(ctx.Commands, world, position, rotation)` instead — the entities are
reserved handles until the next sync point, and references between them resolve correctly anyway.

### 2.5 Place it in a level

A `.map` entity is a **prefab plus field overrides**:

```
{
"classname" "phys_door"
"origin" "9 0.91 -5"
"angle" "0"
"size" "1.4 2.4 0.1"      // an alias from the prefab's "keys"
"Body.mass" "40"          // or a field path directly
"+Trigger" ""             // this instance also has a Trigger
"-BodyVelocity" ""        // …and no BodyVelocity
}
```

`classname`, `origin`, `angle` and `targetname` belong to the loader; every other key is an override, and an
unknown one fails the load with the class and key named. Register the class once:

```csharp
// src/RipNRage.Game/Content/Level/EntityRegistry.cs
AddPrefab("phys_door");                 // the editor picks up its description, gizmo and defaults from the file
AddPrefab("joint_ball", deferred: true); // "deferred" spawns after everything else (connectors need their bodies)
```

### 2.6 Edit in the editor

`./edit.sh levels/dock.map`.

* **Entity window** (right, `I` toggles): the selection's class (a combo swaps it, keeping the keys the new class
  knows), its `origin` / `angle` / `targetname`, then **every field of every component** its prefab gives it —
  root and children. Overridden values are marked and right-click reverts them. There is a filter box, Copy /
  Paste of a selection's keys, a remove button per component and an add-component combo. The **Level** tab is
  the same grid over the worldspawn (sun, sky, fog, kill height, sky spin), applied live.
* **Prefab window** (`Open prefab`, or `Save as prefab` to make a new one from an instance): the template itself
  — the node tree and its flags, each node's components, the key aliases, the editor block. Every edit lands in
  the library at once, so every instance in the level updates as you type; **Save** writes the file.
* Editing a prefab file by hand while the editor runs works too: the folder is watched and the class re-previews.
* A saved prefab is regenerated from its data, so **comments in a prefab file are lost when the editor saves it**.

### 2.7 Give a field a better editor

Hints are attributes on config fields, read by the property grid:

```csharp
[InspectRange(0, 50, 0.05f)] public float intensity = 1.5f;
[InspectTip("what a bullet kicks up off it")] [InspectAsset(AssetKind.Surface)] public string surface = "default";
[InspectColor] public float[] color = { 1, 1, 1, 1 };
[InspectHidden] public int bookkeeping;
```

`AssetKind` picks which registry the combo lists: `Texture`, `Sprite`, `Material`, `Mesh`, `LevelTexture`,
`Surface`, `Sound`, `Effect`, `Animator`, `Classname`, `Entity`. Free text is always allowed — the bake decides
whether it is valid, and says so on the row.

### 2.8 Use assets from a component

```csharp
public struct Decal : IComponent { public AssetHandle<Sprite> Sprite; }

public sealed class DecalConfig : ComponentConfig<Decal>
{
    [InspectAsset(AssetKind.Sprite)] public string sprite;
    public override Decal Bake() => new Decal { Sprite = RenderAssets.Sprites.GetOrLoad(sprite, SpriteLibrary.Resolve) };
}
```

Always intern **by name** (`GetOrLoad` / `Intern(item, name)`). A registry never shrinks: interning a fresh copy
per level build is a leak, and `deathf8`'s asset-count probe exists to catch exactly that.

---

## 3. Running and checking your work

```bash
./run.sh                     # the game
./edit.sh levels/dock.map    # the level editor        ./anim.sh / ./vfx.sh / ./skeleton.sh
./bench.sh --scenario all    # the physics benchmark
./gates.sh out               # the whole gate sweep (checks, 26 scripts, 4 shots, 2 manifests, 2 editor runs)
./gates.sh --compare a b     # diff two sweeps' probe lines and manifests
```

Useful flags while developing: `--script walk --frames 400 --fixed-dt 0.016667 --threads 1 --validate-access`
reproduces a gate run exactly; `--dump-level out.level` writes the **level manifest** (every entity, every
component, every field, ordered by content) — the fastest way to see what a change did to the scene;
`--screenshot shot.png`, `--overlay 0`, `--no-vsync`, `--perf-log 2`.

**The three kinds of check**

| Where | What belongs there |
|---|---|
| `tests/RipNRage.EngineChecks` | the engine alone: ECS, transforms, physics, prefabs, the property model. No game references, no window. |
| `tests/RipNRage.GameChecks` | gameplay over a real `World`: configs, systems, flow, saves, the schedule pin. |
| `--script <name>` gates | the whole game running: probe lines (`[test] …`) compared between sweeps, plus pixel shots and level manifests. |

Add a check with the layer's file (`Layer10Checks.cs`…) — assertions are `True(condition, "what it means")`
and `Equal(expected, actual, "…")`; the suites print a count, and the count is part of the record.

**Headless rule.** Checks and tools run without a GPU. Data types keep their data on the CPU and upload later
(`Texture2D` on first bind, `Mesh` on `Upload`), so configs, manifests and prefabs all work headless; a GL call
without a context throws `GraphicsContextException` naming the entry point instead of killing the process.
Compiling a shader or building a `Material` still needs a window.

---

## 4. Rules that save you a day

1. **Never change structure inside a system.** `ctx.Commands` only. The error says "structural change while
   systems run" when you forget.
2. **Declare what you touch.** `[Reads]` / `[Writes]`, or the system is exclusive. `--validate-access` throws
   with the system's name.
3. **Claim a singleton in `OnCreate`.** Reading one from inside a phase can otherwise create its carrier mid-frame.
4. **Intern assets by name**, and never intern per build.
5. **Seed from placement, not from entity order.** `AiBrains.Seed(position, salt)` — entity indices depend on
   what else exists, so a seeded-by-index level stops being reproducible the moment you add a pickup.
6. **A system that moves things at creation runs before `TransformSystem`.**
7. **Keep the manifest honest.** Before and after any change to how a level builds, run `--dump-level` and diff.
   Content changes are fine; *surprises* are the bug.
8. **One prefab name, one file.** The library throws if two files claim a name.
9. On this Linux VM every `dotnet` command needs `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (no `libicu`); on the
   dev Mac `source tools/env.sh` first (the system `dotnet` is a 6.0 SDK).

---

## 5. Where things live

| What | Where |
|---|---|
| ECS core (storage, queries, scheduler, events, prefabs, manifest) | `src/RipNRage.Engine/Ecs/` |
| Engine components and their systems | `Ecs/Transforms`, `Ecs/Rendering`, `Ecs/Physics`, `Ecs/Animation`, `Ecs/Audio`, `Ecs/Effects`, `Ecs/Destruction`, `Ecs/View`, `Ecs/Levels`, `Ecs/Input` |
| Game components and systems | `src/RipNRage.Game/Ecs/` (player, combat, AI, rage, flow, falls, pickups, environment) |
| Authoring configs for the game's components | `src/RipNRage.Game/Ecs/Configs.cs` |
| Level build: classes, loader, textures, visuals, saves | `src/RipNRage.Game/Content/Level/` |
| Entity classes as data | `prefabs/level/*.json` (demo scenes in `prefabs/demo/`) |
| Levels | `levels/*.map` |
| Tools | `src/RipNRage.Editor` (+ `EntityInspector`, `PrefabWindow`), `AnimEditor`, `VfxEditor`, `SkeletonEditor`, `Benchmark` |
| Shared editor widgets | `src/RipNRage.ImGui/` (`PropertyGrid`) |
| Tunables (hot-reloaded) | `tunables.cfg` — post-fx, lighting, shadows, fog, physics, movement, rage |
| Gates | `gates.sh`, `tools/pngdiff.py` |
