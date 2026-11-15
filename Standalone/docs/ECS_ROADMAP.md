# ECS migration roadmap

Status and plan for moving Rip-n-Rage from the Unity-style `GameObject` / `Behaviour` model onto the ECS in
`src/RipNRage.Engine/Ecs`. Written to be picked up by a future session: read **Decisions**, check **Status**,
then start at the first unchecked layer.

## Decisions (agreed, do not reopen without asking)

| Topic | Decision |
|---|---|
| Storage | **Hybrid**: archetype tables + `[SparseComponent]` types in sparse sets (tags, frequently toggled state) |
| Signature | Fixed **256-bit** bitset per entity; exceeding 256 component types throws |
| Config / runtime | Exactly **one `IComponentConfig` class per `IComponent` struct**; `Bake()` produces the struct. **JSON prefabs** with `base` inheritance |
| Events | Systems subscribe by interface: `OnSimStart`, `OnSimEnd`, `Tick`, `FixedTick`, `LateTick` (end of frame), `OnEntityCreated`, `OnEntityDestroyed` |
| Structural events | Fire at **sync points** (after a batch / command-buffer playback). Created: after initial components. Destroyed: before removal |
| Custom events | `IPostProcessEvent` structs posted during the frame, consumed by `IConsumes<T>` systems **at end of frame**, then the list is cleared |
| Ordering | Attributes: `[UpdateAfter]` / `[UpdateBefore]`; `[Reads]` / `[Writes]` drive parallel batching; undeclared systems run alone |
| Discovery | Components, configs and systems found **by reflection** |
| Threading | Job system **from the start**: conflict-free systems run in parallel batches; `Query.ScheduleParallel` for chunk jobs |
| Hierarchy | `Parent` + `LocalTransform` → `LocalToWorld` (TransformSystem) |
| AI | **Keep the behavior tree** (shared tree data + per-entity runtime state component) |
| Physics | **Convert** to components / systems (not a handle wrapper) |
| Migration | **Big bang**, but done in **layers, foundational engine systems first, then gameplay**, stopping for review after each layer |
| Priority (set during layer 2) | **The ECS comes first; the game may be broken while we refactor.** Legacy code is removed as soon as its ECS replacement exists — or before, if nothing replaces it yet. Gameplay that depended on it goes inert until its layer ports it. Don't spend effort keeping legacy gameplay working |
| Editors | Editor integration of configs comes **later** (after gameplay) |
| Assets in components | **Handles, not references**: `AssetHandle<T>` into an `AssetRegistry<T>` (`Ecs/Assets.cs`); registries keep the name a config baked from (`NameOf`) so handles serialise back to config data. Render registries: `RenderAssets`. Physics collider shapes reuse the same pattern |

## Status

- [x] **Layer 0 — ECS core** (`067537e`): storage, queries, command buffers, scheduler, events, prefabs, chunk jobs. 36 assertions.
- [x] **Layer 1 — Transforms + rendering** (`3ed7e45`): hierarchy (verified against legacy `Transform`), the renderer
  consumes `RenderFrame` draw items from producers, ECS render components + `EcsRenderProducer`, `--ecs-demo`. Pixel
  parity with the pre-ECS renderer is within run-to-run noise.
- [x] **Layer 2 — Physics**: ECS-only simulation, joints, characters, movers, triggers; legacy physics deleted (close-out below)
- [x] **Layer 3 — Animation, audio, particles, destruction** (details below)
- [x] **Layer 4 — Input, camera, player**: input singleton, the player as an ECS character, the camera and the
  first-person viewmodel as entities; `ViewSprite` and the look / bob / sway / recoil behaviours deleted (close-out below)
- [x] **Layer 5 — Weapons, combat, damage**: health / armour / hitboxes on entities, weapons and projectiles as data +
  systems, alt-fire finishers as entities; the Weapon / Projectile class hierarchies, `Inventory`, `CameraRecoil` and
  the no-VFX fallback branch deleted (close-out below)
- [x] **Layer 6 — Enemies, AI, spawning**: the enemy is only an entity — body, look, a data-oriented state tree,
  perception (sight query + noise entities), steering, attacks as entities, spawners as components; the behaviour
  trees, `AIAgent`, `AIState`, `MonsterSpawner` and the enemy GameObjects deleted; system declarations derived from
  queries and enforced (close-out below)
- [x] **Layer 7 — Pickups, rage, environment**: rage is two meters the player fills (Fury by aggression, Calm by
  evasion under threat) and spends on ENRAGED / DEPRESSED, tuned live from tunables.cfg `[rage]`; the style meter
  retired; pickups, water, checkpoints, sky and impact surfaces are entities and systems; the trigger bridge and
  every trigger-listener MonoBehaviour deleted (close-out below)
- [ ] Layer 8 — UI, HUD, save / load, game flow
- [ ] Layer 9 — Remove the legacy model
- [ ] Layer 10 — Editors on configs

### Pick-up point for the next session (written 2026-11-14, after 7.8; layer 8 is next)

Layers 0–7 are done. **Compare scripts under `--fixed-dt 0.016667 --threads 1 --validate-access`** (default seed
implied). The established rules hold as written in layer 6's notes below, plus what layer 7 added:

- **Build the test project before running it.** `dotnet build RipNRage.sln` does *not* refresh
  `tests/RipNRage.GameChecks`' copy of the game assembly, so `dotnet run --no-build` after a solution build can
  test stale code — layer 7 read two false PASSes this way, one hiding a compile error. Run
  `dotnet build tests/RipNRage.GameChecks` explicitly (the gate scripts should do it). `CHECKS_TRACE=1` prints the
  stack of a non-assertion failure.
- **Seed by placement, never by entity index** (`AiBrains.Seed`): an index depends on how many entities exist
  before it, so every entity a later layer adds at load would reshuffle the AI (and a buffered entity's index is
  not final until playback).
- **Build level entities immediately when the loader must stamp them** (`LevelRef.Index`); `PhysicsEntities`
  builds through the command buffer, so its entity is not alive when `spawnEntity` returns. Tags that ride on such
  a collider go through the same buffer (`Surfaces.Tag`).
- **`--threads 1` pins the rigid solver, not the ECS**: parallel batches still run on workers. Determinism holds
  (command buffers), but the per-system profiler cannot see systems inside a parallel batch — use `--perf-csv`
  and the `ecs` total.
- A pixel gate needs its floor measured first: `look` is 0.000% same-build; `rage` and anything with muzzle
  particles ~1.1–1.3%.

What layer 8 inherits:

- **Death has two doors.** `FallRespawn` calls `PlayerState.Die()` directly, so a fall posts no `DamageEvent` and no
  `DeathEvent` reaches the ECS. Rage copes (empty health is its signal); anything new keyed on `DeathEvent` misses
  falls. Layer 8 owns death and game flow — route the fall through `HealthSystem`.
- **HUD**: the two rage meters are placeholder bars beside the portrait (`HudController.DrawRageMeters`); the
  portrait reads `Rage`; the rage grade is `RageScreen` (a drawer, no state). The style meter's HUD is gone.
- **Save format**: `PlayerSave.rage` holds Fury (name kept), `calm` is new, `combatState` unused; every saved level
  entity is keyed by `LevelRef` (monsters, spawners, pickups) — `LevelEntityRef` is deleted. (The `object cycle`
  save error was fixed in layer 4.)
- **Controls**: Q / E and D-pad left / right are the rage meters; quick swap moved to Tab; weapons 2 / 4 have no
  D-pad binding. `controls.json` is versioned (v2) with a one-time migration.
- `EcsObjects` (`Ecs/EcsObjects.cs`) is the last entity ↔ GameObject directory, used by the combat bridge.
- No melee damage path exists (the finisher deals none), so Fury has no melee bonus yet.
- **Open performance item**: on `approach`, the Tick-phase `ecs` total is +0.05–0.17 ms (0.19–0.23 → 0.24–0.40
  over 3 runs). Every new system that runs alone costs 1–2 µs a frame; the remainder sits outside per-system work —
  the parallel batch the profiler cannot see into, and four more batch boundaries (15 → 19 Tick batches, each a
  playback + sync). Under 1% of a 60 fps frame, inside the noise on `walk` / `fire`; a per-batch profile settles it.

The notes below were written for layer 6 and still hold where they are not about enemies.

What layer 6 inherits:

- Enemies already have their combat half: a `Health` entity with the legacy capsule (`AIState` creates it), `Hitbox`
  headshot judgement, death → `DeathEvent` → ragdoll. Layer 6 adds the body (`CharacterMotor`, profile Enemy),
  steering, and the state tree that drives `AIAgent`'s surviving mechanics (burst fire via `Projectiles.Launch`,
  vision via the still-uncalled `AgentVisionSensor` trigger).
- Enemy bullets fly as entities and will hurt the player the day the AI fires — `approach` / `peek` are the
  watch-gates; instrument player HP there before wiring the AI.
- Bridges layer 6 can shrink or delete: `AIState`'s per-frame pose sync (goes when the enemy body is the entity),
  `EnemyDestruction` / `EnemyHitFlash` listeners (can become `IConsumes<HitEvent>/<DeathEvent>` systems),
  `DamageInfo` (only the facade path still reports it), and `EcsObjects` entries for enemies.
- `killenemy` (killedAtFrame), `glockalt` (finisher refusal), `jab`, `rocket` (hp / armour / y) are the instrumented
  combat gates; keep them honest when AI changes enemy behaviour.
- The animation editor hosts `AnimatedSprite`s **without registering the game assembly's systems**; keep
  editor-facing pieces engine-side. Rockets do not collide with trigger volumes (water detonation = layer 7).
- Gates as in the layer 2/3 close-outs: compare bench interleaved against the previous commit (`--threads 1` for the
  stable numbers; the per-step metric only counts frames with a fixed step); ragdolls sit at 8.3–8.7 ms vs budget 8
  (knobs: pieces sleep less than boxes, and legacy-faithful CCD on every piece costs ~0.85 ms serially).
- Running anything on the Linux sandbox VM needs `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`: there is no `libicu` and
  the SDK fails to start without it. `build.sh` / `run.sh` / `bench.sh` do not set it. **On the dev Mac** the trap is
  a different one: `/usr/local/share/dotnet` holds only a 6.0 SDK, so a bare `dotnet build` fails with NETSDK1045 —
  `source tools/env.sh` first (it finds the 10.x SDK in `~/.dotnet`), or use the `./*.sh` scripts, which do.
- The scripted runs do **not** stop on their own: they need `--frames N` (the gate script used for 4.3a runs 400, or
  620 for `strafe` / `fall`), and on macOS there is no `timeout` binary to bound them with.
- Absolute bench numbers on the Mac are much slower than the VM figures quoted below (single-threaded crates ~11.7,
  ragdolls ~17 ms/step, both over budget **on both sides of the change**) — only the interleaved A/B is meaningful here.

### Resolved: asset handles (answered before layer 2)

Render components now hold `AssetHandle<T>` values (plain data) instead of `Sprite` / `Mesh` / `Material` /
`Texture2D` references; `EcsRenderProducer` resolves them through the `RenderAssets` registries when building the
frame. The per-component `MaterialPropertyBlock` was dropped (nothing used it; per-entity material overrides will
return as a dedicated component when something needs them). Verified pixel-identical `--ecs-demo` screenshots
before/after.

## Where things are

| What | Where |
|---|---|
| ECS core | `src/RipNRage.Engine/Ecs/` — `Core.cs` (IComponent, configs, Entity, Signature), `ComponentRegistry.cs`, `Storage.cs`, `World.cs`, `CommandBuffer.cs`, `Query.cs`, `Systems.cs` (scheduler + events), `Events.cs` (post-process), `Prefab.cs` |
| Transforms | `src/RipNRage.Engine/Ecs/Transforms/` |
| ECS rendering | `src/RipNRage.Engine/Ecs/Rendering/` (components, configs, `MeshLibrary`, `EcsRenderProducer`) |
| Renderer input | `src/RipNRage.Engine/Graphics/RenderData.cs` (`RenderFrame`, `SpriteDraw`, `MeshDraw`, `LightDraw`, `CameraView`, `IRenderProducer`) |
| Temporary bridge | `src/RipNRage.Engine/Graphics/LegacySceneProducer.cs` — delete in layer 9 |
| Prefabs | `Standalone/prefabs/` (`demo/` holds the layer-1 demo) |
| ECS physics | `src/RipNRage.Engine/Ecs/Physics/` (components, configs); collider table `src/RipNRage.Engine/Physics/ColliderTable.cs` |
| Camera rig | `src/RipNRage.Engine/Ecs/View/CameraRig.cs` (`CameraShake`, `ViewBob`, `CameraShakeSystem`, `ActiveCamera`); the player's rig and its GameObject bridge in `src/RipNRage.Game/Ecs/PlayerView.cs` |
| Tests | `tests/RipNRage.EngineChecks/` — `EcsChecks.cs`, `TransformChecks.cs`, `PhysicsEcsChecks.cs`, `Layer3Checks.cs`, `Layer4Checks.cs` (run in CI) |
| World ownership | `Application.World` (created in the constructor, `Update` after scene + physics, cleared in `ResetScene`) |

## Per-layer plan

Each layer ends with: solution builds, `EngineChecks` pass, a review stop. **Regression gates** (the game must not get
worse while the old and new models coexist):

- **Visual parity**: deterministic screenshots (`--script look --frames 30 --fixed-dt 0.016667 --no-vsync --width 640
  --height 360 --overlay 0`) against the previous commit, compared pixel-wise; the difference must be within
  run-to-run noise (measured at ~0.07–0.10% of pixels on the dev Mac). Do not use `--script fire` for this:
  particles make it nondeterministic (~9% noise).
- **Performance**: `frame … ms avg` with `--no-vsync --frames 600`, median of 3, no regression.
- **Behaviour**: the scripted automation runs (`--script walk|fire|weapons|rage|saveload|strafe`) still complete.

### Layer 2 — Physics (largest engine layer: ~8.3k lines in `src/RipNRage.Engine/Physics`)

Convert, keeping the tuned solver behaviour:

- Components: `RigidBody` (mass, inertia, velocity, damping, flags), collider shapes (`SphereCollider`,
  `CapsuleCollider`, `BoxCollider`, `ConvexCollider`, `MeshCollider` — shape data by handle/reference),
  `PhysicsMaterial`, `Joint` variants (ball, distance, spring, hinge, slider, fixed; limits, motors, break force),
  `CharacterBody` (capsule on a spring), `KinematicBody`, `Trigger`, `[Sparse] Sleeping`.
- System-owned state (not components): broadphase grid, contact manifolds + warm-start cache, islands, solver scratch.
  Key caches by `Entity` instead of object references.
- `PhysicsStepSystem` in **FixedTick**: sync `LocalToWorld` → bodies, step (keep the existing `JobSystem` usage inside
  the solver — the ECS has its own job system so they never nest), write poses back to `LocalTransform`.
- Queries API (`Raycast`, `Sweep`, `Overlap`) stays a service on the physics world, callable from systems.
- Contact / trigger events become `IPostProcessEvent`s (`CollisionEvent`, `TriggerEnterEvent`, `TriggerExitEvent`).
- **Gates specific to physics**: the physics benchmark (`./bench.sh --scenario all`) must match the current numbers
  (crates, ragdolls, projectiles, runners); `--script strafe` must not regress.
- Known: `--script strafe` **already fails on macOS** on the original `master` (`00448a2`) — the player walks off the
  dock edge near frame 100; CI runs it on Linux. Investigate as part of this layer (likely float / timing difference),
  but don't treat it as caused by the migration.
- **Coexistence constraint**: until layers 4–7 port the gameplay, legacy bodies (player, enemies, rockets, level
  colliders) must collide with ECS bodies. So there is **one simulation**, not two: the solver / broadphase move off
  `Collider` / `RigidBody` objects onto an internal body/collider table indexed by int; legacy behaviours and ECS
  entities both register into it (legacy as thin adapters, deleted in layer 9).
