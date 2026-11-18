# Layer 11 spec — navigation: tiled A* over column spans, carved by the world

The phase-by-phase build. The **direction, the decisions and the answered questions live in
[`ECS_ROADMAP.md`](ECS_ROADMAP.md) under "Layer 11"** and are not repeated here; this is what to type, in what
order, and what has to be true before each phase is a commit.

One phase = one commit, in order. Every phase ends with `./gates.sh OUT` green and `./gates.sh --compare` against
the previous sweep showing **only** the differences the phase's own notes predict.

## The seams this layer attaches to

Checked at 10.7 — verify before leaning on one.

| Seam | Where | Why it matters |
|---|---|---|
| Level start | `GameSession.StartLevel` (`src/RipNRage.Game/Content/GameSession.cs:33`) | where the bake is called, synchronously, after the brushes exist |
| Brush colliders | `LevelLoader.BuildBrushes` (`Content/Level/LevelLoader.cs:205`) | every non-sky brush → one static collider on `Layers.Walkable`; the bake reads the world, not the `MapDocument` |
| The tile cut | `LevelLoader.MeshChunkSize = 16f` (`:202`) | nav tiles use the same cut, so a nav tile and a render chunk are the same box |
| Settings | `Worldspawn` + `WorldspawnConfig` (`Engine/Ecs/Levels/Worldspawn.cs`), `prefabs/level/worldspawn.json` | `cellSize` / `cellHeight` / `maxDrop` are map keys, baked onto the singleton, bumped `Version` |
| The body's limits | `CharacterProfiles.Enemy` (`Engine/Physics/CharacterProfile.cs:35`) — ride `0.35`, `groundTolerance 0.25`, `maxSlopeAngle 50` | clearance, step and slope are **derived**, never typed twice |
| The capsule | `prefabs/level/monster_rifleman.json` — radius `0.3375`, height `1.9` | the erosion radius and headroom |
| Queries | `Engine/Physics/Physics.cs` — `Raycast`, `CapsuleCast`, `OverlapShape`, `CheckCapsule`, `QueryFilter` | the bake and the straight-line test |
| The level mask | `Noises.WallMask` (`Game/Ecs/AI/Perception.cs`) | Default \| Wall \| Walkable \| OnlyGroundCollision — what counts as world |
| System registration | `app.World.AddSystemsFromAssemblies` (`Game/Program.cs:187`) | a new `SystemBase` is picked up by the scan; **no wiring call to add** |
| Gate scripts | the `switch` in `DemoScript` (`Game/Program.cs:~700`), `AiProbe` (`:485`), `gates.sh` `ALL` list | new scripts are a `case` + a line in `ALL` |
| Checks | `tests/RipNRage.EngineChecks/`, `tests/RipNRage.GameChecks/` | engine pieces engine-side, gameplay pieces game-side (the layer rule) |

**Namespaces**: `RipNRage.Engine.Ecs.Nav` for the grid, the bake, the search and the carve;
`RipNRage.Game.Ecs.AI` for the request, the follower and the obstacle tagging.

---

## Phase 11.0 — Instrument

**Goal.** Three gates that today's AI *fails*, recorded as the baseline, plus the measurements that set the
defaults. Nothing in `Ecs/Nav` exists yet.

**Build.**

1. `Game/Program.cs`, `DemoScript`: three cases.
   - `maze` — spawn a rifleman, spawn a brush-made wall between it and the player (or use a dock corner that
     already blocks), player stands still and visible. Probe `maze <frame>` at 60 / 180 / 300 / 390.
   - `door` — the same through a `phys_door`; the door shut at spawn, opened at frame 200 by a trigger.
   - `ledge` — player on the deck below, the way down a drop and the way back a step run. Probe at the same
     frames.
2. Extend `AiProbe` to print, after the existing fields: `moved <d>` (distance from `_enemyHome`), `to target
   <d>`, and — stubbed to `-1` until 11.3 — `path <len> corners <n> ms <t>`.
3. `gates.sh`: add `maze:400 door:400 ledge:400` to `ALL`.
4. A throwaway `--nav-measure` flag (or a check) that loads the dock and prints bounds, tile count and span
   count at `cellSize` 0.5 / 0.25 and `cellHeight` 0.2 / 0.1, with the bytes each would cost. **Delete it at
   11.1** once the defaults are chosen.

**Done when.** The three new scripts run 400 frames and their probes show the failure plainly: `moved` small and
`to target` not shrinking (the enemy grinding on the wall). The dock's measurements are written into this file
under 11.1's defaults. `approach` / `peek` / `close` / `killenemy` re-baselined into the sweep.

### What 11.0 recorded (2026-11-18)

The grunt never *sees* the player in these three — a wall or a drop is in the way. It is told where he is by the
**gunshot**: a 30 m noise, and `Noises.WallMuffle` only muffles past half of that, so it hears him through the
wall and chases the sound. A shot every second (`_frame % 60 == 30`) keeps the chase from lapsing on the 5 s
memory, so a stalled grunt stays stalled for the whole run instead of quietly going Idle.

| Gate | Frame 60 | Frame 180 | Frame 300 | Frame 390 |
|---|---|---|---|---|
| `maze` | d 10.1, moved 0.00, Chase | d 6.7, **moved 3.43** | d 6.7, **moved 3.43** | d 6.7, **moved 3.43** |
| `door` | d 10.1, moved 0.00, Chase | d 6.7, **moved 3.43** | d 6.7, **moved 3.43** | d 6.7, **moved 3.43** |
| `ledge` | d 17.2, moved 0.00, Chase | d 14.4, **moved 2.90**, anim IDLE | d 14.4, **moved 2.90** | d 14.4, **moved 2.90** |

`moved` freezes before frame 180 and never changes again: the grunt reaches the wall (or the platform edge) and
stays there for 220 frames, still in `Chase`, still wanting the player. `maze` and `door` are identical today —
the straight line hits the same wall segment and the gap is never found; they separate at 11.4 (`door` finds the
gap) and 11.6 (the door shuts across the path). On `ledge` the animation is **IDLE**, not MOVING: the ledge guard
has zeroed `Desired`, so the grunt is not even pushing.

