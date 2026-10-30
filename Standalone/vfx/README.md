# vfx/ — data-driven particle effects

One JSON file per effect (`<name>.json`, see `src/RipNRage.Engine/Graphics/Particles/ParticleEffect.cs` for every
field) plus `bindings.json`, which maps surfaces / ammo types / explosion kinds to effect names.

The folder is filled with the built-in defaults (blood, impacts per material, explosions, tracers, trails) the
first time the game or an editor starts and finds it empty. Edit with `./vfx.sh` (or any text editor); the game
hot-reloads saved files while it runs.
