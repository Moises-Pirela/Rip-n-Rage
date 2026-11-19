# Content pass — plan

Status: **V1–V1e, V2, V4, V5 built** (2026-11-19), plus swimming and caustics; the rest planned. Scope: the two unfinished weapons (katana, sonic shotgun),
throwables, more enemy kinds, and the Harbor — one sprawling level ending in a speedboat chase onto a container
ship. Written from a survey of the code as of `07afed7` (layer 11 closed); paths are relative to `Standalone/`.

Each **F** step is gameplay foundation code, each **V** step is vessel/physics engine work, and each **C** step
is content on top. The game has not released, so **nothing here preserves save compatibility** — bindings, slot
order, the ammo enum and `GameSave` all change freely.

**The chase is built for real.** Boats are dynamic rigid bodies floating on water by buoyancy, driven by thrust,
steered by pilots, at real speeds. The player and enemies ride them, fight on them, and jump between them under
the same physics as everything else. Nothing is faked until the real version has been made as robust as it can
be. Part three is that work, and it is the largest single piece of the pass.

---

## Part one — where content stands today

### Weapons

Glock, M1887, MG338 and the rocket launcher are done (`GameAssets.cs:376-446`, listed in `PlayerPrefab.cs:31`).

| | State |
|---|---|
| **Katana** | Atlas loaded (7 frames), `swordslash1.wav` loaded. No clips, no controller, no data. |
| **Sonic shotgun** | Two loose sprites (rest + kick, nearly identical), `SonicShotgun.wav`. Nothing else. |
| **Throwables** | Nothing. An unused `ammo_granade` HUD icon. |

The katana art is two attacks: **01** held level (idle) → **02** swing → **03/04** smears, and **05** two-handed
raise → **06** downward swing → **07** smear. A light slash and a heavy overhead chop.

What blocks them is the fire path, not the art:

- `WeaponFireSystem` (`Ecs/Combat/Weapons.cs:109-251`) only fires *while held*, gated on ammo (`:143`); no
  charge/release state.
- A shot is 100 m hitscan rays or a projectile — no sweep, no cone, no projectile gravity, and projectiles do not
  inherit the shooter's velocity.
- `Weapons.All` is fixed at four; `weapon_1..4` hard-wired (`CombatController.cs:108-111`).
- `isUnlocked` is never read; no `weapon_*` pickups.
- `AmmoType.ENERGY` has an icon, a pickup sprite and `item_ammo_energy` — and no weapon.
- Free gamepad buttons: **X** and right-stick click.

### Enemies

- **Rifleman**: ranged, 3-round burst of `EnemyBullet`. The only kind placed in a map.
- **Brawler**: a working melee rusher that **cannot be placed** — never registered (`EntityRegistry.cs:111`).
- AI is a fixed state machine (`AiBrain.cs:20`: Idle / Wander / Chase / Attack); per-kind data is numbers only.
- Enemies fire one `EnemyBullet` kind (`EnemyAttack.cs:186`) and aim straight at the target — no lead.
- **Enemies never jump.** The motor has `PendingJump`, but nothing in the AI ever sets it.
- Navigation (layer 11): A* over a baked span grid, one body size, **world-static** — it cannot express a
  floor that moves (`LAYER11_NAV.md:848-871`).
- The kill plane applies to the **player only** (`Falls.cs:75`).

### Levels

One real level (`dock.map`), drifted from its generator (`DockLevel.cs`). No exit, no level sequence, no level
logic (`targetname` only links joints), ammo-only pickups, prototype textures, no music, MP3 not decoded.

### The physics the chase stands on

What already works:

- **Riding**: `CharacterMotor` reads the ground body's point velocity (`Movers.cs:239`, rotation included) and
  adds it to the motor target (`:212`) and to the ride spring (`:204`). Player and enemies share the motor.
- **Brush entities in the format**: `MapEntity.brushes` exists and the parser reads brushes inside any entity
  (`MapFile.cs:99`) — "only worldspawn uses them in this engine".
- **Convex hulls from planes** (`ConvexHull.FromPlanes`) and GJK distance (`ConvexDistance.cs`), used for static
  brush colliders today.
- **Imported meshes**: `MeshRender.mesh` can name an .obj.
- 50 Hz fixed step × 4 substeps (`tunables.cfg:155,171`), warm-started solver, continuous collision, physics
  materials, water volumes.

What does not, and is in the way:

1. **Movement is written for ground that stands still.** On a deck doing 25 m/s:
   - In the air the motor drives toward `Desired` with no frame (`Movers.cs:212-215`). With no input it brakes
     at `AirAcceleration` (20 m/s²) **toward world zero**, so a standing jump off a 25 m/s boat lands about 6 m
     behind its takeoff point.
   - Dash sets an absolute velocity `dir * DashSpeed` (`PlayerCharacter.cs:398`), so a forward dash on a boat
     throws you backwards.
   - Wall-run targets `WallRunSpeed` in world space while airborne (`:351`), and the wall jump subtracts world
     velocity (`:361`). A moving wall leaves you behind.
   - Slide seeds its speed from world velocity (`:262-265`), then the motor adds deck velocity on top: you get
     twice deck speed.
   - Readers of world speed: movement spread (`Weapons.cs`), footsteps (`PlayerCharacter.cs`) and `PlayerLink`.
     On a boat you'd have full movement spread and constant footsteps while standing still.
2. **The ground never feels who stands on it.** The ride spring and the motor push only the character. A boat
   doesn't dip when four enemies land on it, and running across a dinghy doesn't push it.
3. **No compound bodies.** A body has one collider (`SolverBody.collider`) and inertia is a diagonal from one
   shape (`SolverBody.cs:202-227`). Mesh colliders are static-only, and convex-mesh bodies are not implemented
   (README, Known gaps). A hull with a cabin and rails can't be one rigid body.
4. **No buoyancy or hydrodynamics.** Water is a swim volume and a kill height.
5. **Rigid-body state is not saved** (README, Known gaps), so a mid-chase checkpoint can't restore the flotilla.

What a moving ship with interiors adds. The ship moves, and so does everything on it: interior and exterior.

6. **Placed things assume the world stands still.** A static collider re-bakes only when its own
   `LocalTransform` changes (`PhysicsSystems.cs:253,273`), and a moved static is a teleport with no velocity.
   - A trigger, wall or prop parented to the ship would stay at its spawn point while the ship sails off.
   - Anything that is moved by hand gives riders no ground velocity.
7. **The kill plane is a height** (`Falls.cs`: `pos.y >= killY`). Anyone in the ship's hold or engine room below
   the waterline is dead.
8. **Water is a world-space box** (`WaterVolume.Contains`, `Environment.cs`).
   - A camera in the hold below sea level counts as underwater: the sky turns off and the view goes underwater.
   - The sea surface also draws straight through every interior below the waterline.
9. **Hollow hulls don't float by their walls.** If V4's buoyancy came from the collision brushes alone, a hollow
   ship would displace only its steel and sink. Speedboat cockpits have the same problem on a small scale.
10. **Lighting is built for a static world.**
    - The shadow map caches static geometry and covers 70 m around the camera (`tunables.cfg:89-96`).
    - A 300 m moving hull is neither static nor within that range, so interiors would leak sunlight at their
      edges.
11. **Decals are world-space particles** (splats and scorch marks: `ParticleEffect.cs:19`, sticking by raycast
    `:169`). Blood on a moving deck would hang in the air behind the ship.

Already fine: **multi-floor navigation**. A nav column holds several spans, "high floor first" (`NavGrid.cs:59`), so
stacked decks and stairwells fit layer 11's bake as it is. The occlusion buffer is rebuilt every frame, so a moving
hull can occlude.

### Stale docs

`ROADMAP.md` still lists as open: player death, blood/hit VFX, particle VFX, enemy navigation, menus, gamepad and
CI. All of these are done, and its file links point at deleted files. `README.md` advertises a **style meter**
(deleted in layer 7.1) and a **behaviour tree** (the AI is a state machine). Both are fixed in F0.

