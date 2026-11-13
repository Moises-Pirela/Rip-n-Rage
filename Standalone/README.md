# Rip-n-Rage — Standalone engine and game

![standalone build](docs/screenshot.png)

A from-scratch runtime for the Rip-n-Rage first-person shooter that **does not depend on Unity
at all**. The original gameplay C# scripts (`_Game/Scripts`, `_AI_BehaviorTree`, `Tools`) were
ported onto a small custom engine. The art and audio the game needs (PNG sprite sheets,
TexturePacker `.tpsheet` atlases, WAV/MP3 audio, the TrenchBroom dock export) were restored from
the old Unity project into `assets/`.

```
Standalone/
├── RipNRage.sln
├── Directory.Build.props       shared build settings (net10.0, unsafe code, warnings)
├── Directory.Build.targets     copies native libs (build) and game data (publish) beside each exe
├── src/RipNRage.Engine/        custom engine (no external packages)
│   ├── Platform/               GLFW + OpenGL 3.3 core bindings (P/Invoke, cross-platform loader)
│   ├── Graphics/               render pipeline, PNG decoder, sprites/atlases, meshes, UI batch, font
│   ├── Core/                   GameObject / Behaviour / Transform / Scene / coroutines / time
│   ├── Animation/              sprite animation clips + Unity-style Animator state machine
│   ├── Audio/                  OpenAL bindings, WAV loader, AudioSource
│   ├── Physics/                collision world (grid broadphase), sphere/capsule/box/brush/mesh colliders, GJK + conservative-advancement sweeps, move-and-slide, KinematicBody, CharacterController, RigidBody (contact solver: soft / Baumgarte / NGS, static-dynamic friction, warm-start cache, CCD), joints (ball / distance / spring / hinge / slider / fixed constraints + Unity-style Joint components with limits, motors, springs, break force), physics materials, triggers, nav agent
│   ├── Destruction/            sprite destruction: skeletons (JSON), the SpriteRagdoll slicer (cut-out sprite pieces on jointed rigid bodies)
│   ├── Math/                   Vector2/3, Quaternion (Unity ZXY euler), Matrix4x4, Mathf, Random, curves
│   └── Tween/                  DOTween-style tweens
├── src/RipNRage.Level/         level data model: brushes/entities, .map read/write, polygons, collision shapes
├── src/RipNRage.Game/
│   ├── Scripts/                the ported game scripts (same files/classes as the Unity _Game/Scripts)
│   ├── Tools/EnragedInjector.cs
│   ├── Framework/              MonoBehaviour/ScriptableObject/attribute stand-ins, Prefab factories
│   ├── Content/                prefabs, ScriptableObject data, animator graphs and the TestArea level
│   │                           rebuilt in code from the values in the .prefab/.asset/.anim/.controller files
│   └── Program.cs              entry point, CLI options, debug overlay, automation scripts
├── src/RipNRage.ImGui/         Dear ImGui integration used by the editors (renderer, input, docking)
├── src/RipNRage.Editor/        Quake-style level editor executable (RipNRageEditor)
├── src/RipNRage.AnimEditor/    sprite animation editor executable (RipNRageAnimEditor)
├── src/RipNRage.VfxEditor/     particle effect editor executable (RipNRageVfxEditor)
├── src/RipNRage.SkeletonEditor/ sprite destruction skeleton editor executable (RipNRageSkeletonEditor)
├── src/RipNRage.Benchmark/     physics / engine benchmark executable (RipNRageBenchmark, bench.sh)
├── assets/                     runtime art/audio: Sprites/, Textures/, Audio/, Levels/dock.obj
├── animations/                 animation clips and controllers (JSON, hot-reloaded)
├── vfx/                        particle effects + bindings (JSON, hot-reloaded; filled with the defaults on first run)
├── skeletons/                  sprite destruction skeletons (JSON, hot-reloaded; rifleman.json ships, see skeleton.sh)
├── levels/                     .map levels (dock.map = the test area)
├── shaders/                    GLSL shaders (hot-reloaded)
├── native/                     optional GLFW / OpenAL shared libraries, per runtime id (see below)
└── tunables.cfg                post-processing / lighting settings (hot-reloaded)
```

Project references: `Engine` ← `Level` ← `Game` ← `Editor`, and `Engine` + `Game` ← `AnimEditor` / `VfxEditor` / `SkeletonEditor`.

## Requirements

* **.NET 10 SDK.** Any install works: Microsoft's installer, a distro package, or
  `tools/setup-dotnet.sh`, which installs a private copy into `.dotnet/` without root.
  The engine, the game and the level library use no NuGet packages and build offline. Only the editors have a
  dependency: `RipNRage.ImGui` wraps [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET) (Dear ImGui 1.91.6,
  docking branch) for their panels, so nothing ships with the game. `nuget.config` limits package sources to
  nuget.org, which is otherwise only contacted for the runtime packs a self-contained publish (`build.sh`) needs.
* An OpenGL 3.3 capable driver.
* GLFW 3 and (optionally) OpenAL Soft shared libraries. These are **bundled** in `native/<rid>/` and
  copied next to the executable on build (see `native/NOTICE.md` for versions, sources and licences):
  * **Windows x64**: GLFW 3.4 + OpenAL Soft 1.24.3.
  * **Linux x64**: GLFW 3.4 (glibc 2.34+) + OpenAL Soft 1.24.2 (glibc 2.38+, e.g. Ubuntu 24.04 / Debian 13).
    On older distros install `libopenal1` from your package manager, or the game runs silently.
  * **macOS** (arm64 and x64): GLFW 3.4. Sound uses Homebrew's `openal-soft` if installed, otherwise the
    system OpenAL framework.

  The loader searches `$RIPNRAGE_NATIVE_DIR`, `<exe>/native/<rid>`, `<exe>/runtimes/<rid>/native`,
  `<exe>` and finally the system library paths. If OpenAL is missing the game runs silently.

## Quick start

```bash
cd Standalone
./run.sh          # build + run the game
./edit.sh         # level editor
./anim.sh         # animation editor
./vfx.sh          # particle effect editor
./skeleton.sh     # sprite destruction skeleton editor
./build.sh [rid]  # self-contained publish for this machine (or linux-x64 / osx-arm64 / osx-x64 / win-x64) into dist/<rid>; macOS also gets Rip-n-Rage.app
```

On Windows: `run.cmd`, or the manual commands below.

* `build.sh` publishes the game and both editors into `dist/linux-x64/` with the runtime included,
  along with `assets/`, `shaders/`, `animations/`, `levels/` and `tunables.cfg`. Copy that folder
  to any x64 Linux box with OpenGL 3.3 and run `RipNRage`.
* The game finds `assets/` by walking up from the executable and the current directory. Override it
  with `--assets <dir>` or `RIPNRAGE_ASSETS`, pointing at the assets folder itself or at the folder
  that contains it.
* `tools/env.sh` (`source` it) puts a private `.dotnet/` on PATH when one exists, and defaults
  `DISPLAY` to `:1` when it is unset (handy for Xvfb on a headless box).

## Build & run (manual)

```bash
cd Standalone
dotnet build RipNRage.sln -c Release
dotnet run -c Release --project src/RipNRage.Game
```

### Controls

Every gameplay action is **rebindable** (pause menu / title screen → CONTROLS) and has a keyboard/mouse slot and
a gamepad slot; defaults:

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Move / strafe | W A S D (bunny-hop when airborne, as in the original FPSController) | left stick |
| Look | mouse | right stick |
| Fire | left mouse | RT |
| Jab combo / grab | right mouse | LT |
| Jump | Space | A |
| Slide / crouch (while running; jump out of it to keep the speed) | Left Ctrl | B |
| Dash (in the movement direction; one air dash per jump) | Left Shift | L3 |
| Wall run | airborne next to a wall while pushing forward; Jump = wall jump | |
| Glock / M1887 / MG338 / Rocket launcher | 1 / 2 / 3 / 4 | D-pad up / right / down / left |
| Next / previous weapon | mouse wheel | RB / LB |
| Quick swap (last weapon) | Q | Y |
| Pause menu | Esc | Start |
| Quick save / quick load | F6 / F9 | – |
| Menus: navigate / confirm / back | arrows or W S A D, Enter / Space, Esc | D-pad or left stick, A, B |
| F7 / F8 | load the last checkpoint save / restart the level | |
| F3 / F11 / F12 | debug overlay / profiler GPU sync / screenshot | |
| F4 / F5 | toggle post-processing / reload `tunables.cfg` + all shaders | |

Bindings, mouse sensitivity / inversion and gamepad look speed / inversion / deadzone / response live in
`<persistentDataPath>/controls.json`.

### Command line

```
--width W --height H   window size           --no-vsync        --mute         --verbose
--assets dir           assets folder (or the folder containing it); default: "assets" found in exe dir / cwd / parents
--frames N             exit after N frames   --fixed-dt 0.0166 deterministic frame time
--screenshot out.png   save on exit, or every --shot-interval N frames ("{n}" = frame number)
--script name          automation: look | spin | walk | fire | weapons | jab | m1887 | glockfire | rocket | close | approach | rage | crates | sunward | water | killenemy | die | pause | vfx | fall | strafe | slide | style | dash
--fall-mode die|respawn falling off the level: lethal (default) or an arcade respawn on a spot stood on before the fall
--menu / --no-menu     start on the title screen / straight in the level (default: title screen, unless --script, --frames or --load is given)
--shaders dir          folder with the .glsl shader files (default: "shaders" found in cwd / exe dir / parents)
--tunables file.cfg    post-processing config (default: tunables.cfg found in cwd / exe dir / parents)
--no-postfx            start with post-processing disabled
--animations dir       folder with the animation clip/controller JSON (default: "animations" found in cwd / exe dir / parents)
--export-animations d  write the code-built clips and controllers as JSON into d and exit
--vfx dir              folder with the particle effect JSON files (default: "vfx" found in cwd / exe dir / parents)
--perf-log S           print a profiler summary (real frame time, stage times, GC) every S seconds
--perf-csv file.csv    one CSV row per frame: frame ms, GPU ms, allocations, every stage's CPU (and GPU) ms
--profile              glFinish after every GPU stage so the CPU stage times include the GPU work (= RIPNRAGE_PROFILE=1, F11 in game)
--gpu auto|high|software  GPU preference: high = discrete GPU on hybrid Linux systems, software = accept a CPU rasterizer without the warning / clamp
--gpu-info             print renderer / vendor / version + software-rasterizer verdict and exit (exit code 3 = software)
```

Example used for the screenshots in this document:
`dotnet run --project src/RipNRage.Game -- --frames 200 --fixed-dt 0.016667 --mute --script close --screenshot shot.png`

## What renders / works in this milestone

* First-person weapon sprites (Glock, M1887, MG338, rocket launcher) with the exact prefab
  hierarchy: `Camera(0,0.5,0) → WorldCamera(fov 77) → PlayerArms(0.017,-0.65,2.36) → Weapon(scale 0.1)`,
  per-weapon `weaponPosition/weaponScale`, **WeaponSway**, **HeadBob**, **CameraRecoil**.
