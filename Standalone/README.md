# Rip-n-Rage — Standalone (Unity-free) build

![standalone build](docs/screenshot.png)

A from-scratch runtime for the Rip-n-Rage first-person shooter that **does not depend on Unity
at all**. The original gameplay C# scripts (`Assets/_Game/Scripts`, `Assets/_AI_BehaviorTree`,
`Assets/Tools`) were ported onto a small custom engine, and the game's own art/audio assets are
loaded straight from the Unity `Assets/` folder (PNG sprite sheets, TexturePacker `.tpsheet`
atlases, WAV audio, the TrenchBroom dock map).

```
Standalone/
├── RipNRage.sln
├── src/RipNRage.Engine/        custom engine (no external packages)
│   ├── Platform/               GLFW + OpenGL 3.3 core bindings (P/Invoke, cross-platform loader)
│   ├── Graphics/               render pipeline, PNG decoder, sprites/atlases, meshes, UI batch, font
│   ├── Core/                   GameObject / Behaviour / Transform / Scene / coroutines / time
│   ├── Animation/              sprite animation clips + Unity-style Animator state machine
│   ├── Audio/                  OpenAL bindings, WAV loader, AudioSource
│   ├── Physics/                colliders, raycasts, CharacterController, layer matrix, triggers, nav agent
│   ├── Math/                   Vector2/3, Quaternion (Unity ZXY euler), Matrix4x4, Mathf, Random, curves
│   └── Tween/                  DOTween-style tweens
├── src/RipNRage.Level/         level data model: brushes/entities, .map read/write, polygons, collision shapes
├── src/RipNRage.Editor/        Quake-style level editor executable (RipNRageEditor)
├── levels/                     .map levels (dock.map = the test area)
├── src/RipNRage.Game/
│   ├── Scripts/                the ported game scripts (same files/classes as Assets/_Game/Scripts)
│   ├── Tools/EnragedInjector.cs
│   ├── Framework/              MonoBehaviour/ScriptableObject/attribute stand-ins, Prefab factories
│   ├── Content/                prefabs, ScriptableObject data, animator graphs and the TestArea level
│   │                           rebuilt in code from the values in the .prefab/.asset/.anim/.controller files
│   └── Program.cs              entry point, CLI options, debug overlay, automation scripts
└── native/linux-x64/           libglfw.so.3 (zlib licence) + libopenal.so.1 (LGPL) for convenience
```

## Quick start (this VM — no internet, no root, no system .NET needed)

Everything required is inside this folder:

```bash
cd Standalone
./run.sh                       # builds with the private toolchain in ./.dotnet and runs the game
./dist/linux-x64/RipNRage      # or: pre-built self-contained binary, needs no SDK/runtime at all
```

* `.dotnet/` – a private .NET 8 SDK (8.0.132). It was obtained from the **Ubuntu 24.04 package
  archive** (`archive.ubuntu.com`), not from Microsoft, and unpacked with `dpkg-deb -x` — no root.
  If the folder is ever missing, `tools/setup-dotnet.sh` recreates it the same way (the only
  network access it needs is the Ubuntu archive, which this VM can reach).
* `native/linux-x64/` – `libglfw.so.3` and `libopenal.so.1` (also from the Ubuntu archive).
* `dist/linux-x64/` – output of `./build.sh` (`dotnet publish --self-contained`): a ~70 MB folder
  with the runtime included. Copy it anywhere on an x64 Linux box with OpenGL 3.3 and run `RipNRage`.
* The game finds the Unity `Assets/` folder by walking up from the executable/cwd (this repo),
  or via `--assets <repo-root>` / `RIPNRAGE_ASSETS`.
