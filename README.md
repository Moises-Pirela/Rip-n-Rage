# Rip-n-Rage

A fast first-person shooter with retro sprite weapons and enemies, a rage meter that powers you up as you
fight, and a custom C# engine built from scratch. Unity is no longer required.

## Features

- **Movement**: bunny hopping, jumping, head bob, weapon sway and camera recoil.
- **Weapons**: Glock, M1887 shotgun, MG338 machine gun and a rocket launcher, plus a left/right fist jab combo
  that grabs enemies and finishes with a weapon melee.
- **Rage system**: combat builds rage, which changes how you play and adds a red screen tint and vignette.
- **Enemies**: Riflemen driven by a behavior tree (spot, chase, attack, wander), drawn as 4-direction sprites.
- **Pickups, checkpoints and save/load**, plus a HUD with a portrait, a health/armour ring and an ammo counter.
- **Custom engine**: OpenGL 3.3 rendering, a hot-reloadable post-processing stack (bloom, DOF, motion blur,
  chromatic aberration), OpenAL audio, physics and an Animator-style state machine. It uses no NuGet packages.
- **Tools**: a Quake-style level editor (`.map` levels) and a sprite animation editor.

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
    ├── assets/                   runtime sprites, textures, audio and the dock geometry
    ├── animations/               animation clips and controllers (JSON)
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

| Input | Action |
|---|---|
| W A S D | Move |
| Mouse | Look |
| Space | Jump |
| Left mouse | Fire |
| Right mouse | Jab combo / grab |
| 1–4, mouse wheel | Switch weapon |
| F6 / F9 | Quick save / quick load |
| F7 / F8 | Load last checkpoint / restart level |
| F3 | Debug overlay |
| F4 / F5 | Toggle post-processing / reload shaders and `tunables.cfg` |
| F12 | Screenshot |
| Esc | Release mouse (press again to quit) |

## Roadmap

See [ROADMAP.md](ROADMAP.md) for what's done, what's broken and what's planned.

## License

[MIT](LICENSE) © 2026 Moises Pirela