* TexturePacker atlases including **polygon-packed sprites** (the `.tpsheet` vertex/triangle lists
  are used as tight meshes, exactly like Unity's importer) — needed because the M1887/MG338 sheets
  have overlapping bounding rectangles.
* Weapon animation: the `BaseWeaponController` graph (`Idle ⇄ Fire ⇄ WeaponMelee` driven by the
  `IsFiring` / `WeaponMelee` bools) with every override controller and clip rebuilt from the
  `.anim` files (frame times, loop flags, `OnFire`/`OnGlockAltFire`… animation events via SendMessage).
* Fists: `LeftFist`/`RightFist` controllers (Jab trigger, Grapple bool, Any-State transition).
* Hitscan & projectile weapons, ammo/inventory, pickups, rage/combat state, rage "post-processing"
  (colour filter + vignette like the URP volumes), HUD (portrait, radial health/armour ring,
  ammo counter/icon, crosshair with DOTween-style tweens).
* Enemies: entities with senses and a state tree. Sight is range → cone → line of sight with a memory; sound is
  an entity (`Noise`) whose front crosses the level at 80 m/s, muffled by walls — shots, blasts and footsteps make
  one, and an enemy's shot tells the others whom it is shooting at (`Ecs/AI/Perception.cs`). The brain
  (`Ecs/AI/AiBrain.cs`: unaware = idle → wander, engaged = chase the last known position / attack what it sees in
  range) is the **BehaviorTree2** graph as a value stepped by a pure function; `AIState` / `AIAgent` are the
  GameObject half still being ported. 8-sided sprite sheets (`EnemyVisualSystem`), projectiles that damage the
  player through armour.
* Level: the dock brush map (`dock.obj`, imported like Unity: X mirrored, 0.25 u/unit, y −3.09),
  AABB collision per brush (rotated brushes are grid-approximated), sand/water planes, sky,
  the Unity **layer table and collision matrix** from `ProjectSettings`.
* Audio: OpenAL playback of all WAV clips (gunshots, footsteps, jump/land, punches).

## Post-processing (`tunables.cfg`)

`tunables.cfg` (next to this README; also copied beside the executable) configures the
post-processing stack and is **hot-reloaded** — save the file while the game runs and the new
values apply within half a second (F5 forces a reload, F4 toggles the whole stack).

| Section | Keys |
|---|---|
| `[postfx]` | `enabled` (lens effects), `render_scale` (scene resolution, try 0.6 on software GL), `bloom_scale`, `hdr` |
| `[bloom]` | `enabled`, `threshold`, `soft_knee`, `intensity`, `radius`, `passes` |
| `[chromatic_aberration]` | `enabled`, `intensity`, `radial` |
| `[vignette]` | `enabled`, `intensity`, `smoothness`, `roundness`, `color` |
| `[motion_blur]` | `enabled`, `intensity`, `samples`, `max_velocity` |
| `[color]` | `exposure`, `saturation`, `contrast` |

Pipeline (`Engine/Graphics/PostFx/PostProcessStack.cs`): the world is rendered into an offscreen
target with a depth texture → **volumetric fog** → **motion blur** (per-pixel camera velocity from depth
reprojection against the previous frame's view-projection) → first-person sprites are drawn on
top, sharp → **bloom** (soft-knee bright pass, iterative blur at `bloom_scale`) → final pass:
**chromatic aberration** (radial RGB split), colour filter/exposure/saturation/contrast,
**vignette** → HUD/UI, unaffected. The rage states push their own volume into the same stack
(EnragedPP: red colour filter + red vignette + chromatic aberration; DepressedPP: cold filter).

Every effect is skipped when disabled, and the whole chain is bypassed when nothing is active.
On a software renderer (Mesa llvmpipe) the full chain costs roughly 60–90 ms/frame at 1280×720;
use `render_scale = 0.6` or fewer bloom passes for a smoother run. On a GPU it is
negligible. `RIPNRAGE_PROFILE=1 ./run.sh --frames 120 --mute` prints the per-stage cost.

Use `--tunables other.cfg` to point at a different file and `--no-postfx` to start with the stack off.

## Lighting, shadows & volumetric fog (`tunables.cfg`)

Also hot-reloaded from `tunables.cfg`:

| Section | What it drives |
|---|---|
| `[lighting]` | sun elevation/azimuth/colour/intensity, hemisphere ambient (`ambient_sky` / `ambient_ground`), Blinn-Phong `specular`, `sprite_wrap` (wrapped lighting for billboards), `max_point_lights` (gathered per frame), `lights_per_draw` (uploaded per object) |
| `[sky]` | procedural sky gradient colours |
| `[fog]` | classic distance fog in the surface shaders |
| `[shadows]` | directional shadow map: `resolution`, `distance` (box half-size around the camera), `bias`, `normal_offset`, `softness` (0 = one hardware-PCF fetch, > 0 = four), `sprite_shadows`, `cache` (static casters rendered only when needed), `recenter` (drift before the box moves) |
| `[volumetric_fog]` | height fog with light shafts: `density`, `height`, `height_falloff`, `color`, `sun_scattering`, `anisotropy` (Henyey-Greenstein g), `ambient`, `steps` (shadow samples), `max_distance`, `shadows` (god rays), `point_lights`, `scale`, `upsample` (depth-aware), `dither` |

How it works (`Engine/Graphics/Lighting/`, `PostFx/PostProcessStack.cs`):

* **Lights** are `Light` components (`Directional` overrides the sun, `Point` with colour/intensity/
  range). Meshes *and* lit world sprites (enemies, pickups) are shaded per pixel: hemisphere
  ambient + sun + point lights + specular. The `max_point_lights` nearest lights are gathered per
  frame, but each draw only uploads the ones whose range sphere touches the object's bounds
  (`lights_per_draw`, at most 16; consecutive draws with the same set skip the upload), so a pixel
  pays for the 0–3 lights that can reach it instead of all 16. The weapon muzzle flash, rockets and
  explosions spawn short-lived point lights; the TestArea has lamp posts and crates as casters.
* **Shadows**: an orthographic depth map from the sun around the camera. The *static* casters
  (every mesh whose transform did not change — a cheap per-frame fingerprint detects changes) are
  rendered into a cached depth map only when the camera drifts `recenter × distance` from the last
  centre (snapped to texels, so nothing shimmers), a mesh moves or the sun changes; casters whose
  bounds lie outside the light box are skipped. Every frame the cached depth is blitted into the live
  map and only the billboard sprites (enemies, pickups) are drawn on top. Receivers sample the map
  with hardware depth comparison (`sampler2DShadow`): one fetch is already a bilinear PCF, `softness`
  > 0 adds three more — down from nine manual fetches — and texels facing away from the sun skip the
  fetch entirely. Slope-scaled polygon offset + normal offset fight acne.
* **Volumetric fog**: the smooth parts of the integral are closed forms — the optical depth of the
  exponential height profile along the ray (→ transmittance), the ambient in-scatter and the
  point-light glow (chord of the ray through each light's range) — so the per-pixel ray march only
  has to sample the shadow map (one hardware-PCF fetch per step) to shape the sun shafts, and its
  result is normalised by the analytic absorption: `steps` changes shaft detail, never brightness.
  Pixels whose ray never enters the fog exit immediately; with `shadows = false` there is no march at
  all. The fog runs at `scale` resolution (default 0.35) and is composited with a depth-aware
  (bilateral) upsample, so the low resolution leaves no halos around depth edges.

Debug helpers: `RIPNRAGE_DEBUG_SHADOW=1` also writes `<screenshot>_shadowmap.png`;
`RIPNRAGE_DEBUG_SHADOW=2` renders meshes with their shadow factor only.

Cost: see *Profiling* below. The F3 overlay also shows the shadow cache (static renders so far, casters
drawn / culled, dynamic casters) and the per-draw light-set uploads. Further knobs, cheapest first: `volumetric_fog.scale` (0.25 is fine with
`upsample`), `volumetric_fog.steps`, `shadows.softness = 0`, `lights_per_draw`, `shadows.resolution`,
and `postfx.render_scale` for everything at once.

## Is the GPU actually rendering? (`--gpu-info`, `--gpu`)

Everything in this engine is OpenGL; if the OpenGL implementation the driver hands out is a **software
rasterizer** (Mesa `llvmpipe` / `softpipe`, Windows "GDI Generic" / "Microsoft Basic Render Driver", Apple's
software renderer, SwiftShader) then shadows, lighting, fog and post-processing all execute on the CPU and no
setting in `tunables.cfg` can make that fast — 1280×720 with the full pipeline is single-digit fps. The engine
now checks for this at start-up (`Engine/Platform/GpuInfo.cs`):

* the console prints `GPU: <renderer> (<vendor>) — OpenGL <version>` and, for a software renderer, a red
  `!!! SOFTWARE RENDERER …` block with the likely causes and fixes for the current OS (wrong `DISPLAY`,
  `LIBGL_ALWAYS_SOFTWARE`, missing vendor driver / ICD, Remote Desktop or VM without 3D, hybrid laptop with the
  discrete GPU idle);
* the F3 overlay shows a red banner (in every mode, even "off") and the GPU line in the full view;
* an interactive session on a software renderer gets a **quality clamp** (render scale 0.5, no volumetric fog /
  motion blur, 1024 shadows, one bloom pass, fewer lights) so the game stays usable while you fix the
  driver; `--gpu software` keeps your configured values and silences the warning (headless automation).
* `./run.sh --gpu-info` prints the renderer / vendor / version, whether timer and occlusion queries exist, and
  exits with code 3 on a software rasterizer — handy in scripts.
* `--gpu high` asks for the discrete GPU on hybrid Linux systems (`DRI_PRIME=1`, NVIDIA PRIME render offload,
  set in the native environment before libGL loads). On Windows the choice is made per application in
  *Settings → System → Display → Graphics* (add `RipNRage.exe` / `dotnet.exe`, "High performance") or in the
  vendor control panel — a managed executable cannot export the `NvOptimusEnablement` symbol.
* `tools/env.sh` no longer forces `DISPLAY=:1`; it only defaults to it when that X server exists (the headless
  Xvfb case), because a wrong display silently lands you on llvmpipe.

The CPU-side work the renderer itself did has been moved to the GPU as well: occlusion culling is now
**hardware occlusion queries** (`[culling] occlusion = gpu`, `Engine/Graphics/Culling/GpuOcclusion.cs`) — after the
opaque pass every frustum-visible chunk's bounding box is drawn without colour/depth writes inside an
`ANY_SAMPLES_PASSED` query and the result read back the next frame decides whether the chunk is drawn (unknown
= visible, so nothing pops out; a chunk that becomes visible appears one frame later). The legacy CPU software
rasterizer remains available as `occlusion = cpu`. Static shadow casters are cached (see *Lighting*) and the
particle sheet, fog and lighting math run in shaders.

## Profiling (F3, `--perf-log`, `--perf-csv`)

The frame counter measures **wall-clock time between loop iterations** (`Engine/Core/Profiler.cs`) — it is
independent of `--fixed-dt`, which only fakes the *simulation* delta (the old counter divided by that fake
delta and happily reported 1/dt fps while the machine crawled; the overlay now says `SIM DT FIXED … (NOT REAL
TIME)` when that mode is on). F3 cycles: off → one line → full profiler:

* **Frame line**: fps, 1 % low (99th-percentile frame time), average / p99 / worst frame ms over the last half
  second, GPU ms (timer queries, when the driver has them), **present** ms (time blocked in SwapBuffers: vsync
  wait or the GPU running behind the CPU), and the time scale.
* **Stage table**: every step of the frame with CPU ms (average, worst) and GPU ms — poll, hot reload, scene
  (fixed update / update / coroutines / late update), physics, tweens, particle simulation, render (collect,
  shadow map, culling, sky, world, particles draw, post-fx world = fog / motion blur, viewmodel, post-fx
  final = bloom / lens, UI) and present — with bars proportional to the frame. GPU values are one TIME_ELAPSED
  query per stage, read back three frames later; drivers without meaningful timers (software GL) report CPU
  submission cost only, so **F11** (or `--profile` / `RIPNRAGE_PROFILE=1`) inserts a glFinish after every GPU
  stage, making the CPU column the true cost of each stage at the price of serialising CPU and GPU.
* **Frame-time graph**: the last 240 frames with 16.7 / 33.3 ms guides (green / yellow / red).
* **Memory**: GC gen 0/1/2 collections per second, managed bytes allocated per frame on the main thread, heap
  size. **Scene**: objects, behaviours, coroutines, fixed steps run this frame (`8 (CLAMPED)` means the
  simulation cannot keep up with real time), draw calls, particles and their collision raycasts.
* **Physics**: colliders in the world (static ones in the broadphase grid + dynamic ones), kinematic and rigid
  bodies (with their contact manifolds / points), movers (trigger-checked colliders) and the raycasts / shape
  sweeps / broadphase queries of the last frame.
* **Mover**: the player's force-driven body — grounded/airborne, speed, the ride gap under the capsule (`RIDE`,
  50 cm when resting), the slope under it, its contacts this step, whether it is dashing, and how many times the
  fall-respawn caught the player (`FALLS`).

`--perf-log 2` prints the same summary to the console every two seconds; `--perf-csv run.csv` writes a row per
frame for spreadsheets. The end-of-run line (`--frames N`) reports the real average frame time and notes when
the simulation delta was fixed.

## Renderer batching (sprites, instanced meshes)

World sprites that share a texture, the default sprite material and lighting state are drawn in **batches**
(`SpriteBatch`): their quads or tight polygon meshes are written in world space — position, uv, per-vertex tint,
lighting normal — into one vertex buffer per flush, uploaded with an orphaning `glBufferData` (a `glBufferSubData`
into a buffer still being read stalls the driver: that is what made 3500 ragdoll pieces cost 875 ms a frame) and
drawn with the `sprite@spritebatch` shader variant, which `ShaderLibrary.GetVariant` derives mechanically from the
sprite shader (the `uModel` / `uTint` / `uSpriteNormal` uniforms become attributes; the variant follows hot
reloads of `sprite.glsl`). Depth order is kept: batchable runs flush whenever a sprite with a custom material or a
property block (hit flash, dissolve) has to be drawn in place, and a batch closes when its bounds would grow past
40 m so the per-draw point-light subset stays local. The shadow pass uses the same batches with a depth-only
shader. Opaque **meshes** that are identical (same mesh, material, texture, colour, uv scale, emissive, no property
block) are drawn with `glDrawElementsInstanced` through the `@instanced` variant of their shader (one model matrix
per instance in attributes 4–7); groups are split at 48 m for the light subset too. `SpriteBatching` /
`MeshInstancing` on the pipeline turn either off; the F3 overlay counts batched sprites and instanced meshes.

## Benchmark (`bench.sh`, `RipNRageBenchmark`)

`src/RipNRage.Benchmark` is a separate executable with no game logic that stress-tests the engine scenario by
scenario in one window and grades each against a time budget:

| Scenario | What runs | Default |
|---|---|---|
| `crates` | one-metre rigid-body crates dropped in a loose block that tumbles into a heap; a blast every 4 s keeps it colliding (box/box SAT + clipping, stacking, sleeping, warm start) | 1000 |
| `ragdolls` | billboard sprite enemies sliced into ragdolls with `SpriteRagdoll` — box / capsule pieces on ball constraints, piece/piece and piece/floor contacts; a blast every 5 s | 500 |
| `projectiles` | swept `KinematicBody` projectiles (Stop mode, trigger volumes, per-frame) kept in the air from ring emitters through a field of pillars | 1000 in flight |
| `ground` | force-driven `CharacterBody` runners at 12–16 m/s on a hilly `MeshCollider` terrain of ~29k triangles (ground sphere casts, capsule/triangle contacts) | 200 |

```bash
./bench.sh                                   # all four, 3 s warm-up + 12 s measured each, report in the console
./bench.sh --scenario crates --crates 2000   # one scenario, more of it
./bench.sh --no-render --report bench.md --csv bench.csv   # simulation cost only; markdown + per-frame CSV
./bench.sh --scale 2 --strict                # twice everything; exit code 3 when a budget is missed (CI)
```

Every frame is sampled at the profiler's end-of-frame hook (`Profiler.FrameEnded`) so the *stage* times of that
very frame are recorded: frame, physics, scene and render ms, fixed steps run, rigid bodies (awake), islands
(largest), threads, manifolds / contacts / joints, kinematic bodies, broadphase queries / sweeps / raycasts, draw
calls, allocations. The rigid-body step runs inside the scene's fixed loop (stage `fixed physics`), and a slow
frame runs several fixed steps, so the physics budget is judged **per fixed step**: `fixed physics / steps` plus
the per-frame mover / trigger pass. The report is a table with average / p99 / max per scenario and **PASS /
FAIL** against `--budget-frame` (16.7 ms) and `--budget-physics` (8 ms per step); `--duration`, `--warmup`,
`--scale`, `--crates/--ragdolls/--projectiles/--runners`, `--threads`, `--fixed-dt` and `--gpu software`
(headless machines) adjust the run. The scenarios are what drove the dynamic broadphase grid, the cached world
inertia / rotation matrices, the Gauss-map edge pruning, the island / graph-coloured multithreaded solver, the
allocation-free scene loop and the sprite batching / mesh instancing above.

## Render passes, viewmodel FOV & culling (`tunables.cfg`)

`Engine/Graphics/RenderPipeline.cs` is an explicit multi-pass forward renderer:

| # | Pass | Notes |
|---|---|---|
| 0 | **Shadow map** | every caster (meshes + lit sprites), never culled — off-screen walls still cast shadows |
| 1 | **Sky** | full-screen gradient, rotation-only view |
| 2 | **World** | frustum + occlusion culling → opaque meshes by render queue → transparent meshes → world sprites back-to-front → debug lines; then the world post-fx stage (volumetric fog, motion blur) |
| 3 | **Viewmodel** | everything with `renderLayer = RenderLayer.FirstPerson` (the weapon, hands, muzzle flash; meshes too). Same view matrix as the camera but its **own projection** (`[viewmodel] fov/near/far`) on a **cleared depth buffer**, drawn *after* the world post-fx so motion blur / fog never smear the gun. Optionally lit by the sun, point lights and the muzzle-flash light (`lit`, `wrap`, `exposure`); `SpriteRenderer.fullBright` opts a sprite out (the flash itself). |
| 4 | **Lens post-fx** | bloom, chromatic aberration, vignette, colour → default framebuffer |
| 5 | **UI** | `IUiDrawer` components through `UiBatch` |

```ini
[camera]      fov = 0  near = 0  far = 0        ; world camera; 0 = keep the game's value (77 / 0.05 / 2000)
[viewmodel]   enabled = true  fov = 77  near = 0.02  far = 50  lit = true  wrap = 0.85  exposure = 1.15
[culling]     frustum = true  occlusion = gpu  buffer_width = 192  buffer_height = 96  lights = true  debug_view = false
```

Because the two FOVs are independent you can set `[camera] fov = 100` for a wide world view while the
weapon keeps its 77° look (or shrink/enlarge the weapon with `[viewmodel] fov` alone). `enabled = false`
hides the weapon layer. All values hot-reload like the rest of the file; the debug overlay (F3) shows
`FOV WORLD x / VIEWMODEL y`.

**Culling** (`Engine/Graphics/Culling/`) runs on the world pass only:

* **Frustum** — planes extracted from the view-projection matrix; every world `MeshRenderer`
  (mesh bounds transformed to a world AABB), `SpriteRenderer` (quad corners; billboards use a
  yaw-independent box) and point light (range sphere) is tested. Bounds get 0.3 m of padding so
  shader vertex animation (water waves) can never pop.
* **Occlusion** — by default hardware occlusion queries (see above). In `cpu` mode a CPU `OcclusionBuffer` (default 192×96) rasterises the level chunks flagged
  `MeshRenderer.occluder` (every opaque brush chunk from the level loader) with perspective-correct
  depth, near-plane clipping and back-face rejection. Occludees are tested with their AABB's screen
  rectangle against the box's nearest depth: an object is skipped only when every pixel it could
  touch is covered by nearer geometry (pixel-centre coverage for occluders + over-covering rectangles
  for occludees keep it conservative, so nothing visible is ever dropped). Point lights are culled the
  same way, which also removes them from the shading loops. The dock level is split into 16 m chunks
  per texture (`LevelLoader.MeshChunkSize`) so parts of the level behind walls are skipped too.
  Typical cost on a software renderer: 0.1–0.3 ms/frame. Shadow casters and the viewmodel are never culled.
* `debug_view = true` draws the depth buffer (white = near, black = empty) with the counters in the
  bottom-right corner; the overlay line reads e.g. `MESHES 6/30 (F6 O18)` = 6 drawn of 30, 6
  frustum-culled, 18 occluded.

![culling](docs/culling.png)

`levels/culling_test.map` is the dock with a wall in front of the player start — run it with
`debug_view = true` (`./run.sh --level levels/culling_test.map`) to watch the counters.

## Materials & shaders (dynamic renderer changes)

`Engine/Graphics/Materials/`:

* **`Material`** — a shader + typed properties (`SetFloat/SetColor/SetVector/SetTexture`) + render
  state (`blend`, `cull`, `depthWrite`, `depthTest`, `renderQueue`, `alphaCutoff`). Assign to any
  `MeshRenderer.material` / `SpriteRenderer.material` at runtime; change properties any time.
  Unassigned renderers use the engine defaults (`mesh`, `sprite` shaders).
* **`MaterialPropertyBlock`** — per-object overrides on top of the material
  (`renderer.propertyBlock.SetFloat("_Flash", 1f)`).
* **`ShaderLibrary`** — shaders are `.glsl` files in `Standalone/shaders/` (`#pragma vertex`,
  `#pragma fragment`, `#include "lighting.glsl"`), **hot-reloaded** when saved (a compile error is
  logged and the previous program is kept). The engine built-ins (`mesh`, `sprite`, `sky`, `ui`,
  `include/lighting.glsl`) are files too — edit `mesh.glsl` and every mesh changes live. Without the
  folder the embedded copies are used. `--shaders <dir>` / `RIPNRAGE_SHADERS` override the location.
* **`MaterialLibrary`** — named materials retunable from `tunables.cfg`:
  ```ini
  [material.water]
  _WaveSpeed = 2.0
  _DeepColor = 0.05, 0.25, 0.45      ; r,g,b[,a] -> colour, one number -> float, "x, y" -> vec2
  shader = water                     ; blend = alphablend|additive|multiply|opaque, cull, queue, depth_write, alpha_cutoff
  _NormalA = tex:Textures/Normals/waternormal.png
  ```

Uniform contract for custom shaders (set by the engine when present): `uViewProj`, `uModel`,
`uTime`, `uTimeVec`, `uTex` (unit 0: the sprite/renderer texture), `uTint`, `uUVScale`, `uEmissive`
(meshes), `uAlphaCutoff`, `uLit`, `uSpriteNormal`, `uWrap` (sprites) plus everything in
`include/lighting.glsl` (sun, ambient, point lights, shadow map, fog, camera). Material/property
textures are bound from unit 4 upwards.

Shipped examples: `water.glsl` (vertex waves + the project's normal maps + fresnel/specular, on the
water plane), `mesh_emissive_pulse.glsl` (lamp heads), `sprite_dissolve.glsl` (enemy death) and the
default `sprite.glsl` `_Flash` property (enemy hit flash, driven by `EnemyHitFlash` through the
property block). PNG **and baseline JPEG** textures load (a few `.png` assets are actually JPEGs).

## Game flow: title screen, pause and death

The game boots to a **title screen** (`Scripts/UI/MainMenuScreen.cs`) drawn over an attract-mode view of
the level — the level is built without a player and a `MenuCamera` slowly pans from the player start.
NEW GAME starts the level, CONTINUE loads the checkpoint (or the quick save) when one exists, QUIT
exits. Every screen is driven by the same immediate-mode widget (`Scripts/UI/MenuList.cs`: arrows / W S
+ Enter / Space, or mouse hover + click) and `GameSession` (`Content/GameSession.cs`) performs the
transitions at the start of the next frame, so a scene is never torn down from inside a callback.

**Esc** opens the **pause menu** (`PauseMenu`): the world freezes (`Time.timeScale = 0`), the mouse is
released and the player's input component is disabled; RESUME, RESTART LEVEL, LOAD CHECKPOINT (when the
checkpoint save belongs to this level), GRAPHICS, MAIN MENU and QUIT are offered.

**Input schemes** (`Engine/Input/InputSystem.cs`): input is action based. Two schemes exist — *gameplay*
(move, look, fire, jab, jump, weapons, pause, quick save/load) and *UI* (up/down/left/right, confirm, back,
close) — and exactly one is live at a time: every screen with buttons (pause menu, death screen, graphics and
controls pages, the title screen) registers itself as a UI owner while it is up, which switches the active
scheme, idles every gameplay action (no shots fired through a menu, no camera drift) and frees the cursor;
when the last owner closes the gameplay scheme and the mouse lock come back. Each action has a keyboard/mouse
slot and a gamepad slot. Gamepads are read through GLFW's SDL-style mapping (`Input.GetPadButton/GetPadAxis`,
hot-plug, first connected pad): left stick moves (deadzone + response curve), right stick looks at
`gamepadLookSpeed` degrees per second, triggers are digital bindings with an analog `Value`, D-pad and stick
navigate menus with auto-repeat, and the pointer hides while the pad is the active device.

