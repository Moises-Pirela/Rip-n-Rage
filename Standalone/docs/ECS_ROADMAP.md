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
- [ ] Layer 4 — Input, camera, player **(next — pick up here)**
- [ ] Layer 5 — Weapons, combat, damage
- [ ] Layer 6 — Enemies, AI, spawning
- [ ] Layer 7 — Pickups, rage, style, environment
- [ ] Layer 8 — UI, HUD, save / load, game flow
- [ ] Layer 9 — Remove the legacy model
- [ ] Layer 10 — Editors on configs

### Pick-up point for the next session (written 2026-11-10, at `0ef0b23`)

Layers 0–3 are done; start **layer 4**. Everything below is already true in the tree:

- The player cannot move: `FPSController` / `FallRespawn` kept only their public surface (tunables, `AddImpulse`,
  `IsSliding`...) — the movement logic was deleted with the legacy physics; rewrite it on `CharacterMotor`
  (`Ecs/Physics/Movers.cs`: intent in `Desired` / `Pending*`, state out; `PendingHeight` is the slide crouch).
  The capsule + 80 kg body the player needs is described in a comment in `PlayerPrefab.Build`.
- **Delete the `ViewSprite` bridge** (`RipNRage.Game/Ecs/ViewSprite.cs` + `ViewSpriteEventSystem`): the camera and the
  first-person viewmodel (weapon / fists / muzzle flash, `RenderLayer.FirstPerson`) become camera-child entities; the
  weapon scripts then talk to `SpriteAnimator` directly. The animation editor's preview also uses the bridge.
- Hooks to re-point when the camera is an entity: `RagdollSystem.Viewer` and the `EcsRenderProducer` camera priority
  (an ECS `CameraLens` already outranks the legacy camera).
- Restore the overlay's MOVER line (`Program.cs`, removed in 2.L) from `CharacterMotor` state.
- **Strafe**: `--script strafe` has passed vacuously since 2.L (no movement). With the player on `CharacterMotor` it
  becomes meaningful again — and the pre-existing dock-edge fall (macOS + WSL, `(-18.9, 1.2, 6.7)`, see the 2.0
  baselines) must be investigated as part of this layer.
- `trigger_checkpoint` / water / pickups still fire nothing (`TriggerSystem` posts events, but the listeners are
  GameObjects): wire the player's trigger events when it is an entity, or leave for layers 7–8 as planned.
- Gates as in layer 2/3 close-outs: compare bench interleaved against the previous commit (`--threads 1` for the
  stable numbers; the per-step metric only counts frames with a fixed step); ragdolls sit at 8.3–8.7 ms vs budget 8
  (knobs: pieces sleep less than boxes, and legacy-faithful CCD on every piece costs ~0.85 ms serially).

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
| Tests | `tests/RipNRage.EngineChecks/` — `EcsChecks.cs`, `TransformChecks.cs`, `PhysicsEcsChecks.cs` (run in CI) |
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

- `InputState` singleton component written by an `InputSystem` (first in Tick).
- Player as a prefab: `PlayerTag`, `CharacterBody`, `PlayerMotor` (speeds, jump, slide, wall run, dash — currently
  `MovementSettings`), `PlayerLook`, `CameraLens`, head bob / sway / trauma shake as components.
- Systems: `PlayerMoveSystem` (FixedTick), `PlayerLookSystem`, `CameraShakeSystem`, `HeadBobSystem` (LateTick before
  TransformSystem — add `[UpdateBefore(typeof(TransformSystem))]`).
- The first-person viewmodel is a child entity of the camera with `RenderLayer.FirstPerson`.

### Layer 5 — Weapons, combat, damage (~0.8k + shared structs)

- `Inventory` / `Ammo` components, `Weapon` (data from a weapon config), `Spread`, fist jab combo state.
- `DamageEvent`, `DeathEvent`, `HitEvent` (IPostProcessEvent); `HealthSystem` consumes damage.
- Hitscan via physics queries, projectiles as entities (rocket = rigid body + trigger + explosion event).

### Layer 6 — Enemies, AI, spawning (~1.1k lines)

- Behavior tree kept: the tree is shared data (loaded once), each enemy has `BehaviorTreeState` (blackboard + node
  status). `BehaviorTreeSystem` ticks trees; leaf actions read/write components.
- `EnemyData` → prefab configs. `DungeonMaster` / spawners → `SpawnerSystem` with prefab references.

### Layer 7 — Pickups, rage, style, environment (~0.4k lines)

- `Pickup` + trigger events; `Rage` (sparse flags for enraged / depressed), `StyleMeter`, environment props.

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