**Measurement (`--script navmeasure`, deleted at 11.1).** 29 static colliders on the dock.

| | bounds | tiles @16 m | cell 0.5 m | cell 0.25 m |
|---|---|---|---|---|
| all statics | 1732 × 1000 m | 109 × 63 | 6.93 M columns, **167 MB** | 27.7 M columns, **669 MB** |
| playable (6 planes over 200 m excluded) | 724 × 36 × 176 m | 46 × 11 | 510 k columns, 285 k spans, **8.2 MB** | 2.04 M columns, 1.14 M spans, **32.9 MB** |

**Three findings that bind 11.1:**

1. **Bounds must come from the playable brushes, not from every static collider.** The dock's water and sky
   planes are 1000 m across; taking them in costs **167 MB** at 0.5 m against 8.2 MB without them — a 20×
   difference that decides whether this layer is viable. The bake excludes colliders whose horizontal extent
   exceeds a threshold, and a check pins that.
2. **Tiles must be sparse.** Only ~52% of columns in the playable box have any ground under them (267 k of
   510 k), and the box is 46 × 11 tiles for a level that is mostly a long thin pier. A tile with no brush is
   `null`, as the spec already says — this is the measurement that says how much that saves.
3. **Defaults confirmed: `cellSize` 0.5, `cellHeight` 0.2.** 8.2 MB is cheap. 0.25 m is affordable at 33 MB if a
   level ever needs it, which is exactly why it is a worldspawn key. Spans per occupied column is **1.06** — the
   dock has little stacked geometry, so the column-of-spans structure costs almost nothing where it is not used,
   and is there for the levels that do stack.

**Commit.** `ECS layer 11.0: instrument the gates; maze, door and ledge fail today`

---

## Phase 11.1 — Tiles of spans bake from the level

**Goal.** A baked, queryable, deterministic navigation structure. Nothing walks on it yet.

**Files.** `Engine/Ecs/Nav/NavGrid.cs` (types), `NavBake.cs` (the bake), `NavSettings.cs` (the derived limits).

**Types.**

```csharp
// one walkable surface in a column; a deck over a deck is two of these
public struct NavSpan {                 // 16 bytes
    public float Floor;                 // world y of the walkable top
    public float Headroom;              // clear height above Floor
    public ushort Flags;                // Walkable, Slope, Edge
    public ushort Dynamic;              // carve counter (11.5); > 0 = blocked by something that moves
}
public struct NavColumn { public int First, Count; }   // slice into NavTile.Spans

public sealed class NavTile {
    public int X, Z;                    // tile coords on the MeshChunkSize cut
    public Bounds Bounds;
    public int Width, Depth;            // columns = Width * Depth
    public NavColumn[] Columns;         // indexed [z * Width + x]
    public NavSpan[] Spans;
    public NavLink[] Links;             // step / drop links, per span, as a slice
    public NavPortal[] Portals;         // filled by the serial pass
    public int Version;                 // bumped by a carve (11.5)
}
public struct NavLink { public int FromSpan, ToTile, ToSpan; public byte Dir; public bool OneWay; }
public struct NavPortal { public int Span, OtherTile, OtherSpan; }

public sealed class NavWorld {          // the level's tiles, reached through World.Singleton<NavRef>()
    public float CellSize, CellHeight, MaxDrop;
    public NavTile[] Tiles;             // sparse: null where the level has no brush
    public bool TryFindSpan(Vector3 p, out NavRef r);   // nearest span under/at p
}
```

**The bake, per tile, in parallel (`IJob` per tile; tiles touch only their own arrays).**

1. For each column centre, cast **down through the whole tile**, collecting *every* solid top on
   `Noises.WallMask` — not the first. `Physics.Raycast` repeatedly from just under the previous hit.
2. Each top becomes a candidate span: `Floor` = hit y, slope from the hit normal, rejected when the angle
   exceeds `maxSlopeAngle` (50°). `Headroom` = distance up to the next solid.
3. Reject by body: `Headroom` < capsule height (1.9) → not walkable.
4. **Clearance erosion**: `Physics.CheckCapsule` at the span, radius 0.3375 — a span whose capsule overlaps the
   world is not walkable. (A cheaper 2-D erosion over the blocked mask is the fallback if this is slow; measure.)
5. **Links** to the 8 neighbours' spans: `up` when `ToFloor - FromFloor <= StepUp`, `down` when
   `FromFloor - ToFloor <= MaxDrop` and **one-way**; nothing otherwise.

`NavSettings` derives, from the Enemy profile and the prefab capsule:
`StepUp = rideHeight + groundTolerance` (0.60), `Radius = 0.3375`, `Height = 1.9`, `MaxSlope = 50°`.
**A check pins each to its source** — change the profile, the check fails, not the AI.

**Portals** in a second, **serial** pass: for each tile pair sharing an edge, match spans across it by the same
step/drop rule; store both ways (a one-way drop is a one-way portal).

**Settings.** Add to `WorldspawnConfig`: `cellSize = 0.5f`, `cellHeight = 0.2f`, `maxDrop = 2.5f`
(`[InspectRange]`, `[InspectTip]`); to `Worldspawn`: the same three; to `prefabs/level/worldspawn.json` keys:
`nav_cell_size`, `nav_cell_height`, `nav_max_drop`.

**Call site.** `GameSession.StartLevel`, after `BuildBrushes` and after `BakeWorldspawn` (it needs the settings),
synchronously — a build, not a system (the 9.x rule).

**Checks** (`EngineChecks/Layer11Checks.cs`, headless — no GPU):
a flat box level bakes the expected column and span counts; a wall column has no walkable span; **a deck over a
deck is two spans in one column**; a 0.4 m step links both ways, a 0.7 m step only down, a 3 m drop not at all; a
55° ramp is not walkable, a 45° one is; the derived limits equal the profile's; two bakes of the same level are
byte-identical; a tile with no brush is null.

**Done when.** Checks green; the dock bakes at level start inside its budget (report ms and MB in the commit);
the sweep is unchanged — **nothing reads the grid yet**, so every probe line must be byte-identical to 11.0.

**Commit.** `ECS layer 11.1: the level bakes into nav tiles of column spans`

---

## Phase 11.2 — A* over spans, inside a tile and across tiles

