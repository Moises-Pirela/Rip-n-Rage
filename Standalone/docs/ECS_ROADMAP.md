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
| Editors | Editor integration of configs comes **later** (after gameplay) |
| Assets in components | **Handles, not references**: `AssetHandle<T>` into an `AssetRegistry<T>` (`Ecs/Assets.cs`); registries keep the name a config baked from (`NameOf`) so handles serialise back to config data. Render registries: `RenderAssets`. Physics collider shapes reuse the same pattern |

## Status

- [x] **Layer 0 — ECS core** (`067537e`): storage, queries, command buffers, scheduler, events, prefabs, chunk jobs. 36 assertions.
- [x] **Layer 1 — Transforms + rendering** (`3ed7e45`): hierarchy (verified against legacy `Transform`), the renderer
  consumes `RenderFrame` draw items from producers, ECS render components + `EcsRenderProducer`, `--ecs-demo`. Pixel
  parity with the pre-ECS renderer is within run-to-run noise.
- [ ] Layer 2 — Physics
- [ ] Layer 3 — Animation, audio, particles, destruction
- [ ] Layer 4 — Input, camera, player
- [ ] Layer 5 — Weapons, combat, damage
- [ ] Layer 6 — Enemies, AI, spawning
- [ ] Layer 7 — Pickups, rage, style, environment
- [ ] Layer 8 — UI, HUD, save / load, game flow
- [ ] Layer 9 — Remove the legacy model
- [ ] Layer 10 — Editors on configs

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
| ECS physics | `src/RipNRage.Engine/Ecs/Dynamics/` (components, configs); collider table `src/RipNRage.Engine/Physics/ColliderTable.cs` |
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
  - [x] 2.1 Physics components + configs + tests (data only, nothing simulates them). `Ecs/Dynamics/`: `Body`,
        `BodyVelocity`, `ColliderShape` (one component, `ShapeKind` enum; hull / mesh by handle via `PhysicsAssets`),
        `SurfaceMaterial`, `CollisionFilter`, `[Sparse] Trigger`, `[Sparse] Sleeping`. Namespace is `Dynamics` because
        `Ecs.Physics` would shadow the legacy `Physics` facade. Convex shapes are code-only for now (no config form).
  - [x] 2.2 Internal collider table (`Physics/ColliderTable.cs`): slots with generation-checked `PhysicsProxy`
        handles, owner = legacy `Collider` or ECS `Entity`; broadphase grids store slot indices. Gates: single-threaded
        bench equal or slightly faster (ragdolls median 34.8 vs 35.9 ms/step), strafe fails identically to baseline,
        look-script pixel diff 0.037%. Multi-threaded bench is too noisy here (ragdolls 6–16 ms) to gate on: use
        `--threads 1`, interleaved with a baseline worktree.
  - [ ] 2.3 `RigidSolver` / contact cache / islands on table indices instead of `RigidBody` / `Collider` objects.
  - [ ] 2.4 `PhysicsSyncSystem` + `PhysicsStepSystem` in FixedTick: ECS entities enter the table; poses written back
        to `LocalTransform`; fixed-step lockstep with the legacy `FixedUpdate`.
  - [ ] 2.5 Joints as components; constraints keyed by table index.
  - [ ] 2.6 `CharacterBody` / `KinematicBody` components and systems; triggers.
  - [ ] 2.7 Contact / trigger events as `IPostProcessEvent`s; query API returns `Entity` for ECS hits.
  - [ ] 2.8 ECS physics demo prefabs, full gate run, strafe investigation, roadmap update.
- **Baselines (2.0, `37794e8`, Linux/WSL, 16 threads, `--no-render`)** — physics ms/step avg:
  crates 1.19 (PASS), ragdolls 11.21 (**FAIL at baseline**: 25.4 ms frame, over budget — pre-existing on this
  machine), projectiles 0.48 (PASS), ground 0.15 (PASS). `--script strafe --frames 620` **also fails at baseline here**
  (falls off the dock edge at (-18.9, 1.2, 6.7), same symptom as macOS). Gate = no worse than these.
- Ordering note: the ECS currently updates **after** the legacy scene and `Physics.Step()`. When physics moves into
  `FixedTick`, the ECS fixed step and the legacy `FixedUpdate` must stay in lockstep (same `fixedDeltaTime`, same step
  count per frame) until gameplay is ported.

### Layer 3 — Animation, audio, particles, destruction (~3.1k lines)

- `SpriteAnimator` component (clip/controller reference, state, time, parameters) + `AnimationSystem` (Tick) writing
  `SpriteRender.Sprite`; animation events become `IPostProcessEvent`s.
- `AudioSource` / `AudioListener` components + `AudioSystem` (LateTick) driving OpenAL voices.
- Particles: keep `ParticleWorld` simulation; spawn requests through a `SpawnEffectEvent`; effect anchors as entities.
- `SpriteRagdoll` → spawns pieces as entities with physics components and joints (depends on layer 2).

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
