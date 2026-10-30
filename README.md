# Rip-n-Rage

A fast first-person shooter with retro sprite weapons and enemies, a rage meter that powers you up as you
fight, and a custom C# engine built from scratch. Unity is no longer required.

## Features

- **Movement**: bunny hopping, jumping, sliding, wall running, rocket jumps, head bob, weapon sway and a
  trauma-based camera shake (weapon kick, nearby explosions).
- **Combat feel**: bloom + spread on every weapon with a reactive crosshair, headshots, quick weapon swap and a
  style meter (F → S) that rewards kill variety — S rank regenerates health, speeds you up and generates ammo.
- **Weapons**: Glock, M1887 shotgun, MG338 machine gun and a rocket launcher, plus a left/right fist jab combo
  that grabs enemies and finishes with a weapon melee.
- **Rage system**: combat builds rage, which changes how you play and adds a red screen tint and vignette.
- **Enemies**: Riflemen driven by a behavior tree (spot, chase, attack, wander), drawn as 4-direction sprites.
- **Pickups, checkpoints and save/load**, plus a HUD with a portrait, a health/armour ring and an ammo counter.
- **Game flow**: title screen with an attract-mode camera, pause menu with a per-effect graphics menu (live frame
  time readout, choices saved) and a controls menu (full key / mouse / gamepad rebinding), and a death loop — the camera falls in slow motion, a "You are dead" screen fades
  in and offers Restart (last checkpoint) or Main menu. Falling into the harbour is lethal too (or an arcade
  respawn with `--fall-mode respawn`).
- **Visual effects**: data-driven particle effects (JSON, hot-reloaded) — blood that splats where it lands,
  impacts per material (concrete, wood, metal, glass, sand, water), explosions with light, debris and scorch marks,
  tracers per calibre and rocket trails — with a live editor.
- **Custom engine**: OpenGL 3.3 rendering, a hot-reloadable post-processing stack (bloom, volumetric fog,
  motion blur, chromatic aberration), OpenAL audio, a sub-stepped swept-box character controller that cannot
  tunnel through geometry, and an Animator-style state machine. It uses no NuGet packages.
- **Tools**: a Quake-style level editor (`.map` levels), a sprite animation editor and a particle effect editor.

## Repository layout

```
Rip-n-Rage/
├── Files/         source art: weapon, enemy, HUD and pickup sprites, plus TexturePacker (.tps) projects
└── Standalone/    the engine, game, editors, shaders, levels and animation data
    ├── src/RipNRage.Engine/      engine (platform, graphics, audio, physics, animation, math, tweens)
    ├── src/RipNRage.Game/        gameplay scripts, content and the game entry point
    ├── src/RipNRage.Level/       level data model and .map reader/writer
    ├── src/RipNRage.Editor/      level editor
    ├── src/RipNRage.AnimEditor/  animation editor
    ├── src/RipNRage.VfxEditor/   particle effect editor
    ├── assets/                   runtime sprites, textures, audio and the dock geometry
    ├── animations/               animation clips and controllers (JSON)
    ├── vfx/                      particle effects and bindings (JSON)
    ├── levels/                   .map levels (dock.map is the test area)
    ├── shaders/                  GLSL shaders
    └── tunables.cfg              post-processing settings, hot-reloaded while the game runs
```

## Getting started

Requirements:

- .NET 10 SDK
- A GPU and driver with OpenGL 3.3

GLFW and OpenAL Soft are bundled for Windows x64, Linux x64 and macOS in `Standalone/native/`; see its
[NOTICE.md](Standalone/native/NOTICE.md) for versions and licences.

Build and run:

```bash
cd Standalone
./run.sh          # build and run the game
./edit.sh         # level editor
./anim.sh         # animation editor
./vfx.sh          # particle effect editor
./build.sh        # build a self-contained Linux release into dist/
```

On Windows, run `Standalone/run.cmd`, or:

```bash
cd Standalone
dotnet run -c Release --project src/RipNRage.Game
```

See [Standalone/README.md](Standalone/README.md) for the full engine docs: native library lookup, command-line
options, automation scripts and the post-processing pipeline.

## Controls

All gameplay controls are rebindable in the Controls menu; gamepads work out of the box.

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Move / Look | W A S D / Mouse | Left stick / Right stick |
| Jump / Slide | Space / Left Ctrl | A / B |
| Fire / Jab combo | Left mouse / Right mouse | RT / LT |
| Quick swap (last weapon) | Q | Y |
| Switch weapon | 1–4, mouse wheel | D-pad, LB / RB |
| Pause menu | Esc | Start |
| Menus | Arrows, Enter, Esc | D-pad or stick, A, B |
| Quick save / load | F6 / F9 | |
| Load last checkpoint / restart level | F7 / F8 | |
| Debug overlay / screenshot | F3 / F12 | |
| Toggle post-processing / reload shaders and `tunables.cfg` | F4 / F5 | |

## Roadmap

See [ROADMAP.md](ROADMAP.md) for what's done, what's broken and what's planned.

## License

[MIT](LICENSE) © 2026 Moises Pirela