- **Steps** (one commit + review stop each):
  - [x] 2.0 Baselines recorded (below).
  - [x] 2.1 Physics components + configs + tests (data only, nothing simulates them). `Ecs/Physics/`
        (namespace `RipNRage.Engine.Ecs.Physics`): `Body`, `BodyVelocity`, `ColliderShape` (one component, `ShapeKind`
        enum; hull / mesh by handle via `PhysicsAssets`), `SurfaceMaterial`, `CollisionFilter`, `[Sparse] Trigger`,
        `[Sparse] Sleeping`, and runtime-only `PhysicsHandle` (the entity's `PhysicsProxy`; set by the 2.4 sync system,
        always bakes unregistered). Convex colliders bake from config: `vertices` (faces derived, ≤64 points) or
        `vertices` + `planes` (brush form, validated against each other); interned by content.
        Caveat: inside `RipNRage.Engine.Ecs.*`, `Physics.X` now resolves to this namespace — write
        `RipNRage.Engine.Physics.X` (or alias it) when ECS code needs the legacy facade.
  - [x] 2.2 Internal collider table (`Physics/ColliderTable.cs`): slots with generation-checked `PhysicsProxy`
        handles, owner = legacy `Collider` or ECS `Entity`; broadphase grids store slot indices. Gates: single-threaded
        bench equal or slightly faster (ragdolls median 34.8 vs 35.9 ms/step), strafe fails identically to baseline,
        look-script pixel diff 0.037%. Multi-threaded bench is too noisy here (ragdolls 6–16 ms) to gate on: use
        `--threads 1`, interleaved with a baseline worktree.
  - [x] 2.3 Solver on `SolverBody` (`Physics/SolverBody.cs`): all simulation state and maths moved out of
        `RigidBody`, which is now an adapter (public fields forward; implicit `RigidBody → SolverBody`). Manifolds,
        contact cache, islands, constraints and `PhysicsWorld.Bodies` hold `SolverBody`. Pose source is the owner (legacy
        transform, or `ecsPosition` / `ecsRotation` for ECS). Cache keys: body `id` (monotonic, never reused — keeps
        the old pair order) and collider slot `uid`. Collider slots link to their body (`Slot.body`,
        `ColliderTable.BodyOf`). Liveness / collider are snapshotted per step (`RefreshStepState`): reading the owner
        on every constraint iteration cost ~10% on ragdolls. Gates: bench at parity (ragdolls 34.6 vs 34.3 ms/step,
        `--threads 1`, interleaved, 5 runs), strafe identical, look 0.040%. **Left for 2.4**: narrow-phase candidates
        and `QueryFilter` are still `Collider`-typed; ECS colliders need the slot-typed path there.
  - [x] 2.4 ECS entities simulate (`Ecs/Physics/PhysicsSystems.cs`). `PhysicsSyncSystem` registers entities with
        `ColliderShape` / `Body` (slot + `SolverBody` + `PhysicsHandle`), treats external `LocalTransform` writes as
        teleports and external `BodyVelocity` writes as authoritative; `PhysicsStepSystem` steps the one simulation;
        `PhysicsWriteBackSystem` writes poses / velocities / the `Sleeping` tag back. **One fixed clock**: the scene's
        fixed loop calls `Scene.FixedPhysicsStep` (default = legacy `Physics.FixedStep`; the Application re-points it
        at `World.RunFixed`, and `World.DriveFixedInternally = false` disables the ECS's own accumulator) — lockstep
        by construction. The narrow phase + `QueryFilter` are slot-typed (ECS colliders collide; the old
        `Collider`-typed `QueryBoundsConcurrent` is gone); static ECS colliders bake world shapes (box / sphere /
        capsule / convex). Constraints: physics entities must be **root** entities; ECS **mesh colliders are not
        simulated yet** (2.6); ECS colliders are still invisible to legacy raycasts / sweeps / kinematic movers and
        CCD (public query API converts in 2.7); no per-frame interpolation for ECS bodies. `--ecs-demo` drops a
        4-crate ECS stack onto the dock's legacy geometry.
  - [x] **2.L Legacy physics removed** (ahead of 2.5–2.7, by request). Deleted: `Collider` and every subclass
        (`Box`/`Sphere`/`Capsule`/`Mesh`/`ConvexCollider`, `CharacterController`), `RigidBody`, `CharacterBody`,
        `KinematicBody`, every `Joint` behaviour, `NavMeshAgent`, `ITriggerListener`/`ITriggerStayListener`,
        `SpriteRagdoll`, the mover / kinematic / trigger passes in `PhysicsWorld`. Collider slots are ECS-only;
        their algorithms moved, not lost: per-shape raycasts → `PhysicsWorld.RaycastSlot`; sweeps / penetration /
        overlap on slots; mesh BVH + triangle hulls → `Physics/MeshShape.cs`; move-and-slide (`KinematicSolver`) on
        slots. Queries return `Entity` (`RaycastHit.entity`, `ShapeHit.entity`, `Contact.entity`,
        `MoveResult.ground`, `OverlapSphere` → `List<Entity>`); `QueryFilter.self` is a `PhysicsProxy`,
        `ignore` an `Entity`. New ECS replacements: static **mesh** colliders (`TriangleMesh` assets,
        `PhysicsAssets.AddMesh`), static box / convex hulls baked at registration, **`TriggerSystem`**
        (`TriggerEnterEvent` / `TriggerExitEvent`), opt-in **`CollisionEvent`**s (`[Sparse] ReportCollisions`),
        and `Ecs.Physics.PhysicsEntities` helpers (`StaticBox` / `StaticConvex` / `StaticMesh` / `Dynamic`).
        `CharacterProfile` (tunables) survives in `Physics/CharacterProfile.cs` for 2.6.
      - **Bug found and fixed**: since 2.4, ECS bodies never collided *with each other*. `ReadTransform` → `Touch`
        bucketed a body's slot before `InsertEcs`, which then skipped it, so it never joined the dynamic list.
        Slots now carry an `inserted` flag; a body-vs-body stacking test guards it.
      - **Game (inert until ported)**: level brushes, the dock mesh, sand, water, lamp posts, crates and
        `prop_physics` are ECS entities (dynamic props render through the ECS). Gameplay that drove deleted types
        was **deleted, not fenced** (in 2.5/2.6, by request; git history has the originals): player movement and
        fall/respawn (`FPSController`, `FallRespawn` keep only their public surface → rewrite in layer 4), enemy
        steering (`AIAgent`, BT nodes → layer 6), ragdolls (`EnemyDestruction`, skeleton-editor preview → layer 3),
        weapon move spread (→ layer 5), per-entity surfaces (→ layer 7). Inert: projectiles move by transform and
        hit nothing (layer 5); trigger callbacks (pickups, water, checkpoints, vision) are uncalled (layers 6–8);
        hit → `EntityState` lookups resolve to nothing because characters aren't entities yet (layers 4–6).
        `PhysicsPlayground` was deleted (recreate as ECS prefabs in 2.8).
      - **Gates changed**: `--script strafe` now **passes vacuously** (the player cannot move or fall), so it says
        nothing until layer 4.
  - [x] 2.5 **Joints** (`Ecs/Physics/Joints.cs`): one `Joint` component (`JointKind` Ball / Fixed / Hinge /
        Slider / Distance / Spring; `Connected` entity or the world; anchors in local space; limits / motor / spring;
        break force / torque) + `JointConfig`; `JointSystem` (FixedTick, after sync, before step) creates the solver
        constraint once both ends are registered, re-applies parameters every step (motors can be driven live),
        keeps motorised bodies awake, and on a break (or the connected body leaving) removes it, adds
        `[Sparse] JointBroken` and posts `JointBrokenEvent`. Level `phys_*` machines and `joint_*` connectors are
        back on ECS joints (`EntityFollower` keeps each prop's named GameObject on its entity for targetname
        lookups and rods; `SliderPatrol` component/system in the game). **Parity**: the dock's breakable shelf
        (`phys_fixed`, break 260 N) breaks on spawn with exactly the legacy numbers (666 N / 147 N·m — a crate
        lands on it from 2 cm), so that is level content, not a regression.
  - [x] 2.6 **Characters + movers** (`Ecs/Physics/Movers.cs`). `CharacterMotor` (the force-driven capsule on a
        ride spring; profiles Player / Enemy track tunables live) — gameplay writes intent (`Desired`,
        `PendingJump` / `PendingImpulse` / `PendingDash` / `PendingStopDash` / `PendingMinVerticalSpeed`), reads
        state (`IsGrounded`, `Ground` entity, `GroundDistance`, `GroundNormal`, `GroundVelocity`, `IsDashing`);
        `CharacterMotorSystem` configures the body (no rotation / sleep / friction, CCD) and steps it. Not ported:
        `SetHeight` (slide crouch) — add with layer 4. `KinematicMover` (swept move-and-slide, Slide / Bounce /
        Stop, gravity / drag / terminal speed, pushes bodies, `Ignore` entity, `CollisionEvent`s with
        `ReportCollisions`); runs in the **fixed** step (the legacy PerFrame mode is gone). A mover with a
        `ColliderShape` is re-baked as it moves and fires triggers (`TriggerSystem` now counts any non-static
        collider). **Bench**: all four scenarios back on ECS. ragdolls = 500 jointed figures (6 bodies, 5 ball
        joints each: 3000 bodies / 2500 joints, same load as the old sprite ragdolls; those return in layer 3);
        projectiles = `KinematicMover`s in Stop mode; ground = `CharacterMotor` runners again. Fixed a bench bug
        inherited from legacy: runners jumped in mid-air and drifted into the sky (now only from the ground).
        Baseline (`--threads 1`, 3 runs, ms/step avg): crates 0.9–2.5, ragdolls 30–54 (**FAIL**, as at the 2.0
        baseline: legacy single-threaded was 34–40), projectiles ≈0.01–0.04, ground 0.03–0.16. **Metric note**:
        "physics/step" averages per frame with `max(1, steps)`, so step-less frames count as 0 — at ~1000 fps
        (`--no-render`) it understates fixed-step work (now including movers); compare "ms/frame" too.
  - [x] 2.7 **Remaining physics features.** `CharacterMotor.PendingHeight` (slide crouch: the capsule resizes from
        the top, mass properties and centre of mass follow, character settings re-applied). Per-frame
        **interpolation**: `PhysicsInterpolationSystem` (LateTick, before `TransformSystem`) shows `Body.Interpolate`
        bodies blended by the new `World.FixedAlpha` (the Application copies the scene's value); the sync treats the
        blend as physics-written, not a teleport; characters interpolate by default. The old
        `SolverBody.InterpolateTransform` was deleted (it wrote the blend into the true pose). **Mesh colliders on
        bodies** simulate as the mesh's bounding box (legacy semantics; exact triangles stay static-only). Moving boxes
        cache their plane hull per pose (`HullOf` no longer allocates per query).
  - [x] 2.8 Demo prefabs, ragdoll performance pass, gate run — see **Layer 2 close-out** below.
- **Baselines (2.0, `37794e8`, Linux/WSL, 16 threads, `--no-render`)** — physics ms/step avg:
  crates 1.19 (PASS), ragdolls 11.21 (**FAIL at baseline**: 25.4 ms frame, over budget — pre-existing on this
  machine), projectiles 0.48 (PASS), ground 0.15 (PASS). `--script strafe --frames 620` **also fails at baseline here**
  (falls off the dock edge at (-18.9, 1.2, 6.7), same symptom as macOS). Gate = no worse than these.
- Ordering note: the ECS currently updates **after** the legacy scene and `Physics.Step()`. When physics moves into
  `FixedTick`, the ECS fixed step and the legacy `FixedUpdate` must stay in lockstep (same `fixedDeltaTime`, same step
  count per frame) until gameplay is ported.

#### Layer 2 close-out (2.8)

- **Demo**: `--ecs-demo` builds the joint playground from prefabs (`prefabs/demo/ecs_demo_{prop,door,pendulum,bungee,
  seesaw,windmill,piston,shelf,link}.json`; base prefab + inheritance, sizes by `LocalTransform.scale`, anchors scaled
  with it). The chain (ball joints to the link above) and the piston's `SliderPatrol` are wired in `EcsDemo` because
  prefabs cannot reference other entities yet (layer 10).
- **Ragdoll / solver performance pass** (each step measured interleaved against `0d84805`):
  1. **Rolling resistance** (`RigidSolver.rollingDamping`, 3/s on spheres / capsules in contact): the contact model
     had no rolling friction, so limbs rolled forever and no ragdoll island ever slept (3000 / 3000 awake for 15 s).
     Now 1500–2000 of 3000 sleep between blasts. Bench figures also got the legacy SpriteRagdoll tuning (friction
     0.7, damping 0.1 / 0.6).
  2. **Exact box fast paths**: analytic sphere-vs-oriented-box closest point (`ConvexShape.isBox`) and capsule-over-
     a-box-face contacts without GJK. Randomised parity test vs the generic path and a brute-force reference: 0
     wrong; GJK itself is off in 8/4000 deep overlaps, where the fast path is the accurate one. Narrow phase ~13 → 4 ms.
  3. **Threading** — the multithreaded solver ran 3–4× *slower* than single-threaded: (a) every stage's job counters
     shared one cache line (now one line per stage in `JobSystem`); (b) a shared interlocked counter per broadphase
     query and per contact point (now per worker, summed after the stage); (c) small islands were claimed one at a
     time in build order (now longest-first, small batches); (d) **two worker pools** — the ECS world's 23 + the
     solver's 16 spinning threads on 24 cores, so workers holding chunks were descheduled and stalled every barrier.
     One process-wide pool now (`JobSystem.DefaultThreads`, shared by `World` and `RigidSolver`; explicit
     `new World(n)` keeps a private pool). Measured concurrency went from 2–3 active workers of 8 to all of them.
  - **Result, default threads (3 runs)**: ragdolls 44.5–53.0 → 4.9–5.6 ms/step (frame 222–267 → 0.6–0.7 ms; it no
    longer spirals), crates 27.9–35.1 → 5.3–5.7 ms/step. Single-threaded: ragdolls 29–35.5 → 10.3–10.9, crates
    13–16 → 12.2–12.8. Above ~4 threads there is no further gain on this VM (memory-bound).
  - **Bench metric fixed**: physics/step now averages only frames that ran a fixed step (it used to average in the
    step-less ones, which at ~1000 fps reported a fraction of the real cost). Numbers before this layer's close-out
    are not comparable — crates had always been over budget; the old metric hid it.
- **Gate run** (default threads, `--no-render`): crates 5.8–6.1 PASS, projectiles 0.33–0.36 PASS, ground 1.55–1.58
  PASS; ragdolls 3.9 / 4.3 / 4.6 / 5.8 / 7.6 / 8.4 ms across six runs (budget 8: one run over — WSL noise). Game
  scripts walk / fire / weapons / rage / saveload complete without errors; look screenshot vs `0d84805` 0.035–0.081%
  differing pixels against a same-build floor of 0.059–0.062%; 123 engine checks.
- **Strafe**: still passes vacuously — the player has no movement until layer 4, so the macOS / WSL edge-fall
  investigation moves to layer 4 (it needs the player on `CharacterMotor`).
- **Known gaps carried forward**: ragdolls from sliced sprites (layer 3); projectiles / pickups / triggers / hit
  resolution in gameplay (layers 5–8); prefab entity references (layer 10); GJK's deep-overlap approximation for
  capsules vs non-box polytopes (pre-existing).

### Layer 3 — Animation, audio, particles, destruction ✅

- **Audio** (`Ecs/Audio/AudioSystems.cs`): `PlaySoundEvent` (fire-and-forget, listener-relative) consumed by
  `AudioSystem` (pool of 32 OpenAL voices, oldest finished reused, dropped when all busy); `Sfx.Play(clip, volume)`
  posts one from scripts; `AudioEmitter` component = a persistent / looping voice owned by an entity (released with
  the component or entity). Clips by handle (`AudioAssets`). The legacy `AudioSource` behaviour was **deleted**: every
  game use was `PlayOneShot`, all 25 call sites now go through `Sfx.Play`, so sound still works everywhere.
- **Animation** (`Ecs/Animation/AnimationSystems.cs`): `SpriteAnimator` (controller handle, state index, time, speed;
  parameters in a fixed 8-slot block indexed by the controller's ordinal-sorted parameter names, triggers as a bit
  mask; `SetBool / SetFloat / SetTrigger / Play` on the component by ref) + `AnimationSystem` (Tick): the legacy
  Animator's semantics (any-state and state transitions, exit times, trigger consumption, loop-tail events, hot
  reload by state name). Writes `SpriteRender.Sprite`, applies clip "enabled" keys to the new
  `SpriteRender.Disabled` (separate from `Hidden`, the owner's switch), posts clip events as `AnimationEvent`s; hidden
  animators pause. Speed 0 pauses. The legacy `Animator` behaviour was **deleted**.
  - **Bridge** (`RipNRage.Game/Ecs/ViewSprite.cs`, delete in layer 4): the first-person weapon, fists, muzzle flash
    and the rocket explosion are GameObject-anchored ECS sprites: `ViewSprite` owns the entity, copies the
    GameObject's world pose each frame, maps activation to `Hidden`, exposes the Animator calls the weapon scripts
    use, and forwards `AnimationEvent`s as SendMessage (`ViewSpriteEventSystem`). The viewmodel therefore still
    animates and fires. The animation editor's controller preview runs through it too.
- **Particles** (`Ecs/Effects/EffectSystems.cs`): `ParticleWorld` keeps the simulation; effects can now follow an
  entity (`ParticleEffectInstance.Follow(Entity)`, its `LocalToWorld`); `SpawnEffectEvent` / `Vfx.Spawn(...)` spawn
  through the ECS; `EffectEmitter` ties an effect to an entity's lifetime. Transform-following stays until GameObjects
  go (layer 9); game VFX calls (`GameVfx`) are ported with their gameplay (layers 5–7).
- **Sprite ragdolls** (`Ecs/Destruction/Ragdolls.cs`): `SpriteRagdolls.Spawn` slices a sprite along a skeleton view into
  piece entities (cut-sprite `SpriteRender`, `Body`, box / capsule `ColliderShape`, `CollisionFilter` with one negative
  group per ragdoll, ball `Joint` to the parent piece at its pivot); the blow / gib scatter are `PendingImpulse`s
  applied once the bodies are simulated; `RagdollSystem` billboards and rolls pieces, fades them over their last
  second and removes them; `Detach` tears a piece off; breaks and detaches post `RagdollPieceDetachedEvent`. Supporting
  joint additions: `Joint.UseWorldAnchor` / `WorldAnchor`, `Joint.BreakImpulse`, and `JointSystem` now drops the
  constraint when the `Joint` component is removed. Enemy death ragdolls (`EnemyDestruction`) and the skeleton
  editor's preview (tear-off clicks via raycast → entity) are back. `RagdollSystem.Viewer` points at the legacy camera
  until layer 4.
- **Bench**: ragdolls are real sliced rifleman sprites again (500 → 3000 pieces); the jointed figures from 2.6 are gone.
  Default threads: 8.3–8.7 ms/step (budget 8). Not comparable to the figures' 5 ms: different shapes, fewer sleep, and
  CCD on (as the legacy ragdolls had) costs ~0.85 ms serially. Crates at parity with `69da737` (`--threads 1`, 5
  interleaved runs: medians 13.6 vs 13.7). `--screenshot` / `--shot-interval` added to the bench for visual checks.
- 14 new engine checks (137 total). Game scripts, `--ecs-demo` and the bench run without errors.

### Layer 4 — Input, camera, player (PlayerSystems ~2.2k lines)

- **Steps** (one commit + review stop each):
  - [x] 4.1 **Input singleton** (`Ecs/Input/InputComponents.cs`): `InputState` written by `EcsInputSystem` (Tick) —
        one snapshot of the "gameplay" action map that every system reads, so nobody polls devices mid-frame.
        `InputOverride` carries the scripted-automation overrides (and a hard `Blocked` gate that replaces
        `PlayerInputActions.SetIsBlockingInput`, which could only silence the mirror, not the systems).
        **Jump is latched** (`JumpQueued` / `ConsumeJump`): rendering outruns the fixed clock, so an edge that only
        lived for its own frame would be dropped most of the time — this is the old `IsJumping` / `CanJump` pair.
        Needed **ECS singletons**: `World.Singleton<T>()` (by reference), `GetSingleton<T>()` (non-creating snapshot),
        `SingletonEntity<T>()`. Creating a carrier is structural, so a writer system claims its singleton in
        `OnCreate`; `DestroyAllEntities` forgets them.
  - [x] 4.2 **The player is an ECS character** (`RipNRage.Game/Ecs/PlayerCharacter.cs`, `PlayerLink.cs`).
        `PlayerMotor` (the locomotion tunables + slide / wall-run / dash state) and `PlayerLook` (yaw / pitch / roll)
        on the capsule entity (0.4 × 1.3, 80 kg, ride 0.5) with `CharacterMotor` profile Player.
        `PlayerMoveSystem` (**FixedTick**, before `CharacterMotorSystem`) ports the deleted `FPSController`
        locomotion — smoothing, bunny-hop speed, rage / style multipliers, slide, wall run, dash, footsteps — and
        writes it as motor intent; `PlayerLookSystem` (Tick) integrates the look. `FPSController`,
        `FPSCameraController` and `PlayerInputActions` are now **thin facades** over those components for the scripts
        layers 5–8 still own; `PlayerLink` copies the entity's pose back onto the GameObject hierarchy each frame and
        aims the camera pivot / Orientation child. `MovementSettings.ApplyTo` writes the component, live as before.
        **`FallRespawn` is restored** on the new body (`Teleport`, the footprint probes, EDGE vs THROUGH GEOMETRY),
        and the overlay's MOVER line is back.
  - 4.3 **Camera and viewmodel as entities**, split in two (by request):
    - [x] 4.3a **The camera is an entity.** `Ecs/View/CameraRig.cs` (engine): `CameraShake` (the weapon kick +
          the trauma shake, numbers from the deleted `CameraRecoil` behaviour) and `ViewBob` (the head bob) applied
          by `CameraShakeSystem` in **LateTick, `[UpdateBefore(typeof(TransformSystem))]`** — so the renderer sees the
          shaken pose and nothing simulates against it. Both own the local pose of the node they sit on, so they go on
          **different entities**: `PlayerLink` now builds the prefab's view chain as entities parented to the character
          — character → pivot (`ViewBob`, the look angles) → camera (`CameraLens` fov 77, `CameraShake`) — and
          `PlayerViewSystem` (game) drives it from `PlayerLook` / `PlayerMotor` / `CharacterMotor`. The `HeadBob`
          behaviour is **deleted**; `CameraRecoil` is a facade over the component (`PlayerCamera.Shake`), as
          `FPSCameraController` is over `PlayerLook`. `RagdollSystem.Viewer` reads the active ECS camera
          (`ActiveCamera`, the same highest-priority choice `EcsRenderProducer` makes) and falls back to `Camera.main`
          for the editors' preview rigs. `PlayerDeathSequence` takes the pivot over through
          `PlayerViewRig.External` and animates the entity. The GameObject "Camera" / "WorldCamera" nodes now
          **follow** their entities (`PlayerViewFollowSystem`, at the end of the ECS frame) for the unported scripts;
          the legacy `Camera` stays only for `Camera.main` readers. Layer 9 deletes both.
      - **Engine bug found and fixed** (pre-existing since 4.1, A/B-confirmed on `649f6a1`): a level restart
        (`DestroyAllEntities`) dropped the singleton claims, and a writer system claims its singleton in `OnCreate`,
        which does not run again — so the next read came from inside `EcsInputSystem.Tick` and creating the carrier
        there is a structural change. `--script saveload` died on the reload after a checkpoint load
        (`structural change while systems run`). Carriers are now re-created immediately, values back at default.
      - **Gates**: builds; 187 engine checks (was 151 — 34 for the rig, 2 for the singleton fix); `walk` / `fire` /
        `weapons` / `rage` / `saveload` / `slide` / `dash` / `fall` / `strafe` all complete with no errors (the
        baseline fails `saveload`, see above). **The `look` shot differs by 9.2% of pixels against a 0.057% floor,
        and that is the point of the step**: the camera *position* is identical to four decimals (probed:
        `(0.0000, 2.3057, 8.0000)` in both), but the yaw is 185.800° against the legacy 185.600° — exactly one frame
        of the script's turn. The ECS camera renders the look angles produced in the same frame, where the GameObject
        camera was written in the scene phase and therefore lagged by one; at 77° fov that 0.2° is ~3 px of
        horizontal smear across the plank texture in the lower half of the frame (the flat sky is untouched, which is
        how we know it is not a pose or projection change). One frame less view latency, not a regression.
    - [x] 4.3b **The viewmodel is entities.** The `ViewSprite` bridge is **deleted** (`ViewSprite`,
          `ViewSpriteEventSystem`, 4.3a's `ViewSpritePoseSystem`, the `WeaponSway` behaviour). In its place:
          - `Ecs/Animation/AnimatedSprite.cs` (engine): a handle on a sprite entity (`SpriteRender` + optional
            `SpriteAnimator`, `Parent`-able) wrapping the component access behind the legacy Animator vocabulary
            (controller, `SetBool` / `SetTrigger` / `Play`, `speed`, the editor's read-back, `SetActive` ↔ `Hidden`).
            Clip events reach its `OnEvent` through `AnimationEventRouter` — an **engine** system on purpose: the
            animation editor hosts these without the game assembly's systems, and its event flashes work again
            (`ViewSpriteEventSystem` never ran there, so they were silently dead).
          - `RipNRage.Game/Ecs/ViewModel.cs`: the viewmodel as entities under the camera entity — arms
            (`WeaponSway` component, prefab numbers; `WeaponSwaySystem`, LateTick before `TransformSystem`,
            grounded from the player's `CharacterMotor`) → weapon / fists (`RenderLayer.FirstPerson`), muzzle flash a
            child of the weapon sprite, hidden until the clip events show it. No per-frame GameObject→entity pose
            copy remains: the chain hangs off the camera entity itself.
          - The weapon scripts keep their shape: fields became `AnimatedSprite`, `.gameObject.SetActive` → `.SetActive`,
            `.transform.localPosition/Scale` → `.LocalPosition/.LocalScale`. The weapon's clip events still arrive as
            `SendMessage` on the "Weapon" GameObject (wired `vm.Weapon.OnEvent` → `SendMessage`), so
            `WeaponAnimationEvents` is unchanged but for its muzzle-flash field. The GO nodes "PlayerArms" / "Weapon" /
            "ProjectileSpawn" survive only as script anchors; `CombatController` re-applies the weapon data's
            position / scale to the GO on equip so the rocket's spawn point stays where it was. The GO "MuzzleFlash" /
            "LeftFist" / "RightFist" nodes are gone.
          - The rocket explosion (the one world-space `ViewSprite`) is an `AnimatedSprite` placed once by
            `EntitySpriteAnchor` (it never moves) — replaced end to end in layer 5 with the projectiles. Its `FadeOut`
            was dropped: it only ever faded a `SpriteRenderer`, which the explosion did not have (a silent no-op).
          - The animation editor's preview is an `AnimatedSprite` whose pose follows the preview node each frame in
            the editor's own Update.
          - **Bug found in review and fixed** (`AnimatedSprite`, same commit as the follow-up): the handle created its
            entity through the **command buffer**, which only *reserves* it — `World.IsAlive` stays false until the
            next sync point — while every accessor on the handle checks `Alive` first. So the viewmodel build's
            `MuzzleFlash.SetActive(false)` on the line after construction was silently dropped and the muzzle flash
            stayed lit over the gun for the whole game, which reads exactly like "the weapon is stuck on the firing
            frame"; `Weapon.Equip`'s pose write had the same hole (harmless for the glock, whose offset is zero, but
            not for the other three). The old `ViewSprite` never hit this because it added `Hidden` through the same
            command buffer at creation instead of through a checked setter. Fixed by creating the entity
            **immediately** when the caller is not inside a system (prefab building, the editors, a projectile's hit
            effect) and falling back to the buffer only while systems run; `ViewModel.Build` creates the arms entity
            the same way. Verified by probe: the weapon animator runs `Idle`→`Glock01`, `Fire`→`Glock01`/`Glock02`/
            `Glock01`, back to `Idle`, with `IsFiring` toggling per shot, and the flash hidden unless a clip event
            shows it. Note the pre-4.3b builds drew **no** viewmodel at all (the bridge's `Hidden` was never lifted),
            so this area had no working baseline to compare against.
          - **Gates (light, by request)**: builds; 196 engine checks; `fire` / `weapons` / `saveload` complete with no
            errors; a `fire` screenshot shows the weapon, hands and muzzle flash rendering from the entities; the
            animation editor starts clean.
- **Two engine bugs found by putting a real body back under the player** (both pre-existing since 2.7, invisible
  while nothing moved between fixed steps):
  1. `SolverBody.EndStep` only wrote the owner pose for **non**-interpolated bodies, so an interpolated body's
     `LocalTransform` — read by gameplay between steps — was stuck at its spawn point while the body flew away.
     `EndStep` now always publishes the pose; interpolation stays a separate overlay, never the truth.
  2. `PhysicsInterpolationSystem` overwrote any teleport written between fixed steps **and** updated `LastPos`, so
     the sync never saw it either and the body snapped back. It now leaves an externally moved transform alone.
  Guarded by new checks in `Layer4Checks.cs` (14 assertions, 151 total).
- **Strafe: solved, and it was never a physics bug.** `--script strafe` steers back towards x = 0 with a *world-space*
  correction but feeds it as *player-relative* move input. The player spawns facing −z (`info_player_start` angle
  180), where a raw `(+1, …)` means "strafe towards −x", so the correction pushed the player further off centre every
  frame and walked it straight off the side of the dock at x ≈ −19 — exactly the "(-18.9, 1.2, 6.7)" of the 2.0
  baseline, on macOS and WSL alike, because it is deterministic and not a float / timing difference at all. The
  script now rotates its sideways correction into the view's frame (the forward/back wiggle stays player-relative)
  and **strafe passes for real**: 600 frames on the planks at y = 1.81, 0 falls. `--script fall` still walks off the
  edge on purpose and is classified EDGE at x ≈ −18.6, which is the same dock edge seen from the other side.
- Fixed on the way: the save failed with `A possible object cycle was detected` as soon as checkpoints fired again —
  `Vector3.normalized` / `magnitude` (and the `Vector2` / `Quaternion` equivalents) are derived, and a serializer
  walked `normalized.normalized…` for ever. They are `[JsonIgnore]` now. (Was listed under layer 8.)
- **Gates run**: solution builds, 151 engine checks pass, `walk` / `fire` / `weapons` / `rage` / `saveload` /
  `slide` / `dash` / `fall` / `strafe` complete with no errors. Bench (default threads, `--no-render`): crates 3.52,
  ragdolls 6.23, projectiles 0.31, ground 1.80 ms/step — all PASS, all inside the layer-2/3 close-out ranges.
  **No pixel gate this layer**: the player now falls and stands on the planks instead of hovering at the spawn point,
  so the camera sits ~0.3 m lower than in a build where it could not move; the `look` shot differs by ~89% of pixels
  by construction. Checked by eye that the framing is otherwise the same scene (and a yaw bug was caught that way —
  the spawn rotation was being applied twice, once on the root transform and once in `PlayerLook`; `PlayerLink` now
  clears the root, which owns no rotation any more).

### Layer 5 — Weapons, combat, damage (1,216 lines: `Scripts/Weapons/*`, `CombatController`, `EntityState`, `Inventory`, `DamageInfo`, `WeaponData` / `WeaponStats`)

- `Inventory` / `Ammo` components, `Weapon` (one component + a `WeaponKind` enum, the `ColliderShape` / `Joint`
  precedent; data from a weapon config), `Spread`, fist jab combo state.
- `DamageEvent`, `DeathEvent`, `HitEvent` (IPostProcessEvent); `HealthSystem` consumes damage. `DamageInfo`'s static
  hand-off folds into the `DamageEvent` payload (point / direction / impulse / explosive / radius).
- Hitscan via physics queries, projectiles as entities (rocket = `KinematicMover` in Stop mode + `ReportCollisions`,
  detonating on its first `CollisionEvent`).
- **Health and hitboxes come first and whole** (agreed): every damageable — player, enemies, props — carries `Health`
  before any weapon is ported, rather than a bridge that layer 6 would throw away.
- Fire rate / refire run on **scaled** `Time.deltaTime` (so the death slow-motion still slows the gun, as the
  `WaitForSeconds` coroutines it replaces did), as timers on components — no coroutines in the fire path.
- Gameplay assertions live in **`tests/RipNRage.GameChecks`** (added in 5.0): `EngineChecks` only references the
  engine, so game-side components cannot be reached from it. Both run in CI.

#### 5.1 — Health and hitboxes, whole (done)

- `Ecs/Combat/Combat.cs` (game): `Health` (current / max, armour / max, resistance), `[Sparse] Dead`, `Hitbox`
  (owner + the 2.25 m / 76% headshot judgement, runtime-only like `PhysicsHandle`), `DamageEvent` / `HitEvent` /
  `DeathEvent`, and `HealthSystem` — the one place damage is applied: the pinned armour arithmetic, clamped, death
  **latched** with `Dead` (deliberate change: the legacy base called `Die()` for every further lethal hit and left the
  guard to subclasses) and reported once even for two lethal blows in one frame.
- **Everything damageable carries `Health` now** (no bridge, by request): the player's sits on the character entity
  (`PlayerLink`, resistance 1); each enemy's sits on an entity `AIState` creates with the **restored pre-2.L capsule**
  (r 0.34, h 1.9, centred half the ride height down, `Layers.Enemies`) — so rays and projectiles stop at enemies again
  and `RaycastHit.entity` names them. Armour still initialises from `maxHealth`, faithfully porting what is almost
  certainly a legacy typo (`RiflemanData.maxArmor = 0`, yet the grunt soaks 1000 armour and the killenemy script
  budgets for it) — **decide after 5.3** whether to fix the data.
- `EntityState` is a facade over the component (`MIGRATION FACADE`): `TakeDamage` posts a `DamageEvent` (folding the
  static `DamageInfo` hand-off into the payload), `HealthState` derives from `Dead`; `CombatBridgeSystem` delivers
  `HitEvent` / `DeathEvent` back to the GameObject listeners (HUD bars, hit flash, `Die()`), re-reporting `DamageInfo`
  around `Die()` for the ragdoll slicer. `PlayerLink.Attach` creates the whole player rig **immediately** now (the
  AnimatedSprite doctrine), so `PlayerState.Init` can write the component in the same scene update.
- **Found by the `die` gate**: `PlayerState.Die` began with `if (IsDead) return;` — correct when `Die` ran before the
  state flipped, dead wrong once the pipeline applies `Dead` and zero health *before* the bridge calls it: the death
  sequence never started. It latches on its own flag now, reset by `Init`.
- Changed semantics, documented in the facade header: damage lands at end of frame (same-frame readers see the old
  value — the weapons' synchronous "did this kill" checks are rewritten on `HitEvent` in 5.3; until then rocket
  style-kills do not register), and death latches.
- Gates: 196 engine + 35 gameplay assertions (the damage model re-pinned through the pipeline, the hitbox recipe hit
  by real rays — including that the capsule tops out at 1.9 m so the top of the sprite is unhittable, as pre-2.L);
  all 13 combat scripts complete; `die` shows the full sequence (timeScale 0.2, death screen) at parity with 5.0,
  twice per run as before. `killenemy` still reports ALIVE 1000/1000 — the weapons' stubbed hit resolution is 5.3.

#### 5.4a — Armour is its own component (done)

- `Armor { Current, Max, Resistance }` split out of `Health`. **Absent = wears none**, which is how the data reads:
  `RiflemanData.maxArmor` is 0, so a grunt has no `Armor` component and its 1000 hit points are the whole of it.
  `AIState.Init` now passes `data.maxArmor` instead of the legacy `data.maxHealth` — the typo carried faithfully
  through 5.1, corrected here by request, and **measured**: `killenemy`'s new `killedAtFrame` probe reports 97 with
  the fix against 142 with the typo (37 vs 82 frames of Glock fire, 0.62 s vs 1.37 s — 1000 vs 2000 damage to kill).
- The player's armour is a component with `Resistance = 1` (a hit lands wholly on it), so `DamageEvent`'s
  resistance override and `PlayerState`'s whole `TakeDamage` override are gone — the override existed only to pass
  that 1 and to guard `IsDead`, which `HealthSystem` already does by dropping events for `Dead` targets.
  `EntityState.TakeDamage(float)` lost its second parameter; `InitializeArmor(max, resistance)` adds, re-fills or
  removes the component.
- `WeaponData.spawnableVFX` deleted: dead since the hitscan classes went in 5.3.
- Gates: 196 engine + 43 gameplay assertions (the damage pins re-expressed as wears-armour / wears-none, including
  "a thing with no armour carries no Armor component"); all 13 combat scripts + `die` at parity.

#### 5.4b — Projectiles are entities (done)

- `Projectile.cs` / `RocketProjectile.cs` and the `ProjectileRocket` / `ProjectileStandardBullet` prefab GameObjects
  are **deleted**, with the whole no-VFX fallback branch that turned out to be dead in practice (`RocketExplosion`,
  `BulletImpact`, `EntitySpriteAnchor`, `FadeOut`, `FlashLight`, `ProjectileStats.hitVFX`, `GameVfx.Trail`): with the
  VFX library present — always, in this project — `GameVfx.Explosion` particles were the entire explosion visual and
  the animated-sprite prefab never spawned.
- A projectile is one entity: `KinematicMover` (Stop) + sphere `ColliderShape` + `ReportCollisions` for flight that
  cannot tunnel, `SpriteRender` + `LightSource` for the glowing slug (no child "Visual" GameObject), `EffectEmitter`
  for the trail (layer 3 machinery, zero new code), the new engine `Lifetime` for range expiry, and
  `Projectile { Kind, Instigator }` slotting into `Projectiles.All` — shared immutable defs, the 5.3 weapon pattern.
  The per-class `ShouldCollide` rules became data: the enemy bullet's `CollisionMask` excludes `Enemies`.
- `Lifetime { Left }` + `LifetimeSystem` (engine, ~20 lines): approved as the one new primitive, and smaller than
  planned — the fade variants had no live user once the fallback prefabs died.
- `ProjectileSystem : IConsumes<CollisionEvent>`: bullets post a `DamageEvent` on whatever they ran into; a detonating
  kind blasts — damage and rage over **one cached overlap query** (the legacy path called
  `Scene.FindObjectsOfType<EntityState>()` per rocket, the named 5.0 offender), impulses via the allocation-free
  `AddExplosionForce`, trauma via `CameraShake.ShakeAt`, and the rocket jump written as `CharacterMotor.PendingImpulse`
  (capped 40, dash-cancelling) instead of the `FPSController.AddImpulse` facade hop.
- Launching is one pure function used by `WeaponFireSystem` and `AIAgent.SpawnProjectile` (one line; the behaviour
  tree itself is untouched — it is to be replaced by data-oriented state trees later, per direction). Rockets no
  longer collide with trigger volumes (water detonation returns with the water rework); enemy bullets will hurt the
  player again the day the AI fires — `approach` / `peek` stay the watch-gates for that.
- **Verified in numbers, not vibes**: `killenemy` unchanged (killedAtFrame 97); the new `rocket` gate line reads
  `hp 80 armour 0 y 3.82` — the blast's 120 self-damage eaten 100 by the player's starting armour and 20 by health
  (exactly the legacy arithmetic; "rocket jumping is not suicide" was always armour-funded) with the player airborne
  at y 3.82 from the motor impulse, fireball + scorch + burning launcher on screenshots; 199 engine + 50 gameplay
  assertions (bullet flight → hit → damage, range expiry, blast kill, 12% self-damage, Lifetime + child teardown);
  alloc/frame 8.3–9.9 KB vs the 5.0 baseline's 8.2–9.9; bench projectiles 0.14 ms/step (baseline 0.18).

#### 5.6 — Layer close-out (done)

- Full sweep green: 199 engine + 71 gameplay assertions; all 13 combat scripts + `die`, with every instrumented gate
  at its expected value (`killenemy` killedAtFrame 97, `rocket` hp 80 armour 0 y 3.82, `jab` step 1→2→0, `glockalt`
  step 0 / finisher none / ammo 120).
- Bench (`--threads 1`, single runs, vs the 5.0 baseline on this machine): crates 12.17 / 11.84, ragdolls
  19.00 / 16.77, projectiles 0.26 / 0.18, ground 1.22 / 0.81 — all inside the session's observed run-to-run band
  (ragdolls 16.8–19.0, ground 0.72–1.22 across the layer); the Benchmark binary does not load the game assembly, so
  the layer cannot affect it structurally.
- Allocations steady-state: fire 7.8–10.3, m1887 / rocket / jab ≈ 8.3, walk ≈ 8.1–8.3 KB/frame, `gc 0.0/s`
  throughout — at or below the 5.0 baseline, with the per-shot / per-jab coroutine allocations structurally gone.
- Layer totals (`649f6a1..`): the combat OOP is deleted — 7 weapon classes, 2 projectile classes, `Inventory`,
  `CameraRecoil`, `EntityState`'s mutators, 8 marker/dead files, the no-VFX fallback branch, five coroutines — and
  combat is: 4 data tables' worth of definitions, 10 components, 8 systems, ~30 pure functions, 71 assertions.

#### 5.5 — Alt-fire finishers as entities (done)

- `Melee` grew into **`AltFire`** — the combo component (press 1 and 2 jab, press 3 is the weapon's finisher) — and
  the finisher in progress is itself an **entity, parented to the player** (it is the player's move; the hierarchy
  tears it down with them, and `DisableCombat` destroys it on death). The entity carries exactly one per-weapon
  finisher component, declared by `WeaponData.finisher`, each driven by its own system, from the moves the deleted
  Weapon subclasses carried (recovered from git history):
  - `GrappleFinisher` (Glock): snap the view to the grabbed victim's chest (the old grapplePoint height), the left
    fist's Grapple hold, the gun out at 0.25 s, two point-blank shots, release a beat after the second.
  - `PointBlankFinisher` (M1887): the swing telegraphs 0.5 s, then one shell at arm's length.
  - `BashFinisher` (MG338, and now the rocket launcher too — its legacy Melee was an empty stub, and every weapon
    gets a finisher per direction): the strike sound (`WeaponData.meleeSound`) and the swing, 0.2 s.
- **No cross-system calls**: a finisher never fires the gun — it asks, through `Weapon.ForcedShots`, and
  `WeaponFireSystem` fires those shots through the normal path (ammo, refire, recoil, hitscan), holstered or not.
  Running dry cancels the request. Known simplification: forced shots use the weapon's normal spread (the legacy
  M1887 melee shot was zero-spread; at arm's length the difference is centimetres).
- A grapple with nobody within the 1 m reach spawns nothing and the combo resets — the legacy rule — and the
  `glockalt` gate pins exactly that (no enemy is spawned, per direction): `step 0 finisher none ammo 120`. The
  grapple itself is covered in vitro: finisher entity parented to the player, two rounds through the normal fire
  path landing on the grabbed victim, the view snapped onto it, combo reset, entity gone.
- Gates: 199 engine + 71 gameplay assertions; all 13 combat scripts + `die` at parity, `jab` / `rocket` /
  `killenemy` probes unchanged.

#### 5.4c — The fist combo is a component, and CameraRecoil is gone (done)

- `Melee { Step, JabTimer, ComboTimer, Requested, LeftFist, RightFist }` + `MeleeSystem` (Tick after input, runs alone
  for its 1 m probe) replace `CombatController`'s **three coroutines** and its `_canJab` / `_comboStepCounter` pair:
  two jabs and a finisher on scaled timers, the gun holstered and hidden while the fists are out, the combo
  forgetting itself after 0.75 s. The system reads `InputState.AltFirePressed` itself, so `PlayerInputActions` no
  longer forwards alt-fire; `CombatController.OnAltFire` survives only as the scripted-automation press
  (`Melee.Requested`), and `OnEndMeleeCombo` / `Jab` / the `jabLandSound` + `layerMask` fields are deleted.
- **`CameraRecoil` is deleted outright** (the facade, its `EnragedInjector` registration and the prefab component):
  after 5.3 took `ApplyRecoil` and 5.4b took `ShakeEverywhere`, only three callers remained — the jab, the player's
  damage shake and the dash trauma — and all three now write the camera entity's `CameraShake` through
  `PlayerCamera.Kick()` / `.Shake.AddTrauma()` / `.ClearShake()`. The kick bounds the prefab used (0.5, 0.5, 1.5)
  live in `PlayerCamera` as the one piece of data they always were.
- Gates: 199 engine + 57 gameplay assertions (one step per press however often it is set inside a frame, the gun
  away and back, the 0.75 s reset); **the `jab` script is instrumented** like the others and reports
  `combo step 1 holstered True` → `step 2` → `step 0 holstered False`, with the swinging fist and absent gun on a
  screenshot; all 13 combat scripts + `die` at parity; alloc/frame 8.3–10.0 KB, unchanged, with the per-jab coroutine
  allocations gone.

#### 5.3 — Weapon components and hitscan (done; 5.2 was folded into 5.1 by the no-bridge decision)

- **The Weapon class hierarchy is deleted** (`Weapon` / `HitscanWeapon` / `ProjectileWeapon` / `Glock` /
  `M1887Shotgun` / `MG338` / `RocketLauncher`, the `Inventory` class, the weapon prefab GameObjects and `MakeWeapon`).
  In its place, `Ecs/Combat/Weapons.cs`: weapon definitions are **shared immutable data** (`Weapons.All`, the
  behaviour-tree pattern — the component holds a slot, not copies of twelve floats), state is two components
  (`Weapon`: slot, refire timer, bloom, holstered, the viewmodel sprite entity; `Inventory`: an `[InlineArray]` ammo
  block + last slot), and firing is **one system**. `WeaponFireSystem` (Tick, after input, runs alone): refire and
  bloom on `ctx.DeltaTime` (= the Application's scaled delta, so the death slow-motion slows the gun exactly as the
  deleted `WaitForSeconds` coroutines did — and a headless test world can drive it), move / air spread restored from
  the pre-2.5 original, recoil as a direct `CameraShake` kick on the camera entity, hitscan rays that post
  `DamageEvent`s on whatever entity the ray names (headshots judged by its `Hitbox`), impulses on loose props,
  projectile weapons spawning their GameObject rocket until 5.4. No coroutines, no per-shot allocations.
- Hit feedback consumes `HitEvent` in the bridge (crosshair marker, rage +0.1, style — which needs `Killed`, known
  only at the event, never at fire time; rocket style-kills register again when projectiles post events in 5.4).
- `CombatController` keeps only the GameObject-side surface: switching handlers (the input map and the automation
  call them), the jab combo (now gating `Weapon.Holstered` instead of a CanFire flag), death, equip (component +
  viewmodel + the "Weapon" GO node for ProjectileSpawn), and thin component-backed accessors
  (`CurrentWeaponData` / `AmmoOf` / `AmmoArray` / `LoadAmmo` / `AddAmmo`) for the HUD / save / pickup readers.
- **Gate flipped**: `killenemy` reports `enemy DEAD health 0 armour 0` at frame 240 — the first kill since 2.L — and
  the whole loop fires: ragdoll slicing from the DeathEvent payload, ammo drain, rage to ENRAGED from the hit events
  (red postfx + flaming portrait, caught on screenshot).
- **Test-hygiene trap found**: the global `Physics.World` outlives a disposed ECS `World` (Dispose frees only the job
  pool), so a check's colliders shadow the next check's at the same coordinates and rays resolve to entities of a
  dead world. Checks now `DestroyAllEntities()` (+ one update, so the sync unregisters the slots) before disposing,
  and keep their geometry at distinct coordinates.
- Gates: 196 engine + 43 gameplay assertions (the fire pipeline in vitro: one ray per held-fire window, ammo spent,
  refire holds the trigger, the kill latches once, a holstered gun never fires); all 13 combat scripts + `die` at
  parity; alloc/frame at the 5.0 baseline (fire 9.5–10.2 vs 9.0–10.2 KB, m1887 8.3–9.2 vs 8.3–8.9, gc 0.0/s).
- Carried to 5.4 / 5.5: rockets are still transform-moving GameObjects that damage through the facade; the grapple
  melee (its per-weapon moves went with the classes; the legacy path was already dead — its hit resolved to null);
  the enemy armour-from-maxHealth typo decision now that weapons are live.

#### Baselines (5.0, `d5cb44f`, dev Mac, `--frames 300`)

- **Combat is inert, and now says so**: `--script killenemy` fires a Glock point-blank at a rifleman for 180 frames
  and the probe added to it reports `enemy ALIVE health 1000 armour 1000` at frames 240 and 290 — hit resolution has
  been stubbed out since 2.L (`EntityState entityState = null`). Flipping that line to a dead grunt is the layer's
  headline gate. Before the probe the script only proved the game does not crash while firing.
- Every combat script completes with no errors: `fire`, `glockfire`, `weapons`, `m1887`, `jab`, `glockalt`, `rocket`,
  `killenemy`, `peek`, `close`, `approach`, `rage`, `saveload`.
- **Allocation per frame** (`--perf-log`, the hot path to hold): `fire` 9.0–10.2 KB, `m1887` 8.3–8.9 KB (8 pellets a
  shot), `rocket` 8.2–9.9 KB, against `walk` 7.9–9.6 KB — so firing costs ~1 KB/frame on top of a ~8 KB/frame
  baseline churn, and `gc 0.0/s` throughout. Known offenders to drop in the port: `RocketProjectile` calls
  `Scene.Current.FindObjectsOfType<EntityState>()` per detonation (full scene scan + allocation),
  `Physics.OverlapSphere` allocates its result list per call, and `QueryFilter.predicate` closures allocate per shot.
- **Physics bench** (`--threads 1 --no-render`, ms/step): crates 11.84, ragdolls 16.77, projectiles 0.18, ground 0.81.
  Only the interleaved A/B against the previous commit is meaningful here; absolute numbers are far off the Linux VM's.
- Cleanup: the stale `tests/RipNRage.EcsChecks` (bin/obj only, no csproj, not in the solution) was deleted.

### Layer 6 — Enemies, AI, spawning (~1.1k lines)

The behaviour trees are **not** ported: they become a data-oriented state tree (6.2). Definitions in `Enemies.All`
(flat fields on `EnemyData`), state in components, behaviour in systems; the brain writes intent, never calls anyone.
Every step is judged on the three AI probes (`peek` / `close` / `approach`: `moved`, `spotted`, `seen`, `state`,
`hp`; `approach` needs `--frames 400` for its hearing probe) and `killenemy`'s `killedAtFrame 97`. **Steps** (one commit + review stop each):

- [x] 6.0 **Instrumented the watch gates** and recorded the statue baseline: `moved 0.00`, `spotted False`, `hp 100`
      throughout; `anim IDLE` at frame 60 then `MOVING` (the move node reported arrival instantly).
- [x] 6.1 **The enemy is an entity — body and look.** `AIState`'s entity gained the force-driven capsule (`Body` 85 kg,
      `CharacterMotor` profile Enemy) and the pose flow flipped: the GameObject follows the entity (first frame hands
      the GO's pose over — the `PlayerLink` pattern — so spawners and the save system keep working). The look is a
      sprite child with `SpriteRender` + the new engine `SpriteFx` (per-entity material overrides, filled from a pooled
      property block: `_Flash` / `_FlashColor` / `_Dissolve`, so the dissolve needs no cloned material);
      `EnemyVisual` + `EnemyVisualSystem` play the 8-sided sheets, the hit flash (`HitEvent`), the telegraph, and the
      death dissolve — which owns the corpse (entity and GO go together). Deleted: `AnimationStateController`,
      `EnemyHitFlash`, the `SpriteHolder` GO. `EnemyDestruction` slices the entity's sprite and hides the child through
      the command buffer (it runs inside the death event dispatch; an immediate Add throws).
- [x] 6.2 **The behaviour tree is a state tree** (`Ecs/AI/AiBrain.cs`). The whole of BehaviorTree2 was
      `PlayerSpotted ? InAttackRange(10) ? [LookAt, Attack] : [MoveTo] : [Wait(1), RandomLocation(15, 10), MoveTo]`
      — seven nodes carried by ~500 lines of node classes, clone / traverse / bind, and a blackboard per enemy. Now:
      `Enemy { Kind }` (the identity; `EnemyVisual.Kind` moved here), `AiBrain { State, StateTime, Target, MoveTo }`,
      `AiIntent { Move, Attack, MoveTo, FacePoint }` and `AiBrains.Step` — a pure function: **interrupts first,
      outermost branch down** (a lost / dead target drops to `Idle`; a live one puts the brain in `Chase` or `Attack`
      by distance — the parent branch gets the first word, which is what makes it a tree), then one `switch` over the
      leaf (`Idle` dwells `wanderWait` → `Wander` picks a point beyond `wanderMinDistance` inside `wanderRadius` and,
      arrival being instant until 6.4, returns to `Idle`; `Chase` moves; `Attack` aims). `AiBrainSystem` steps every
      live enemy (a `Dead` one's brain stops, its intent reads quiet) and **writes `AiIntent`; `AIAgent.Update` acts on
      it** — the system never reaches into the MonoBehaviour, and 6.4 becomes a deletion rather than a rewrite. The
      node numbers moved to flat fields on `EnemyData` (`wanderWait`, `wanderRadius`, `wanderMinDistance`;
      `attackRange` is now the tree's 10 — the asset's 12 never decided anything). `AgentVisionSensor` (still uncalled)
      writes `AiBrain.Target`. Deleted: `BehaviorTree`, `BaseNode`, `Nodes` (incl. the dead `RepeatNode` /
      `DebugLogNode` / `LookAtNode`), `Blackboard`, `BehaviorTreeOwner`, `BehaviorTrees`, the `AI.BehaviorTree`
      namespace; `AIAgent` / `AIState` / `AgentVisionSensor` moved to `Scripts/AI/`. Gates: 199 engine + 87 gameplay
      assertions (the interrupt order, the dwell, the wander point's band, a step as a function of its inputs, the
      system reading liveness off the world); all 13 scripts + `die` at parity; the AI probes read exactly the statue
      baseline plus the new `state Idle`; `killedAtFrame 97`, `rocket` `hp 80 armour 0 y 3.82`, `jab` 1→2→0, `glockalt`
      refusal unchanged. (`saveload` prints no "after load" line at frame 240 — pre-existing on 6.1, the load itself
      reports `player at (-0.00, 1.81, -15.42) hp 100 ammo 119`.)
- [x] 6.3 **Perception: sight and sound** (`Ecs/AI/Perception.cs`). `Perception { Target, LastKnown, Seen, SinceSeen }`
      on the enemy, written by two systems, read by the brain (`AiBrain.Target` moved here; the brain now chases
      `LastKnown` and attacks only when `Seen` and in range). **Sight** is a query, not a trigger volume: range → cone
      about the body's facing → one line-of-sight ray against the level (`sightRange` 10 = the old sphere, `sightFov`
      180 and `memory` 5 s are new, flat on `EnemyData`); a dead target is dropped at once, an unseen one after the
      memory. **Sound is an entity**: `Noise { Origin, Source, Loudness, Radius }` — `HearingSystem` advances the front
      at `Noises.Speed` (80 m/s) every tick and tells the listeners it crosses that tick; a wall between
      (`Noises.WallMask` raycast) halves how far it carries; the entity destroys itself once the front passes its
      loudness. A noise made by the player turns the listener on the player; one made by an enemy passes on *that
      enemy's* target (they are shooting at someone), so an alert spreads. Emitters: the player's shots
      (`WeaponData.noise` 30), the rocket blast (`ProjectileDef.Noise` 60), enemy shots (`EnemyData.noise` 30) and
      footsteps (`Noises.Footstep` 8). `AIAgent` reads the acquisition edge off `Perception` for the alert reaction.
      Deleted: `AgentVisionSensor` and the `VisionSensor` GO (`Layers.VisionSensor` stays: it is an index in the Unity
      layer table). **Engine fix found by the gate**: `TransformSystem` added a fresh entity's `LocalToWorld` at
      identity and computed it a LateTick later, so for one Tick every new entity stood at the world origin — 8 m
      from the player start, inside sight range: `peek` / `close` read `spotted True` with nothing in sight, forgotten
      exactly at the 5 s memory. It is now seeded from the `LocalTransform` on creation. **Flips**: `approach 100` →
      `spotted True seen True state Attack anim ATTACKING` (the surviving `AIAgent` burst fires off the intent; the
      player at 11 m/s is not hit), `approach 200` → remembered (`Chase`), and the new probe `approach 345` (fifteen
      frames after the first shot, `--frames 400`) → `spotted True seen False state Chase` at 10.9 m: heard, not seen.
      `peek` / `close` stay at the statue baseline. Gates: 199 engine + 108 gameplay assertions (range, cone edge, a
      wall, memory at 4 s vs 6 s; the front at 13 m vs 22 m, spent at 30 m, 40 m never hears, muffled 30 vs 50
      through a wall, the shooter's target passed on and the shooter deaf to itself; the brain on a heard-only target
      chases the sound and does not shoot); all 13 scripts + `die` at parity, `killedAtFrame 97`, `rocket` / `jab` /
      `glockalt` unchanged.
- [x] 6.4 **Steering** (`Ecs/AI/Steering.cs`). No `Steering` component after all: the intent is the state. **Facing is
      data** — `Heading { Yaw }` — because the physics sync reads any `LocalTransform` rotation write as a teleport,
      and the body never needs to turn (symmetric capsule, billboard sprite); steering turns the heading at
      `turnSpeed`, and the sprite row (`EnemyVisualSystem`), the sight cone (`SightSystem`) and the following
      GameObject (`AIState.LateUpdate`) read it. `Steering.Step` is a pure function (intent in; `CharacterMotor.Desired`
      at `movementSpeed` — the legacy nav agent's live value, 11 m/s — and the yaw out); `SteeringSystem` (after the
      brain, runs alone) adds the **ledge guard** — one ray down a step ahead against `Noises.WallMask`, no ground →
      brake — brakes a `Dead` body, and sets the sheet to MOVING / IDLE (never over an attack or a death the GameObject
      half plays; `AIAgent` hands the sheet back after its burst). The brain gained **arrival** (`arriveDistance` 5 =
      the old `HasReachedLocation`'s 5 m, flat on `EnemyData`): `Wander` holds its point until arrived or a 3 s
      timeout (a point the ledge guard refuses costs an idle), `Chase` stands once within reach of the last known
      position. `AiBrain.Rng` (xorshift, seeded by the entity): wander picks **do not touch the shared `Random`**,
      which is time-seeded — found when two `approach` runs disagreed; the AI gates are now the same run every time
      (sub-decimetre physics noise aside). `AIAgent.Move` deleted. **Flips**: `moved` 6–20 m on all three scripts;
      `peek` wanders unaware; **`close` 290: `hp 100 armour 40`** — the wandering enemy walked into sight range and
      its legacy burst landed: the first enemy damage since 2.L, a step earlier than planned (6.5 was to flip it);
      `approach` 100 → sees and attacks at 4 m, 200 → lost, walked to where they were and stands (`Chase`, IDLE),
      290 / 365 → re-acquired when the player came back to shoot. Gates: 199 engine + 126 gameplay assertions
      (the motor at 11 m/s east, 60° of turn per tick at 3600°/s, braked with no ground ahead, an attack turning at
      the turn speed, a quiet intent standing; wander holds its point, gives up at 3 s, ends within 5 m; the cone
      follows the heading; in a world: held with no floor, walking and MOVING with one, braked dead); all 13 scripts
      + `die` at parity, `killedAtFrame 97`, `rocket` / `jab` / `glockalt` unchanged.
- [x] 6.5 **The attack is an entity; `AIAgent` is deleted** (`Ecs/AI/EnemyAttack.cs`). An attack in progress is an
      **entity parented to the enemy** (5.5's finisher pattern: their move, and the hierarchy tears it down with
      them) carrying `EnemyAttack { WindupLeft, ShotsLeft, BurstTimer, Aim }` — re-aimed from the intent every tick,
      as the legacy kept aiming mid-attack. The enemy keeps `EnemyWeapon { Refire, StaggerLeft, AlertLeft, Alerted,
      Attack }`: the gun between attacks. One `EnemyAttackSystem` (after the steering, runs alone) does engagement —
      the clocks, the alert reaction on the acquisition edge (yellow pulse, sting, `enemy_alert`, then `alertDelay`
      before engaging), the decision to spawn an attack with its telegraph (orange pulse, `EnemyAim`, `enemy_aim` at
      the muzzle) — and then each attack entity's wind-up → one shot per `burstInterval` → recovery (`Refire =
      fireRate`, the sheet handed back to the steering). The muzzle is data (`EnemyData.muzzleOffset`, the prefab's
      child offset scene-scaled) turned by the `Heading`, so the `ProjectileSpawnPostion` GameObject is gone.
      `IConsumes<HitEvent>` sets `StaggerLeft`, which **brakes the body in the steering and freezes the wind-up** —
      the legacy `StopAgent` had been empty since 2.L, so a hit stops an enemy again for the first time since. The
      death timeline moved to the look (`EnemyVisualSystem`: DYING on the `DeathEvent`, the dead pose 0.25 s later),
      and death cancels the attack in progress. **Five coroutines and the last enemy MonoBehaviour behaviour are
      gone**: `AIAgent` (178 lines) is deleted; `AIState` keeps only health, the save hooks and the pose follow.
      Gates: 199 engine + 147 gameplay assertions (the alert beat fires nothing, the attack is an entity parented to
      the enemy, a hit staggers for the definition's time and freezes the wind-up, the burst is three shots from the
      muzzle offset, the recovery is the fire rate, death cancels and a corpse fires nothing; in the steering, a
      staggered walk stops and resumes); all 13 scripts + `die` at parity with 6.4 — `close` 290 `armour 40`,
      `killedAtFrame 97`, `rocket` / `jab` / `glockalt` unchanged — with one intended change: the sheet reads IDLE
      between bursts instead of staying ATTACKING.
- [x] 6.6 **The enemy is only an entity** (`Ecs/AI/Enemies.cs`, `EnemyData.cs`, `EnemyDeath.cs`). Spawning is a
      function — `Enemies.Spawn(world, kind, floorPosition, yaw)` builds the whole archetype (body, health, look,
      senses, mind, gun), no factory — and `EnemyData` is a plain record in `Ecs/AI` (`name` keys the death VFX, the
      new `classname` names the save entry; the capsule constants moved onto `Enemies`). The level pipeline hands
      back entities: `EntityDefinition.spawnEntity` + a `LevelRef { Index, SpawnerIndex }` component stamped by the
      loader / spawner / save load; `MonsterSpawner` tracks a `List<Entity>` until 6.7 replaces it. Death is
      `EnemyDeathSystem` (`IConsumes<DeathEvent>`): the kind’s VFX and the ragdoll slice read the event payload.
      **Deleted**: `AIState` (and with it the enemy’s `EcsObjects` entry, the first-frame pose hand-off — enemies
      now spawn where they stand — and the `GrapplePoint` nothing read), `EnemyDestruction`, `DamageInfo`,
      `Prefabs.Rifleman`, `Scripts/EnemyData/`. `CombatBridge` keys the player’s hit feedback on `Has<Enemy>`;
      `GameSaveSystem` reads monsters off the world (slots keyed by `LevelRef.Index`, a dead slot destroys the
      entity, dynamic monsters respawn through `spawnEntity`); the probes and `killenemy` query the world
      (`Enemies.Collect`; ad-hoc queries are cached per world — `Build()` registers with the world for good).
      Gates: 199 engine + 164 gameplay assertions (the spawn archetype, the per-world query cache, a lethal blow
      running death → dissolve → removal with no GameObject anywhere). **The scripts were compared under
      `--fixed-dt 0.016667`** — wall-clock dt on the dev Mac swamps every number (`killedAtFrame` moves 136–150 run
      to run at the same commit; under fixed dt it is exactly the recorded **97** on both builds): walk / fire /
      weapons / rage / saveload / jab / rocket / glockalt / killenemy / style / die byte-identical to 6.5.
      `peek` / `close` / `approach` keep every capability (wander, sight, chase, the `approach 365` heard-not-seen
      flip) but walk different paths: the baseline teleported each enemy from the prefab origin to its spawn point a
      frame after birth, so the physics history — and with it the wander dice — differ; the `close 290` armour-40
      hit was one of those dice (re-record at 6.8). `saveload` writes the four dock monsters from the world and
      restores them (the missing frame-240 "after load" line predates 6.1 and is unchanged); look screenshot 0.183%
      vs a 0.180% same-build floor; frame 4.4 ms median vs 4.7 (3 interleaved runs).
- [x] 6.7 **Spawning** (`Ecs/AI/Spawners.cs`). `Spawner { Kind, Count, Interval, Radius, ActivateRange, MaxAlive,
      Yaw }` + its running half (`Spawned`, `Timer`, `Active`, scatter dice — xorshift seeded by the entity, not the
      shared `Random`) on one entity the registry builds (`monster_spawner.spawnEntity`); `SpawnerSystem` wakes on
      the player's approach and spawns one monster per interval through the command buffer, capped at `MaxAlive`
      standing — counted off `LevelRef.SpawnerIndex`, which is also how a save knows a spawner's monsters.
      `Enemies.Spawn` gained a **buffered twin** (`CommandBuffer` overload; the immediate API is disabled while a
      phase runs) kept honest by a check comparing the archetypes the two build. `MonsterSpawner` (MonoBehaviour) is
      deleted; the save system round-trips `Spawned` through the world. The step first blamed a style-gate
      regression on the scheduler and "fixed" it with `[UpdateAfter(EnemyAttackSystem)]` — **that diagnosis was
      wrong**; see *6.7b* below. The ordering stays for its intent (new monsters after the frame's combat). Gates: 199 engine + 177 gameplay assertions (sleeps until the player is in range, first
      monster at once, interval pacing, the cap refilled the tick a slot frees, the count spent, scatter inside the
      radius, the monster stamped with its spawner, the twin archetype parity); all 15 scripts byte-identical to 6.6
      under `--fixed-dt 0.016667`; dock.map has no spawner, so the level pipeline was smoke-tested on a scratch copy
      with one added — spawner at level index 12 saved `spawned 2` with both runtime monsters as dynamic entries
      carrying `spawnerIndex 12`, loaded clean; frame 4.9 ms median vs 4.6 (3 interleaved runs, inside this Mac's
      noise band — 6.6 measured the same spread the other way).
- [x] 6.7b **Determinism and the schedule — the investigation and its fixes.** Re-examined the 6.7 "scheduler
      trap": `DescribeBatches` showed the Tick layout is **18 batches of one system each with or without the
      attribute** (nearly every system declares no `[Reads]`/`[Writes]`, so each is `Exclusive` and the schedule is
      fully serial; the attribute only moved `SpawnerSystem` from slot 8 to slot 10 — no sync point was added).
      The style gate was simply **not deterministic**: same build, fixed dt, three runs, three outcomes. The dice
      were the shared time-seeded `Random` in the weapon spread (`Weapons.cs`); nobody called `InitState`. Matrix:
      seeded → 3/3 identical; solver single-threaded only → still differs; seeded, attribute vs none → identical.
      **Fixes**: (1) `--seed N` — scripted runs seed `Random` with 12345 by default (`--seed 0` keeps the clock);
      (2) `--threads N` in the game, as the bench has it: the parallel rigid solver still carries an order
      dependence (`approach` differs run to run at default threads, is stable at 1), so **the gate rule is now
      `--fixed-dt 0.016667 --threads 1`** (seed implied) — every one of the 15 scripts is bit-for-bit repeatable
      under it; (3) the narrow phase now concatenates the workers' manifolds in **item order** (`Manifold.item`,
      k-way merge) and sorts woken bodies by id, so the contact list and the islands no longer depend on which
      worker claimed which batch — closes one solver order source (the residual one is elsewhere in the parallel
      stages: a later pass with the bench; no measurable cost — ragdolls narrowphase 2.0–2.9 ms either way, inside
      this Mac's spread); (4) **`ScheduleChecks`** pins the Tick / FixedTick / LateTick batch layout as a golden list
      — any system added, renamed or re-attributed fails the gameplay checks with both layouts printed, and the
      intended change is made in the same commit, in the open. **Scheduler facts to keep in mind** (recorded, not
      yet changed): a system with no `[Reads]`/`[Writes]` is `Exclusive`; the `QueryBuilder` already records what a
      system reads and writes but the scheduler ignores it; order among unconstrained systems is **alphabetical by
      type name** (`Engine.*` before `Game.*`, `AI.*` before `Combat.*`); command buffers play back after each
      *batch*, so the day two systems share a batch they stop seeing each other's output within the frame —
      deriving Reads/Writes from queries, explicit groups instead of the name tiebreak, and a deliberate sync-point
      policy are design work for layer 9/10, and the golden check is what makes any step towards them visible.
- [x] 6.7c **Declarations are derived and enforced** (`Systems.cs`). The scheduler used to trust hand-written
      `[Reads]`/`[Writes]` and ignore what the `QueryBuilder` already knew; nearly every system wrote neither and
      ran `Exclusive`. Now: a query built in `OnCreate` is part of its system's declaration (`World.DeclareQuery`);
      `[Reads]`/`[Writes]` remain for ad-hoc access on entities a system looks up; a new **`[Exclusive]`** names the
      systems that own state outside components (physics sync / step / write-back / triggers / joints / motors /
      movers, the audio device, the input device, the particle library, weapon fire and finishers with their
      impulses and GameObjects, the camera follower); a system that declares nothing at all is still exclusive. The
      rule is **enforced**: with `World.ValidateAccess` on, every `World.Get`/`Has`, chunk column and buffered
      `Commands.Add`/`Remove` inside a running system is checked against the thread's current system and an
      undeclared one throws, naming both; both check suites run with it on, scripted runs take `--validate-access`
      (gate rule), and `ECS_ACCESS_REPORT=1` turns the throw into a one-line-per-pair audit. The audit found 80
      undeclared pairs across 22 systems — all declared or marked in this step; the three the suites could not reach
      (`PlayerMoveSystem`'s footstep `Noise` and `BodyVelocity`, `PlayerViewSystem`'s rig pivot, `RagdollSystem`'s
      `CameraLens`) came out of the scripted audit. Result: the Tick schedule goes from 18 serial batches to 15, with
      three parallel pairs (`Animation + Lifetime`, `EnemyVisual + Hearing`, `PlayerLook + Spin`) — disjoint data,
      neither feeding the other within a frame; `ScheduleChecks` re-pinned and now prints every phase before
      asserting. Gates: 199 engine + 180 gameplay assertions; all 15 scripts byte-identical to 6.7b under the rule
      and repeatable, with validation on throughout; frame time 4.4–5.6 ms vs 4.3–5.6 interleaved (inside the noise;
      the `ecs=` slice reads 0.1–0.2 ms higher, plausibly the barrier cost of dispatching two tiny systems in
      parallel — worth a look when a batch gains real work). Still true and still written down: unconstrained order is
      alphabetical, playback is per batch — the golden check is what shows either changing.
- [x] 6.8 Close-out (below).

#### 6.8 — Layer close-out (done)

- Full sweep green under the gate rule (`--fixed-dt 0.016667 --threads 1 --validate-access`, `--frames 400`):
  199 engine + 180 gameplay assertions; all 16 scripts (walk, fire, weapons, rage, saveload, jab, m1887, glockfire,
  rocket, glockalt, killenemy, style, die, peek, close, approach) exit 0 and are **byte-identical across two runs**
  (perf lines excluded). Combat gates at their values: `killedAtFrame 97`, `rocket` hp 80 armour 0 y 3.82, `jab`
  1→2→0, `glockalt` step 0 / finisher none / ammo 120.
- **AI baseline, re-recorded** (replaces the 6.0 statue and the 6.4 numbers; `close 290 armour 40` is gone — that hit
  was a dice roll of the pre-6.6 spawn teleport, see 6.6):
  `peek` 60 / 150 / 290: moved 0.00 / 7.04 / 13.42, never spotted, `Idle`, `Idle`, `Wander`, hp 100 throughout;
  `close` 60 / 150 / 290: same path at d 15.9 / 13.1 / 10.2, never spotted, hp 100;
  `approach` 100: d 6.9, `spotted True seen True Attack ATTACKING`; 200: d 21.0 `Chase` remembered, unseen;
  290: d 12.4 forgotten, `Wander`; 365: d 12.9 `spotted True seen False Chase` — heard, not seen. Player hp 100
  throughout: no AI probe currently lands an enemy hit; enemy damage is covered by the 6.5 assertions, not a script.
- Bench (`--threads 1 --no-render --scenario all`, 3 interleaved runs vs 5.6 `033b7ee`, median physics ms/step):
  crates 11.92 / 11.97, ragdolls 16.78 / 16.82, projectiles 0.20 / 0.20, ground 0.95 / 0.93 — flat. Layer 6 touched
  engine hot paths (`TransformSystem` seeding, narrow-phase merge order, the access check in `World.Get`); none shows.
- Allocations: walk 44–52, fire ≈ 48, combat / AI scripts 45–48 KB/frame, **the same at 5.6 and 6.6 built side by
  side on the same flags** — so layer 6 added nothing. The ~8 KB/frame quoted in 5.6 does **not** reproduce at
  `033b7ee` today (46–52 there too, fixed or wall-clock dt): the earlier figure was measured some other way. Treat
  ~48 KB/frame as the real baseline; finding where it goes is a standalone task, not a layer gate. The burst
  coroutine's allocations are structurally gone (no coroutine left in `Ecs/AI`) but sit below this noise.
- Layer totals (`033b7ee..7fba77e`, `src` + `tests`): +2,270 / −1,332 across 65 files, 15 files deleted —
  BehaviorTree2 and its node classes / blackboard, `AIAgent`, `AIState`, `AgentVisionSensor`, `AnimationStateController`,
  `EnemyHitFlash`, `EnemyDestruction`, `DamageInfo`, `MonsterSpawner`, `Prefabs.Rifleman`, `Scripts/EnemyData/` — and
  the enemy is: one definition table (`EnemyData`), 9 files / 1,268 lines in `Ecs/AI` (including the pure
  `AiBrains.Step` / `Steering.Step`), and 109 new gameplay assertions. Engine gains: `SpriteFx`, seeded
  `LocalToWorld`, order-independent narrow phase, `--seed` / `--threads`, derived + enforced system declarations.

### Layer 7 — Pickups, rage, environment (~0.65k lines)

**What is there today** (surveyed at 6.8):

| Piece | Where | Shape |
|---|---|---|
| Rage | `PlayerState` (~90 of its 225 lines), `RageStateData`, `GameAssets.*Rage` | `_rage` 0..1, +0.1 per player hit (`CombatBridge`), +0.05 per enemy in a rocket blast (`Projectiles`); decays 0.2/s only while `CombatState == VIOLENT`; at 1 → `ENRAGED` for 5 s, at 0 → `DEPRESSED` for 10 s, then back to 0.5 / `NORMAL`; `VIOLENT` lasts 30 s after the last shot (`Weapons`, `AltFire` call `SetViolentCombatState`). Three coroutines |
| Rage effects | `RagePostprocess` (112), `PlayerCharacter` speed, `HudController` portrait | polls `PlayerState.RageState`, lerps a volume weight in two coroutines, pushes a post-fx override. `RageStateData` has five fields; **only `movementSpeedModifier` is read** |
| Style | `StyleMeter` (213, half of it Clay HUD) | `RegisterHit` from `CombatBridge` (weapon name, headshot, killed) → points, combo multiplier by weapon variety, rank F..S, decay; S rank: regen, ×1.25 speed (`PlayerCharacter`), ammo trickle. `Time.time` clocks |
| Pickups | `AmmoPickup` (39), `EntityRegistry` `item_ammo_*` | MonoBehaviour + `ITriggerListener` via `TriggerBridge`; `TriggerFollower` keeps the trigger on the bobbing GO |
| Water | `EnterWaterCollision` (25), `water_plane` | trigger listener toggling `RenderSettings.drawSky` on the camera; `GameSession` resets it |
| Checkpoint | `CheckpointAutoSave` (in `EntityRegistry`) **and** `PlayerState.OnTriggerEnter` | **two saves on one overlap**: the registry one writes `GameSaveSystem` "checkpoint"; the `PlayerState` one is a legacy leftover writing `SaveManager` "Save1" on the `Checkpoint` tag |
| Surfaces | `SurfaceMaterial`, `BrushSurface`, `SurfaceResolver` | **written, never read**: `SurfaceResolver.Resolve` returns `Surfaces.Default` (stubbed when physics went ECS in layer 2), so every impact VFX / decal is the default one |
| Sky | `SkyboxRotator` (11) | `RenderSettings.skyboxRotation = t * 1.2` |
| Bridge | `TriggerBridge.cs` (144) | `EcsObjects`, `TriggerVolume`, `TriggerFollower`, `GameTriggerBridge`; its last listeners are the pickup, the water, the two checkpoints |

**Direction (decided 2026-11-14, replaces the earlier rage / style plan): one rage system, two meters.**
The style meter is retired; rage absorbs what was worth keeping. ENRAGED and DEPRESSED stay, but both are now
**earned, chosen and useful**: aggression fills the *Fury* meter, evasion fills the *Calm* meter (working names —
the state names stay). The player activates a full meter; inside a state **neither meter gains** and the state
**cannot be left until its meter has drained**. ENRAGED rewards pressing the attack, DEPRESSED rewards evasive,
defensive play. "Passive" means **evasive under threat** — dodging, wall running, sliding, near misses — not
standing still or not shooting.

#### The rage spec (draft — review before 7.1)

**State** — one component on the player entity, replacing `PlayerState`'s rage half and `StyleMeter`:
`Rage { Fury, Calm, State, Threat }`, `Fury` / `Calm` in 0..1, `State` ∈ Normal / Enraged / Depressed. The active
meter *is* the timer — no separate duration field. `Threat` is described under evasion.

**Rules** (`Rages.Step(in Rage, in RageInputs, dt, in RageDef)`, a pure function like `AiBrains.Step`):

1. **Normal**: gains add to their meter, clamped to 1. A meter that is not full and has gained nothing for
   `IdleDelay` decays at `IdleDecay` (keeps the meter about *recent* play). A full meter does not decay: it is
   banked until spent. *(open: Q1)*
2. **Activate**: the activate input with a full meter enters that meter's state. *(open: Q2 for the input)*
3. **In a state**: every gain is dropped, for both meters. The active meter drains at its `DrainRate`; the other
   one is frozen at its value. At 0 → Normal. No input, damage or kill ends a state early.
4. **Death** clears both meters and the state; a save stores `Fury`, `Calm`, `State` (the active meter is the
   remaining time, so a restored state resumes exactly; this replaces the "full duration on restore" fix).

**Fury: aggression sources** (all from events that exist today, consumed off `HitEvent`):

| Source | Starting value | Note |
|---|---|---|
| Hit on an enemy | +0.02 | spraying alone takes ~50 hits |
| Kill | +0.15 | ~5–6 kills to fill with the other sources |
| Headshot (hit / kill) | +0.03 / +0.10 extra | |
| Close-range kill (< `CloseRange` 6 m) | +0.10 extra | rewards pushing in; distance known at consume time |
| Melee / finisher kill | +0.15 extra | the jab and the alt-fire finishers |
| Enemy in a rocket blast | +0.02 | the old splash call, kept |

**Calm: evasion sources** — all gated by **threat**, so running laps around an empty level fills nothing.
`Threat` is true while at least one living enemy has the player as `Perception.Target` *and* `Seen`, or has an
`EnemyAttack` in progress aimed at the player. Sources:

| Source | Starting value | Where it is detected |
|---|---|---|
| **Near miss**: an enemy projectile passes within `NearMissRadius` (1.5 m) of the player and does not hit | +0.08 | projectile step: closest approach between the projectile's segment this tick and the player |
| **Dodge**: dash, slide start or wall jump while an `EnemyAttack` aimed at the player is in wind-up or burst | +0.12 | `PlayerMotor` edge + the attack entities |
| Wall run under threat | +0.06/s | `PlayerMotor.IsWallRunning` |
| Slide / airborne / dash under threat | +0.03/s | `IsSliding`, `!IsGrounded`, `IsDashing` |
| Breaking line of sight: a pursuer goes from `Seen` to not seen while the player moves | +0.05 | `Perception` edge |
| **Taking damage** | −0.10 of Calm per hit | the opposite of evading; aggression is not penalised |

Standing still and not shooting gains nothing. Shooting does not *cost* Calm; the two meters can fill together,
and choosing which one to spend is the decision.

**Effects** (the starting point for tuning, each with a reader in the same commit):

| | ENRAGED (offensive) | DEPRESSED (defensive) |
|---|---|---|
| Damage | dealt ×1.5 | taken ×0.5 |
| Sustain | each hit heals 2, a kill heals 15 (lifesteal) | armour regenerates 5/s |
| Movement | ×1.2 speed | dash cooldown ×0.5, one extra air dash |
| Enemies | — | sight range ×0.6 and memory halved: easier to lose pursuers and reposition |
| Duration (full meter) | `DrainRate` 1/8 s → 8 s | 1/10 s → 10 s |
| Feedback | the existing red post-fx, a sting on activation | the existing blue post-fx, a sting |

The old DEPRESSED speed ×0.5 penalty is gone: a defensive state that slows the player fights its own purpose.

**What this deletes**: `StyleMeter` (all of it: scoring, ranks, S-rank perks — the HUD drawer goes too, not just
the logic, since there is nothing left to draw), `RageStateData` and `GameAssets.*Rage`, `PlayerCombatState` /
`SetViolentCombatState` (VIOLENT was the timer the old decay hung on; nothing needs it now), and most of the
pre-tuning above (the shared pause clock, the climb-out, the speed cap — they solved conflicts that cannot happen
any more). The `style` script becomes a rage script.

**Engine / gameplay additions the effects need**: `DamageEvent.Instigator` (reader: the ENRAGED dealt multiplier
and the DEPRESSED taken multiplier in `HealthSystem`; projectiles already carry `Instigator`); a perception
modifier read by `SightSystem` (reader: DEPRESSED); a dash-cooldown / air-dash modifier read by the motor.

**Answered at review (2026-11-14), and implemented in 7.1**

- **Q1 — banking**: meters **never decay**, full or partial. What is earned is kept until it is spent.
- **Q2 — activation input**: two actions — `rage_fury` (**Q**, D-pad left) and `rage_calm` (**E**, D-pad right).
  Both D-pad bindings and Q were taken: quick swap moved to **Tab**, and weapons 2 / 4 lost their D-pad bindings
  (number keys unchanged). `controls.json` gained a version and a one-time migration, so a saved config does not
  leave Q doing two things.
- **Q3 — partial activation**: allowed; the meter *is* the clock, so half a meter buys half a state.
- **Q4 — chaining**: no lock-out — the tick a state ends, the other meter can be spent.
- **Q5 — playtest HUD**: two plain bars beside the portrait (FURY red, CALM blue, pulsing while their state runs).
  Replaced by layer 8's HUD pass.
- **Q6 — numbers**: the tables' values are the starting point; tuning happens by playing.

**Steps** (one commit + review stop each; gates under the 6.7b/6.7c rule):

- [x] 7.0 **Instrument the gates.** The rage probes landed with the pre-tuning commit (`rage` / `style` /
      `saveload`); the last two arrived with the 7.3b review. **`pickup`**: the player is put four metres short of
      the dock's shell pickup at (-3, 0.91, -20) and walks in — baseline **shells 64 → 80, pickups 4 → 3** (an
      `AmmoPickup` GameObject through the trigger bridge; 7.5 ports it). **`water`**: the sky probe walks off the
      planks into the water and prints camera height and `drawSky` — baseline **`sky True` all the way down to
      y = -7.2**, i.e. *the water sky-swap does not work today*: `EnterWaterCollision` waits for a GameObject tagged
      MainCamera to enter the volume, and the camera has been an entity since layer 4. 7.6 is a fix, not a port.
- [x] 7.1 **The two meters** (`Ecs/Rage.cs`). `Rage { Fury, Calm, State }` on the player entity, the constants flat
      on `Rages`, and `Rages.Step` as the one place the rules live — a pure function (gains only in Normal and never
      a decay; activation with any amount; inside a state no gain for either meter, no exit, the active meter
      draining as the clock; both pressed at once = Fury). `RageSystem` collects the tick's gains (aggression off
      `HitEvent`: hit / kill / headshot / inside 6 m; anything else off the new `RageGainEvent`, which is how the
      rocket blast and 7.2's evasion sources pay), reads the two presses off `InputState`, and clears everything on
      the player's death. **Deleted**: `StyleMeter` (213 lines, HUD drawer and all), `RageStateData`,
      `GameAssets.*Rage`, `PlayerRageState`, `PlayerCombatState` / `SetViolentCombatState`, and `PlayerState`'s rage
      half — 225 lines down to 114, health / armour / death / the save hooks only. The portrait, the post-fx, the
      pause menu, the overlay and the save read the component (`Rages.TryGet` / `Restore`; the save keeps `rage` as
      the Fury field name and gains `calm`). **No melee bonus**: the finisher deals no damage in this build and the
      jab is cosmetic, so there is nothing to pay it off — it waits for a melee damage path, per the no-reader rule.
      Gates: 199 engine + 213 gameplay assertions (32 new: the meters, activation, partial and chained states, the
      drain as the clock, the effects, and the system over a world); `ScheduleChecks` re-pinned —
      `PlayerLookSystem + RageSystem + SpinSystem` now share a batch (15 batches still, disjoint data); all 16
      scripts byte-identical to 6.8 except the three that changed on purpose (`rage` / `style` are the new probes)
      and **`saveload`, whose second walk is now the same length as the first** — the old rage drained to DEPRESSED
      mid-walk and halved the player's speed; nothing does that any more. Repeatable across runs.
- [x] 7.2 **Calm sources** (`Ecs/RageCalm.cs`). `Rage.Threat` — a living enemy that has the player and can see
      them, or an `EnemyAttack` in wind-up / burst aimed at them — gates every Calm gain, so laps around an empty
      level pay nothing. `RageCalmSystem` pays per second for wall running (0.06) and for sliding / dashing /
      airborne (0.03); the **dodge** (0.12) is an edge — a dash, a slide start or a wall jump *while an attack is
      live* — and **breaking line of sight** (0.05) is the edge of a pursuer losing a moving player.
      `NearMissSystem` pays 0.08 for an enemy bullet whose flight **segment** this tick passed the player inside
      0.6–1.5 m: measured on the segment so a fast round cannot skip past between frames, banded so a bullet that
      would have hit pays the damage penalty instead, and marked on the projectile so it pays once. Taking a blow
      costs 0.10 of Calm (Fury is not penalised). Every gain is posted as a `RageGainEvent`, so it goes through
      `Rages.Step` and is dropped while a state runs. The wall jump became countable (`PlayerMotor.WallJumps`) and
      the projectile carries its last position and a paid mark. Gates: 199 engine + 231 gameplay assertions (16 new:
      each source, the near-miss band and segment measure, "no threat → nothing", a dash that is not a dodge, one
      payment per bullet, the penalty flooring at empty); `ScheduleChecks` re-pinned — 16 Tick batches now
      (`NearMissSystem + PlayerLookSystem + RageCalmSystem + SpinSystem`, then `RageSystem` alone: it writes the
      component the Calm system also writes). New **`evade`** script: a grunt engages, the player runs in and dashes
      across its fire — `threat` from frame 60, Calm 0.23 by frame 210, then banked when the enemy loses them.
      Every other script byte-identical to 7.1 apart from the probe lines (the Fury numbers are unchanged);
      repeatable.
- [x] 7.3 **Effects — what the states are worth.** `DamageEvent` gained `Instigator` (set by the hitscan, the
      bullet and the blast), so `HealthSystem` scales a blow at **both ends**: `Amount × DamageDealtBy(instigator) ×
      DamageTakenBy(target)` — ENRAGED ×1.5 dealt, DEPRESSED ×0.5 taken, 1 for anything with no rage. ENRAGED
      **lifesteal** (2 a hit, 15 a kill, capped at full health) rides the same `HitEvent` the Fury gain does;
      DEPRESSED regrows **5 armour a second**. DEPRESSED also halves the dash cooldown and grants one more air dash
      (`PlayerMoveSystem`), and makes the player **harder to see**: `SightSystem` scales each enemy's range (×0.6)
      and memory (×0.5) **per target**, read off that target's `Rage` — not off a query filter, which would have
      made sight require the component (a layer-6 check caught exactly that). **Death**: a corpse keeps no rage, and
      the signal is empty health, not the `Dead` tag — `FallRespawn` calls `PlayerState.Die()` straight out, so a
      fall posts no damage and **no `DeathEvent` ever reaches the ECS** (a corpse was left DEPRESSED, regrowing
      armour). Worth knowing for layer 8, which owns death and game flow: anything keyed on `DeathEvent` misses a
      fall. Gates: 199 engine + 254 gameplay assertions (23 new: every multiplier including "Normal changes
      nothing" and "a blow with no author is nobody's doing", lifesteal and its cap, armour regen and its absence,
      the dash and sight scales, and the zeroed-health death); the schedule is unchanged. All 16 earlier scripts
      **byte-identical to 7.2** — outside a state every effect is inert, so `killedAtFrame 97`, `rocket` hp 80
      armour 0 y 3.82, `jab` 1→2→0 and `glockalt` are untouched. The probes now print hp / armour: `rage` spends
      ENRAGED on a third grunt at frame 310 (speed ×1.20, the meter draining 0.50 → 0.38), `evade` spends the
      dodged-up Calm at 240 — armour climbing 40 → 48 under fire, and the grunt losing the player at 300.
      **Found and fixed on the way**: the dash's camera kick was an undeclared access (`PlayerMoveSystem` →
      `CameraShake`) that no validated run had ever reached until a script dashed.
- [x] 7.3b **Review of 7.1–7.3 against the rules, and the fixes it found.** Read back the whole rage system
      against the established rules; eight things to answer for, all applied in one commit:
      (1) **state had moved into a system** — `RageCalmSystem` remembered the dash / slide / wall-jump edges and who
      saw the player last tick in private fields. The motor now counts evasive moves (`PlayerMotor.Evasions`, one
      counter for the dash, the slide start and the wall jump), the AI flags the sight break where it is known
      (`Perception.LostSight`, written by `SightSystem`), and the meter keeps what it has paid for
      (`Rage.EvasionsSeen`) — the system remembers nothing between ticks, and the edges are saved and inspectable;
      (2) **`HitEvent` now carries `Instigator`**, so Fury pays for blows the player *struck* rather than guessing
      from "the target is an enemy", and **the rocket jump no longer costs Calm** (a blow the player dealt
      themselves is neither aggression nor carelessness);
      (3) death was handled twice — the `DeathEvent` consumer is gone, the empty-health check covers both paths;
      (4) dead code removed: `Rages.Current()`, and the two style stings orphaned when `StyleMeter` went;
      (5) the two enemy scans in the Calm system became one pass answering both questions, with `Without<Dead>()`
      in the query instead of a per-entity check;
      (6) the near miss was measured **from the player's head** (a copied `+0.9`), which flattered a well-aimed
      shot — it is measured from the capsule centre now, which is where the enemies actually aim, and the "first
      tick" sentinel is gone (`Launch` seeds `LastPosition`);
      (7) every tuning number moved out of `const` into **`RageDef`** — tunables.cfg **`[rage]`**, hot-reloaded like
      `[movement]`, round-tripped by the editor's save; `Rages.Step` takes the table, and the checks pin their own
      so tuning cannot move an assertion;
      (8) 7.0 finished (above). Gates: 199 engine + 258 gameplay assertions (4 new: the sight break driven by the
      real `SightSystem` and paid once, every counted evasion paid, the flag is an edge not a state); schedule
      unchanged; all 16 earlier scripts byte-identical, `evade` unchanged except that it no longer walks off the
      dock, `pickup` / `water` recorded. Repeatable.
- [x] 7.3c **The Calm system asks once, not once per player** (`RageCalm.cs`). The tick now opens by indexing
      **who is after whom, keyed by the target** — one pass over the living enemies (seen / lost-sight counts) and
      one over the attacks in progress — and each player reads its own entry; the index is scratch, cleared every
      tick (the `SpawnerSystem` pattern). It replaces two scans per player per tick that each asked "is this one
      about the player?", a question that stops being answerable the day **enemies fight each other**: an
      infighting pair is simply two more entries under their own targets. Gates: 260 gameplay assertions (2 new:
      two enemies shooting at each other leave the player unthreatened and pay them nothing); all 17 scripts
      byte-identical; frame time unchanged — at one player and one enemy the two shapes cost the same (`ecs`
      1.2–2.5 ms either way, 3 interleaved runs), so this buys scale, not milliseconds today.
- [x] 7.4 **Rage feedback as a system** (`Ecs/RageFx.cs`). `RageFx { EnragedWeight, DepressedWeight, LastState,
      FuryWasFull, CalmWasFull }` on the player; `RageFxs.Step` is pure — the active state's grade eases in at the
      old volumes' `Lerp(w, 0.5, 30·dt)`, the other is cut the same tick (the old `OnEnterNormal`), and the cues
      are edges: a state entered → its sting, a meter filled to the brim *while there is a choice to make*
      (Normal) → a softer cue, each once. `RageFxSystem` (after `RageSystem`, `[Exclusive]` for the audio device)
      steps it and plays four new procedural clips; `RageScreen` is a plain drawer at the UI boundary that pushes
      the grade into the post-fx stack or paints the fallback tint. **Deleted**: `RagePostprocess` (the
      MonoBehaviour, its `Volume` class, two coroutines) and `Scripts/RageSystem/`. Gates: 271 gameplay
      assertions (11 new: ease-in, cut, sting once, full cue once and not on spend, a world following the state);
      `ScheduleChecks` re-pinned — 17 Tick batches (`RageFxSystem` alone after `RageSystem`, `SpinSystem` now
      its own batch behind it); **pixel gate** on the `rage` screenshot mid-ENRAGED: 1.19–1.32% of pixels vs a
      same-build floor of 1.09–1.23% (muzzle particles) — unchanged; all 17 scripts byte-identical.
- [x] 7.5 **Pickups are entities** (`Ecs/Pickups.cs`). One entity per pickup — its own static trigger sphere
      (r 1.5, the prefab's) carrying `Pickup { Ammo, Amount }` and `LevelRef` — and a billboard sprite child with a
      `Bob { BaseY, Phase }` that `BobSystem` steps on sim time. `PickupSystem` (`IConsumes<TriggerEnterEvent>`,
      `[Exclusive]` for the HUD's ammo event) pays the player's `Inventory` once per pickup however many overlaps a
      frame reports, refuses on a full pocket (the pickup stays), ignores anyone but the player, and destroys the
      entity — the sprite goes through the hierarchy. The trigger no longer bobs (it never needed to: 0.15 m on a
      1.5 m sphere), so `TriggerFollower` has nothing left to do. `Pickups.Spawn` builds **immediately**, not through
      `PhysicsEntities` (which goes through the command buffer): the level loader stamps `LevelRef.Index` on the
      entity it gets back, and a deferred one is not alive yet — the save would have lost every pickup's key in
      silence. The save keys pickups by `LevelRef` like monsters and spawners, which left **`LevelEntityRef` with no
      reader**: every saved level entity is an entity now, so it is deleted with the GameObject index the save kept
      for it. **Deleted**: `AmmoPickup`, `Prefabs` (the file — pickups were all that was left), the `Bob`
      MonoBehaviour, `LevelEntityRef`.
      **AI seeding fixed on the way.** The first sweep moved every AI script; removing the pickups entirely moved
      them *again, differently* — the AI was not reacting to pickups, it was reacting to *how many entities existed
      before it*: `AiBrain.Rng` (and the spawner's scatter dice) were seeded by entity index (6.4), so every entity a
      later layer adds at load reshuffled every enemy, and the buffered spawn seeded from a deferred entity whose
      index is not final until playback. Seeds now come from **where the thing was placed** (`AiBrains.Seed`:
      FNV-1a over the centimetre-rounded position and the kind). Proof: `peek` / `close` / `approach` are
      byte-identical with and without the pickups in the level. **AI baseline re-recorded** (one last time for
      this reason): `peek` 60 / 150 / 290 moved 0.00 / 7.26 / 18.03, never spotted; `close` the same walk, never
      spotted; `approach` 100 wandering at 10.2 m (no longer the 6.9 m attack — that was a dice roll), 200 heard and
      chased, 290 forgotten, 365 heard again. `rage` / `evade` grunts roll new dice too: `rage` still fills Fury to
      0.54 and spends ENRAGED; `evade` now shows Calm **lost to hits** (0.13 → 0.07, the damage penalty's first
      sighting in-game), a short DEPRESSED with armour regrowing 10 → 13, then Calm refilling to 0.54 under fire.
      Gates: 199 engine + 284 gameplay assertions (13 new: the entity's shape, the bob within its band with the
      trigger still, paid once on a doubled overlap, refused when full, the player only, the sprite going with it);
      `ScheduleChecks` re-pinned — 18 Tick batches (`BobSystem` after `SpawnerSystem`); every combat script and
      `pickup` / `water` byte-identical (**shells 64 → 80, pickups 4 → 3**, as the 7.0 baseline); repeatable.
- [x] 7.6 **Environment volumes; the trigger bridge is gone** (`Ecs/Environment.cs`, `Ecs/EcsObjects.cs`).
      **Water** is a `WaterVolume { Min, Max }` box the view is tested against — no trigger: what goes under is the
      camera, an entity with no body, which is exactly why the old trigger never fired after layer 4.
      `UnderwaterSystem` (LateTick, after the transforms, `[Exclusive]` for `RenderSettings`) hides the sky on the way
      down and puts back *what it found* on the way up (a level without a sky does not get one). **Getting the gate
      to pass found a level bug**: `MapEntity.GetVector` wants three numbers, `dock.map` writes `"size" "1732 1000"`,
      and the fallback default silently built the dock's water **and the sand floor under it** at 200 × 200 — the box
      did not reach where the player goes in (and the dock has floated over a grey void on both sides). `Plane()` now
      reads `"w l"` or `"w h l"`. **Checkpoints** are trigger entities (`Checkpoint { Used }`), `CheckpointSystem`
      saves once, the player only; the **duplicate save is gone** — the old volume woke two listeners, the real one
      and a leftover `PlayerState.OnTriggerEnter` writing a legacy "Save1" through `SaveManager`. **The sky turns**
      from a `SkySpin` singleton on sim time (`SkySpinSystem`). **Deleted**: `TriggerBridge.cs` (`ITriggerListener`,
      `TriggerVolume`, `TriggerFollower`, `GameTriggerBridge`; `EcsObjects` moved to its own file — the combat bridge
      still uses it — and lost a `Clear()` nothing ever called), `EnterWaterCollision`, `SkyboxRotator`,
      `CheckpointAutoSave`, the `Checkpoint` MonoBehaviour, `SaveManager`, `PlayerState`'s trigger half. Gates: 295
      gameplay assertions (11 new: the box test, sky gone under and restored as found, a checkpoint as a trigger
      entity that ignores enemies, a second of sim time turning the sky 1.2°); `ScheduleChecks` re-pinned — 19 Tick
      batches (`SkySpinSystem`), LateTick gains `UnderwaterSystem`. **`water` probe flips**: `sky True` above,
      **`sky False under True` from frame 180 (y −1.43)** — the 7.0 baseline was `sky True` all the way to y −7.2.
      Every other script byte-identical (`saveload` unchanged: the checkpoint was never on its path). **`look`
      screenshot: 2.8% of pixels changed against a 0.000% same-build floor — all of it the water and sand now drawn
      at the size the map asks for** (inspected: blue water on both sides of the dock where there was grey).
- [x] 7.7 **Surfaces come back** (`Ecs/ImpactSurfaces.cs`). `ImpactSurface { Id, Faces }` rides on the **collider
      entity** — an interned surface id, or for a level brush a handle to its `BrushFaces` table (normals → surface
      ids, an `AssetRegistry` like every other shared table). `Surfaces.Of(world, entity, normal)` is the one
      resolver: a living thing bleeds, a tagged collider is its surface (a brush: the face the hit normal points out
      of), anything else the default. The tags go through the **same command buffer** as the colliders they ride on
      (`PhysicsEntities` builds through it, so the entity is not alive until playback). Tagged: level brushes (per
      face, from the texture table), floor planes, lamp posts, props, the test area's dock / sand / posts / crates,
      and the VFX editor's preview props. The hitscan *and* the projectile impacts resolve through it (enemy bullets
      and rockets on walls were hard-coded to default). **Found by the checks**: surface names were interned
      case-blind but stored in whichever spelling came first, so a level texture saying `"Wood"` before anything said
      `"wood"` would have made every wood resolve to a name the case-sensitive VFX bindings do not have — names are
      keys and are stored lower-case now. **Deleted**: the `SurfaceMaterial` and `BrushSurface` MonoBehaviours (the
      physics `SurfaceMaterial` component — friction — is untouched; this one is about what a bullet kicks up).
      Gates: 305 gameplay assertions (10 new: interning and the canonical spelling, tagged / untagged / flesh /
      nothing, a brush's top vs its side); schedule unchanged (no system); new **`surface`** probe — from the player
      start: planks **wood**, a lamp post **metal**, through the water to the floor **sand** (until now every one of
      them was `default`: the resolver had been stubbed since physics went ECS in layer 2). All 19 scripts
      byte-identical. The `fire` screenshot barely moves (0.006%): that script fires level down the pier and its
      impacts land far off and small, so it is not the evidence here — the probe and the checks are.
- [x] 7.8 Close-out (below).

#### 7.8 — Layer close-out (done)

- **Full sweep green, twice**: 199 engine + 305 gameplay assertions; all 20 scripts exit 0 under the gate rule and
  are byte-identical across two complete runs. Combat gates at their values: `killedAtFrame 97`, `rocket` hp 80
  armour 0 y 3.82, `jab` 1→2→0, `glockalt` step 0 / finisher none / ammo 120. New probes: `pickup` shells 64 → 80,
  pickups 4 → 3; `water` `sky False under True` from y −1.43; `surface` wood / metal / sand; `rage` Fury 0.54 then
  ENRAGED at ×1.2; `evade` Calm earned, lost to hits, spent on DEPRESSED.
- **AI baseline** (re-recorded in 7.5, placement-seeded): `peek` / `close` wander unaware (moved 7.26 / 18.03);
  `approach` 100 wandering at 10.2 m, 200 heard and chased, 290 forgotten, 365 heard again.
- **Bench** (`--threads 1 --no-render`, 3 interleaved vs 6.8 `8d55aa1`, median ms/step): crates 11.65 / 11.81,
  ragdolls 15.72 / 15.93, projectiles 0.19 / 0.21, ground 0.88 / 0.92 — flat (ragdolls spread 14.96–17.09; the
  benchmark loads only the engine, whose layer-7 change is two input fields and two actions).
- **Frame / alloc** (3 interleaved, 600 frames, medians): walk 3.9 / 4.0 ms, 50.6 / 48.2 KB; fire 6.5 / 5.0 ms,
  49.7 / 46.4 KB; approach 6.4 / 7.2 ms, 47.7 / 47.7 KB. Twelve more `approach` samples put its median at 6.0 /
  6.9 ms; the `--perf-csv` breakdown places that in scheduling, not system work (open item in the pick-up point).
  Allocation is unchanged or lower everywhere.
- **Layer totals** (`8d55aa1..HEAD`, `src` + `tests`, 10 commits): +2,349 / −1,124 across 51 files. **Deleted**:
  `StyleMeter`, `RageStateData`, `RagePostprocess`, `AmmoPickup`, `Prefabs`, `TriggerBridge` (the bridge and every
  trigger-listener MonoBehaviour), `EnterWaterCollision`, `SkyboxRotator`, `SurfaceMaterial` / `BrushSurface`, the
  `Checkpoint` MonoBehaviour, `SaveManager`; `PlayerState` 225 → 89 lines (health, armour, death, save hooks).
  **Is**: `Rage` / `RageFx` / `Pickup` / `Bob` / `WaterVolume` / `Underwater` / `Checkpoint` / `SkySpin` /
  `ImpactSurface` components, 11 systems, the `RageDef` table in tunables.cfg `[rage]`, 1,191 lines in all, and 125
  new gameplay assertions (180 → 305).
- **Fixed on the way, not in the plan**: the double checkpoint save; the water sky swap (dead since layer 4); the
  dock's water and sand built at 200 × 200 instead of 1732 × 1000 (map size parse); impact surfaces stubbed to
  default since layer 2; AI dice reshuffled by unrelated entities; a corpse keeping its rage on a fall death; the
  dash's undeclared camera access; case-dependent surface names.

**Rage pre-tuning (2026-11-14; superseded by 7.1 — the shared pause clock, the climb-out, the speed cap and the
drift are all gone with the meters they balanced; only the "a state is a timer, not a coroutine" shape survived):**
the "small fixes" from the review brainstorm, constants on `PlayerState` / `StyleMeter`:
(1) **one pause clock** — `StyleMeter.SinceLastAction` = time since the last style score *or* rage gain; rage only
drains in a fight after `StyleMeter.DecayDelay` (2.5 s) on it, and the style decay reads the same clock;
(2) **climb out of DEPRESSED** — a player kill ends it at once (rage 0.5) and the style meter pays a comeback
multiplier step (`NotifyKill` runs after `RegisterHit`); (3) **ENRAGED holds** — the value no longer drains during
it, hits extend it 0.25 s each (capped at 5 s left) and it ends at 0.7, not 0.5; (4) **speed cap** — rage × S-rank
speed capped at ×1.5 in `PlayerCharacter`; (5) **passive drift** — out of combat rage drifts down 0.02/s to a 0.25
floor (it cannot depress). Enraged / depressed are timers, not coroutines, which also fixes a restored ENRAGED /
DEPRESSED never ending. Instrumentation added: `rage` and `style` log rage every 30 frames, `saveload` every 20,
and `style` spawns a third grunt at frame 700 to exercise the climb-out. Evidence (900 frames): `style` — ENRAGED
now holds 1.00 through frame 330 (was draining to 0.20); the frame-700 kill ends DEPRESSED and pays ×2 (246 pts vs
126 at ×1, still DEPRESSED, on HEAD). **Not fixed and the next thing to decide**: `VIOLENT` lasts 30 s after any
shot, so rage drains after a fight is over — `style` still ends DEPRESSED once the room is clear (frame ~600 vs
~480), and `saveload` reaches it from six shots at nothing. Everything else in the sweep byte-identical; `saveload`'s
second save moves 0.39 m (the drift starts rage 0.02 lower, so DEPRESSED arrives a few frames earlier). The
player death at frame ~760 of the 900-frame `style` happens on HEAD too — a scenario artifact.

**Earlier decision (superseded by the direction above)**: the style meter's HUD was to wait for layer 8; with the
style meter retired there is nothing left to defer.

### Layer 8 — UI, HUD, save / load, game flow (~1.4k lines)

**What is there today** (surveyed at 7.8):

| Piece | Where | Shape |
|---|---|---|
| Game flow | `GameSession` (152), static class | `State` (None / MainMenu / Playing), `CheckpointReached`, `LevelPath`; transitions deferred to the next frame by a `SessionBehaviour` MonoBehaviour; `ResetWorld` resets the scene, three `EnragedInjector`s and `SaveData.Current` |
| Level start | `GameManager` (74), `LevelManager` (42) | an `IManager` coroutine chain (`Init` → `WaitUntil` per child → `OnBeginPlay`) left from Unity; it gates when the player exists, and `GameSaveSystem.ApplyWhenReady` polls for that |
| Pause | `PauseMenu` (183) | `IsPaused` static, writes `Time.timeScale` (4 sites), reads `PlayerState.Current` and the `PlayerInputActions` injector |
| Death | `PlayerState.Die` (89 in all), `PlayerDeathSequence` (113), `DeathScreen` (137) | `DeathEvent` → bridge → `Die()` → a coroutine that disables three MonoBehaviours, animates the pivot on unscaled time, ramps `Time.timeScale` to 0.2; the screen offers RESTART (→ `GameSession.Respawn`) / MAIN MENU |
| Falls | `FallRespawn` (249) | **calls `Die()` directly** (no `DamageEvent`, no `DeathEvent`); also the arcade respawn mode, the safe-spot history, the EDGE / THROUGH classifier and an overlay line |
| HUD | `HudController` (204), `GlockCrosshair` (79) | reads `PlayerState` fields and the `CombatController` injector; placeholder rage bars (`DrawRageMeters`) |
| Menus | `MainMenuScreen` (183), `PauseMenu`, `ControlsMenu` (165), `GraphicsMenu` (260), `MenuList` (287) | Clay / `UiBatch` drawers — UI boundary, OOP allowed; only what they *read* changes |
| Save | `GameSaveSystem` (317), `SaveData` (34), `PlayerStateData` (19) | hand-written JSON model (`GameSave` / `PlayerSave` / `EntitySave` / `MonsterSave`); the player half round-trips through the `SaveData.Current` static that `PlayerState.Init` and `Inventory` read in `Start()`; `combatState` dead; level entities already keyed by `LevelRef` |

The outline's "fix the `object cycle` save error" is stale: it was fixed in layer 4 (`[JsonIgnore]` on the derived
vector properties, see layer 4's notes).

**Direction (reviewed 2026-11-14)**

- **One door for death.** Every death is a `DamageEvent` through `HealthSystem`; everything downstream keys on
  `DeathEvent`. A fall is lethal damage with a `Fall` cause.
- **Flow is a singleton, stepped by a system.** `Flow { Phase, CheckpointReached, ... }` with `Phase` ∈ Playing /
  Paused / Dying / Dead (the title screen is a separate scene build, so MainMenu stays a `GameSession` concern,
  Q3). Pause and the death slow-motion are the only writers of the time scale, both through `Flow`.
- **Scene transitions stay outside the world.** `ResetScene` tears the world down, so `GameSession` remains a
  small static boundary that systems *request* transitions from (a request field, played back after the tick) —
  not a system itself.
- **Save captures from queries and applies to entities, by hand.** No generated `Capture()` per config: the save
  holds ~15 fields across four kinds, and a reflective round-trip is grand design with no second reader. `Saves.Capture(World) → GameSave`
  and `Saves.Apply(World, GameSave)` are pure functions over components; `SaveData.Current` goes.
- **Drawers read components.** The HUD and menus keep their OOP shape and read `Health` / `Armour` / `Rage` /
  weapon components and `Flow` through a query — no MonoBehaviour fields, no injectors.

**Answered at review (2026-11-14)**

- **Q1 — save version**: keep v1. Field names stay (`rage` holds Fury); `combatState` is no longer written. **No
  save versioning or migration yet**: saves are not a compatibility promise, so a format change just changes the
  format.
- **Q2 — fall respawn mode**: **dropped.** Falls are always lethal. `FallMode`, `--fall-mode`, worldspawn
  `fall_mode`, the safe-spot history, the footprint probes and the repeat-fall fallback go; the EDGE / THROUGH
  classifier stays (it guards against collision bugs).
- **Q3 — title screen**: stays a separate scene build in `GameSession`; `Flow` exists only in a level.
- **Q4 — death sequence**: port the camera fall as it is (pivot drop, roll, extra pitch, thud bounce, ×0.2 slow
  motion, same timings) as a component stepped on unscaled time. The `die` screenshot is its gate.

**Steps**

- [ ] 8.0 **Instrument the gates.** Before anything moves:
      - `die`: print `DeathEvent` count, `Flow`-to-be state, time scale at frames after death; add a `falldie` probe
        (the `fall` script in die mode) that prints the `DeathEvent` count — **0 today**, the gate 8.1 flips.
      - `saveload`: dump the whole `GameSave` at save and immediately after load and diff them (today it prints
        position / ammo / hp only); add a checkpoint-respawn script (cross a checkpoint, die, RESTART, assert the
        checkpoint pose and state).
      - `pause`: already prints `paused` / `timeScale`; add the main-menu → new game path under `--menu`.
      - HUD: a screenshot gate on `walk` (measure the same-build floor first) and a GameChecks assertion per
        HUD value read (health, armour, ammo, both meters).
      - Make the gate scripts run `dotnet build tests/RipNRage.GameChecks` (the stale-binary trap).
- [ ] 8.1 **Death has one door.** The lethal fall posts a `DamageEvent` (damage = remaining health + armour,
      cause `Fall`) instead of calling `Die()`; `HealthSystem` produces the `DeathEvent`. Gate: `falldie` shows 1
      `DeathEvent`; `die` unchanged; rage's empty-health fallback can go if nothing else needs it.
- [ ] 8.2 **`Flow` singleton and system.** `Phase`, `CheckpointReached`, a transition request; `FlowSystem`
      consumes `DeathEvent` (→ Dying) and the pause action (Playing ↔ Paused), owns the time scale. `PauseMenu.IsPaused`
      and `GameSession.CheckpointReached` become reads of `Flow`. Re-pin `ScheduleChecks` in its own commit.
- [ ] 8.3 **The death sequence is a component**, ported as it is (Q4). `DeathCam { T, Roll, StartPose }` on the
      pivot, stepped on unscaled time, same curve, timings and ×0.2 slow motion; input / combat / look are switched off by `Flow.Phase` (systems already read it) instead of
      disabling MonoBehaviours. `PlayerDeathSequence` deleted; `PlayerState.Die` / `OnDied` deleted if nothing
      else reads them.
- [ ] 8.4 **Falls as a system.** Kill height as a singleton from the loader; the lethal check and the EDGE /
      THROUGH classifier as a system (its overlay line becomes a drawer read). The arcade respawn is dropped (Q2):
      `FallMode`, `--fall-mode` and worldspawn `fall_mode` deleted with it. `FallRespawn` deleted.
- [ ] 8.5 **Save captures and applies.** `Saves.Capture` / `Saves.Apply` over components; the player half reads
      `Health` / `Armour` / `Rage` / the weapon components directly; `SaveData`, `PlayerStateData`,
      `ApplyWhenReady`'s polling and the `SaveData.Current` reset in `ResetWorld` deleted. v1 field names kept,
      `combatState` no longer written (Q1). Gate: the 8.0 `GameSave` diff is empty.
- [ ] 8.6 **Level start is a function.** `GameManager` / `LevelManager`'s `IManager` coroutine chain replaced by
      the build itself (the player exists when `TestArea.Build` returns, or `Flow` knows when it does).
      `EnragedInjector` resets in `ResetWorld` shrink to what still exists.
- [ ] 8.7 **HUD reads the ECS.** `HudController` reads components through a query; `PlayerState`'s remaining
      facade goes if this was its last reader (else layer 9). The rage meters get a real pass (still a drawer).
      `EnragedInjector<HudController>` deleted.
- [ ] 8.8 **Menus read the ECS.** `PauseMenu` / `DeathScreen` / `MainMenuScreen` read `Flow` and components;
      RESTART / MAIN MENU / Continue go through the `Flow` transition request.
- [ ] 8.9 Close-out: full sweep twice, byte-identical scripts, bench / frame / alloc vs 7.8 (and settle 7.8's open
      `approach` scheduling item with a per-batch profile), layer totals, pick-up point for layer 9.

### Layer 9 — Remove the legacy model

- Delete `GameObject`, `Behaviour`, `Scene`, `Transform`, legacy renderer components, `LegacySceneProducer`, legacy
  physics components. `Application` runs only the `World`.
- Re-run every gate; update `README.md`.

### Layer 10 — Editors on configs

- Level editor places prefabs and edits `IComponentConfig` fields by reflection (like the VFX property grid).
- `.map` entities reference prefabs; worldspawn keys become a singleton config.

## Conventions

- New component: a `struct : IComponent` plus one `ComponentConfig<T>` (JSON field names = config field names).
  Mark toggled state / tags `[SparseComponent]`.
- New system: implement the event interfaces you need; declare `[Reads]` / `[Writes]` or it runs alone; order with
  `[UpdateAfter]` / `[UpdateBefore]`. Never change structure directly inside a system — use `ctx.Commands`
  (`ctx.CommandsFor(worker)` inside chunk jobs).
- Gameplay events: `struct : IPostProcessEvent`, `ctx.Post(...)`; consumers implement `IConsumes<T>`.
- Every layer adds tests to `tests/RipNRage.EngineChecks` for its engine-side pieces.
