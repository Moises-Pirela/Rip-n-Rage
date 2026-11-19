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

- [ ] **11.17.0 Instrument.** Extend the editor's `session` script with nav steps and a `[test] nav editor:`
  probe (tiles, spans, walkable, tight, time). Today it prints "no grid": that is the baseline. The editor gate
  already runs in the sweep, so this costs no new gate.
- [ ] **11.17.1 Geometry on demand.** `LevelLoader.BuildCollisionOnly(doc)` — the brush collider loop, callable
  from the editor, **plus** a static collider per map entity whose prefab carries a `ColliderShape` and no
  `Body` (Q4). The colliders go in a list the editor destroys on rebuild. Check: a preview world plus collision
  has the same static count as the game's build of the same map.
- [ ] **11.17.2 Rebake.** A `NavPanel` (PhysicsPanel's shape): **Rebake**, the readout (tiles, spans, walkable,
  tight, carved, time), and a **stale** badge driven by the document's change counter. It calls
  `PhysicsEntities.FlushRegistrations` then `NavBake.Build` with its own cached query, and sets
  `Navigation.Current` for the editor's world. Gate: the session script rebakes and the probe prints the counts.
- [ ] **11.17.3 The draw.** `NavDebug.Draw` from `DrawGizmos`, three colours plus one-way drop links and tile
  bounds, radius from the panel, a cap on spans drawn. Gate: a screenshot (`shot-edit-nav.png`).
- [ ] **11.17.4 Reachability paint.** Flood-fill the link graph from a root span (player start, or a clicked
  span) and tint what is reachable; the readout gains **unreachable islands**. Engine check: the flood over a
  hand-built grid with a walled-off room reaches exactly the open side; a one-way drop is followed down and not
  up. This is the phase that answers "why can't the AI get there".
- [ ] **11.17.5 What will carve, and where they roam.** `NavObstacle` footprints as gizmos on the prefabs that
  carry them; monster placements draw their wander radius about `Home`. Both read prefab data the editor already
  has.
- [ ] **11.17.6 Live settings.** The Level tab's three nav keys mark the grid stale and (optionally) rebake on
  release. Nothing new to build in the grid — only to act on its edits.
- [ ] **11.17.7 Close-out.** Editor gate green with the nav steps, both screenshots, the doc, and the roadmap's
  layer 11 line finally closed.

## What 11.17 does not do

- Bake the game's grid (props and entity colliders) — Q4, brush geometry only.
- Carve, or show anything moving.
- Edit the grid by hand (painted blockers, off-mesh links): the grid is derived, and a designer changes it by
  changing the level.
- Replace the ray sampler with a document sampler — option (2) above, written down, not taken here.