* The scripts default `DISPLAY` to `:1` (the VM's Xvfb). Mesa/llvmpipe software rendering works;
  expect ~40–60 fps at 1280×720. On a real desktop just run the same commands.

Helper scripts: `run.sh` (build + run), `edit.sh` (level editor), `anim.sh` (animation editor),
`build.sh` (build + self-contained publish), `tools/setup-dotnet.sh` (toolchain install),
`tools/env.sh` (`source` it to get `dotnet` on PATH).

## Requirements (other machines)

* .NET 8 SDK — any source: the bundled `.dotnet`, `tools/setup-dotnet.sh` (Ubuntu/Debian archive),
  a distro package (`dotnet-sdk-8.0`) or Microsoft's installer. No NuGet packages are used —
  `nuget.config` clears all package sources so the solution builds fully offline.
* OpenGL 3.3 capable driver.
* GLFW 3 and (optionally) OpenAL Soft shared libraries:
  * **Linux x64** – bundled in `native/linux-x64` (copied next to the executable on build).
  * **Windows** – put `glfw3.dll` (and `OpenAL32.dll` / `soft_oal.dll`) next to the executable or in
    `native/win-x64/`.
  * **macOS** – `brew install glfw openal-soft` (Homebrew paths are probed automatically).

  The loader searches `$RIPNRAGE_NATIVE_DIR`, `<exe>/native/<rid>`, `<exe>/runtimes/<rid>/native`,
  `<exe>` and finally the system library paths. If OpenAL is missing the game runs silently.

## Build & run (manual)

```bash
cd Standalone
source tools/env.sh                                           # puts ./.dotnet/dotnet on PATH
dotnet build -c Release
dotnet run -c Release --project src/RipNRage.Game -- --assets /path/to/repo-root
```

### Controls

| Input | Action |
|---|---|
| W A S D | move (bunny-hop when airborne, as in the original FPSController) |
| Mouse | look |
| Left mouse | fire |
| Right mouse | jab combo (L/R fists; the 3rd hit triggers the weapon melee on a grappled enemy) |
| 1 / 2 / 3 / 4, mouse wheel | Glock / M1887 / MG338 / Rocket launcher |
| Space | jump |
| F6 / F9 | quick save / quick load (slot `quick`) |
| F7 / F8 | load the last checkpoint save / restart the level |
| F3 | toggle debug overlay, F12 screenshot |
| F4 / F5 | toggle post-processing / reload `tunables.cfg` + all shaders |
| Esc | release mouse; Esc again quits |

### Command line

```
--width W --height H   window size           --no-vsync        --mute         --verbose
--frames N             exit after N frames   --fixed-dt 0.0166 deterministic frame time
--screenshot out.png   save on exit, or every --shot-interval N frames ("{n}" = frame number)
--script name          automation: look | spin | walk | fire | weapons | jab | m1887 | glockfire | rocket | close | approach | rage | crates | sunward | water | killenemy
--shaders dir          folder with the .glsl shader files (default: "shaders" found in cwd / exe dir / parents)
--tunables file.cfg    post-processing config (default: tunables.cfg found in cwd / exe dir / parents)
--no-postfx            start with post-processing disabled
--animations dir       folder with the animation clip/controller JSON (default: "animations" found in cwd / exe dir / parents)
--export-animations d  write the code-built clips and controllers as JSON into d and exit
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
* Enemies: `AIState` / `AIAgent` / `BehaviorTreeOwner` with the **BehaviorTree2** graph rebuilt in
  code (PlayerSpotted → InAttackRange → LookAt+Attack | MoveTo; else Wait → RandomLocation → MoveTo),
  4-direction sprite sheets (`AnimationStateController`), vision trigger, projectiles that damage
  the player through armour.
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
| `[postfx]` | `enabled` (lens effects), `render_scale` (scene resolution, try 0.6 on software GL), `effect_scale`, `bloom_scale`, `hdr` |
| `[bloom]` | `enabled`, `threshold`, `soft_knee`, `intensity`, `radius`, `passes` |
| `[chromatic_aberration]` | `enabled`, `intensity`, `radial` |
| `[vignette]` | `enabled`, `intensity`, `smoothness`, `roundness`, `color` |
| `[dof]` | `enabled`, `autofocus`, `focus_distance`, `focus_range`, `far_blur_start`, `max_blur`, `near_blur` |
| `[motion_blur]` | `enabled`, `intensity`, `samples`, `max_velocity` |
| `[color]` | `exposure`, `saturation`, `contrast` |

Pipeline (`Engine/Graphics/PostFx/PostProcessStack.cs`): the world is rendered into an offscreen
target with a depth texture → **motion blur** (per-pixel camera velocity from depth reprojection
against the previous frame's view-projection) → **depth of field** (circle of confusion from
linear depth around the focus band, half-res separable blur) → first-person sprites are drawn on
top, sharp → **bloom** (soft-knee bright pass, iterative blur at `bloom_scale`) → final pass:
**chromatic aberration** (radial RGB split), colour filter/exposure/saturation/contrast,
**vignette** → HUD/UI, unaffected. The rage states push their own volume into the same stack
(EnragedPP: red colour filter + red vignette + chromatic aberration; DepressedPP: cold filter).

Every effect is skipped when disabled, and the whole chain is bypassed when nothing is active.
On this VM's software renderer (llvmpipe) the full chain costs roughly 60–90 ms/frame at 1280×720;
use `render_scale = 0.6`, fewer bloom passes or disable DOF for a smoother run. On a GPU it is
negligible. `RIPNRAGE_PROFILE=1 ./run.sh --frames 120 --mute` prints the per-stage cost.

Use `--tunables other.cfg` to point at a different file and `--no-postfx` to start with the stack off.

## Lighting, shadows & volumetric fog (`tunables.cfg`)

Also hot-reloaded from `tunables.cfg`:

| Section | What it drives |
|---|---|
| `[lighting]` | sun elevation/azimuth/colour/intensity, hemisphere ambient (`ambient_sky` / `ambient_ground`), Blinn-Phong `specular`, `sprite_wrap` (wrapped lighting for billboards), `max_point_lights` |
| `[sky]` | procedural sky gradient colours |
| `[fog]` | classic distance fog in the surface shaders |
| `[shadows]` | directional shadow map: `resolution`, `distance` (box half-size around the camera), `bias`, `normal_offset`, `softness` (3×3 PCF spread), `sprite_shadows` |
| `[volumetric_fog]` | ray-marched height fog: `density`, `height`, `height_falloff`, `color`, `sun_scattering`, `anisotropy` (Henyey-Greenstein g), `ambient`, `steps`, `max_distance`, `shadows` (god rays), `point_lights`, `scale`, `dither` |

How it works (`Engine/Graphics/Lighting/`, `PostFx/PostProcessStack.cs`):

* **Lights** are `Light` components (`Directional` overrides the sun, `Point` with colour/intensity/
  range). Meshes *and* lit world sprites (enemies, pickups) are shaded per pixel: hemisphere
  ambient + sun + up to 16 nearest point lights + specular. The weapon muzzle flash, rockets and
  explosions spawn short-lived point lights; the TestArea has lamp posts and crates as casters.
* **Shadows**: an orthographic depth map from the sun fitted around the camera and snapped to
  texels (no shimmer). Meshes and billboard sprites (alpha-tested) cast; everything lit receives
  through 3×3 PCF with slope-scaled polygon offset + normal offset against acne.
* **Volumetric fog**: for every pixel a ray is marched from the camera to the scene depth through
  exponential height fog; each step accumulates sunlight scattered towards the camera
  (Henyey-Greenstein phase, occluded by the shadow map → light shafts), ambient sky light and
  point-light glow, and attenuates transmittance. The march runs at `scale` resolution with
  per-pixel dithering and is composited over the scene before motion blur / DOF.

Debug helpers: `RIPNRAGE_DEBUG_SHADOW=1` also writes `<screenshot>_shadowmap.png`;
`RIPNRAGE_DEBUG_SHADOW=2` renders meshes with their shadow factor only.

Cost on this VM's software renderer at 1280×720 with everything on: ~90 ms/frame (shadow pass
~4 ms, volumetric ~25 ms). A "fast" preset — `resolution = 1024`, `steps = 10`, `scale = 0.35`,
`render_scale = 0.7` — brings it to ~60 ms. On a GPU all of this is a few milliseconds.

## Render passes, viewmodel FOV & culling (`tunables.cfg`)

`Engine/Graphics/RenderPipeline.cs` is an explicit multi-pass forward renderer:

| # | Pass | Notes |
|---|---|---|
| 0 | **Shadow map** | every caster (meshes + lit sprites), never culled — off-screen walls still cast shadows |
| 1 | **Sky** | full-screen gradient, rotation-only view |
| 2 | **World** | frustum + occlusion culling → opaque meshes by render queue → transparent meshes → world sprites back-to-front → debug lines; then the world post-fx stage (volumetric fog, motion blur, DOF) |
| 3 | **Viewmodel** | everything with `renderLayer = RenderLayer.FirstPerson` (the weapon, hands, muzzle flash; meshes too). Same view matrix as the camera but its **own projection** (`[viewmodel] fov/near/far`) on a **cleared depth buffer**, drawn *after* the world post-fx so DOF / motion blur / fog never smear the gun. Optionally lit by the sun, point lights and the muzzle-flash light (`lit`, `wrap`, `exposure`); `SpriteRenderer.fullBright` opts a sprite out (the flash itself). |
| 4 | **Lens post-fx** | bloom, chromatic aberration, vignette, colour → default framebuffer |
| 5 | **UI** | `IUiDrawer` components through `UiBatch` |

```ini
[camera]      fov = 0  near = 0  far = 0        ; world camera; 0 = keep the game's value (77 / 0.05 / 2000)
[viewmodel]   enabled = true  fov = 77  near = 0.02  far = 50  lit = true  wrap = 0.85  exposure = 1.15
[culling]     frustum = true  occlusion = true  buffer_width = 192  buffer_height = 96  lights = true  debug_view = false
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
* **Occlusion** — a CPU `OcclusionBuffer` (default 192×96) rasterises the level chunks flagged
  `MeshRenderer.occluder` (every opaque brush chunk from the level loader) with perspective-correct
  depth, near-plane clipping and back-face rejection. Occludees are tested with their AABB's screen
  rectangle against the box's nearest depth: an object is skipped only when every pixel it could
  touch is covered by nearer geometry (pixel-centre coverage for occluders + over-covering rectangles
  for occludees keep it conservative, so nothing visible is ever dropped). Point lights are culled the
  same way, which also removes them from the shading loops. The dock level is split into 16 m chunks
  per texture (`LevelLoader.MeshChunkSize`) so parts of the level behind walls are skipped too.
  Typical cost on this VM: 0.1–0.3 ms/frame. Shadow casters and the viewmodel are never culled.
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
  _NormalA = tex:_Game/Textures/Normals/waternormal.png
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
`EntityRegistry` (`info_player_start`, `monster_rifleman`, `item_ammo_*`, `light`, `lamp_post`,
`water_plane`, `floor_plane`, `trigger_checkpoint`) — both in `src/RipNRage.Game/Content/Level/`, so
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
| `CharacterController`, `Physics.Raycast/SphereCast(All)`, `OnTriggerEnter`, layers | `Physics/` (axis-separated AABB sweeps, ray vs box/sphere, `ITriggerListener`, collision matrix) |
| `NavMeshAgent`, `NavMesh.SamplePosition` | straight-line steering agent that sticks to geometry (no baked navmesh yet) |
| Unity Input System (`PlayerControls.inputactions`) | `PlayerInputActions` polls the engine `Input` with the same bindings |
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

* MP3/FLAC audio (`mg338.mp3`, melee hits, music) is not decoded yet — only PCM WAV.
* Katana and Sonic Shotgun have sprites/animations but no weapon class in the original project
  either; they are loaded but not in the inventory.
* EffectCore particle VFX (bullet impacts, rocket mesh) are replaced by simple sprite flashes.
* The full TestArea scene (Dock.blend with materials, lighting, checkpoints, 48 grunts) is
  approximated by the `dock.obj` brushes, a few pickups and riflemen.
* Enemy navigation is straight-line (no navmesh baking); the 8-direction sprite shader is done on
  the CPU with the 4-side sheets.
* Save/load is wired (JSON) but no checkpoint volumes are placed yet.
* A sokol-gfx backend could be added behind `RenderPipeline`; all GL calls live in `Graphics/` + `Platform/GL.cs`.