**Goal.** A pure, pooled, deterministic search. Still nothing walks on it.

**Files.** `Engine/Ecs/Nav/NavSearch.cs`, `NavPath.cs`.

**Shape.**

```csharp
public struct NavRef { public int Tile, Span; }              // a node
public enum NavStatus { Ok, Partial, Unreachable, Budget }
public sealed class NavPath { public Vector3[] Corners; public int Count; public NavStatus Status; }

public sealed class NavSearcher {        // one per worker; pooled, reused, never allocated per call
    public NavStatus Search(NavWorld w, NavRef from, NavRef to, NavPath into, int maxNodes);
}
```

- 8-neighbour in the plane **plus** the step / drop links; cost = octile distance with the height difference
  folded in; drops cost a little extra so a flat way is preferred to a fall.
- **No corner cutting**: a diagonal needs both orthogonals walkable on the same span level.
- **Ties break on `(Tile, Span)`**, never on insertion order.
- `maxNodes` caps a search; exceeding it returns `Budget` with the best partial path (a partial is useful —
  the enemy makes progress and repaths).
- **Hierarchical when the search leaves its tile**: A* over the portal graph for the tile corridor, then A*
  inside each tile between its entry and exit portals. One tile → plain A*.
- **String-pull** the result over span surfaces, **broken at every step / drop link** (a height change stays a
  corner). Corners are world positions on span floors.
- Heap, open set, closed set and the came-from map live on the `NavSearcher` and are cleared, not reallocated.
  **Zero steady-state allocation per search** — assert it in a check.

**Checks**: optimal on a hand-built maze (compare against a breadth-first reference on the same grid); no corner
cut through a diagonal gap; `Unreachable` when walled off; a two-tile path equals the one-tile path when the
wall between the tiles is removed; a path down a ledge exists and the reverse does not; identical corners across
two runs and across two searcher instances; allocation after warm-up is zero.

**Done when.** Checks green, sweep byte-identical again.

**Commit.** `ECS layer 11.2: A* over spans, hierarchical across tiles, pooled and deterministic`

---

## What 11.1 / 11.2 actually did (2026-11-18), and where they left the spec

Built in one pass. `Engine/Ecs/Nav/`: `NavGrid.cs` (types + `Navigation.Current`), `NavBake.cs`,
`NavSearch.cs`. Game side: `Game/Ecs/AI/Navigation.cs` (`NavProfile`, `NavBakeSystem`). Checks:
`EngineChecks/Layer11Checks.cs` (+35 assertions, 300 total), `GameChecks/Layer11Checks.cs` (+11, 494 total).

**The dock bakes**: 129 tiles, 130,678 spans, 113,082 of them walkable, over 132,096 columns, at cell 0.50 m —
with the derived limits the probe prints and the checks pin: step 0.60, radius 0.338, headroom 1.90.

**Four places the build departed from the plan, and why:**

1. **The bake is a system, not a call in `LevelBuild.Build`.** A brush becomes a static collider through
   `PhysicsEntities`, and those *register at the next fixed step* — at the end of the build the broadphase is
   still empty, so the planned synchronous bake cast its rays through nothing. `NavBakeSystem` runs after
   `PhysicsSyncSystem`, rebaking whenever `Worldspawn.Version` changes (the same hook `WorldspawnSystem` uses,
   which already covers a scene reset). It creates no entities, so the 9.x build/system rule is not in play.
   The schedule check caught the new system immediately and `ScheduleChecks.Expected` is updated — 6.7c working
   as designed.
2. **Links cross tiles directly; the two-level portal A* is deferred.** Links are built in one pass after every
   tile has its spans, so a link may already span a tile edge — that *is* the spec's portal, and a search
   crosses tiles today (there is a check for it). What is not built is the **hierarchical** search (A* over a
   portal graph, then inside each tile). Plain A* with a node budget is correct and simpler; the hierarchy is
   an optimisation that should be driven by a measurement, and 11.9's bench is where that measurement belongs.
   `NavSearcher.MaxNodes` (4000) and the `Partial` status exist so a long search degrades instead of stalling.
3. **Bounds and ground are two different questions.** The 11.0 finding said to exclude scenery over 200 m from
   the level's extent. The first implementation then also refused to *stand* on it — and lost 21,000 spans,
   because the dock's floor plane is one of those huge colliders. `NavBake.Bounds` now returns two lists: what
   sizes the grid (scenery excluded) and what can hold a body up (everything). A column no solid covers at all
   is still skipped without casting.
4. **The unit checks build their grids by hand.** A baked check needs the process-wide `Physics.World`, which
   every other suite in the same process has also been writing to; two static boxes in a fresh check world gave
   only one queryable collider, for reasons that are not navigation's. The checks now construct `NavWorld`s from
   a character map and link them with **`NavBake.Link` — the real link builder, made public for exactly this** —
   so the step, drop and corner rules under test are the shipping rules. The bake's own sampling is gated on the
   real level instead, by the `[test] nav:` probe in `maze` / `door` / `ledge`.

**The open problem: the bake costs 513 ms on the dock**, against the 50 ms the risk table set as the sign of
trouble. It is a load-time cost, not a frame cost, and it is paid again on every level build (a respawn, a save
load). Where it goes: ~137 ms in the per-span capsule clearance check, the rest in the two raycasts per column.
The planned answer — tiles in parallel — was **not taken**, because `PhysicsWorld.Raycast` increments a shared
counter and nothing else in the engine queries it off-thread; a data race in the one system that must stay
byte-deterministic is a bad trade for a load-time win. The cheap deterministic win was taken instead (skip
columns no solid covers, ~55 ms). Real options, in order of preference, for a follow-up phase:
2-D clearance erosion per span level instead of a capsule query per span; caching a bake per `.map` behind a
content hash; and only then threading, after making the query path provably side-effect free.

