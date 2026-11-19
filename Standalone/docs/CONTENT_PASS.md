# Content pass — plan

Status: **V1 built** (2026-11-19); the rest planned. Scope: the two unfinished weapons (katana, sonic shotgun),
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

**The remainder, not built yet** (carried into V4/V5, where there is a hull to push):

- **Newton's third law**: the ride spring's and motor's forces reversed onto the ground body, so boats dip under
  boarders and a dinghy is pushed by someone running across it.
- **Grip limit**: grounded acceleration bounded by the deck's friction, so a hard turn or a ram throws people off
  their feet.

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

### V2. Compound bodies

The fix for #3, in the engine.

- **Child shapes**: a body owns N child shapes (box, sphere, capsule, **convex hull**), each with its own local
  pose. Dynamic convex hulls are new (static brush hulls exist).
- **Mass properties**: from the children's volumes × density. Combined with the parallel-axis theorem into a full
  inertia tensor, then diagonalised into a principal frame. The solver keeps its diagonal
  `inverseInertiaLocal`, expressed in that frame.
- **Collision**: broadphase proxies per child mapping to one body; contact generation and CCD sweeps per child.
  Queries (ray, shape cast, ground probe) resolve to the body's entity, so `CharacterMotor.Ground` is the boat.
- **Sleeping and islands** treat the compound as one body.

**Checks.**
- The inertia of an L-shaped compound matches the analytic value.
- A compound dropped on a slope tumbles the way the analytic tensor predicts.
- 100 compounds stack and settle (a new benchmark case).
- A ray hit on any child names the parent entity.

**Commit.** `Content V2: compound rigid bodies with dynamic convex hulls`

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

### V4. Water and hydrodynamics

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

**Commit.** `Content V4: buoyancy and hydrodynamics — hulls float, plane and slam`

### V5. Propulsion and control

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

**Commit.** `Content V5: engines and helms`

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