---

## Part two — weapons, enemies, level logic

### F0. Housekeeping

- Refresh `ROADMAP.md`; point "Later: content" at this file.
- `README.md`: **remove the style meter**; fix the behaviour-tree and straight-line-nav claims.
- Register `monster_brawler`; make the editor's enemy quick-place cycle kinds.
- Audio decoding for **WAV, OGG and MP3** (decided), which also unsilences `mg338.mp3` and `MeleeHit_01.mp3`
  and gives music a format to ship in.

### F1. Weapon foundation: fire modes, charge, slots, throwables

Give `WeaponData` a **fire mode** and let `WeaponFireSystem` dispatch on it:

| Mode | Behaviour | Used by |
|---|---|---|
| `Hitscan` | today's rays | Glock, M1887, MG338 |
| `Projectile` | today's launch | Rocket launcher |
| `Sweep` | after `hitDelay` s, damage every damageable in an arc (`range`, `arcDegrees`) with line of sight, `Melee = true` | Katana |
| `Cone` | instant: every target in a cone, damage and impulse falling off with distance | Sonic shotgun |

Plus:

- **`ammoPerShot`** (0 = ammoless) and **charge** (`chargeTime`, `chargeMaxAmmo`; hold builds `Weapon.Charge`,
  release fires once at that scale; a tap under 0.15 s fires uncharged; the animator gets `IsCharging`).
- **Projectile physics**: `ProjectileDef.Gravity`, and **projectiles inherit the shooter's velocity**: a rocket
  fired from a boat at 25 m/s leaves at 50 + 25. This is right on land too, at running speeds.
- **Living knockback**: a `DamageEvent` impulse applies to a living body, not only the death ragdoll.
- **Enemy kill plane**: enemies below `Worldspawn.KillY` die, so a knockback into the harbour is a kill. It goes
  through the same function as the player's fall, so that V9b's "submerged" rule later replaces both at once.
- **Slots and bindings**:

  | 1 | 2 | 3 | 4 | 5 | 6 | G / pad X |
  |---|---|---|---|---|---|---|
  | Katana | Glock | M1887 | Sonic shotgun | MG338 | Rocket launcher | Throw |

- **Throwables**: a `throw` action that throws instantly on press, independent of the weapon in hand.
  `AmmoType.GRENADE` takes the enum from 5 to 6, which touches `AmmoBlock`, `AmmoLimits`, `NewGameAmmo`, the
  `ammo_granade` icon and `GameVfx.AmmoKey`.
- **Unlocks**: `isUnlocked` becomes a bitmask on `Inventory`, and is saved. Switching skips locked slots, and
  `weapon_<name>` pickups unlock a weapon. A new run starts with the Glock.

**Checks.**
- A sweep hits two enemies in its arc and not one behind a wall.
- A cone's damage falls off with distance.
- A 50 % charge fires once at 50 % scale.
- An ammoless weapon fires with an empty inventory.
- A grenade arcs, bounces and explodes.
- A rocket fired while moving carries the shooter's velocity.
- An enemy knocked below the kill plane dies.
- Locked slots are skipped.

**Commit.** `Content F1: fire modes, charge, throwables, six slots and unlocks`

### C1. Katana (slot 1)

- **Slash** (tap; frames 01→02→03→04): `Sweep`, 2.6 m, 110°, 0.06 s hit delay, 0.32 s refire.
  - **500** damage to every target in the arc: two slashes kill a rifleman, one when enraged (×2.5 melee,
    `Rage.cs:64`).
- **Chop** (hold; 05 held, release 06→07): `Sweep`, 3 m, 50°.
  - **1500** damage at full charge (0.6 s), heavy knockback, long stagger.
- **Deflect**: a slash destroys `EnemyBullet`s in its arc and feeds Calm. Stretch goal: reflect them.
- Assets and wiring:
  - Clips `Katana_Idle` / `Slash` / `Raise` / `Chop`, plus `KatanaWeaponController`.
  - `swordslash1.wav` on the swing, `MeleeHit` on contact.
  - A `melee` blood key.
  - Finisher: `Bash` for now.

### C2. Sonic shotgun (slot 4)

- **Tap**: `Cone`, 12 m, 30°.
  - **300** damage at point blank, falling to 30 % at range; strong impulse.
  - 1 energy, 0.6 s refire.
- **Charged** (up to 1.2 s, 1–5 energy): 18 m, 45°, damage ×3, impulse ×4. Clears enemy bullets.
- **Sonic jump**: a charged blast pushes the player back along the aim. With real boats it also pushes **boats**:
  blasting a hull shoves it sideways, and blasting the water under your own boat's stern lifts it.
- Ammo is `ENERGY`: limit 100, new game 0.
- Presentation: the charge is sold by viewmodel tremble, a blue emissive ramp, `sonic_charge` / `sonic_wave`
  particle effects and a procedural whine.
- Finisher: `PointBlankShot`.

### C3. Throwables

- **Frag**: 1.6 s fuse, bounces, the rocket's explosion at ~0.7× radius. Carry 5. Thrown from a boat, it carries
  the boat's velocity (F1).
(One throwable for now — decided.)

### F2. Level foundation: logic, pickups, flow

1. **Event wiring**: `target` → `targetname`, plus `trigger_once` / `trigger_multiple` / `trigger_relay`. Doors
   and spawners obey `Use`.
2. **Encounters**: `trigger_encounter` locks doors and wakes spawners on entry, and fires `on_clear` when all its
   enemies are dead. Spawners get waves and place on walkable spans the player can't see.
3. **Pickups**: `item_health`, `item_armor`, `weapon_<name>`, `item_grenade`.
4. **Hazard**: `trigger_hurt`.
5. **Flow**:
   - `level_exit` + an end screen.
   - `trigger_secret`.
   - `levels/campaign.json`.
   - `GameSave` stores the level, unlocks and grenades.
6. **Editor**: draw `target` links as lines; quick-place for the new classes.

**Commit.** One per item — `Content F2.n: …`

### F3. Enemy foundation

- Per-kind **projectile**, **sounds**, **tint** (recoloured rifleman art until real sheets arrive), **size and
  scale**, and **directional armour** (`frontArmor`).
- **Aim lead**: enemies solve the intercept for their projectile speed against the target's velocity **relative
  to the shooter**. Without it, enemies on boats either hit nothing (diverging boats) or everything (parallel
  boats).
- **Strafe** while attacking and **Investigate** on a cold `LastKnown`.
- An **anchored** role flag: stands, turns and shoots, never requests a path. It's for marksmen in towers and
  mounted guns.

### C4–C6. Enemy roster

Every kind ships on recoloured rifleman art.

| Kind | Role | Counter |
|---|---|---|
| **Brawler** (exists) | Melee rusher | Shotgun, slash, sonic tap |
| **Swarmer** | Small, fast, ~150 hp, in packs | Katana cleave, sonic cone |
| **Marksman** | Anchored long range, visible aim line before the shot | Closing, cover, deflect |
| **Grenadier** | Lobs arcing explosives | Movement, killing it first |
| **Shield** | ~90 % frontal armour, escorts riflemen | Flank, chop, sonic knockback |
| **Boarder** | The chase's own enemy: agile, jumps boat to boat (V8), closes on the player's deck and fights up close | Shooting it mid-air, sonic it into the water, katana |
| **Heavy** | Large, armoured, rockets — encounter finales and the ship | Everything, rage |

- **C4**: Brawler, Swarmer, Marksman.
- **C5**: Grenadier, Shield.
- **C6**: Boarder (needs V8) and Heavy (needs a second nav profile).
- Riflemen and grenadiers jump too once V8 lands. The boarder's difference is that boarding is its plan, not a
  fallback.

---

## Part three — vessels (V)

The order is chosen so that each step can be tested on its own, before any boat exists, and everything after it
stands on tested ground.

