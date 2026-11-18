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