**Controls menu** (`Scripts/UI/ControlsMenu.cs`): every gameplay action with both bindings side by side. Enter /
A / click on a row and press the new control: a key, mouse button or wheel notch fills the keyboard slot, a
pad button, trigger or stick direction fills the gamepad slot; Esc / B cancels, Backspace clears the keyboard
slot, the pad's BACK button clears the gamepad slot; a control already used by another action is taken away
from it. Below the actions: mouse sensitivity and Y inversion, gamepad look speed, Y inversion, stick deadzone
and response curve, RESET TO DEFAULTS. Saved to `controls.json` as you go.

**Graphics menu** (`Scripts/UI/GraphicsMenu.cs`, from the pause menu or the title screen): every visual effect
as its own row — post-processing master switch, render scale, vsync, HDR buffers, bloom (+ passes), chromatic
aberration, vignette, motion blur, volumetric fog, light shafts, fog quality / resolution,
distance fog, shadows, shadow resolution, soft shadows, sprite shadows, lights per object, specular, lit weapon,
particle density, rage screen effects, occlusion culling mode and the performance overlay. Left/Right, Enter or
a click changes a value; it applies immediately and the real frame time is shown at the top so the cost of each
effect is visible while you toggle it. Choices are saved to `<persistentDataPath>/graphics.json` as
**overrides on top of `tunables.cfg`** (`Content/GraphicsSettings.cs`): the file keeps hot-reloading and the
player's choices are re-applied after every reload; RESET TO CONFIG DEFAULTS forgets them.

