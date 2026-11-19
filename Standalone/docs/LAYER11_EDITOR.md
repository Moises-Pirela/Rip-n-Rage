# Layer 11.17 — the editor sees the grid

The last piece of layer 11, held back on purpose until 11.10–11.16 had said what a designer actually needs to
see. The navigation rules and types are in [`LAYER11_NAV.md`](LAYER11_NAV.md); this is the editor side only.

## Audit — what is there today (2026-11-19)

| Piece | Where | State |
|---|---|---|
| The editor's world | `Application` → `World.AddSystemsFromAssemblies(engine)` (`Application.cs:135`) | **engine systems only**. `NavCarveSystem` ticks (harmlessly, `Navigation.Current` is null); the game's `NavBakeSystem` and the AI are *not* there, and must not be |
| Level preview | `LevelLoader.Load(doc, preview: true)` | builds brush **visuals**, entity previews, and bakes the worldspawn singleton (`level.killY = BakeWorldspawn(doc)` runs for preview too) |
| Brush collision | `LevelLoader.BuildBrushes`, inside `if (!preview)` | **not built in preview** — the editor world has no static colliders at all |
| The nav settings | `WorldspawnConfig.navCellSize / navCellHeight / navMaxDrop` | **already editable**: `EntityInspector`'s Level tab is the same property grid over `doc.worldspawn`, applied live. Nothing to build — only to act on |
| Gizmo drawing | `EditorController.DrawGizmos()` → `DebugDraw.Line/Box` | the grid can be drawn here; `NavDebug.Draw` already exists (11.7) and takes a viewer position |
| Panels | `PhysicsPanel` (`Open`, `Revert`, `HandleKeys`, `Draw`), `EntityInspector`, `PrefabWindow` | the shape a nav panel follows |
| Rebuild on edit | `EditorController.Rebuild()`, called from every edit path | the preview is rebuilt wholesale on **every** edit — what a bake must not hang off |
| The gate | `gates.sh` → editor `--script session --frames 90`, screenshot `shot-edit.png` | frame-driven input simulation; nav steps go here |

## The one blocker, and the options

**A ray-based bake needs colliders, and the editor has none.** Three ways out:

1. **Build brush collision in the preview, on demand.** Lift the collider loop out of `if (!preview)` into a
   function the editor calls when it wants a bake, and destroy those colliders with the preview. Small, local,
   uses the bake exactly as the game does. Cost: the collider build is per-brush and the editor rebuilds on every
   edit — so it must be tied to **Rebake**, not to the preview.
2. **A physics-free bake**: sample the `MapDocument`'s brush planes directly instead of casting rays. Removes the
   dependency entirely, would let the game bake without the broadphase flush, and would make the bake testable
   headlessly (killing the hand-built-grid workaround in the checks). This is the better end state and a
   layer-12-sized change — it is a second implementation of the sampler, and the two must agree span for span.
3. Bake in a scratch world. More moving parts than (1) for the same result.

**Recommendation: (1) now, (2) written down as the direction.** (1) gets a designer the answer this quarter;
(2) is worth doing when something else needs it (the editor is not reason enough on its own).

## Decisions (to confirm before 11.17.1)

- **The editor bakes on request, never on edit.** A 185 ms bake per keystroke is unusable, so: a **Rebake**
  button and a **stale** badge that lights the moment the document changes. No automatic rebake.
- **The editor does not write the nav cache.** Its world has brush collision but no prop colliders, so its grid
  is not the game's grid; writing it under a key the game may hit would hand the game a wrong grid.
- **The editor does not carve.** Nothing moves in it. `NavObstacle` footprints are drawn as gizmos instead, so a
  designer sees what *will* carve.
- **Three colours, and `Tight` is one of them.** Walkable, blocked-by-level, and **too tight for the body** —
  the last is the usual reason a doorway a designer drew is not a doorway the AI can use.

## Answered (2026-11-19)