**The bake and the AI are on the job system (same day, after the above).** The open problem's first answer
turned out to be the engine's own: `PhysicsWorld.QueryBoundsConcurrent` already existed — a stamp-free broadphase
the rigid solver's narrow phase runs on its workers. What was missing was a raycast and an overlap on top of it.
`RaycastConcurrent` / `OverlapsAnyConcurrent` (and the `Physics.RaycastConcurrent` / `CheckCapsuleConcurrent`
facade) take their candidates from it over the ray's segment box, test each with `RaycastSlot` (reads a baked
static), keep a `[ThreadStatic]` scratch list instead of the shared pool, and count with one interlocked add
instead of the shared counter. The narrow phase's own scratch was already per thread. They give the main-thread
answer hit for hit — checked — and give up only the grid walk's early-out, which is why the main-thread path
keeps its stamps.

On that: **the bake's tiles are items of one `IJob` on `World.Jobs`** (item i fills slot i and nothing else;
linking stays the serial pass), and **`AiBrainSystem`, `SteeringSystem` and `SightSystem` are chunk jobs**
(`ScheduleParallel`, batches of 64 / 32 / 16). `HearingSystem` stays serial: it writes across two queries and
destroys entities, and noises are few. The schedule is unchanged — each AI system is still a batch of its own,
because each reads what the one before it wrote; what changed is that each batch now uses every worker.

| | serial | on the job system (8-thread pool) |
|---|---|---|
| dock bake | 514 ms | **208–260 ms**, same 129 tiles / 130,678 spans / 113,082 walkable |
| `maze` `door` `ledge` `approach` `peek` `close` `killenemy` | — | **byte-identical** probe lines |

Why that stays deterministic, in three facts rather than hope: a brain, a body or an eye reads its own row (and
shared immutable data) and writes its own row, so a chunk's result does not depend on which worker ran it or
when; a tile writes its own arrays and reads a broadphase nothing moves inside a fixed tick, and the spans are
numbered in tile order afterwards; and the gate sweep runs the ECS pool at **8 threads** even under
`--threads 1` (that flag sizes the rigid solver's pool, not the world's), so the byte-identical sweep *is* the
parallel case, not a single-threaded alibi. The bake's speed-up is only ~2–2.5× on eight threads: the dock is
a long pier whose tiles are unequal (one item per tile, no splitting inside a tile), and the M1 is four fast
cores and four slow ones. A finer item — a tile's rows — is the next step if 250 ms still matters.

**Two things the agreement check turned up.** First, a capsule wholly inside a box reads as *not* overlapping
on both query paths — the GJK deep-overlap blind spot the physics suite's own box report already notes. The bake
had leaned on the capsule to reject a floor span *inside* a wall's footprint (the ground under a brush), and the
up-ray cannot see that wall either, because it starts inside it. The bake now casts a **roof ray** down from the
top of the body's height to just above the floor: an enclosing solid's top is found in between, and the span is
LowCeiling. It fired on nothing in the dock (same 130,678 / 113,082) and costs ~50 ms, and stays: the `maze`
wall sits on the floor plane, which is exactly this case, and 11.4 will path around it. Second, the check world
in this process cannot produce a positive capsule overlap at all — not even touching the face — while the game's
bake marks thousands of spans Tight with the same call; the check therefore asserts only that the two paths
agree, which is its claim, and leaves which answer is right to the physics suite.