**Death loop.** When the player's health reaches zero `PlayerState.Die()` runs the
`PlayerDeathSequence` on the player prefab: input, combat (weapon and fists hidden), look, head-bob and
recoil are switched off, the camera drops to floor level and rolls over while the world slows to 20 %,
a synthesised thud plays (`Content/ProceduralSounds.cs` — no audio asset exists for it) and the
**"YOU ARE DEAD" screen** (`DeathScreen`) fades in: blood-red tint and vignette, then the title, then
RESTART / MAIN MENU ~2.4 s after death. RESTART respawns from the last checkpoint when the current run
reached one (or was loaded from a save) and the `checkpoint` slot belongs to this level, otherwise from
the level start; MAIN MENU returns to the title screen.
Enemies drop a dead player as their target and go back to wandering; damage to a corpse is ignored and
the HUD shows the death portrait. `--script die` automates the whole loop (kill, wait, RESTART).

**Falling / clipping.** The player is a force-driven rigid body (`CharacterBody`, see
[Physics](#physics-collision-world-move-and-slide-kinematic-bodies)) simulated by the rigid-body solver with
continuous collision: its motion is swept every substep and clamped at the first contact, so a long frame cannot
push it through a floor slab or a wall whatever the frame time (the simulation step is also capped at 100 ms),
and the contact solver pushes a capsule that does start inside geometry out along the contact normal. The F3
overlay's `MOVER` line shows the ride gap and the slope under the player.

On the rendering side the GPU occlusion culler
never culls geometry within 6 m of the camera and re-tests meshes that re-enter the view, so the floor you
stand on can no longer blink out for a frame.

**Falling off the level** (`FallRespawn` on the player prefab). The kill height is the worldspawn `kill_y` key,
else one metre under the `water_plane`, else five metres under the lowest brush. Dropping below it is **lethal by
default**: the body stops, the death camera falls, the screen reads "YOU ARE DEAD — YOU FELL INTO THE HARBOUR" and
RESTART reloads the last checkpoint or the level start, like any other death. `--fall-mode respawn` (or the
worldspawn key `fall_mode respawn`) switches to an arcade respawn instead: the player is put back, undamaged,
on a spot they stood on **at least a second before** the fall whose whole footprint has ground under it (four
probes down from the capsule's corners) — never the last grounded frame, which for a walk off an edge *is* the
brink; falling repeatedly within eight seconds falls back to the level start.

Every fall is classified and logged: the game probes straight down at the point where the player first dropped
half a metre below the level they last stood on. Floor right there at that level means they went **THROUGH
GEOMETRY** (a collision bug — the log line carries the collider name and coordinates, please report it); no floor
means they went **off an edge**. The F3 overlay's `MOVER` line shows `FALLS n (MODE …, LAST: EDGE | THROUGH
GEOMETRY)`. Keep in mind the dock is 32 m wide and the run speed is 16 m/s: a sideways run from the middle
reaches the water in one second. `--script fall` strafes off the dock (expects `EDGE`); `--script strafe`
strafes back and forth across the *middle* of the dock for 600 frames with jumps and prints `strafe PASS` /
`strafe FAIL` — any fall there is a real through-the-floor bug.

Two related fixes: the player's maximum health/armour is now a fixed 100 (the original used the saved
health as the maximum, so healing after a checkpoint load capped at the saved value) and the legacy
"Save1" checkpoint file is no longer re-read on every level start, which made restarts inherit the
health and ammo of an old checkpoint.

UI drawers can implement `IUiLayer` (engine) to control their draw order: HUD 0, debug overlay 50,
menus 200, the death screen 300 — so overlays always draw above the HUD regardless of object order.

## Physics: collision world, move-and-slide, kinematic bodies

`src/RipNRage.Engine/Physics/` is a self-contained physics engine (no packages, no unsafe code): a collision
world with exact sweeps for everything that moves by code, triangle-mesh collision, and rigid bodies with a
soft-step contact solver.

**World** (`PhysicsWorld`, reachable as `Physics.World`). Every `Collider` registers itself. Colliders marked
`isStatic` (every brush, the level props) have their bounds cached once in a uniform grid (8 m cells; a shape
spanning more than 64 cells goes on a short "large" list instead); dynamic colliders (characters, projectiles,
pickups, rigid bodies) live in a second, finer grid (`DynamicCellSize`, 2 m) by a **fat box** — their bounds
plus `FatMargin` (25 cm) — and are re-bucketed only when their real bounds leave it (`Touch`, called by every
body that writes its pose; `RefreshDynamics` once per fixed step catches transforms moved by scripts), so
resting and slow bodies cost nothing and a query against five thousand movers visits a handful of cells
instead of the whole list. Queries visit the grid cells they touch, with a stamp so a shape that spans several
cells is tested once, and raycasts walk both grids along the ray (3D DDA) and stop as soon as the best hit is
nearer than the next cell. Candidate lists come from a pool: no query allocates. `ConvexCollider` is static by nature (world-space planes); set `isStatic = true` on any `BoxCollider`
that never moves (and call `RefreshBounds()` if it ever does).

**Shapes.** The general-purpose collider components are `SphereCollider`, `CapsuleCollider` (along the local Y
axis), `BoxCollider` (oriented by its transform), `ConvexCollider` (brushes) and `MeshCollider`; each exposes
its exact world-space `CollisionShape` — a polytopal *core* (a point, a segment, eight corners, the brush's
vertices) plus a rounding radius. Every query is exact for every pair: distance, overlap and sweeps between
spheres, capsules, boxes, brushes and triangles go through `ConvexDistance` — **GJK** on the cores with the radii
subtracted afterwards (flat features only, which keeps it robust; witness points from the final simplex's
barycentrics) and **conservative advancement** for sweeps (advance by the current distance over the closing
speed until within the skin), which yields the true time of impact along the whole motion. Axis-aligned box
movers against brushes and meshes keep a faster exact path: every solid is also a `ConvexHull` (outward planes
with **bevel planes**, Quake's Minkowski trick — `ConvexHull.TraceBox` / `BoxPenetration`). Sweeps stop a `skin`
(1 cm) short of the surface so the next one never starts inside. Raycasts are exact for all of them too
(`Physics.RayCapsule`, `RaySphere`, hulls, Möller–Trumbore for meshes).

**Queries** (`Physics.*`): `Raycast` (Unity signatures), `SphereCast` / `CapsuleCast` / `BoxCast` (oriented
too) / `ShapeCast` — all continuous sweeps — `SphereCastAll`, exact `OverlapSphere` / `OverlapBox` /
`OverlapCapsule` / `OverlapShape`, `CheckSphere` / `CheckBox` / `CheckCapsule` / `CheckShape`,
`ComputeDistance` (closest points of two shapes) and `ComputePenetration` (the way out of a collider), and for
movement `Sweep`, `MoveAndCollide` (move until the first contact) and `MoveAndSlide` — each with a
`CollisionShape` overload, so a sphere, capsule or oriented box can be moved with the same solver as a box. A `QueryFilter` says what a query may see:
layer mask, an optional `self` collider whose hierarchy is skipped and whose layer-matrix row is applied, an extra
`ignore` transform, whether triggers count, and a predicate for per-collider vetoes.

**Move and slide** (`KinematicSolver`, via `Physics.MoveAndSlide(center, halfExtents, motion, MoveSettings,
MoveResult)`). Collide-and-slide in the PM_FlyMove lineage with the floor handling of a modern character
controller: the box is swept along what is left of the motion, stops at the first contact and the remainder is
redirected along the surface; walls and ceilings lose the component into the plane, floors (surfaces flatter
than `floorMaxAngle`) keep the *horizontal* motion and follow the slope with it, so a box standing on a slope
does not creep downhill; a second plane turns the motion into the crease between the two, a third stops it, and
the motion is never turned back against its original direction. Ledges up to `stepHeight` are climbed by
re-tracing from a raised position (up, forward, down — only accepted when the box lands on a floor higher than
before, so steep slopes cannot be "stepped" up), and a box that was on the ground is snapped back down onto a
floor within `snapLength` (stairs down, slope crests) unless it is moving up. Before all that the box is pushed
out of anything it starts inside (`depenetration`); the result reports the contacts (collider, point, normal,
floor / wall / ceiling), the ground, the remainder of the motion and `ClipVelocity()` to keep a body's velocity
consistent with what it touched. The broadphase runs once per move; one `MoveResult` is reused per mover.

**Bodies.** `KinematicBody` is the component for entities and objects moved by code — enemies, projectiles,
thrown things, moving props. It integrates its `velocity` with gravity (`gravityScale`), `drag` and a terminal
speed, moves through `MoveAndSlide` and responds per `motionMode`: `Slide` (characters — velocity clipped by the
contacts), `Bounce` (`bounciness` / `friction`, comes to rest below `restSpeed` on a floor) or `Stop` (stops dead
at the first contact and stays `Stopped` until told otherwise — projectiles). `simulation` picks when it moves:
`FixedStep` (after the FixedUpdate callbacks of every fixed step), `PerFrame` (after Update, for things that
must look smooth at any frame rate) or `Manual` (`Move(motion)` / `Simulate(dt)`). The shape is the collider on
the object or explicit `halfExtents`; `collisionMask`, `useLayerMatrix`, `collideWithTriggers`, `ignore` and
`collisionPredicate` make up its filter; `Collided` fires per contact after the move with the exact point and
normal; `reportTriggers` registers the collider as a mover so `ITriggerListener` callbacks fire for it.
`CharacterController` (the player: a box by default, or a true capsule with `controllerShape = Capsule`, which
rounds corners and slides around edges like Unity's) and `NavMeshAgent` (enemies: gravity, wall sliding, 0.4 m
steps, their capsule collider as the exact shape) are thin shells over the same solver, keeping their
Unity-shaped APIs.
Projectiles fly as `Stop` bodies swept every frame (`Prefabs.AddProjectileBody`): they cannot skip through a
wall or an enemy at any speed or frame time, they detonate at the exact contact point, and the contact is handed
to the same `OnTriggerEnter` the original scripts implement (trigger volumes such as the water still arrive
through the trigger pass).

**Settings** (`PhysicsSettings`, `tunables.cfg`). Every global knob of the simulation lives in `tunables.cfg`
next to the rendering tunables and is hot-reloaded the same way: `[physics]` (tick rate = the fixed step,
max steps per frame, max frame delta, gravity), `[collision]` (contact margin, sweep skin, broadphase cell,
CCD skin, body speed caps, sleep threshold / delay), `[solver]` (substeps, velocity / relax / position
iterations, position-correction mode, warm starting, contact cache lifetime, friction model, contact hertz /
damping, Baumgarte factor, slops, max correction / bias velocity, restitution threshold, the level surfaces'
static / dynamic friction, restitution and combine modes, joint hertz / damping), `[movers]` (kinematic
floor angle, slide iterations, agent step height / slope, push mass), `[character]` / `[enemy_character]`
(the force-driven bodies: mass, ride height, spring frequency / damping / cap, ground tolerance, slope,
accelerations, jump grace) and `[movement]` (the player's speeds, jump, gravity multiplier, smoothing, slide,
wall run and dash). `PhysicsSettings.ApplyToWorld` pushes them into the engine, the defaults of new bodies /
joints / agents and the live characters (`CharacterBody.profile` Player / Enemy follows the shared profiles);
`MovementSettings` does the same for the FPS controller. The level editor's **physics panel** (`F9`) edits all
of them with drag cells — three pages: step / collision / solver, characters / movers, player movement —
applies them to the editor's world as you drag and saves them back into the file (existing keys keep their
place and comments), which a running game picks up within half a second.

**Triggers.** Any collider with `isTrigger` is a trigger volume; any collider can be a trigger source. Once per
frame, after the per-frame bodies, every mover (characters, kinematic bodies, projectiles and every rigid body's
collider) is tested against the trigger volumes near it with the **exact shapes** (a sphere trigger is a
sphere, not its bounding box) and the pairs are diffed against the previous frame: enter / stay / exit go to
`ITriggerListener` / `ITriggerStayListener` components on both objects and to the colliders' own `TriggerEnter`
/ `TriggerStay` / `TriggerExit` events (no component needed). Mesh triggers use their bounds.

**Mesh collision** (`MeshCollider`). A rendered `Mesh` (or explicit triangles) collides as a static triangle
surface: the triangles are baked to world space when the collider registers (`RefreshBounds()` re-bakes after a
move) and indexed by an AABB tree, so raycasts (Möller–Trumbore, nearest-first traversal), box sweeps and rigid
bodies only touch the triangles near them. Triangles are double-sided and have no thickness; for the box sweeps
each one becomes a small bevelled `ConvexHull` on first use (Quake 3's patch collision), which is exact and does
not catch on the edges between triangles. The legacy arena's dock now collides as the `dock.obj` mesh itself —
the slanted pier is exact instead of a grid of boxes.

**Rigid bodies** (`RigidBody`). A body has mass, a box, sphere or capsule inertia tensor from its `BoxCollider` /
`SphereCollider` / `CapsuleCollider` (other colliders are simulated as their bounding box), linear and angular velocity, damping,
`restitution`, `staticFriction` and `friction` (dynamic), forces and torques (`AddForce`, `AddTorque`, `AddForceAtPosition`,
`AddImpulse(AtPosition)`, `AddExplosionForce`, the four `ForceMode`s), `isKinematic` (infinite mass, moved by its
velocity or transform, pushes everything), `freezeRotation`, sleeping (`sleepThreshold` / `sleepDelay`, woken by
forces and by moving bodies), a `Collided` event with point, normal, impulse and approach speed, and
`MovePosition` / `MoveRotation` for teleports. Bodies collide with every solid (brushes, meshes, props, the
player and other movers, which they treat as immovable) and with each other; the player and kinematic bodies in
turn shove them (`pushMass`: bodies lighter than it take the mover's speed, heavier ones proportionally less).

*Narrow phase* (`ConvexShape`, `ContactGeneration`): polytopes carry full topology (vertices, face polygons,
edges with their two faces). Box/box, box/brush and box/triangle use the separating-axis test over both shapes'
face normals and the edge pairs — only those whose arcs cross on the Gauss map, i.e. that build a face of the
Minkowski difference (Gregorius' test; a box pair goes from 144 edge pairs to about 18) — faces preferred over
edges by a small tolerance, and the reference face stays on the same shape unless the other is clearly better,
so manifolds do not flip between steps; the contact manifold is the
incident face clipped against the reference face's side planes (Sutherland–Hodgman), up to 8 points, or the
closest points of the two edges. Spheres and capsules use GJK closest points: a capsule gets its two end
spheres against the other shape's support plane so it rests flat, plus the closest pair, and rounded/rounded
pairs (including the player's capsule or any sphere / capsule collider) the same way. Contacts are kept alive
while the shapes are within a 1 cm margin. Against mesh triangles the triangle's own normal wins whenever its
penetration is shallow, the usual mitigation for bodies catching on internal edges of a tessellated floor.

*Solver* (`RigidSolver` + `ContactSolver`, Erin Catto's soft-step scheme): each fixed step is cut into 4
substeps; every substep integrates velocities, warm-starts joints and contacts with the impulses cached from the
previous step, solves the joints then the contacts `velocityIterations` times with position bias, clamps
continuous-collision motion, integrates positions, solves again without bias (`relaxIterations`, so the
velocities end up consistent) and, in NGS mode, runs `positionIterations` position passes. Restitution is
applied after the substeps (contacts that approached faster than `restitutionThreshold` bounce), then the
impulses are stored in the contact cache, constraints may break and resting bodies fall asleep.

*Position correction* (`positionCorrection`): **Soft** (default) treats every contact and joint as a stiff
spring-damper (`contactHertz` 30 Hz / `dampingRatio` 10, capped at a quarter of the substep rate) that works
penetration out without injecting energy — this is what keeps tall stacks still and lets them sleep; the classic
solver was tried first and a six-box stack "breathed" itself apart. **Baumgarte** adds a velocity bias of
`baumgarte` × error / h (clamped to `maxBiasVelocity`), the classic sequential-impulse look (bouncier, can add
energy). **NGS** (non-linear Gauss-Seidel) leaves the velocity solve unbiased and pushes the poses apart directly
after it (`baumgarte` fraction per pass, at most `maxLinearCorrection`, ignoring `linearSlop` of penetration and
`angularSlop` of joint error) — no velocity is added, so it is the gentlest on resting stacks but the softest on
deep penetration. Slightly separated contact points are speculative in every mode: they may only approach
enough to touch this substep.

*Friction* (`frictionModel`): **StaticDynamic** (default) is a Coulomb cone with two coefficients — a contact
point that was (nearly) at rest tangentially at the start of the step (slower than `slipSpeed`, 5 cm/s) is held
inside the static cone μs·N, so a crate on a slope below the static angle does not creep, while a point that was
already sliding is bounded by the dynamic cone μd·N (the lower coefficient: once moving it keeps going on a
slope steeper than the dynamic angle, and a sliding crate on a floor decelerates at μd·g until it sticks);
**Coulomb** uses μd alone. The coefficients come from the two
surfaces: a collider's `PhysicsMaterial` (`staticFriction`, `dynamicFriction`, `restitution`, `frictionCombine` /
`restitutionCombine` — Average, Minimum, Maximum or Multiply, the stricter mode of the pair winning), else its
rigid body's values, else the solver's defaults for level geometry (`staticFriction` 0.6 / `dynamicFriction`
0.5 / `staticRestitution` 0).

*Islands and threads* (`[solver] threads`, 0 = one per core): every step the awake bodies are grouped by
union-find over the contacts and joints that connect two dynamic bodies (static, kinematic and sleeping bodies
connect nothing). Each island is solved on its own — Gauss-Seidel inside, independent of every other island — so
the islands are distributed over worker threads (`Parallel.For`), and an island only falls asleep when *every*
body in it has rested for the sleep delay (no more crates dozing off under a moving stack). An island with
`colorIslandThreshold` (96) bodies or more — a heap of a thousand crates is one island — is **graph coloured**
like Box2D v3: its contacts and joints greedily take the lowest colour none of their dynamic bodies has yet
(24 colours, the rest go to a sequential overflow list), the colours are solved one after another, and within a
colour every constraint runs in parallel because no two touch the same body. The narrow phase (one body's pairs
per work item, per-thread scratch, a stamp-free broadphase query) and the contact preparation are parallel as
well; the broadphase updates, continuous-collision sweeps, contact cache writes, events and sleeping stay on the
calling thread. The F3 overlay shows islands / largest island / threads; `threads = 1` turns it all off.

The threads come from the engine's own **job system** (`JobSystem`, `Physics/JobSystem.cs`): persistent workers
that spin while a step is running and block between steps, and *multi-stage dispatches* — one dispatch carries a
whole substep phase as a sequence of stages (the small islands as independent items, integrate the large island's
bodies, warm-start colour 0, 1, …, solve colour 0, 1, …, the overflow list as a single-item stage); workers pull
chunks of the current stage from an atomic counter and barrier among themselves before moving to the next stage,
so nothing returns to the calling thread and nothing is allocated per stage. A step is about eleven dispatches
(narrow phase, prepare, 2 per substep, restitution) carrying ~150 stages; the first version used a `Parallel.For`
per colour per pass and spent more time in thread-pool latency than in arithmetic (the benchmark showed
~285 KB of allocations and ~175 dispatches per step). `RigidBody.collisionGroup` (Box2D's group index: bodies in
the same negative group never collide) lets a ragdoll skip every pair between its own pieces before the narrow
phase sees them (`RagdollOptions.selfCollision` turns the old behaviour back on). The rigid step is broken down in
the profiler (`rigid begin / narrowphase / prepare / islands / solve / ccd / finish`) and in the benchmark report.

*Warm starting and the contact cache*: the contact solver keeps every manifold's accumulated normal and
friction impulses keyed by the body / collider pair (per triangle for meshes) and matches the new step's
points by *feature id* (which vertex / edge pair of the two polytopes produced them), falling back to the
nearest cached anchor within 2 cm; a manifold survives `contactCacheLifetime` steps of separation so a brief
bounce does not lose its warm start. `warmStarting = false` runs the solver cold (every step starts from zero
impulses — useful to see what the cache buys). `ManifoldPoint.persistent` / `age` and the solver's
`CachedManifolds` / `MatchedPoints` expose what happened. `Physics.World.Rigid` exposes all of the tuning
(`substeps`, `positionCorrection`, `velocityIterations`, `relaxIterations`, `positionIterations`,
`warmStarting`, `contactCacheLifetime`, `frictionModel`, `slipSpeed`, `contactHertz`, `dampingRatio`, `baumgarte`,
`linearSlop`, `angularSlop`, `maxLinearCorrection`, `maxBiasVelocity`, `restitutionThreshold`,
`staticFriction`, `dynamicFriction`, `staticRestitution`, `frictionCombine`, `restitutionCombine`, `threads`,
`parallelThreshold`, `colorIslandThreshold`).

*Continuous collision* (`RigidBody.collisionDetection`, Unity's names): `Discrete` finds contacts from the
poses once per step; `Continuous` sweeps the body's exact shape (conservative advancement) against the static
world — brushes, meshes, props, the player and other movers — in every substep in which it would move more than
half its smallest extent, and clamps that substep's displacement at the first contact (it then sits within the
contact margin, so the next step's manifold bounces or rests it); `ContinuousDynamic` additionally sweeps it
against the other rigid bodies with their own motion. Nothing a swept body meets can be skipped, however fast it
goes (`maxSpeed` still caps speeds at 80 m/s). The sweeps are translational: a body keeps its orientation within
a substep (a fast-spinning thin plank could clip a corner). The arena's crates are `Continuous`, the balls
`ContinuousDynamic`; the F3 overlay counts the CCD sweeps of the last step.

*Characters* (`CharacterBody`): every entity that walks is a force-driven rigid body now. The component takes
the capsule `RigidBody` on its object (never rotates, never sleeps, no contact friction, continuous collision,
interpolated transform) and keeps it `rideHeight` above the ground on a spring: a downward sphere cast finds the
ground, the spring's acceleration (`rideFrequency`, `rideDamping`, capped at `maxRideAcceleration`) pulls the
capsule to the ride height — so ledges lower than the ride height are climbed without any step logic, slopes are
followed, and the body never scrapes the floor. Steering is `Move(desiredHorizontalVelocity)` once per frame:
the fixed step accelerates the body towards it (`acceleration` on the ground, `airAcceleration` in the air,
`stopDeceleration` when nothing is asked), riding along with whatever it stands on. `Jump`, `Dash` (exact velocity
with gravity off for a duration), `AddVelocityChange` (knockback, rocket jumps) and `ClampVerticalVelocity`
(wall-run glide) are impulses; a jump or an upward push lets the spring go for `jumpGrace` seconds. Walls,
ceilings, crates and other characters are handled by the contact solver — the player pushes crates with its 80
kg and is shoved by blasts and by enemies, and enemies shove each other. The `FPSController` keeps all of its
feel (bunny hop, slide, wall run, dash, landing, rage / style speed) and only computes the velocity it wants;
the `NavMeshAgent` drives a `CharacterBody` when the enemy has one. Rigid bodies are simulated at 50 Hz and a
body with `interpolate` writes its transform every frame blended between the last two steps
(`Scene.FixedAlpha`), so the player moves smoothly at any frame rate. The old kinematic `CharacterController` /
`KinematicBody` movers remain for things that should not be part of the force simulation (projectiles).

*Joints* come in two layers. The solver layer is `Constraint` and its subclasses (`Physics.World.AddConstraint`,
two bodies or a body and the world with `bodyB = null`), solved in the same substeps as the contacts — joints
first, then contacts, in the biased, relax and position passes — warm-started from their accumulated impulses
(`hertz` / `dampingRatio` set the stiffness of their rigid rows in Soft mode, `collideConnected` lets the two
bodies still collide) and **breakable**: `breakForce` (N) / `breakTorque` (N·m) are compared with the reaction the
constraint had to transmit (`lastForce`, `lastTorque`, the peak substep impulse over the step length); when
exceeded it snaps (`Broke` event, removed from the world). Jointed bodies keep each other awake.

- **`BallConstraint`** — ball-and-socket, the anchors stay together (3×3 effective mass). Ragdolls, chains.
- **`DistanceConstraint`** — the anchor distance stays in [`minLength`, `maxLength`]: a rod (equal), a rope
  (min 0) or a slack range; with `stiffness` (N/m) / `damping` (N·s/m) it pulls towards `restLength` inside
  that range. **`SpringConstraint`** is the spring preset (no hard limits unless asked for).
- **`HingeConstraint`** — one shared axis through the anchors, with optional limits (`lowerLimit` /
  `upperLimit` degrees), a motor (`motorSpeed` rad/s up to `maxMotorTorque`, `motorFreeSpin` = never brakes)
  and a torsion spring (`springStiffness` N·m/rad, `springDamping`, `springTargetAngle` — a self-closing door).
  `angle` / `angularSpeed` describe A's rotation relative to B (right-hand rule about the axis).
- **`SliderConstraint`** — prismatic: A translates along an axis fixed in B and keeps its orientation, with
  optional limits (`lowerLimit` / `upperLimit` m), a linear motor (`motorSpeed` m/s up to `maxMotorForce`) and a
  spring (`springStiffness` N/m, `springDamping`, `springTargetPosition`). `position` / `speed` are A's travel.
- **`FixedConstraint`** — a weld: relative position and orientation locked (breakable shelves, glued pieces).

Springs are real spring-dampers (Catto's soft-constraint form with ω = √(k/m), ζ = c/2√(km) from the row's
effective mass), applied in the biased pass only so the relax pass does not stiffen them; motors are
velocity rows with an impulse budget of force × substep; limits are inequality rows that go speculative when
open (they may close the gap this substep but not more).

The component layer is Unity's: a **`Joint`** behaviour on the object with the `RigidBody` — `BallJoint`,
`DistanceJoint`, `SpringJoint`, `HingeJoint`, `SliderJoint`, `FixedJoint` — with `connectedBody` (null = the
world), `anchor` / `axis` in the object's local space, `autoConfigureConnectedAnchor` / `connectedAnchor`,
`breakForce` / `breakTorque`, `enableCollision`, and for hinges and sliders `useLimits` + `limits {min, max}`,
`useMotor` + `motor {targetVelocity, force, freeSpin}` (deg/s and N·m for hinges, m/s and N for sliders),
`useSpring` + `spring {spring, damper, targetPosition}`; `angle` / `velocity` / `position` read the state. The
constraint is built when the component is enabled (the RigidBody is woken first, so component order does not
matter), the editable fields are pushed to it every fixed step (so a motor can be reversed or a limit edited at
run time), motors keep their bodies awake, and a break fires `JointBroke` and disables the component.

In the game: `prop_physics` entities (`size`, `mass`, `texture`, `shape box|sphere|capsule`, `targetname`) can
be placed in levels, the legacy arena has a stack of loose crates and two balls near the start, rocket
explosions throw them (`ProjectileStats.explosionImpulse` / `explosionRadius`), bullets and projectiles shove
them (`hitImpulse`) and walking into them pushes them. The **physics entities** (`PhysicsEntities`) build
machines and puzzles out of joints: `phys_door` (a panel hinged along an edge — `hinge left|right|top|bottom`,
`limits "min max"`, `spring` / `damper` / `closed_angle` for a self-closing door, `motor` / `motor_torque` for a
powered one), `phys_hinge` (a prop turning about a local `axis` through `pivot` — seesaws, levers, windmills,
rotating platforms; limits / motor / spring), `phys_slider` (a prop on a rail along `axis` — pistons, lifts,
sliding doors; `limits`, `motor` m/s + `motor_force`, `patrol 1` to shuttle between the limits, `spring` + `target`), `phys_pendulum` (a prop hanging
`length` below the origin on a rod or `rope 1`, swung out by `start_angle`), `phys_spring_prop` (a prop on a
spring: `length`, `stiffness`, `damper`, `max_length`) and `phys_fixed` (welded in place until `break_force` /
`break_torque`). The connector entities `joint_hinge`, `joint_slider`, `joint_fixed`, `joint_ball`,
`joint_distance` and `joint_spring` are spawned after everything else and join two props by `targetname`
(`body` = the prop the joint sits on, `connected` = the other, absent = the world; the origin is the anchor,
`axis` / `angle` orient hinges and sliders, plus the same limit / motor / spring keys, `connected_anchor`,
`min` / `max` / `rope` / `rest` / `stiffness`, `break_force` / `break_torque`, `collide 1`). The arena's
**physics playground** east of the start (`PhysicsPlayground`) has one of everything: a swing door that
closes on a spring, a pendulum, a crate on a bungee spring, a seesaw, a motorised windmill, a piston that
patrols its rail (`SliderPatrol` reverses the motor at the limits), a hanging chain that breaks under a big
enough blow and a shelf welded to a post that snaps off; `levels/dock.map` carries the same set as entities
(door, pendulum, bungee crate, seesaw, windmill, patrolling piston, a `joint_ball` chain of `prop_physics`
links, breakable shelf) along its east side.

## Combat & movement feel

* **Camera**: `CameraRecoil` now applies the weapon kick that the original left commented out (pitch up + random
  yaw/roll per `WeaponRecoil`, scaled by `kickScale`) and adds a *trauma* shake — smooth noise on rotation and
  position with `shake = trauma²`: heavy weapons add trauma per shot (`WeaponRecoil.cameraShake`: shotgun, MG,
  rocket), explosions shake the camera by distance (`CameraRecoil.ShakeEverywhere`, full at the centre, gone at
  22 m) and knock the player back (rocket jumps: the player's own rockets deal 12 % damage to them).
* **Bloom + spread** on every weapon (`WeaponStats`: `weaponSpread`, `spreadMax`, `bloomPerShot`,
  `bloomRecovery`, `moveSpread`, `airSpread`, `projectileSpreadScale`): each shot widens the cone, it recovers
  while you hold fire, running and being airborne widen it further; the crosshair grows four ticks that open with
  the current spread. Rockets scatter too, less than bullets.
* **Sliding**: Left Ctrl / B while running — the capsule shrinks, the camera drops, a speed boost decays with
  friction, left/right steers; release, slow down or jump to get up (a slide-jump keeps the momentum; a low roof
  keeps you crawling). **Wall running**: airborne next to a wall while pushing forward — gravity drops to 20 %,
  you run along the wall at `wallRunSpeed` with the camera tilted away from it, for up to 1.6 s; Jump launches
  you off the wall. Tunables are public fields on `FPSController` (`[Slide]`, `[Wall run]`).
* **Dash**: Left Shift / L3 — a 0.16 s burst at 26 m/s in the direction of the movement input (forward when idle),
  gravity suspended, one air dash per jump, 0.7 s cooldown, with a whoosh, speed lines and a camera nudge.
* **Quick swap**: Q / Y returns to the previously held weapon.
* **Readable enemies** (`AIAgent`, `EnemyData`): an attack is *telegraphed* — the grunt pulses orange, an aim glow
  swells at its muzzle and a charge whine plays for `attackWindup` (0.35 s) — then it fires a burst of
  `burstCount` slugs (`burstInterval` apart) with a muzzle flare and a shot sound each, and rests for `fireRate`.
  Spotting the player triggers an alert (yellow flash, sting, a burst above the head, `alertDelay`) before the
  tree engages; being hit staggers it (`staggerTime`: stops, delays the shot). Enemy slugs are twice as lethal
  (10 damage), slightly slower (40 m/s), bigger, full-bright, lit and towing a hot trail so they can be read and
  dodged. Tunables live on `RiflemanData` in `GameAssets`.
* **Style meter** (`Scripts/PlayerSystems/StyleMeter.cs`): hits, headshots (the top quarter of an enemy's capsule,
  double damage, bigger blood) and kills add points; killing with a *different* weapon than the last kill within
  6 s raises a multiplier up to x4, points decay after 2.5 s idle. Ranks F → D → C → B → A → S
  (FORGETTABLE … SAVAGE); the HUD element slides in at **B** and fades below it. At **S** the player regenerates
  health, moves 25 % faster and receives ammo of every type every half second, with a gold vignette.
  `--script style` / `--script slide` exercise the systems.

## Visual effects (`vfx/`, `vfx.sh`)

Impacts, blood, explosions, tracers and trails are **data-driven particle effects**: every effect is a JSON file
in `vfx/` (one `ParticleEffect` = a list of emitters plus an optional light flash), `vfx/bindings.json` says
which effect plays for what, and `./vfx.sh` is a live editor for both. The game hot-reloads saved files.

```
vfx/impact_wood.json       { "name", "description", "emitters": [ {...}, ... ], "hasLight", "lightColor", ..., "cullRadius", "scale" }
vfx/bindings.json          { "surfaces": { "wood": "impact_wood", ... }, "bullets": { "heavy": { "tracer": "tracer_heavy", "impactScale": 1.3, "bloodScale": 1.4 }, ... },
                             "explosions": { "rocket": "explosion_rocket" }, "trails": { "rocket": "rocket_trail" }, "deaths": { "default": "blood_death" }, "blood": "blood_hit" }
```

**Runtime** (`Engine/Graphics/Particles/`): `ParticleWorld` simulates every live effect on the CPU (burst / rate
emission from cone, sphere, hemisphere, ring or box shapes; gravity, drag, turbulence, size / colour / rotation over
life; optional raycast collision with bounce, friction, rest, die-on-hit and a sub-effect spawned where a particle
lands — blood drops become splats); `ParticleRenderer` draws everything in the world pass, sorted back to front and
batched per texture / blend / lighting into dynamic vertex buffers (one draw call per batch, depth tested, no depth
writes). Render modes: **Billboard**, **Stretched** along the velocity (sparks, tracers), **Horizontal**, **Aligned**
to the surface the effect hit (flashes, decals, shockwaves). Emitters can be lit (sun / ambient / point lights /
fog through the same `lighting.glsl` as the world) or full bright, alpha / additive / multiply blended, animate
through sprite frames, and reference any registered sprite (`Particles/*` is a procedural sheet of 16 shapes —
soft, dot, smoke, spark, flare, ring, drop, splat, chunk, shard, splinter, fire, dust, scorch, bullet, streak — so
no art files are needed; atlas sprites such as `RocketLauncher/explosion_01` work too). Effects are frustum
culled by `cullRadius`, `ParticleWorld.Density` scales every count (quality setting), and effect lights are
real point lights.

**Game side** (`Game/Content/Vfx/`): `GameVfx` is what weapons call — `Impact(hit, calibre)` resolves the surface
under the hit (`SurfaceResolver`: living thing → flesh, `SurfaceMaterial` on props, `BrushSurface` on level brushes —
the hit normal picks the face and the face's texture maps to a surface through `LevelTextures`, e.g. planks = wood,
metal, glass, sand, water) and plays the bound effect scaled for the calibre; `Blood`, `Tracer` (muzzle → hit point,
lifetime from distance), `Explosion`, `Trail` (follows a projectile, stops when it dies) and `Death`. Hitscan
weapons and the shotgun melee shot, enemy bullets, rockets (trail + explosion) and enemy deaths are wired up; the
old sprite-flash prefabs remain as a fallback when no effect library exists. The base library
(`DefaultEffects.cs`) ships blood_hit / blood_splat / blood_death, impact_concrete / wood / metal / glass / sand /
water / energy, explosion_rocket, rocket_trail and tracer_light / heavy / shell / energy; it is written to `vfx/`
the first time the folder is found empty.

**Editor** (`./vfx.sh [--effect name]`, `src/RipNRage.VfxEditor`): effects and emitters on the left, a property grid
on the right generated by reflection from the emitter / effect fields (ranges, groups and tooltips come from the
`[VfxRange]`, `[VfxGroup]`, `[VfxTip]`, `[VfxSprite]`, `[VfxEffectRef]` attributes — add a field to
`ParticleEmitter` and it appears in the editor and the files), and the effect playing in a loop in the middle on a
concrete wall, the plank floor or in the air (T), or wherever you click (LMB on any surface). Drag numbers to change
them (Shift ×10, Ctrl ÷10, wheel, RMB resets), click sprites to open the palette, trails follow a circling dummy,
tracers fly in towards the anchor. Ctrl+S saves, Ctrl+Z/Y undo, F1 lists every key. Edits apply to the running
effect immediately; structural changes (counts, sprites, lifetimes) restart it.

## Sprite destruction (`skeletons/`, `skeleton.sh`)

Dying sprites are **sliced like a ragdoll**: the enemy's billboard is cut into pieces along a *destruction
skeleton*, each piece becomes a rigid body hung off its parent with a ball joint, the killing blow throws the
part it hit, and explosions tear every joint and scatter the pieces. The pieces are real sprites (only their
polygon of the texture is drawn, lit and shadowed like the original), they tumble in their billboard plane (the
body's roll is read back into `SpriteRenderer.billboardRoll`), collide with the level but not with characters
or projectiles (layer `OnlyGroundCollision`), and fade out after the skeleton's `lifetime`.

**Skeletons** (`skeletons/<name>.json`, `Engine/Destruction/SpriteSkeleton.cs`) are *not* animation
skeletons — nothing is posed with them; they only say where to cut and what hangs off what. A skeleton names a
sheet texture (`texture`, relative to assets/), its grid (`frames` × `sides`), `pixelsPerUnit`, the pieces'
`depth` and `lifetime`, and a list of **views**, one per sheet side (`side`, reference `frame`). Each view lists
**parts**: a `name`, a `parent` (empty = root), a `pivot` (the joint, where the part hangs off its parent), a
`polygon` outline, `mass`, `shape` (`Box` or `Capsule`), `breakImpulse` (0 = never tears) and `loose` (starts
detached). Coordinates are normalised (0..1, bottom-left origin) inside the cell, so one view serves every frame
of that side, whichever sheet (idle, run, attack) the sprite currently shows. `rifleman.json` ships (head, torso,
two arms with the rifle, two legs); `EnemyData.skeleton` names the skeleton an enemy uses (empty = the old
death animation only). The game reloads saved skeletons live; `--skeletons dir` overrides the folder.

**Runtime.** `SpriteRagdoll.Spawn(skeleton, sprite, position, scale, RagdollOptions, template)` slices any
sprite the sheet produced (`Sprite.Cut` makes the cut-out sprites, ear-clipping their polygons) and returns the
ragdoll: `parts`, `Detach(name)` (dismemberment), `BreakAll()`, `AddImpulseAt`, `AddExplosion`, `Nearest`,
`PartDetached`. `EnemyDestruction` (on the rifleman prefab) does it on death using `DamageInfo` — the hit point,
direction and impulse the weapons report right before `TakeDamage` (`WeaponStats.hitImpulse`; rockets report an
explosion, which gibs).

**The skeleton editor** (`./skeleton.sh [--skeleton rifleman]`, `src/RipNRage.SkeletonEditor`). Left: the
skeleton files and every sheet under `assets/Sprites` (set its frames / sides, `Ctrl+N` makes a skeleton from
it). Centre: the view's reference cell with the parts drawn over it — `N` names a new part and you trace its
outline by clicking (Enter or the first vertex closes it, Backspace undoes a vertex), click a part to select it,
drag vertices and the pivot, `I` inserts a vertex on the nearest edge, `Del` deletes a vertex or the part, `J`
places the pivot, `P` picks the parent by clicking it, `L` marks it loose; wheel zooms, middle / right drag pans,
`F` fits. Right: frames / sides / pixels per unit / depth / lifetime, the views (`+ VIEW` adds one for the next
side with the current parts copied, `,` `.` change the side, `[` `]` the reference frame), the parts with their
parent, and the selected part's mass, break impulse, shape and loose flag. `Tab` switches to the **physics
preview**: the cell stands on a plank floor, `Space` slices it into a ragdoll with the real solver (the same code
the game runs), `Ctrl+Space` gibs it, clicking a piece tears it off, `K` breaks every joint, `Up` sets off a
blast, `R` resets, right-drag orbits. `Ctrl+Z` / `Ctrl+Y` undo / redo, `Ctrl+S` saves, `F1` lists the keys.

## Saving & loading

`Content/Level/GameSaveSystem.cs` writes JSON saves to `<persistentDataPath>/saves/<slot>.json`
(`~/.local/share/RipNRage/saves` on Linux): the level path, the player's position/view/health/
armour/rage/combat state, inventory (ammo + equipped weapon), every level entity's state (monsters
alive + health + position, pickups taken, spawner progress) and monsters created at runtime by
spawners. Loading rebuilds the level (`GameSession.RestartLevel`) and re-applies the state once the
player exists. **F6/F9** quick save/load, `trigger_checkpoint` volumes write the `checkpoint` slot
(**F7** reloads it), **F8** restarts the level, `--load <slot>` starts from a save. The original
`SaveData`/`SaveManager` classes are kept in sync for the ported checkpoint code.

## Levels & the level editor

Levels are Quake-style brush maps in the classic `.map` text format (`levels/*.map`; metres, Y up,
texture scale = world units per repeat; plus optional `paint` lines for vertex painting). The game loads `levels/dock.map` by default
(`--level file.map` for another one, `--no-level` for the old hard-coded arena,
`--export-dock out.map` regenerates the dock level from the original `dock.obj`).

```bash
./edit.sh levels/dock.map            # open a level (creates it on save if it does not exist)
./dist/linux-x64/RipNRageEditor levels/mymap.map
```

![editor](docs/editor.png)

**Editor** (`src/RipNRage.Editor`, a separate executable on the same engine — what you see is the
game's own `LevelLoader` in preview mode, with the lighting/shadows/fog from `tunables.cfg`):

| Input | Action |
|---|---|
| RMB + mouse, WASD/QE, Shift, wheel | fly camera (look / move / fast / speed), F = focus selection |
| LMB drag on the ground or on a brush face | draw a new box brush (extruded from the drawing plane) |
| LMB click | select brush + face (Shift adds); drag a selected brush to move it (Shift = vertical) |
| Alt + drag a selected face, `+`/`-` | push / pull the face along its normal (grid steps) |
| Arrows / PgUp / PgDn | nudge the selection by one grid step (camera-relative) |
| R, Ctrl+D, Del | rotate 90°, duplicate, delete |
| `,` `.` / Shift+`,` `.` / T | cycle face texture / halve-double texture scale / texture the whole brush |
| `[` `]`, H, G | grid size, set the work plane height from the hovered point, toggle wireframes |
| Tab | entity mode: `1`-`0` `,` `.` pick a class, LMB on a surface places it, LMB on an entity selects/drags it, R rotates 45°, `+`/`-` edit the class's numeric property (Shift = second one) |
| V (or Tab) | **vertex paint mode**: LMB hold paints the face under the cursor (Shift = erase to white, Ctrl = pick colour), `[` `]` radius, `+`/`-` strength, `,` `.` cell size for newly painted faces, `1`-`0` colour presets, arrows = hue/value, PgUp/PgDn = saturation, Del clears the hovered face |
| P / M / N | quick-place (any mode) the player start (moves the existing one), a `monster_rifleman`, a `monster_spawner` at the cursor |
| Ctrl+S, Ctrl+Z/Y, F5, F1 | save (+ automatic snapshot), undo/redo, save + launch the game on this level, help |
| F7 / F8 | take a snapshot / open the snapshot browser (Up/Down select, Enter restore — undoable —, Del delete) |
| F9 | physics settings panel: every simulation / collision / character / player-movement tunable, saved to `tunables.cfg` (Tab = page, Ctrl+S = save, Esc = close) |

**Snapshots** are timestamped copies of the level in `.snapshots/<levelname>/` next to the file:
manual ones (`…-manual.map`) and an automatic one on every save (`…-auto.map`, the last 20 are
kept). They are plain `.map` files, so any of them can also be opened directly.

**Entity spawns**: `info_player_start` (one per level; P moves it), `monster_rifleman` (a grunt
placed directly) and `monster_spawner` — a wave spawner with `monster`, `count`, `interval`,
`radius`, `activate_range` (0 = immediately, otherwise when the player gets that close) and
`max_alive`; in entity mode `+`/`-` edit `count` (Shift: `interval`). Spawners show a ghost of the
monster in the editor.

**Vertex painting** stores an RGB grid per face in the face's texture space (`paint <face> <w> <h>
<cell> <u0> <v0> <base64>` lines inside the brush block — an engine extension of the .map format;
grids survive moving/resizing the brush). Painted faces are tessellated into cells at load time and
the colours go through a new vertex-colour attribute that multiplies the surface texture (paint on
the `white` texture for pure colours).

Texture names come from `LevelTextures` (planks, sand, grid, dark, orange, green, purple, red, white,
metal, lamp, water, clip = invisible collision, sky = invisible) and entity classes from
`EntityRegistry` (`info_player_start`, `monster_rifleman`, `item_ammo_*`, `prop_physics`, the `phys_*` machines
and `joint_*` connectors, `light`, `lamp_post`, `water_plane`, `floor_plane`, `trigger_checkpoint`) — both in
`src/RipNRage.Game/Content/Level/`, so
adding a texture or entity type is one line for the game *and* the editor. Worldspawn keys
`sun_elevation`, `sun_azimuth`, `sun_color`, `sky_zenith`, `sky_horizon`, `fog_color` override the cfg.

Under the hood (`src/RipNRage.Level`): brushes are convex polyhedra defined by planes (three points
per face, as in Quake); polygons are generated by half-space clipping, textures use the Quake
dominant-axis projection, axis-aligned brushes become box colliders and everything else a
`ConvexCollider` (separating-axis test against the player's box, so ramps and rotated brushes work).
`MapFile.ImportObjBrushes` converts TrenchBroom OBJ exports (that is how the dock was imported).

## Sprite animation editor (`anim.sh`)

![animation editor](docs/animeditor.png)

`RipNRageAnimEditor` (`src/RipNRage.AnimEditor`) edits the sprite-swap clips and animator controllers
the weapons, fists and the rocket explosion use, with the preview drawn by the real renderer: the
sprite sits on the player-prefab rig (`PlayerArms` → weapon position/scale from `WeaponData`, the
fist offsets, or a world billboard for the explosion), goes through the viewmodel pass, the lights,
shadows and post-fx, and in controller mode it is driven by the very same `Animator` the game runs.

```bash
./anim.sh                              # opens animations/ (created from the code defaults if missing)
./anim.sh --clip M1887_Fire            # or --controller LeftFist
```

**Files.** `animations/clips/<Name>.json` and `animations/controllers/<Name>.json`
(`Engine/Animation/AnimationLibrary.cs`). Sprites are referenced as `Atlas/spriteName` through
`SpriteLibrary` (every TexturePacker atlas is registered by `GameAssets`). A clip:

```json
{ "name": "GlockFire", "frameRate": 60, "length": 0.0833, "loop": false,
  "sprites": [ { "time": 0, "sprite": "Glock/Glock01" }, { "time": 0.0333, "sprite": "Glock/Glock02" } ],
  "enabled": [], "events": [ { "time": 0, "function": "OnFire" } ] }
```

Controllers store parameters, states (clip slot, speed, transitions with conditions / exit time),
any-state transitions and the default state; override controllers (`"base": "BaseWeaponController"`,
`"overrides": {...}`) keep the same relationship as Unity's `AnimatorOverrideController`. The game
loads the folder at start-up (code-built clips are the fallback) and **hot-reloads** it: save in the
editor while the game runs and the weapon animates with the new keys immediately — clips and
controllers are updated in place and running animators re-resolve their state by name.
`--export-animations dir` regenerates the defaults from code.

**Editing.** Timeline with a frame ruler at the clip's frame rate, sprite-key blocks (thumbnails),
an enabled track (green/red segments) and an event track; scrub with the mouse, drag keys / event
markers / enabled handles to move them, drag the red end marker (or `[` `]`) to change the length.
Click a sprite in the palette to set the key at the playhead (replaces the key on that frame or
inserts one). `E` adds an event (typed name), `Enter` renames it, `N` toggles an enabled key,
`Del` deletes the selected key, `Ctrl+C/V` copies keys, `L` loop, FPS/LEN/SPEED cells click +/-.
`O` onion skin (previous/next key as ghosts), `G` switches between the centred view and the real game
framing, `Space` play, `←/→` step, `↑/↓` previous/next key. `Ctrl+N` new clip, `Ctrl+D` duplicate,
`F2` rename, `Shift+Del` delete, `Ctrl+Z/Y` undo/redo (clip and controller edits), `Ctrl+S` save all.

**Controller panel** (`Tab`): parameters (click to toggle / fire / ±1 in preview, `+ PARAMETER`),
states (`DEF` = default, clip cell cycles through the library with LMB/RMB, speed cell, `X` removes),
transitions per state and from Any State (destination, parameter, mode, threshold and exit-time cells,
`+ TRANSITION`), override slots for override controllers. `P` starts the controller preview: the
Animator runs the graph, the parameter cells drive it, events flash in the viewport and the timeline
follows the current state. Edits to an override's graph apply to its base (they share the graph).
`F1` shows the key list in the editor.

Not covered (different system): the enemy 4/8-direction sheets (`AnimationStateController`,
`DirectionalSpriteSheet`) — frames × sides grids driven by the AI state, not by clips.

## How the Unity dependencies were replaced

| Unity | Standalone |
|---|---|
| `MonoBehaviour`, `Awake/Start/Update/FixedUpdate`, coroutines, `Instantiate/Destroy` | `RipNRage.Engine.Behaviour` + `Scene` (same callback order; prefab fields are assigned before `Awake`) |
| `Transform`, `Vector3`, `Quaternion.Euler` (ZXY), `Mathf`, `Random`, `Time` | API-compatible engine types (see `Math/`) |
| Prefabs / ScriptableObjects / `.anim` / `.controller` | code in `Content/` with the serialized values copied from the asset files |
| `SpriteRenderer`, `Sprite`, TexturePacker importer | `Graphics/SpriteRenderer`, `Sprite`, `SpriteAtlas` (polygon meshes supported) |
| `Animator` / `AnimatorOverrideController` / animation events | `Animation/Animator` (bool, trigger, float params; exit-time & Any-State transitions; events); clips/controllers as JSON in `animations/` (hot reload, `anim.sh` editor) |
| `CharacterController`, `Physics.Raycast/SphereCast(All)`, `OnTriggerEnter`, layers, `Rigidbody`, `MeshCollider`, `CharacterJoint` (ragdolls) | `Physics/` (collision world with a grid broadphase, exact box-vs-hull sweeps, `Physics.MoveAndSlide`, `KinematicBody`, `RigidBody` + contact solver, `Joint` components (`HingeJoint`, `SliderJoint`, `SpringJoint`, `DistanceJoint`, `BallJoint`, `FixedJoint` over the solver's `*Constraint`s), `PhysicsMaterial`, `MeshCollider` with an AABB tree, `ITriggerListener`, collision matrix); `Destruction/` slices sprites into jointed pieces |
| `NavMeshAgent`, `NavMesh.SamplePosition` | straight-line steering agent that sticks to geometry (no baked navmesh yet) |
| Unity Input System (`PlayerControls.inputactions`) | `InputSystem` action maps (gameplay / UI schemes, rebindable keyboard + gamepad slots, `controls.json`); `PlayerInputActions` dispatches the same On* callbacks |
| `AudioSource` / `AudioClip` | OpenAL-backed equivalents (WAV only for now) |
| `UnityEngine.UI.Image`, TextMeshPro, Canvas 1920×1080 | `UiImage`/`UiText` drawn by `HudController` through `UiBatch` (built-in bitmap font) |
| DOTween (`DOLookAt`, `DOSizeDelta`) | `Tween/TweenRunner` |
| URP post-processing volumes | `RagePostprocess` colour filter + vignette |
| `BinaryFormatter` saves + surrogates | `System.Text.Json` (the surrogates are no longer needed) |

Scripts were kept line-for-line where possible; every change is commented at the top of the file.
Small deviations worth knowing: `SetupProjectile(aiState)` in `AIAgent` compiled in Unity through
the implicit `Object→bool` conversion (the instigator was never set) — the port passes the entity
explicitly; `CombatController.EquipWeapon` also stores `currentWeaponIndex` so wheel/number-key
switching stay in sync; the `0` glyph of the built-in font is plain.

## Known gaps / next steps

* Physics: rigid bodies are boxes, spheres and capsules (convex-mesh bodies are not implemented), joints are
  ball / distance / spring / hinge / slider / fixed (no cone-twist or 6-DOF configurable joint), continuous
  collision is translational (orientation is not swept within a substep), movers push bodies but are not pushed
  back, mesh triggers overlap by bounds, the simulation is not deterministic with `threads > 1` (the order in
  which worker threads hand in contacts changes the Gauss-Seidel order), and rigid-body poses are not saved in
  save games.
  The mesh "ghost edge" handling is a heuristic (triangle normal preferred for shallow contacts), not a full
  edge-adjacency test.
* MP3/FLAC audio (`mg338.mp3`, melee hits, music) is not decoded yet — only PCM WAV.
* Katana and Sonic Shotgun have sprites/animations but no weapon class in the original project
  either; they are loaded but not in the inventory.
* The EffectCore particle prefabs of the Unity project were replaced by the data-driven effects in `vfx/`.
* The full TestArea scene (Dock.blend with materials, lighting, checkpoints, 48 grunts) is
  approximated by the `dock.obj` brushes, a few pickups and riflemen.
* Enemy navigation is straight-line (no navmesh baking); the 8-direction sprite shader is done on
  the CPU with the 4-side sheets.
* Particles have no soft depth fade against geometry and decals are long-lived particles (they do not
  clip to the surface they sit on).
* Save/load is wired (JSON) but no checkpoint volumes are placed yet.
* A sokol-gfx backend could be added behind `RenderPipeline`; all GL calls live in `Graphics/` + `Platform/GL.cs`.