- **Q1 — panel only.** No key toggle: the panel holds the draw switch, Rebake and the readout.
- **Q2 — the player start** is the reachability root.
- **Q3 — a radius slider**, default 40 m, with a hard cap on spans drawn.
- **Q4 — yes, the editor shows the game's grid**, entity colliders included. In practice that means the
  **static** ones: a prop with a `Body` is dynamic, is never baked in the game either, and is carved at runtime —
  so 11.17.1 builds collision for brushes **and** for map entities whose prefab has a collider and no body, and
  the things that move are drawn as footprints by 11.17.5. Without this the editor's grid would show doorways
  open that a static prop closes.

## Phases

- [x] **11.17.0 Instrument.** Extend the editor's `session` script with nav steps and a `[test] nav editor:`
  probe (tiles, spans, walkable, tight, time). Today it prints "no grid": that is the baseline. The editor gate
  already runs in the sweep, so this costs no new gate.
- [x] **11.17.1 Geometry on demand.** `LevelLoader.BuildCollisionOnly(doc)` — the brush collider loop, callable
  from the editor, **plus** a static collider per map entity whose prefab carries a `ColliderShape` and no
  `Body` (Q4). The colliders go in a list the editor destroys on rebuild. Check: a preview world plus collision
  has the same static count as the game's build of the same map.
- [x] **11.17.2 Rebake.** A `NavPanel` (PhysicsPanel's shape): **Rebake**, the readout (tiles, spans, walkable,
  tight, carved, time), and a **stale** badge driven by the document's change counter. It calls
  `PhysicsEntities.FlushRegistrations` then `NavBake.Build` with its own cached query, and sets
  `Navigation.Current` for the editor's world. Gate: the session script rebakes and the probe prints the counts.
- [x] **11.17.3 The draw.** `NavDebug.Draw` from `DrawGizmos`, three colours plus one-way drop links and tile
  bounds, radius from the panel, a cap on spans drawn. Gate: a screenshot (`shot-edit-nav.png`).
- [x] **11.17.4 Reachability paint.** Flood-fill the link graph from a root span (player start, or a clicked
  span) and tint what is reachable; the readout gains **unreachable islands**. Engine check: the flood over a
  hand-built grid with a walled-off room reaches exactly the open side; a one-way drop is followed down and not
  up. This is the phase that answers "why can't the AI get there".
- [x] **11.17.5 What will carve, and where they roam.** `NavObstacle` footprints as gizmos on the prefabs that
  carry them; monster placements draw their wander radius about `Home`. Both read prefab data the editor already
  has.
- [x] **11.17.6 Live settings.** The Level tab's three nav keys mark the grid stale and (optionally) rebake on
  release. Nothing new to build in the grid — only to act on its edits.
- [x] **11.17.7 Close-out.** Editor gate green with the nav steps, both screenshots, the doc, and the roadmap's
  layer 11 line finally closed.

## What 11.17 does not do

- Bake the game's grid (props and entity colliders) — Q4, brush geometry only.
- Carve, or show anything moving.
- Edit the grid by hand (painted blockers, off-mesh links): the grid is derived, and a designer changes it by
  changing the level.
- Replace the ray sampler with a document sampler — option (2) above, written down, not taken here.


---

## 11.17.0 / 11.17.1 — done (2026-11-19)

**The baseline, now a gate** (`editnav`, a second short editor run beside `session`):

```
[test] nav editor start: grid none | static colliders 0 | broadphase 0
```

**Collision on demand.** `LevelLoader.BuildBrushCollision` is the brush loop lifted out of `BuildBrushes` (the
game build calls it, unchanged), and `BuildCollisionOnly` adds a static collider for every map entity's class
that has a collider, no body and no trigger — **walking the prefab's children**, which is where a class usually
keeps it. The editor builds this on demand and destroys it on `Rebuild`; it is deliberately not part of the
preview, which is rebuilt on every edit.

| | colliders | tiles | spans |
|---|---|---|---|
| editor, root configs only (first try) | 18 | — | — |
| editor, children walked | **24** | **131** | **130,634** |
| the game, same map | 30 (raw static count) | **131** | **130,700** |

**Two things the measurement taught.** The root-only version found 18 of 30: the dock's six lamp posts and four
joint anchors keep their colliders on a *child*, so a third of the level was missing. And the collider *counts*
are the wrong proxy — the game's 30 includes triggers (the checkpoint, the ammo pickups, the water), which are
static colliders that rays skip, so they can never be in the grid. The right comparison is the grid, and it is
**the same 131 tiles and within 0.05% on spans** (130,634 against 130,700), baked in 164 ms.

