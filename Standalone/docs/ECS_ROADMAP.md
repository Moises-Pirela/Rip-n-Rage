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
- [ ] Layer 6 — Enemies, AI, spawning
- [ ] Layer 7 — Pickups, rage, style, environment
- [ ] Layer 8 — UI, HUD, save / load, game flow
- [ ] Layer 9 — Remove the legacy model
- [ ] Layer 10 — Editors on configs

### Pick-up point for the next session (written 2026-11-12, after 5.6; layer 6 is in progress — see its step list)

Layers 0–5 are done. Layer 6 (enemies, AI, spawning) is under way: 6.0–6.4 are in, next is **6.5 the attack**. The
standing direction — the behaviour trees replaced by a **data-oriented state tree**, not ported as-is — is done
(6.2). Established rules: ECS + functional,
OOP only at UI boundaries; simplicity over grand design (fewer lines per feature); no speculative fields — nothing
goes in a component until a reader exists in the same commit; definitions as shared immutable tables, state in
components, behaviour in systems; instrument a gate before porting what it watches.

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
- [ ] 6.5 **The attack is components.** `EnemyAttack { WindupLeft, ShotsLeft, BurstTimer, Cooldown }` + system
      (telegraph → burst via `Projectiles.Launch` → recover, on scaled `ctx.DeltaTime`), `Stagger` from
      `IConsumes<HitEvent>` (which also brakes the body — the legacy `StopAgent` has been empty since 2.L, so a hit
      does not stop an enemy today), the muzzle offset as data turned by the `Heading`. **`AIAgent` deleted.** Enemy
      damage is live since 6.4: pin `close`'s `armour 40` and watch the scripts a hurt player changes.
- [ ] 6.6 **The enemy is only an entity.** `EnemyData` → definition records + prefab config; `EnemyDestruction` /
      corpse → an `IConsumes<DeathEvent>` system reading the event payload (`DamageInfo` dies); `AIState` and the
      enemy's `EcsObjects` entry go; `GameSaveSystem` reads `Health` / `LocalTransform` off the entity; the grapple
      point becomes a height constant.
- [ ] 6.7 **Spawning.** `Spawner { Def, Count, Interval, Radius, ActivateRange, MaxAlive }` + `SpawnerSystem`
      (entities, not GameObjects); `LevelEntityRef` / save round-trip kept honest by `saveload`.
- [ ] 6.8 Close-out: full parity run, the AI probes re-recorded as the new baseline, `--threads 1` interleaved bench,
      alloc/frame (the burst coroutine's allocations should be structurally gone), close-out section.

### Layer 7 — Pickups, rage, style, environment (~0.4k lines)

- `Pickup` + trigger events; `Rage` (sparse flags for enraged / depressed), `StyleMeter`, environment props.
- **Already live since 4.2** (asked for alongside layer 4): trigger callbacks fire again.
  `RipNRage.Game/Ecs/TriggerBridge.cs` reinstates the deleted `ITriggerListener` contract on top of the ECS
  `TriggerEnterEvent` / `TriggerExitEvent`: `EcsObjects` is the entity ↔ GameObject directory the bridges share,
  `TriggerVolume` owns a volume entity for a GameObject (`TriggerFollower` keeps a moving one on top of it — the
  bobbing ammo pickups), and `GameTriggerBridge` notifies **both** sides of an overlap, as Unity's `OnTriggerEnter`
  did. Wired: `trigger_checkpoint` (saves), the water volumes, and the ammo pickups — which never had an ECS trigger
  at all and now get their prefab's r = 1.5 sphere. `AgentVisionSensor` (layer 6) and the projectiles (layer 5) still
  wait for their layers. These listeners become `IConsumes<T>` systems and the bridge goes away in layer 9.

### Layer 8 — UI, HUD, save / load, game flow (~1.4k lines)

- HUD / menus stay Clay drawers but read ECS state (queries / singletons) instead of `MonoBehaviour` fields.
- Save / load serialises entities via their configs (round-trip runtime → config needs a `Capture()` counterpart
  to `Bake()` per config — decide whether that is generated or hand-written). Fix the existing
  `object cycle` save error as part of this.
- Game flow (title, pause, death loop, checkpoints) as systems + singleton state.

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