The test rig comes first: `--vessel-test`, a scene in the family of `--ecs-demo` and the physics benchmark, that
grows with each step. It starts as a kinematic platform at 25 m/s over open ground. By V5 it's one speedboat on
water with the player aboard, and by V8 it's two boats side by side with enemies jumping between them. Every V
step adds its checks to it and a gate to `gates.sh`.

### V1. Movement in a moving frame — **BUILT**

The fix for "what does not work" #1. It doesn't need a single boat; it is tested on a kinematic platform moving
at chase speed. The reaction force on the ground (#2) is **not** done — see the remainder at the end of this step.

- **`CharacterMotor.FrameVelocity`** — the velocity of the frame the character moves in:
  - **grounded**: the ground body's point velocity (as `GroundVelocity` today);
  - **wall-running**: the wall body's point velocity at the contact;
  - **airborne**: **frozen at the value it had on leaving the surface**. The frame you jumped from is inertial,
    and you keep its velocity until something acts on you.
- The motor targets `FrameVelocity + Desired` everywhere, not just on the ground. Air control steers **relative to
  the launch frame**: no input means ballistic, with no braking toward world zero.
- Dash, slide, wall-run, wall jump and bunny-hop are all computed in the frame. A dash is
  `FrameVelocity + dir * DashSpeed`, and a slide seeds from velocity relative to the deck.
- `RelativeVelocity` (`Linear − FrameVelocity`) replaces world velocity for movement spread, footsteps, head bob
  and `PlayerLink`.
- **Yaw follows the deck**: while grounded, the look yaw turns with the ground body's yaw rate, so a turning boat
  carries your aim with it. Camera roll stays level, as an inner ear would keep it.
**What it does, as built** (`Movers.cs`, `PlayerCharacter.cs`, `PathFollow.cs`, `Weapons.cs`):

- `CharacterMotor.FrameVelocity` and `FrameYawRate`, with `Relative(v)` for everything that reads a speed.
- The motor targets `FrameVelocity + Desired` in the air as well as on the ground, and judges braking on what was
  *asked for*, so letting go stops you on the deck rather than braking towards a standstill the sea is in.
- Jump rise adds to the deck's own rise; a dash is `FrameVelocity + dir × DashSpeed`, and ends on a run relative
  to the deck.
- Slide seeds from relative speed; the wall jump subtracts relative speed; a wall run takes the wall's own
  velocity as its frame.
- Spread and footsteps read relative speed, so a deck under way is not "running" for either.
- The look yaw turns with the deck's yaw rate.
- **The AI**: an enemy on moving ground drops its path and walks straight at its goal — the grid is baked for the
  world and the sea is not in it (`PathFollow.cs`). Enemies ride decks on the same motor, so they get all of the
  above for nothing.

**Checks** (`tests/RipNRage.EngineChecks/PlatformChecks.cs`, `tests/RipNRage.GameChecks/VesselChecks.cs`):

- The same five measurements are taken twice, on still ground and on a deck at 25 m/s, and compared to each
  other: standing still, a standing jump, walking, stopping, and a dash. The deck must behave like still ground.
- Standing still on a deck drifts < 0.1 m and reads zero relative speed.
- A standing jump lands within 0.1 m of where still ground lands it.
- The gate sweep's movement scripts (`walk flowmoves evade ledge stay maze`) were run before and after and
  compared: **no probe line differs** apart from the motor's new fields, which are all zero on still ground.

**The remainder** — **built, and reformulated** (V1e below): the reaction force and the grip limit.

### V1e. The ride is a constraint — **BUILT**

The character used to float on a spring the motor applied to its own velocity before the step, with the
reaction thrown at the ground as a force. It is now a `RideConstraint` (`Physics/Constraints.cs`) solved by the
solver with the contacts: a one-sided soft spring along the ground normal for the support, and two motor rows for
the feet. `Movers.cs` only places it each step from the ground probe.

**A correction first.** The previous version of this entry blamed a static-weight shortfall on "the two springs
fighting". **That was wrong.** The measurements it rested on were corrupted by two bugs, and once those were fixed
the old spring transferred weight exactly:

1. **Ghost colliders.** `World.Dispose()` never ended the simulation, so every disposed test world left its bodies
   in the global physics world, still simulated. The next world's characters stood on them. The checks had been
   placing each scenario at a different x to dodge it. Fixed: `Dispose` now calls `EndSim`.
2. **Chattering buoyancy.** A sample was skipped as soon as its *centre* left the water, cutting its lift from half
   a cell's worth to nothing in one step. The test raft floated with a row of samples right on that edge and
   chattered at 0.2 m/s for ever, so every load measured on it was an average over the chatter. `Submerged` was a
   count with the same flaw, switching the heave damping on and off like a relay. Both are continuous now. The raft
   rests at Archimedes' height to four decimal places, at every load.

**Why the ride was reformulated anyway.** With the measurements clean, a matrix of characters on loose bodies
turned up three real failures, all from forces applied outside the solver:

- **Light crates went through the floor.** On a 2 kg or 8 kg crate the spring caught the rider at its limit and put
  thousands of newtons into the crate in a single step. Solved with the contacts, the floor holds the crate while
  the crate holds the rider.
- **Traction overshot.** The motor matched the rider's velocity to the ground's by pushing both bodies and ignoring
  that the second one moves. That overshoots by (1 + rider ÷ ground): eleven-fold on an 8 kg crate, which buzzed
  under the rider at several m/s and walked away. As solver rows it cannot overshoot. Moving the feet there was
  also what cured the 2 kg crate; capping the support alone did not.
- **Iterative solvers and mass ratios.** A stiff spring above a 2 kg body and a rigid floor below it does not
  converge. In the ride's rows, ground lighter than rider ÷ 10 is treated as rider ÷ 10
  (`RideConstraint.maxMassRatio`). Its contacts, buoyancy and everything else still use its real mass. The one
  cost: a floating prop that light, stood on, feels only part of the rider's weight. One that light could not hold
  a person up anyway.

**What it does now** (`tests/RipNRage.EngineChecks/RideChecks.cs`):

- A character sinks a raft **exactly** as far as an 80 kg box does, and four of them four times as far.
- Standing on, or landing from 3 m on, crates of 2, 8, 80 and 400 kg: the crate stays within 5 cm and under 0.5 m/s,
  and the rider stays on it.
- A character can stand on another's head.
- On still ground the feet follow the old motor's curve **exactly**, step for step, both running up and stopping.
  The movement feel is unchanged by construction.
- A standing character's vertical velocity now reads exactly zero. The old spring left it at −0.06 m/s for ever,
  because it cancelled gravity a step late.

**The grip limit** is built as `RideConstraint.grip`: a friction coefficient bounding the feet by μ × the support
force. It is **off by default, deliberately.** The motor accelerates at about 7 g for the game's feel, real friction
is about 1 g, and a realistic grip limit makes every deck feel like ice. A slippery deck sets it.

**Gates** (full sweep, all 36 pass, compared line by line with the previous commit):
- `walk`, `flowmoves`, `die`, `jab`, every weapon gate and the editor gates are identical.
- The others tell the same story — the same states, the same damage, the same deaths — with positions moved by
  centimetres to decimetres. Characters hover 2.6 mm lower (now exactly g/ω²), and a crowd of AI decisions
  compounds that.
- `evade` genuinely diverged. The same mechanics fire, but a few near misses became hits, so it ends on 19 armour
  instead of 43.

**Cost**: 200 runners on a 29k-triangle hill, physics per step 0.66 → 0.68 ms, p99 1.1 → 0.9 ms.

**Not done**: the walk-off shove. Pushing off a light crate still slides it about a metre, because the motor's
7 g push-off is real force on what it pushes against. That is bounded and the crate stays upright; `grip` tames it
where a designer wants.

**Commit.** `Content V1: the character moves in the frame of what it stands on`

### V1b. Decks that move, and things that ride them — **BUILT**

The first piece of V9a, built now because V1 needed something to stand on and the ship needs it anyway
(`src/RipNRage.Engine/Ecs/Physics/Platforms.cs`).

- **`PlatformMotion`**: a kinematic body driven by its **velocity**, never posed. The solver integrates a
  kinematic body from its velocity, so the ride spring, `FrameVelocity`, contacts and friction all get the right
  answer with no special case. Velocity, yaw rate, a patrol distance and a bob. A vessel differs from it only in
  where the velocity comes from.
- **`CarriedBy`**: "I ride that thing" — held at a fixed pose in the carrier's frame and moved every fixed step.
  It exists because **a physics entity cannot be a child**: `PhysicsSyncSystem` refuses to register anything with
  a `Parent` ("physics entities must be roots"), so a collider aboard a vessel is carried, not parented.
  - A rider **with a body** is posed where the carrier is now and given the carrier's velocity at its own
    position, so it carries in turn: it can be stood on.
  - A rider **without one** (a trigger, a decoration) is posed where the carrier *will be* at the end of this
    step, because nothing integrates it. Without that it trails its deck by half a metre at chase speed — which
    is exactly what the first run of the check showed.

**Checks.** A crate and a trigger hold their place on a deck doing 25 m/s through a 10°/s turn (< 0.05 m), and
the carried crate is handed the deck's velocity.

**Commit.** `Content V1b: decks that move, and the things that ride them`

### V1c. The infinite ocean — **BUILT**

- `WaterVolume.Infinite`: everything below the surface, however far the level runs. A harbour is still a box; an
  ocean cannot be one, and a chase that runs for kilometres would otherwise sail off the edge of the water.
  Written in a map as `infinite 1` on `water_plane`.
- `OceanSurfaceSystem` re-centres the drawn plane on the camera every frame, height untouched. **Nothing swims**,
  because the water shader takes its ripples and normals from the world position of each pixel
  (`vWorldPos.xz * _Tiling`, `shaders/water.glsl`) rather than from the mesh: the sea is anchored to the world
  and only the patch we bother to draw follows the eye.
- `Waters.HeightAt(...)`: one surface height function, so that what you swim in, what you drown under and (V4)
  what holds a hull up are the same surface. Flat today, a sum of waves at V11.

**Commit.** `Content V1c: the infinite ocean`

### V2. Compound bodies — **BUILT**

The fix for #3. A `Body` plus a `CompoundShape` (an asset of parts: shape, pose, density) makes one rigid body
from many colliders (`Physics/CompoundBody.cs`, `CompoundShapeConfig` for prefabs).

**What it does:**
- **Parts of every kind**: box, sphere, capsule and **convex hull**. Moving convex hulls are new. Each part is
  posed every step by a polytope that shares its template's topology, so posing allocates nothing.
- **Mass**: shared out by volume × density. The centre of mass is the parts'. The **full inertia tensor** comes from
  each part's own inertia, turned into the body frame and moved by parallel axes, then diagonalised (Jacobi) into a
  principal frame (`inertiaFrame`). The solver keeps its diagonal; only the frame it is diagonal in is new.
- **Narrow phase**: a compound collides part by part, and a single body collides with a compound's part as that
  part. The pair rules are a single body's, applied per slot pair, so each pair is generated exactly once whichever
  side holds the compound. Every part names the body and its entity: a ray, a contact or a ground probe on any part
  is on the boat, so `CharacterMotor.Ground` is the boat.
- **Continuous collision**: each part sweeps, and a fast body sweeps against a compound's parts, not its overall box.
- **Buoyancy** takes an explicit **displacement box** (the hull's envelope), so a hull floats on what it encloses,
  not on its parts. In passing: buoyancy sampled an off-centre shape from the centre of mass *and* its offset,
  twice over. It is measured from the entity's origin now. That was harmless for the centred shapes used until
  then, and wrong for everything else.

**Checks** (`tests/RipNRage.EngineChecks/CompoundChecks.cs`):

- *Mass*:
  - A one-part compound turns under a torque exactly as the box it is made of.
  - An L of two boxes turns as a tensor computed independently in the check says, off-diagonal terms included.
  - A turned part equals the same part built turned.
  - Density moves the centre of mass where it should.
- *Collision*:
  - A four-legged table settles on its legs, level and still.
  - A ray between its legs passes under it; one in line stops at the first leg's face.
  - A crate stacks on its top.
  - A compound thrown at 80 m/s does not tunnel through a 20 cm wall.
  - A prefab of five parts, convex among them, settles on its lowest part.
  - A ray onto the convex part hits its slope, not a box round it.
  - A hundred compounds stacked three high settle and none goes through the floor.
- *Vessels*:
  - A speedboat (heavy hull, rails, a cabin with roof and front wall) floats on its envelope at the predicted
    height, near level.
  - A character aboard is not left behind as it gets under way, walks forward into the cabin, and is stopped by its
    front wall.
- *Boarding and offboarding*, each a walk and a jump, judged by what the character is standing on afterwards:
  - **boat → ship** at rest, onto a deck 2.7 m above the boat's;
  - **ship → boat**, stepping off the deck, and the boat sinks by the rider's weight and comes back up when they
    leave;
  - **boat → ship at 20 m/s**: the ship becomes the frame and they stay where they landed;
  - **ship → quay**: the frame becomes still ground.
- *Aboard the ship* under way:
  - in through the deckhouse door, under its roof, stopped by its back wall;
  - down the ramp through the hatch into the hold, on under the deck, and back up.

**Found on the way — a real bug for the chase.** Characters are drawn interpolated between the last two physics
steps. Kinematic bodies were never interpolated, and boats were not by default, so a player standing on a moving deck
was **drawn up to a whole step's motion off it**: 13 cm at 8 m/s, 42 cm at a speedboat's 25 m/s. On screen that is
judder into the cabin wall every frame.

It surfaced as what looked like the character sinking 13 cm into a moving wall. It took six experiments to pin down:
- not compounds (a single kinematic wall did the same);
- not the contact solver (a plain box pressed into the moving wall stopped at 1 mm);
- not the new traction (still ground: 1 mm, as before);
- the measurement — the character's *drawn* pose against the wall's true one.

The fix: kinematic bodies interpolate like any other, and **whatever carries an interpolated character is drawn
interpolated with it**, so a designer cannot forget the flag on a vessel. A check walks frame times that never line
up with the physics step: a character standing on a deck doing 25 m/s must be drawn standing still (< 1 cm).
Removing the fix fails it.

**Gates**: the full sweep, 36 of 36, is **byte-identical** to the previous commit in every probe line, and the four
pixel gates are identical to the pixel. Single-shape scenes are untouched.

**Cost**: the contact-heaviest benchmark (1000+ crates) is about 2% slower per step (12.19 → 12.43 ms, three runs
each; run-to-run noise about 0.17 ms). The likely cause is the larger body object and one more branch per pair.

**Limits** (all recorded where they live):
- Parts assume a body at unit scale.
- A convex part's own inertia is its bounding box's (its mass and centre are exact).
- A convex part sweeps as its own box (it can only stop a little early, never late).
- Explosion impulses on a compound use its overall box.
- One displacement box per hull; several, from hull brushes, come with V3.
- The ship is a kinematic stand-in with a `PlatformMotion`. Its own simulated motion (heavy-vessel dynamics) and its
  moving-frame navigation are V9.

**Commit.** `Content V2: compound rigid bodies - hulls, decks and the parts of a ship`

### V3. Brush entities

Boats and the ship are built in the level editor, out of brushes, like everything else.

- **Loader**: an entity's own brushes become a mesh and a compound collider (V2) **in the entity's local space**,
  so moving the entity moves its geometry.
- **Editor**:
  - Select brushes → "Tie to entity", and its reverse.
  - Moving or rotating the entity moves its brushes.
  - Brush editing works in entity-local space.
- An optional .obj visual (`MeshRender.mesh`) can replace the brush mesh while the brushes stay the collision.
- **New classes**: `vehicle_boat` and `vehicle_ship`.
- **Special brush textures** (invisible, like `clip`):
  - `hull`: the displacement envelope (V4). Non-colliding.
  - `interior`: dry volumes (V9b). Non-colliding.
- **Placing on a vessel**: any point entity gets a `parent` key naming a vessel's `targetname`. Placing one inside
  a vessel's bounds sets it automatically. The editor shows parented entities in the vessel's space and moves them
  with it.
- **Previewing**: the editor previews a vessel at rest. In play, it sails.

**Checks.**
- A tied boat round-trips through save → load in the editor.
- In game, its collider moves with it.
- Its compound mass matches the brush volumes × density.

**Commit.** `Content V3: brush entities — the editor builds things that move`

### V4. Water and hydrodynamics — **BUILT**

The fix for #4.

- **Water surface function** `H(x, z, t)`: one function, shared by buoyancy and the water renderer. Flat in this
  step; V11 adds waves.
- **Buoyancy, exactly**:
  - Each convex **displacement** brush is clipped against the surface, giving the submerged volume and its
    centroid. This is exact for convex pieces.
  - Force = ρ g V at the centroid.
  - It is computed once per fixed step, not per substep.
- **Displacement is not collision.** Buoyancy comes from the `hull` brushes, the outer envelope, not from the
  colliding walls. A hollow ship, with holds, corridors and an engine room inside, displaces its whole envelope
  as a real one does.
  - The vessel's **mass and centre of mass are authored** on the entity, because a hollow hull's mass is not its
    walls' volume × density.
  - The same rule covers speedboat cockpits.
  - (No flooding: hull damage is out.)
- **Hydrodynamics** on submerged faces:
  - pressure drag and lift from each face's normal velocity (the face-pressure model);
  - viscous drag along the faces;
  - slamming on faces that enter the water fast.
  - This is what makes a speedboat **plane** at speed, pitch over its own bow wave and slap down after a jump —
    from the hull's shape, not from tuned animations.
- **Tunables**: a `[water]` section in `tunables.cfg` (density, drag coefficients, slamming), hot-reloaded like
  the rest.
- **Splash events** for VFX and audio.

**Checks.**
- A box of density 500 floats at exactly half its height.
- A hull settles at the draft where displaced mass equals its mass.
- A heeled hull rights itself.
- A hull dropped from 3 m bobs and comes to rest.
- A speedboat hull towed at 25 m/s rises to a planing trim (bow up a few degrees, less wetted area).

**Revised with V1e**:
- Buoyancy is continuous (see the correction there).
- Heave damping now applies in full whenever the hull is in the water, instead of scaling with the wetted fraction,
  which damped a hull riding high a tenth as hard.
- **Rock damping** is new: pitch and roll damped as a fraction of critical for the hull's own righting stiffness
  (ρg × the waterplane's second moment).

The continuous buoyancy exposed that the test hull's "stable 3.4° trim" had been the old relay-like damping holding
it down. Made continuous, it **porpoised**: trim swinging 0.6° to −7.7°, the propeller leaving the water, speed
wandering. With rock damping it runs dead steady, at 11.0 m/s with the propeller always in the water.

**Known limit**: a box hull trims about 3° **bow-down** at speed, where real planing hulls run 2–5° bow-up. Moving
the planing centre of pressure forward on a box makes it worse — it buries the bow, then launches clear of the
water. Realistic trim comes from hull shape (deadrise, wetted faces), which is what the convex-brush hulls of V2/V3
are for. The check asserts steadiness, not a realistic trim.

**Built as** `Ecs/Physics/Buoyancy.cs`: sampled displacement, body-level drag (per-sample drag over-counts the
wetted area several times over and leaves a boat unable to move), heave damped as a fraction of critical, and
planing from the bottom's angle of attack acting aft of the middle — forward of it, a hull somersaults; at the
middle it rears until it stalls. Displacement is authored apart from the collider, which is what lets a hollow
hull float. **Not built**: wetted-face integration from real convex hulls (needs V2/V3), and flooding.

**Commit.** `Content V4+V5: hulls float, plane and answer their helm`

### V5. Propulsion and control — **BUILT**

- **`VesselEngine`**: thrust applied at the propeller's position, only while the propeller is submerged (a boat
  off a wave crest loses drive). A throttle response, and an outboard that steers by vectoring its thrust.
- **`VesselControl`**: throttle and steer, both −1..1. A pilot writes it; nothing else moves a boat.
- **Speeds**: speedboats at 20–30 m/s top speed. The top speed is an outcome of thrust against V4's drag, not a
  clamp.

**Checks.**
- Top speed is reached, and held, from thrust and drag alone.
- The turning circle and roll in a turn are plausible and stable.
- A boat that leaves the water loses thrust until it lands.
- The player stands through a full-throttle start and a hard turn on V1's motor, or is thrown when the grip limit
  says so.

**Built as** `Ecs/Physics/Vessels.cs`: `VesselControl` (throttle, helm) and `VesselEngine` (thrust at a stern
propeller, swung by the helm, dead while out of the water). **Not built**: pilots and the chase director (V6).

**Commit.** `Content V4+V5: hulls float, plane and answer their helm`

### V6. Pilots and the chase director

- **Pilot**: an AI that drives `VesselControl`.
  - It reaches a target position and velocity with heading and speed controllers.
  - It avoids hulls and obstacles using predicted positions.
  - Behaviours:
    - **hold slot**;
    - **come alongside** (match a target boat's velocity at a set distance, which is what opens a jump window);
    - **ram**;
    - **peel off**;
    - **flee**.
  - **The pilot is a character at the helm.** Kill them and the boat goes adrift: throttle to zero, slowly
    turning. The player can use that, and so can a sonic blast that throws them overboard.
- **Chase director**:
  - The course: a spline down the channel from the moorings to the ship.
  - A **chase anchor** that advances along it, paced against the player's progress, so the flotilla neither
    abandons a slow player nor stalls for a fast one.
  - **Beats**: slot layouts relative to the anchor, which set up jump sequences, convoys, barges and ramming runs.
  - The pilots chase their slots under real physics; the director never moves a boat.

**Checks.**
- A pilot holds a slot within 2 m at 25 m/s.
- Come-alongside converges to under 1 m/s relative.
- Killing the pilot cuts the throttle.
- A scripted beat sequence reproduces its layout run after run within tolerance.

**Commit.** `Content V6: pilots and the chase director`

### V7. Combat between moving frames

- F1's inherited projectile velocity and F3's relative aim lead, verified boat to boat.
- **Hits move boats**: rocket and grenade blasts and sonic blasts apply impulses to hulls. Hull hits use a metal
  or fibreglass impact surface.
(No hull damage or sinking — decided. A boat is stopped by killing its pilot, not by holing it.)

**Checks.**
- An enemy on a diverging boat hits a strafing player at a plausible rate (tuned, not perfect).
- A rocket on a hull visibly rolls it.

### V8. Enemies jump

The enemy side of the chase. **No pathfinding on boats**: decks are small and open, so a deck is walked directly.

- **Deck steering**:
  - Steer in the boat's frame toward the target on the deck, along a straight line.
  - The existing ledge guard, made frame-relative, keeps them aboard unless they mean to leave.
- **Jump ability per kind**: `jumpSpeed`, `runUp` and `airControl`. Enemies use the same V1 motor as the player,
  so their jumps obey the same physics.
- **Jump planner**:
  - Predict where a point on the target deck will be over the flight time, from the target body's linear and
    angular velocity and acceleration.
  - Solve the ballistic launch from the jumper's frame velocity.
  - Validate the landing by casting against the **predicted** hull pose, and reject anything outside the kind's
    ability.
  - Re-solve each tick until the jump is committed; once committed, it's physics.
- **`Board` state** in `AiBrain`: wait for a window (the pilots' come-alongside makes them), take it, land,
  re-acquire.
- Jumps are also used by riflemen and grenadiers when their boat is adrift, sinking or out-gunned, and onto the
  ship (V9).

**Checks.** In the rig, with two boats 3 m apart at 25 m/s:
- An enemy lands its jump in ≥ 90 % of trials.
- It aborts when the gap opens beyond its reach.
- It never commits to a jump the planner scored as impossible.
- A miss drops it in the water, where the kill plane takes it.

**Commit.** `Content V8: enemies jump — deck steering, the jump planner, boarding`

### V9. The container ship — a moving world

The ship sails under its own simulated power, and **everything on it moves with it**, inside and out: decks,
holds, corridors, the engine room, the superstructure, containers, crates, doors, lights, enemies, pickups,
triggers, blood on the floor. It is massive, and it is the one place in the chase with pathfinding. Three steps.

**The ship's motion is real; its collision is the accepted simplification.**
- It uses the same buoyancy, hydrodynamics and propulsion as a boat (V4–V5): it pitches on the swell (V11) and
  answers its helm.
- In **contacts**, the solver treats it as infinite mass. Characters landing on it, boats ramming it and props
  falling on it cannot move it.
  - At a mass ratio near 10⁶ (a 100,000 t ship against 80 kg characters and 1.5 t boats), anything striking it
    would change its velocity by about a millionth anyway.
  - An iterative contact solver does not converge at that ratio; it jitters.
- The speedboats stay fully dynamic both ways.

#### V9a. Everything aboard moves

Fixes #6 and #11. Every entity class a map can place must work parented to a vessel. Each category follows one
rule:

| What | How it rides | Why |
|---|---|---|
| Solid geometry (walls, decks, stairs, lashed containers) | Child shapes of the vessel's compound (V2/V3) | One body; riders get its point velocity |
| Loose props, crates, dropped pickups, ragdolls | Independent dynamic bodies, carried by friction | Real physics: they stay put in a gentle turn and slide in a hard one |
| Characters | The V1 motor | Already frame-relative |
| Doors, hatches, lifts, cargo cranes | Joints to the vessel body | Joints to kinematic-in-contact bodies are supported (`RigidSolver.cs:588`) |
| Triggers (checkpoint, encounter, hurt, secret, exit) | **Kinematic trigger proxies** posed from the vessel each step | Statics only re-bake on their own `LocalTransform` (#6) |
| Lights, audio sources, particle emitters | Transform children | Render and audio read `LocalToWorld` |
| Spawners | Spawn in vessel space, and give the spawn the vessel's point velocity | A spawn must not appear at rest on a moving deck |
| Decals, impacts, blood splats, scorch marks | **Attached to the body they hit**, as particles in that body's local space | Fixes #11; applies to speedboats too |
| Nav carves | Vessel-local (V9c) | — |

**Checks.**
- A crate on the deck stays put through a gentle turn and slides in a hard one.
- A checkpoint on the moving ship fires and saves.
- A door swings correctly about its hinge while the ship pitches.
- Blood sprayed on the deck travels with the ship.
- An enemy spawned in the hold doesn't fly into the aft bulkhead.

**Commit.** `Content V9a: everything aboard a vessel moves with it`

#### V9b. Interiors

Fixes #7, #8 and #10. The ship has interiors and exteriors, and the seam between them has to be invisible.

- **Dry volumes**: `interior` brushes (V3) mark the spaces inside the hull. Inside one:
  - you are **not in water**: no underwater view, no swimming;
  - the **sea surface is masked**. The dry volumes are drawn into a stencil mask before the water, so the sea
    doesn't draw through the hold;
  - the **sun, sky ambient and light shafts are off**. It's a lighting zone, so interiors are dark because they're
    inside, not because a 70 m shadow map happened to cover them;
  - (optional) interior reverb.
- **"Submerged" replaces the kill height**, for the player and for enemies. Death needs both:
  - being below the water surface `H(x, z, t)`, outside any dry volume;
  - being past a depth or a time spent there.

  This one rule covers falling between boats, being knocked off the ship, and standing in the engine room below
  the waterline, where you are safe. It replaces F1's height-based enemy rule.
- **Moving lights and shadows**:
  - Interior point lights ride the ship (V9a).
  - The ship is a **moving shadow caster**, so it is outside the static shadow cache. It's drawn every frame
    through a simplified caster mesh: the `hull` envelope plus the superstructure.
  - Measure its cost early.
- **Culling**: the hull is a moving occluder. The per-frame occlusion buffer already handles that, and it hides
  the interior from outside and the sea from inside. Portals between interior and exterior only if measurements
  show they're needed.
- **Doorways**: interior and exterior meet at doors and hatches (V9a joints). Crossing one swaps the lighting zone
  and the water state smoothly. The swap is driven by the camera's position, blended across the threshold.

**Checks.**
- In the engine room below the waterline: no water on screen, no underwater effect, no death.
- Out on the deck in daylight, then through a hatch: the interior is dark and has its own lights, with no
  sunlight leaking at the edges.
- Knocked off the ship, you die in the sea; standing in the hold, the same height is safe.

**Commit.** `Content V9b: interiors — dry volumes, submerged instead of a kill height, lighting zones`

#### V9c. Navigation aboard

Container stacks make a maze across the deck, and the interiors stack holds, corridors and the engine room under
it. One grid covers all of it.

- **Nav grids attached to an entity**:
  - The ship's grid is baked in **its local space** from its collision (V3), using layer 11's bake unchanged.
  - It covers interior and exterior decks in one grid. Multi-floor spans already exist (`NavGrid.cs:59`), and
    stairs are step-ups.
  - Agents whose ground body is the ship query its grid. Path corners are stored ship-local and converted to
    world each tick, so a path stays valid while the ship turns and pitches.
  - Carves are ship-local (doors closing, containers knocked loose). The world grid is untouched.
  - An enemy jumping from a boat onto the ship switches to the ship's grid when it lands.
- **Jump links** (optional): enemies can jump since V8, so the ship's grid can add jump-up links (onto
  containers, up a hatch coaming). This is "jumping up" from layer 12's list, made possible by V8.
- **Bake**: once, on loading the ship's content, cached by a hash of that content (as layer 11 caches the world
  grid).

**Checks.**
- On the moving ship, an agent paths from the engine room up through the superstructure to the player on deck.
- Its path stays valid through a turn.
- An enemy landing from a boat picks up the ship's grid.
- The full ship's bake fits the layer 11 budget.

**Commit.** `Content V9c: nav grids that move — the ship inside and out`

**Boarding routes** (level design, on real physics):
- a stern ramp;
- a container swinging from the ship's crane, to wall-run;
- a pilot ladder;
- a sonic jump off a wave crest.

### V10. Physical state in saves

The fix for #5. Rigid-body pose, velocity and angular velocity are saved for every dynamic body, along with
vessel controls, pilot states and the director's progress. A checkpoint mid-chase restores the flotilla as it
was.

Everything aboard a vessel (V9a) is saved **in that vessel's local space**: the crates in the hold, an enemy on the
bridge, a door half open. On load it is restored relative to wherever the vessel is. This also keeps precision
when the ship is kilometres from the origin.

**Checks.**
- Save mid-chase and load: every boat is in the same place at the same speed, the riders are on their decks,
  and play continues.
- Save inside the ship's hold and load: the crates, enemies and doors are where they were relative to the ship.

### V1d. Swimming and caustics — **BUILT**

Asked for alongside the reaction force, and both sit on V1c's one water surface.

- **Swimming** (`PlayerCharacter.UpdateSwim`): in deep enough water the floor stops mattering — gravity goes, the
  look direction becomes the swim direction, jump kicks for the surface and crouch dives. The body floats with its
  head out, pushed up when it sinks and held down when it rides over, so you bob along the top instead of being
  spat out of it. Two depth thresholds rather than one: floating puts you exactly where a single threshold sits,
  and the swim flickers on and off with the bobbing.
- **Breath**: it runs down with the head under and comes back fast at the surface. Out of breath, drowning takes
  health through the ordinary damage path — but `DamageEvent.IgnoresArmor`, because a plate carrier is no help
  against water.
- **Swimmable is a property of the water** (`swim 1` on `water_plane`, default off). A harbour a level means as a
  way of dying still kills: the dock's is untouched, and the `fall` gate still watches a player drown in it. The
  `swim` gate runs on `levels/swim_test.map`, a pool with a bottom 11 m down and steps out.
- **Caustics** (`Shader.LightingCommon`, `[caustics]` in `tunables.cfg`): two drifting lattices beaten against
  each other give the wandering web the surface focuses onto everything under it. They fade with depth, land
  hardest on what faces up, and only fall where the sun reaches — they are sunlight, bent, so a surface in shadow
  gets none. The renderer takes the water height from the same volumes the player swims in.

**Commit.** `Content V1d: swimming, drowning and caustics`

### V11. Waves

After flat water is solid:

- `H(x, z, t)` becomes a sum of Gerstner waves, evaluated identically by the water shader and by buoyancy. A check
  compares the two.
- Swell is tuned along the course: a calm harbour, a short chop past the breakwater, open-water swell and the
  ship's own wake.
- Boats pitch and slam, and a jump becomes a matter of timing on the swell.

### V12. Presentation

- Wakes, bow spray, slam splashes and spray on the camera.
- Engine audio with Doppler (OpenAL source velocities).
- Head bob from deck heave, with no roll.

### Risks to watch, in order of when they'll show

1. **The ride spring on a pitching deck** (V1/V4). The spring uses relative vertical velocity, which is right,
   but a fast-pitching bow may chatter it. Tune with the rig before content.
2. **Rotation is not swept by CCD** (README, Known gaps). A boat pitching fast can push a thin rail through a
   character. Keep rails thick, and add rotational sweep for compounds if the rig shows tunnelling.
3. **Float precision over the course.** About 3–4 km at chase speed. At 4 km, float resolution is about 0.5 mm:
   acceptable for physics, worth watching in rendering. Put the origin mid-course to halve it. Floating-origin
   rebasing only if measured jitter demands it.
4. **Solver cost**: about 10 compounds × 20 children, buoyancy clipping at 50 Hz, plus riders. Add a
   physics-benchmark case early.
5. **The ship's size as a compound** (V9): hundreds of collision children, plus every loose crate, door and
   trigger proxy aboard. The broadphase sees each child, so it scales. Measure a greybox ship in the benchmark
   before detailing it.
6. **The ship as a moving shadow caster** (V9b): redrawn every frame instead of cached. Budget it with the
   simplified caster mesh from the start.
7. **The interior/exterior seam** (V9b): the sea mask, the lighting-zone blend and the underwater state all change
   at a doorway. Any mismatch between them shows as a flash or a leak. Test it in a doorway first.

### The limits, and what to do about each

Every limit the V steps have recorded, in one place, each with where it gets fixed. They fall into five kinds:

- **fix now**, before V3 builds vessels on top of them;
- **fix with the hull physics** that V3 makes possible;
- **already scheduled** in a later step;
- **watch**, with a measurement that would turn it into work;
- **accepted by design**, with what would make us revisit it.

Sizes are relative: S is a commit, M a few, L a step of its own.

#### 1. V2b — **BUILT**

All four closed, plus two bugs found on the way. Checks are in `tests/RipNRage.EngineChecks/CompoundLimitsChecks.cs`;
each was built so the old behaviour would fail it.

| # | Was | Now | Proved by |
|---|---|---|---|
| L1 | A convex part's inertia was its bounding box's | Exact. Each part contributes a full tensor; a convex part's comes from the tetrahedra its volume already used (∫x·xᵀ dV = V/20·(s·sᵀ + Σvᵢ·vᵢᵀ) each), moved to its centroid. | A convex part shaped as a box turns exactly as the box. Turned arbitrarily, it turns exactly as the turned box, off-diagonal terms included. |
| L2 | A convex part swept as its box | Swept as itself. **And a latent tunnelling bug under it**: `CollisionShape.Translated` moved a polytope's centre and none of its vertices, so a hull could not be swept at all. Polytopes now carry an `offset` that `Support()` and `bounds` honour. Static ones keep it at zero, so nothing that existed changed. | A wedge at 80 m/s is held back exactly where its slope meets a bar (±5 cm). With the old box sweep put back, it is held where its box would be — the check fails. |
| L3 | Blasts on a compound used its overall box | The closest point over the parts. | A blast within reach of the box round an open deck, but of none of its parts, does not move the boat. The same blast beside the deckhouse roof does. |
| L4 | Parts assumed unit scale, **silently** | **Supported fully** (decided). Uniform scale scales everything. Under non-uniform scale an axis-aligned box stays a box; a turned box becomes the parallelepiped it really is (an exact convex part); convex parts are transformed exactly (vertices by S·R, planes by its inverse transpose, mirroring included, no point cap). Spheres and capsules take the single-shape rule, the largest scale across them. A runtime change of scale sets the parts up again in place. Buoyancy envelopes and authored volumes, and propeller positions, scale too. | An L at scale 2 equals one built twice the size. Under (1, 2, 1), a box is twice as tall and a turned box becomes a stretched diamond, its corner and sides where they should be. A wedge mirrored by a negative scale slopes the other way, not inside out. A table at scale 2 stands on its scaled legs, and rescaled to 1.5 while standing it is set up again. A hull at scale 2 floats on its scaled envelope. |

**Found on the way**: the propeller's position was measured from the centre of mass rather than the hull's origin —
the same bug buoyancy had. It was harmless for a centred box, but misplaced V2's compound speedboat's propeller by its
centre-of-mass offset. It is measured from the origin now.

**Gates**: 36 of 36, byte-identical to the previous commit, pixel-identical.

#### 2. Fix with the hull physics — "V4b", after V3 (L)

**This is the biggest open risk to the chase, not a tidy-up.** The test boat makes 11 m/s, and the chase needs 20–30. The lumped planing model (`PlaningLift`, `PlaningTrim`, `PlaningCentre`) on a box cannot get there honestly: moving its centre of pressure either buries the bow or launches the hull.

| # | Limit | Fix |
|---|---|---|
| L5 | One displacement box per hull | Displacement from the hull's own `hull` brushes (V3), each clipped **exactly** against the water plane: a convex polytope below a plane has an exact volume and centroid. This replaces the sampled box for vessels, and the sampled box stays for crates. |
| L6 | Planing and drag are lumped on a box; a box trims about 3° bow-down | Per submerged face of those brushes: pressure drag and planing lift from the face's own normal and velocity, plus viscous drag over the wetted area. Bow-up trim, planing and top speed then come from the hull's shape and the engine's power. The three lumped knobs are retired for vessels. |

**Proved by**:
- A prism hull floats at its analytic draft.
- A box displacement agrees with today's sampled result.
- A V-bottomed speedboat of plausible power planes, runs 2–5° **bow-up**, holds steady without porpoising, and reaches chase speed (≥ 20 m/s).

**Watch inside it**: whether rock damping (V4) is still needed once faces damp pitch for real. Keep it if a check still needs it, and say why.

#### 3. Already scheduled — nothing new, just their home

| Limit | Where |
|---|---|
| The ship is a kinematic stand-in (`PlatformMotion`); its own motion is not simulated | V9: heavy-vessel dynamics, kinematic in contacts |
| No pathfinding on the moving ship | V9c |
| Rigid-body state (and vessel, pilot and director state) not saved | V10 |
| Flat water | V11 |
| The kill height kills in a ship's hold; water is a world box; the sea draws inside hulls | V9b: dry volumes, "submerged" |
| Enemies have no kill rule in water and do not swim | F1 (enemy kill plane), V9b (the submerged rule) |
| MP3 and OGG not decoded | F0 |

#### 4. Watch — with the measurement that would make it work

| Limit | Measure | Turns into work when |
|---|---|---|
| CCD sweeps translation, not rotation | Boats ramming and pitching in the V6 flotilla rig | A rail or deck edge passes through a character or a hull |
| The ride on a fast-pitching deck | The V4b planing boat, then V11 waves | The ride chatters, or a rider leaves the deck without jumping |
| Float precision over 3–4 km | C7b, a character at the far end of the course | Visible jitter in the drawn deck or the view |
| Solver cost: ~10 boats × ~20 parts, and a ship of hundreds | A "flotilla" benchmark scenario at V6; the ship greybox at V9 | A budget fails. V2's own 2% is in this column, and the larger body object is the first suspect |
| Not deterministic with `threads > 1` | — | A feature needs replays or lockstep. V10 saves do not |

#### 5. Accepted by design — and what would reopen each

| Limit | Why it stands | Reopen if |
|---|---|---|
| Ground lighter than rider ÷ 10 is solved as rider ÷ 10 (`maxMassRatio`) | An iterative solver cannot converge a stiff spring over a very light body against a rigid floor. Such a floating prop feels only part of a rider's weight, and could not hold a person up anyway. | A design wants characters riding very light floating things |
| Grip limit off by default (`RideConstraint.grip`) | The motor runs at about 7 g for feel; real friction (~1 g) makes every deck ice | A slippery-deck design, which sets it per deck |
| **Walk-off shove**: pushing off a light crate slides it about a metre | Newton's third law, honestly: the motor's 7 g push-off is real force on what it pushes. **Decided: kept honest.** | A design where pushing off light props reads as wrong in play |
| Caustics use one water level (the highest surface) | Every level so far has one sea | A level with two water heights in view |
| Only the player swims | Nothing else needs to yet | An enemy design that swims |

**Decided** (2026-11-19):
- The walk-off shove stays honest.
- Scaled compounds are fully supported (built in V2b).
- The order is V2b (done), then V3, then V4b, then V6.

Engine-wide gaps in the README that are not on this pass's path — joint types, mesh triggers overlapping by
bounds, movers that are not pushed back, the mesh ghost-edge heuristic — stay in the README's list.

---

## Part four — the Harbor (C7)

One sprawling level. **The land sections teach the movement systems; the chase is the exam.** Each section adds
a weapon, an enemy and a movement skill, and ends in an encounter. Checkpoints at every boundary.

| Section | Weapon / enemy | Movement it teaches | Shape |
|---|---|---|---|
| **1. Harbour entrance** | Glock, M1887; riflemen, brawlers | Jump, slide | Seawall approach, gatehouse, the first encounter. |
| **2. Container yard** | **Katana**; swarmers | Wall-run, wall jump | Stacked containers: aisles to cleave, tops to run, gaps to wall-jump. |
| **3. Docks and cannery** | **Sonic shotgun**; shields, grenadiers | Dash, air dash | Catwalks over water, conveyor lifts (`phys_slider`), moored boats bobbing at the quay. The first moving decks, at rest. |
| **4. Pier end** | MG338, rocket launcher; marksmen, a heavy | Rocket jump, sonic jump | Cranes, long sightlines. The heavy guards the moorings; the speedboats are tied up below. |
| **5. Speedboat chase** | Throwables; boarders, riflemen, grenadiers | Everything, on moving ground | Boat-to-boat across the flotilla toward the ship. Barges with containers to wall-run, low canopies to slide under, gaps only a dash or a sonic jump crosses, pilots to kill, boats to blast aside. |
| **6. Container ship** | — ; heavy, everything | All of it, on a world that moves | Moving the whole time, inside and out. Board by ramp, crane container or sonic jump. Fight through the container maze on deck, down through the holds and the engine room below the waterline, then back up — and the **boss fight on deck ends the level**. |

- **C7a**: greybox sections 1–4, playable end to end. Measure the land nav bake on a greybox of the full extent
  **before** detailing. If it doesn't hold, split at a section boundary using F2's campaign list.
- **C7b**: the chase (V1–V8, V10), course and beats authored against the director.
- **C7c**: the ship (V9a–c). Greybox it in the vessel rig first: one hold, one corridor, one doorway to the
  deck, sailing. Then build the full ship.
- **C7d**: art pass — textures, lighting, audio, waves (V11), presentation (V12).
- `dock.map` becomes section 3's starting point or a test map. Pick `dock.map` or `DockLevel.cs` as the source
  of truth and delete the other.

---

## Order

```
F0 ─ F1 ─┬─ C1 katana ─ C2 sonic shotgun ─ C3 throwables
         ├─ F2 level logic ─────────────────────────┬─ C7a land sections ─────────────┐
         └─ F3 enemies ─ C4 ─ C5 ───────────────────┘                                 │
V1 frame-relative movement ──────────────┐                                            │
V2 compound bodies ─ V3 brush entities ─ V4 water ─ V5 engines ─ V6 pilots/director ─ V7 ─┤
                                         └──── V8 enemy jumps (needs V1, V5) ─ C6 boarder ┤
V3 + V4 ─ V9a everything aboard ─ V9b interiors ─ V9c ship nav ───────────────────── C7b chase ─ C7c ship ─ C7d art
                                                V10 saves (after V6, V9a), V11 waves (after V4), V12
```

- **V1 and V2 start on day one.** Neither needs a boat, and neither touches F-step files.
- V1 is also the one step that changes how the game already plays, so its "unchanged on static ground" gate is
  the most important check in the pass.

---

## Asset list

| Asset | For | Exists? |
|---|---|---|
| Katana viewmodel frames | C1 | Yes (7) |
| Sonic shotgun frames | C2 | 2 near-identical — a charged frame and a stronger kick would help |
| Grenade world sprite + viewmodel throw | C3 | No |
| World sprites for `weapon_*` pickups | F2 | No — crop viewmodel frames for now |
| Enemy sheets per kind | C4–C6 | Rifleman only; others recoloured until then |
| Speedboats, barges, the container ship | V3, C7 | Built from brushes in the editor; optional .obj visuals later |
| Harbour textures (containers, steel, hulls, concrete, crane) | C7d | Prototype only |
| Sounds: sword hit, sonic charge, grenade, engines, hull slams, splashes, enemy barks | C1–C7 | Slash and sonic shot only |
| Music | C7 | None — format undecided |

---

## Decisions taken (2026-11-19)

1. **Slots**: 1 katana, 2 Glock, 3 M1887, 4 sonic shotgun, 5 MG338, 6 rocket launcher; throw on G / pad X. No save
   or binding compatibility kept.
2. **Enemy art**: recoloured rifleman sheets until real art.
3. **Style meter**: removed from the README.
4. **Throwables**: in scope, thrown instantly on G.
5. **Levels**: one sprawling Harbor ending in the speedboat chase onto the container ship.
6. **The chase is real**: dynamic boats on buoyancy and thrust at real speeds; the player and enemies ride, fight
   and jump between them on the same physics. Pathfinding only on the container ship. The chase is a high-octane
   platforming exam of the movement systems.
7. **The ship moves, and so does everything on it**: interior and exterior, crates, doors, lights, enemies,
   triggers, blood. Its motion is simulated like a boat's. Its **collision is the accepted simplification**: the
   contact solver treats it as infinite mass for landing on it and running into it.
8. **The ship has interiors and exteriors**: holds, corridors, an engine room below the waterline and the
   superstructure, with seamless transitions.
9. **Audio formats**: WAV, OGG and MP3 are all decoded (F0 grows a decoder instead of converting the two files).
10. **The ship ends the level** with a boss fight on deck.
11. **One throwable** for now (frags). **No hull damage or sinking.**

## Still open

- Who makes the enemy and hull art, and when.
- Whether the boss on deck is a bigger Heavy or its own kind.