The 66-span difference is the honest limit of a config-level bake: the game runs placement systems at spawn
(`StandOnBase`, `HingeOnEdge`) that nudge transforms before the colliders register, and this walks configs
without running them; capsule and convex children are skipped too. The editor's grid is a preview of the game's —
accurate enough to answer "can the AI get there", and documented as not a substitute for it.


## 11.17.2 – 11.17.6 — done (2026-11-19)

One panel (F6 opens it; the draw switch is inside it, as decided) with one button. `Rebake` builds the collision,
bakes, measures and floods; the badge says **STALE** the moment anything changes the level — every edit path goes
through `Rebuild`, and a worldspawn edit (where the three nav keys live) says so too.

```
[test] nav panel: tiles 131 spans 130634 walkable 113118 tight 17276 carved 0
                  colliders 24 reached 21078 unreachable 92040 stale False drawn 3328
[test] nav panel after an edit: ... stale True
```

**The draw** is five colours, not three: green walkable, **blue reachable from the player start**, orange **too
tight for the body**, grey the level, red carved. Radius slider (10–160 m, default 40) and a span cap (default
20,000) which the readout reports — the dock is 130k spans and a slider cannot be trusted with that; the gate
drew 3,328.

**The reachability paint earns its place immediately.** On the dock, 21,078 spans are reachable from the player
start and **92,040 are not**. That is not a bug in the flood — checked: every reached span is at y 0.9, the deck,
while the unreachable set runs from y −16 to y 28.9. They are the sea bed, the roofs and the container tops:
surfaces the body fits on that no path from the spawn can reach. Two things follow — a designer can see at a
glance which parts of a level the AI will never use, and **the bake spends four fifths of its spans on surfaces
nobody can stand on in practice**, which is a real optimisation for a later layer (bake what is reachable, or
drop scenery ground).

**Placements** (11.17.5) are drawn from the same data the game reads: a class carrying `NavObstacle` gets the
footprint it will stamp when it moves — the grid never shows it, because the editor does not carve — and a
monster gets the circle it will wander inside, from its `enemies/*.json` definition.


## 11.17.7 — close-out (2026-11-19)

**The sweep ran, in four batches** (`./gates.sh --batch k/4`), which is what the batching was built for. All four
green: 32 scripts, both suites in every batch (engine **322**, gameplay **540**), six screenshots each, both
manifests, and the three editor gates — `edit`, `properties` and the new `editnav` — in every batch.

**The batching had a bug and the sweep found it.** `--batch … --list` worked because it exits early; the loop
header underneath had been duplicated by the 11.10d edit (`for entry in $SLICE; dofor entry in $SLICE; do`) and
no run had ever reached it. Fixed, `bash -n` clean, and then four batches passed.

**Determinism**: batch 1 was run twice and `./gates.sh --compare` reports no differing probe lines.

**The gates are at the values the layer recorded**, unchanged by the editor work: `maze` moved 21.05 at d 9.4 ·
`door` 21.44 at d 8.8 · `ledge` 12.32 at d 5.0 · `stay` 5.51 at d 21.0 · `brawler` 10.91 at d 0.9 · `crowd` all
three grunts in and the player down.

**The manifest moved, as predicted, and the cause is named**: 103 → 104 entities, hash `054f07cd` → `7b22b147`.
The classes new to it are exactly layer 11's — `PathRequest` and `PathFollow` on the enemies, `NavObstacle` on
the doors and props, and `NavStats`, whose singleton carrier is the extra entity.

**Layer 10.8's close-out is covered except the bench**: this sweep is the checks, the scripts, the shots, the two
manifests and the editor gates it asked for. What it still wants, and what layer 11 owes with it, is the
**interleaved bench A/B** (`--threads 1`) against the pre-layer parent. That is the one close-out item left.

**One thing this close-out's own review caught**: `DrawNavPlacements` resolved a prefab per entity per frame, and
the editor draws every frame. The placements are resolved once now and rebuilt with the grid.