**Not yet true of 11.2** (the spec's wording, honestly scored): the search is deterministic, pooled and
zero-allocation once warm (all three checked), string-pulls, refuses corner cuts, honours one-way drops and the
carve counter. It is *not* hierarchical, and its string pull is purely geometric — the spec's "within a span
surface" phrasing anticipated a physics-aware pull, which would cost a cast per corner and is not needed while
11.4 keeps the straight-line test in front of it.

---

## Phase 11.3 — Requests are budgeted

**Goal.** A gameplay-side service: ask for a path, get one within a bounded per-tick cost, in an order that does
not depend on entity indices.

**Files.** `Game/Ecs/AI/NavPaths.cs`.

```csharp
[NotInPreview] public struct PathRequest { public Vector3 Goal; public int Serial; public bool Pending; }
[NotInPreview] public struct PathResult  { public int Serial; public NavStatus Status; /* corners in a side buffer */ }

[UpdateAfter(typeof(AiBrainSystem))] [UpdateBefore(typeof(SteeringSystem))]
public sealed class NavPathSystem : SystemBase, ITick { ... }
```

- Serves at most `N` searches per tick (`N` a tunable, from 11.0's measurement; start at 4).
- Queue ordered by **`AiBrain.Rng`'s placement seed**, not entity index — the 6.7b lesson: a buffered spawn
  changes indices, and the gate sweep diffs byte-for-byte.
- A new request while one is pending **supersedes** it (`Serial` increments; a late result for an old serial is
  dropped).
- Corner arrays come from a pool keyed by the asker, not `new` per path.

**Checks** (`GameChecks/Layer11Checks.cs`): the budget holds with 50 askers (count searches per tick); the
service order is identical when the same enemies are spawned in a different sequence; a superseded request
never lands.

**Done when.** Checks green; the sweep still byte-identical (nothing *asks* yet — the follower lands next).

**Commit.** `ECS layer 11.3: path requests are budgeted and ordered by placement`

---

## Phase 11.4 — Following, straight line first

**Goal.** The enemy gets round the wall. **This is the phase where the gates move.**

**Files.** `Game/Ecs/AI/PathFollow.cs`. `AiBrain.cs` and `Steering.cs` are **not** edited.

```csharp
[NotInPreview] public struct PathFollow { public int Corner, Count, Serial; public Vector3 Goal; public bool Direct; }

[UpdateAfter(typeof(AiBrainSystem))] [UpdateBefore(typeof(SteeringSystem))]
public sealed class PathFollowSystem : SystemBase, ITick { ... }
```

Per enemy with `AiIntent.Move`:

1. **Straight line first.** `Physics.CapsuleCast` along the flattened line to `intent.MoveTo`, body radius, with
   the ground under the line sampled against the drop rule. Clear **and** the goal on the same span → leave
   `MoveTo` alone, `Direct = true`, no request. This is today's behaviour and most chases take it.
2. Blocked, or the goal on another span → `PathRequest` and, while it is pending, keep walking the straight
   line (progress beats standing).
3. With a path: write **the current corner** into `intent.MoveTo`. Advance when within `CornerRadius`
   (≈ `cellSize * 1.5`) — **not** `def.arriveDistance`, which stays the *goal* tolerance the brain uses.
4. Repath when the goal has moved more than `RepathDistance` (≈ 2 m) from `PathFollow.Goal`, rate-limited.
5. `Unreachable` or a failed search → the straight line, as today. **Never freeze.**

**Expected gate movement** (write the actual numbers in the commit): `maze` and `ledge` go from fail to pass;
`approach`, `peek`, `close`, `killenemy` shift where the enemy now takes a different route. `door` still fails —
11.6 fixes it. Every *non-AI* script must stay byte-identical; if `rage` or `pickup` moved, something is wrong.

**Commit.** `ECS layer 11.4: enemies follow paths; straight line when the way is clear`

---

## Phase 11.5 — The world carves its tile

**Goal.** A shut door blocks, an open one does not.

**Files.** `Engine/Ecs/Nav/NavCarve.cs`; prefab edits to `phys_door.json`, `phys_slider.json`,
`prop_physics.json`.

```csharp
public struct NavObstacle : IComponent { public Vector3 HalfExtents; public Vector3 LastStamp; public bool Stamped; }
public sealed class NavObstacleConfig : ComponentConfig<NavObstacle> { ... }   // so it is a prefab field

[UpdateBefore(typeof(NavPathSystem))] [Exclusive]   // one writer of the grid, at one point in the schedule
public sealed class NavCarveSystem : SystemBase, ITick { ... }
```

- Stamp = increment `NavSpan.Dynamic` on every span the obstacle's box overlaps; unstamp = decrement the spans
  of the **previous** footprint. **Counters, not flags** — two crates on a span, one removed, still blocked.
- Re-stamp only when the entity has moved more than `StampThreshold` (≈ half a cell) from `LastStamp`, so a
  jittering crate is not re-stamped every tick.
- A stamp bumps `NavTile.Version` on each tile it touches and adds it to a dirty set for 11.6.
- A span is blocked for the search when `Dynamic > 0` **or** not walkable statically — the two never mix, so
  lifting a carve cannot make a wall walkable.
- Tag in the prefabs, not in code: `phys_door` and `phys_slider` always; `prop_physics` only above a size
  threshold (the crate, not the can).

**Checks**: a door closing blocks its doorway spans and opening frees them (exact counts); a crate pushed across
a tile edge leaves no span stuck on either side; two overlapping props removed one at a time leave the counter
right; a carve never makes a statically-unwalkable span walkable; stamping is idempotent under the threshold.

**Done when.** Checks green. `door` may still fail (paths made before the door shut are stale) — that is 11.6.

**Commit.** `ECS layer 11.5: doors, sliders and props carry their footprint in the nav grid`

---

## Phase 11.6 — A carve invalidates the paths that cross it

**Goal.** `door` passes. Nobody walks into a shut door.

**Build.** `PathFollowSystem` keeps the tile set its current path crosses (a small fixed array, or a bitmask
when the tile count allows). Each tick, against the carve's dirty set:

1. **Cheap**: does the path's tile set intersect the dirty tiles? No → done.
2. **Exact**: does any remaining corner-to-corner segment cross a span whose `Dynamic` is now > 0? Yes →
   drop the path, request a new one, walk the straight line meanwhile.

**Checks**: a door shut across a walking enemy's path triggers exactly one repath; a door shut in a tile the
path does not touch triggers none; a repath storm is impossible (rate limit holds under a swinging pendulum).

**Expected gate movement.** `door` fail → pass. Others unchanged from 11.5.

**Commit.** `ECS layer 11.6: a carve repaths whoever crossed it`

---

## Phase 11.7 — Seeing it

**Goal.** The grid is visible, in the game behind a flag and in the level editor while building.

**Build.** A debug overlay (`--nav-debug`, off by default) drawing: tile bounds; spans by height; blocked spans
in two colours (**static** vs **dynamic**); portals; one-way drop links as arrows; each enemy's path and its
corners; the straight-line test when it is taken. In the level editor, the same plus a **Rebake** button and the
worldspawn's `cellSize` / `cellHeight` / `maxDrop` live in the property grid, so a designer sees the effect of a
brush edit on where the AI can go.

**The 9.x screen rule**: an `IUiScreen` gets `Update` / `DrawUi` / `Close` and **no `Start`** — wire it fully,
then call your own init. The editor and the benchmark both crashed on this before.

**Done when.** The overlay draws on the dock and in the editor; the sweep is byte-identical (the overlay is off
by default and must not tick when off).

**Commit.** `ECS layer 11.7: the nav grid, its carves and its paths are visible`

---

## Phase 11.8 — The leftovers

1. **Delete `Engine/Physics/Navigation.cs`** — the Unity `NavMesh` / `NavMeshPath` / `NavMeshHit` stub, dead
   since the port (`git grep NavMesh` finds no caller; re-verify, then remove).
2. **Retune the rifleman** now that movement works: `arriveDistance` (5 m is the Unity agent's
   `remainingDistance <= 5` and far too loose), `wanderRadius`, `wanderMinDistance`. **Move them into
   `prefabs/level/monster_rifleman.json`**, out of `GameAssets.cs` — layer 10's whole point.
3. Remove 11.0's `--nav-measure` scaffolding if it survived.

**Expected gate movement.** The AI gates shift again with the retune — expected, recorded, deliberate.

**Commit.** `ECS layer 11.8: the Unity navmesh stub is gone; the rifleman's numbers are in its prefab`

---

## Phase 11.9 — Close-out

As every layer: full sweep **twice**, hermetic (`RIPNRAGE_DATA_DIR`) and device-isolated
(`InputOverride.Scripted`); both check suites; every script under the gate rule; the shots; both manifests; all
four editors and the benchmark launched.

**Report in the commit**: bake cost at level start (serial vs tiles in parallel) and peak memory for the dock;
search cost per tick at the budget, worst case; carve cost with a door swinging and a crate rolling; bench
interleaved against 11.8's parent (`--threads 1`); **allocation per frame against 10.x** — it was 6.0 KB after
9.8 and 11.2's pooling is what keeps it there; layer totals (files, lines, deletions).

Then rewrite the roadmap's **Status** line and the **pick-up point** for layer 12.

**Commit.** `ECS layer 11.9: layer close-out`

---

## Risks, and what to do about them

| Risk | Sign | Answer |
|---|---|---|
| The bake is slow at level start | > ~50 ms on the dock | tiles in parallel first; then 2-D erosion instead of per-span `CheckCapsule`; then cache per `.map` with a content hash |
| Per-span capsule overlap is the bake's cost | profile says so | erode the blocked mask in 2-D per span level — one pass, no physics query |
| Paths jitter at corners | the enemy wobbles between two corners | `CornerRadius` hysteresis; never re-advance backwards |
| Repath storms under a pendulum | `repaths` probe climbs | the movement threshold in 11.5 and the rate limit in 11.6; both tunable, both probed |
| Determinism breaks | `--compare` shows drift between identical runs | the three rules: tie on `(Tile, Span)`, carve at one schedule point, serve in placement-seed order |
| Spans explode on a tall level | span count per column high | `cellHeight` is a setting; merge spans closer than it during the bake |
| Enemies clump in doorways | visible in `maze` with two enemies | **out of scope** (see the roadmap's fence) — local avoidance is a later layer |


---

## Close-out (11.9, 2026-11-18)

Layer 11 is built: 11.0 through 11.8, in five commits. **The enemy gets there now.** The three gates written at
11.0 to fail all turn over, and the two that were only ever about the straight line are untouched.

| gate | 11.0 (before) | after 11.8 |
|---|---|---|
| `maze` | moved **3.43, frozen** from frame 180, never arrives | moved **7.72**, sees the player and fights (armour 100 → 10) |
| `door` | identical to maze — the gap never found | through the gap; then the leaf drops and it goes **round**: moved **22.39** |
| `ledge` | moved **2.90**, anim IDLE at the platform edge | comes down the 3.5 m drop, d 17.2 → **8.3**, trading fire |
| `approach` `close` `peek` | — | behaviourally the same through 11.4 (straight line first); moved up ~35% at 11.8 from `arriveDistance` |

**The dock**: 129 tiles, 130,678 spans, 113,082 walkable, 132,096 columns at 0.50 m; 45 spans carved by the
level's own doors and props before anything is spawned. Bake **220 ms** on the job system (514 ms serial).

**Checks**: engine 265 → **312**, gameplay 483 → **501**. `ScheduleChecks` pins `NavBakeSystem` (FixedTick),
`NavCarveSystem`, `NavPathSystem` and `PathFollowSystem` (Tick).

### What this layer did not do, and should be picked up knowingly

- **No full sweep was run for the close-out.** Verification was targeted throughout: the nav gates, the four AI
  gates against their 11.0 baseline, both suites after every step, and a crash-check of `walk` / `fall` /
  `saveload` / `pickup` / `die`. **Not run**: the other 20 scripts, the four pixel shots, the two manifests, the
  four editors, the benchmark. The `walk` manifest hash *will* have moved — the worldspawn gained three nav keys
  and the rifleman prefab two components — and that needs confirming against a real sweep before this is called
  closed. Likewise the bench A/B against 11.8's parent, which the plan asked for and which has not happened.
- **The hierarchical search is still deferred** (see the 11.1/11.2 note). Links cross tiles, so searches work;
  what is missing is the two-level portal A* for long paths. `MaxNodes` 4000 and `Partial` keep that honest.
- **EnemyData is still code.** 11.8 retuned `arriveDistance` in `GameAssets.cs` rather than moving three fields
  into the prefab and leaving seventeen behind.
- **Wander drifts.** It picks relative to where the grunt *is*, so a grunt with nothing to do random-walks away
  down the dock — 11.8 found this when lowering the minimum pick distance made it worse, and left the numbers
  alone. The fix is a home anchor on the spawn point, which is behaviour design, not tuning.
- **The editor half of 11.7 is not built.** The overlay is in the game (`--nav-debug`, F4); the level editor has
  no nav draw and no rebake button, so a designer still cannot see where the AI can go while placing brushes.
- **No local avoidance.** Two grunts in one doorway still shove each other through the solver, as the fence said.
- **Still no melee damage path** — inherited from layer 9 and untouched here.


---

# Part two — what is left, in the order it bites (planned 2026-11-18)

Layer 11 is built and verified targeted. What follows is the rest of the layer: the books, four things the
build found and put down, the features the plan fenced off, and the editor pass **last**, once 11.10–11.16 have
said exactly what a designer needs to see. Same rules as part one: one phase, one commit, the sweep's probe lines
move only where the phase says they will.

## The seams part two attaches to

| Seam | Where | Why it matters |
|---|---|---|
| The grid's identity | `NavBakeSystem.FixedTick` sets `Navigation.Current = nav` (`Game/Ecs/AI/Navigation.cs:71`) | a rebake is a **new object**; nothing downstream notices yet |
| The carve's skip | `NavCarveSystem.Tick`, `if (o.Stamped && …) continue;` (`Engine/Ecs/Nav/NavCarve.cs:136`) | an unmoved obstacle after a rebake is never re-stamped — **a bug** |
| The side buffer | `NavPaths` dictionary, forgotten only by `OnEntityDestroyed` (`NavPaths.cs:70`) | never cleared on a level start |
| The wander pick | `AiBrains.Step`, `self + (cos, 0, sin) * dist` (`AiBrain.cs:128`) | a disc about the grunt, not a span; relative to where it is, so it drifts |
| Melee | `EnemyData.meleeAttackRange` | **no reader anywhere** |
| The straight-line test | `PathFollowing.StraightLineClear` | one capsule cast + up to 24 rays per enemy per 0.2 s; the place local avoidance would also read |
| The bake's cost | `NavBake.BakeTile`: down-ray, up-ray, roof-ray, capsule per candidate span | 220 ms on the dock at 8 threads |
| The probe | `AiProbe` prints `way / corners / at / blocked` | 11.0 asked for `path ms`, `repaths`, `tiles crossed` too — never added |
| Enemy tuning | `GameAssets.RiflemanData` (code) + `EnemyConfig.kind` (classname only) | the definition is not authored |

## Phase 11.10 — Close the books, and fix what the review found

**Goal.** Layer 11 is actually closed, with the two defects the close-out review turned up fixed first, because a
sweep that bakes them into the baseline is a sweep that has to be redone.

**Build.**

1. **Rebake re-stamps.** `NavWorld` gets a `BakeId` (a counter the bake increments). `NavCarveSystem` remembers
   the id it stamped for; when it differs, every `NavObstacle.Stamped` is cleared and `BlockedSpans` reset before
   the tick's stamping. Check: bake, stamp a door, rebake, the door's spans are blocked again without it moving.
   Gate: `door` with a `NavRebake()` after the leaf drops — `blocked` must not fall to 45.
2. **The side buffer is cleared on a level start** (`NavPaths.Clear()` from `NavBakeSystem` when the bake id
   changes — the same moment). Check: `NavPaths.Count` is 0 after a rebuild.
3. **The probes 11.0 asked for.** `NavPathSystem` records per-serve `LastNodes` and elapsed ticks; `PathFollow`
   counts repaths; `AiProbe` prints `nodes N ms T repaths R`. Expected movement: the AI gates' probe lines gain
   fields and nothing else.
4. **The sweep, twice**, hermetic and device-isolated. **Re-baseline** `walk` and `menu` manifests (the worldspawn's
   three nav keys and the rifleman's two components are the only expected differences — read the manifest diff
   and say so in the commit). Four pixel shots, four editors, the benchmark. Bench interleaved against `0f39630`
   (11.0) with `--threads 1`: frame time must be flat in the noise; game allocation per frame must be within
   ~0.5 KB of 10.x (the side buffer, the queue and the searcher are pooled — if it is not flat, one of them is
   allocating, and that is the finding).
5. **Layer 10.8's close-out**, which has been open since before this layer: run it with the same sweep.

**Done when.** Both sweeps byte-identical to each other; every diff against the 11.0 sweep is one the commit
names; the roadmap's Status line for layer 11 loses its "not swept" caveat.

**Commit.** `ECS layer 11.10: the books are closed — rebake re-stamps, the side buffer resets, the sweep is run`

## Phase 11.11 — Wander lives on the grid

**Goal.** A grunt with nothing to do stays where it was put and only ever walks to places it can stand.

**Build.**

- `AiBrain` gains `Home` (set at spawn: the floor position; the prefab bake sets it from `ctx.Position`).
- The wander pick becomes **a span**: roll a disc about `Home` (not `self`), `Navigation.Current.TryFindSpan`
  the point with the drop reach, reject if none or not `Open`; three rolls, then idle. `wanderMinDistance` is
  measured from `self` as before. With picks guaranteed walkable, the 3 s `WanderTimeout` becomes a backstop
  rather than the usual exit.
- The rolls still come from `b.Rng` — same dice, same run.
- **Then** retune: `wanderMinDistance` 10 → 4, `wanderRadius` 15 → 10, now that the thing that made lowering
  them a drift is gone.

**Checks (game).** A hand-placed grid with a hole: no wander pick lands in the hole across 1000 rolls; the pick is
within `wanderRadius` of `Home` after the grunt has been displaced 20 m.

**Gates.** `approach` / `close` / `peek` move — the grunt no longer leaves. Record the new values. New gate
`stay:600`: a grunt alone on the dock for ten seconds ends within `wanderRadius` of where it started.

**Commit.** `ECS layer 11.11: wander picks a span about a home, not a point about itself`

## Phase 11.12 — The bake pays once

**Goal.** Level start, respawn and save-load stop paying 220 ms each.

**Build, in order, measuring after each — stop when the number is good enough:**

1. **2-D clearance erosion.** Replace the per-span `CheckCapsuleConcurrent` with a pass over each tile after
   sampling: a walkable span whose 8 neighbours (at step height) include a non-walkable or missing one within
   `ceil(Radius / CellSize)` cells is `Tight`. Conservative by up to one cell; no physics query. Expect −100 ms.
   Check: the hand-built grid's erosion matches a brute-force distance test.
2. **Rows as job items**, not tiles: `TileJob` over `(tile, row)` so a long thin pier's tiles stop being the
   long pole on 8 workers. Expect the 2–2.5× to become 4–5×.
3. **A cache per map.** Serialize the `NavWorld` (settings, tiles, spans, links) to
   `$RIPNRAGE_DATA_DIR/nav/<map>-<hash>.bin`, hash = brushes + worldspawn nav keys + the `NavSettings` the body
   derived (so a profile change misses the cache). Load instead of bake when it hits; bake and write when it
   misses. Static `Dynamic` counters are zero in the file by construction. Check: round-trip equality; a changed
   setting misses. **Determinism**: a loaded grid and a baked one must be byte-identical — the `nav` probe is
   the gate, run cold and warm.

**Done when.** Cold bake < 120 ms, warm (cache hit) < 20 ms on the dock; the `nav` probe identical cold and warm.

**Commit.** `ECS layer 11.12: the bake erodes in 2-D, jobs by row, and is cached per map`

## Phase 11.13 — Local avoidance

**Goal.** Two grunts in one doorway get through it instead of shoving each other through the solver.

**Build.** A **velocity-space** term in `Steering.Step`, not a position hack: each enemy reads the positions and
`Desired` velocities of neighbours within `AvoidRadius` (~2.5 m) from a snapshot taken **before** the steering
job (a small array filled serially, read-only in the job — order-independent, so the chunk job stays
deterministic), and adjusts its own `Desired` by the reciprocal half of the separating velocity (RVO-lite: each
side does half the work, so two symmetric grunts do not mirror each other for ever). A path corner is not
abandoned for it — avoidance only perturbs the velocity towards the corner. Capped at the body's speed.

**Checks (game).** Two bodies head-on in a 1.4 m corridor pass; neither stops; the result is identical over two
runs. Three in a 2 m corridor do not deadlock within 5 s.

**Gates.** New `crowd:600`: two grunts spawned side by side behind the `door` wall, one gap, both arrive and fight
by frame 500. The existing gates are single-grunt and must not move.

**Commit.** `ECS layer 11.13: enemies make way for each other`

## Phase 11.14 — The hierarchical search (conditional)

**Decide from 11.10's `path ms` / `nodes` probes.** If the worst search on the dock stays under ~0.3 ms and
under 2000 nodes, **skip this phase and say so**. Otherwise:

- Per tile, a **portal graph**: the open span pairs across each edge, grouped into portals (runs of adjacent
  cross-tile links), with portal-to-portal costs inside the tile precomputed at bake (Dijkstra per portal, cached
  in the tile; invalidated with the tile's `Version` on a carve).
- `NavSearcher.Search` goes two-level when `from` and `to` are in different tiles: A* over portals for the tile
  corridor, then A* within each tile of the corridor. Same tie-break rules; same `Partial` on budget.

**Checks.** The two-level answer equals the flat answer's length on the two-tile grid and on a hand-built 4×1
corridor; a carve inside a tile invalidates only that tile's portal costs.

**Commit.** `ECS layer 11.14: long searches go portal to portal`

## Phase 11.15 — The enemy definition is a file, and there is a second enemy

**Goal.** `EnemyData` is authored, whole, and a second kind proves it is.

**Build.**

- `enemies/<classname>.json` with every field of `EnemyData` (movement, engagement, attack readability, the state
  tree's numbers, the senses, the status, the sheets by sprite name, the skeleton). `Enemies.All` is built from
  the folder at `GameAssets.Load`; `GameAssets.RiflemanData` becomes `Enemies.ByClassname("monster_rifleman")`.
  The layer-10 property grid edits it (`[InspectTip]`s carry over), and the prefab watcher reloads it.
- The rifleman's numbers move into `enemies/monster_rifleman.json` — all of them, in one commit, so the
  definition has one source of truth. A check pins that the file bakes to what `GameAssets` used to build.
- **A second kind**: `monster_brawler` — the melee grunt. Same body and sheets as the rifleman for now (the art
  comes later), `attackRange` = `meleeAttackRange`, no burst, higher `movementSpeed`. It exists to make 11.16
  real and to prove a kind is data.

**Gates.** The `nav` probe and all AI gates unchanged (same numbers, different source). New `brawler:400`: a
brawler closes to melee range through the `maze` wall.

**Commit.** `ECS layer 11.15: enemies are files; the brawler is the second`

## Phase 11.16 — The melee damage path

**Goal.** `meleeAttackRange` has a reader; Fury's melee bonus has something to apply to.

**Build.**

- `EnemyAttack`: when the target is within `meleeAttackRange` the attack is a **swing**, not a burst — the same
  windup telegraph, then a `DamageEvent { Cause = Melee, Instigator = enemy }` on the target if still in range and
  in the cone at the moment of the swing (one capsule overlap, no projectile). Stagger and the rage hooks
  (`Instigator`) already read `DamageEvent`.
- The player side: the fist combo's hits (`AltFire`) post `DamageEvent { Cause = Melee }` where they currently
  post whatever they post, and `Rage` applies the Fury melee multiplier to `Cause == Melee` — closing layer 9's
  "still no melee damage path".
- `DamageCause.Melee` added if absent; the HUD's hit marker does not change.

**Checks (game).** A brawler at 1.5 m swings and the player loses health; at 3 m it does not; a Fury-state jab
does more than a calm one by exactly the multiplier.

**Gates.** `brawler` now ends with the player hurt; `jab` moves (its probe prints the combo step; add the
damage); everything else unchanged.

**Commit.** `ECS layer 11.16: melee has a damage path, both ways`

## Phase 11.17 — The editor pass (last, by design)

**Deferred until here on purpose**: by now 11.10–11.16 have said what a designer actually needs to see, instead
of guessing at 11.7. The needs list to carry in, to be confirmed against what the phases found:

- the grid drawn in the level editor (reuse `NavDebug.Draw`, viewer = the editor camera), with **static vs carved
  vs tight** as three colours — 11.12's erosion makes *tight* the common reason a doorway is shut, and that is
  the thing a designer most needs to see;
- a **Rebake** button and the worldspawn's three nav keys live in the property grid (the editor world has no
  `NavBakeSystem`; it calls `NavBake.Build` directly — engine-side, no game assembly needed);
- `NavObstacle` footprints as gizmos on the doors and props that carry them;
- the `enemies/*.json` definitions (11.15) in the Prefab window, editable;
- **wander homes and radii** (11.11) drawn for placed monsters, so a designer sees where a grunt will roam;
- a **reachability paint**: pick a span, flood-fill, tint what it can reach — the question "can the AI get from
  here to there" answered without running the game;
- a coverage readout: walkable spans, tight spans, carved spans, unreachable islands.

**Done when.** A designer can place a wall in the editor, press Rebake, and see the doorway shut. The editor is
a gate in `gates.sh` already; add the nav draw to its script.

**Commit.** `ECS layer 11.17: the editor sees the grid`

## Missing features beyond this layer (for layer 12's survey, not phased here)

Things the AI does not do that the grid now makes possible. Listed so layer 12's plan starts from them rather
than rediscovers them; none is started.

- **Investigate**: `Chase` walks to `LastKnown` and stands. A grunt that arrives and finds nothing should look —
  a short search pattern on nearby spans before giving up. Needs 11.11's span picking.
- **Alert propagation**: hearing passes a target along; nothing passes *alarm*. A grunt that sees the player
  could make a noise others act on (shout → `Noise` with its own target).
- **Cover and flank**: spans with a wall between them and the target are cover; a search that weights by
  exposure finds a flank. Both are span attributes the bake could add cheaply (one ray per span per target is
  not cheap — do it lazily for the engaged grunt only).
- **Spawners on the grid**: `monster_spawner` places at its origin; it should place on a walkable span, and
  not one the player can see.
- **Off-grid as ground**: riding a moving platform. Carved as an obstacle today; a platform is a span whose
  floor moves, which the structure cannot express. Only if a level needs it.
- **Jumping up** as a deliberate move. The drop rule is one-way by design; a jump link is the opposite edge and
  needs the body to be able to jump, which it currently cannot.
- **Flow fields** if counts go past ~20 engaged grunts with one target; 11.3's budget is where it would plug in.
- **Save/load of the nav state**: `PathFollow` / `PathRequest` are runtime and rebuild themselves; carves
  re-stamp (after 11.10). Nothing to save — confirmed, not assumed, when 11.10 runs `saveload`.
