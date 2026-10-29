# Roadmap

Where Rip-n-Rage stands and what's left to do. Items marked with a file link come from a `TODO`, a stub or a
known gap in the code; the rest are proposals based on art and data that already exist but aren't used yet.

## Done

- [x] Custom C# engine: OpenGL 3.3 renderer, OpenAL audio, physics, Animator-style state machine, tweens
- [x] Post-processing stack (bloom, DOF, motion blur, chromatic aberration, vignette), hot-reloaded from `tunables.cfg`
- [x] Lighting: sun, point lights, shadow maps, volumetric fog; frustum and occlusion culling
- [x] Player movement: bunny hopping, jumping, head bob, weapon sway, camera recoil
- [x] Weapons: Glock, M1887, MG338, rocket launcher, plus the fist jab/grab combo
- [x] Rage system with its own post-processing look
- [x] Rifleman enemy with behavior-tree AI and 4-direction sprites
- [x] Ammo pickups, checkpoints, quick save/load (JSON)
- [x] HUD: portrait, health/armour ring, ammo counter, crosshair
- [x] Level editor (`.map` brushes, entities, vertex painting, snapshots) and sprite animation editor
- [x] Unity removed; runtime assets restored into `Standalone/assets/`, builds on .NET 10

## Now: repo and build health

- [ ] Commit GLFW/OpenAL binaries for Windows (`Standalone/native/win-x64/`), or a script that downloads them
- [ ] Launch-test the game and both editors on Windows, Linux and macOS
- [ ] Restore or remove the missing README images (`Standalone/docs/*.png`)
- [ ] Untrack the committed `.DS_Store` files (`git rm --cached`)
- [ ] Point the TexturePacker projects in `Files/` at `Standalone/assets/Sprites/` so re-exported sheets land where the game loads them
- [ ] CI: build the solution on push, and run a headless smoke test (`--frames 200 --script close`)

## Next: gameplay gaps

- [ ] **Player death**: `PlayerState.Die()` is empty, so the player can't die
  ([PlayerState.cs:64](Standalone/src/RipNRage.Game/Scripts/PlayerSystems/PlayerState.cs:64)). Needs a death screen and a respawn from the last checkpoint.
- [ ] **MP3 audio**: only PCM WAV is decoded ([Audio.cs:63](Standalone/src/RipNRage.Engine/Audio/Audio.cs:63)), so the MG338 shot
  and melee hit sounds are silent. Add an MP3 decoder, or convert those two files to WAV.
- [ ] **Blood and hit effects** for hitscan weapons
  ([HitscanWeapon.cs:63](Standalone/src/RipNRage.Game/Scripts/Weapons/WeaponClasses/HitscanWeapon.cs:63),
  [M1887Shotgun.cs:58](Standalone/src/RipNRage.Game/Scripts/Weapons/WeaponClasses/M1887Shotgun.cs:58)).
- [ ] **Particle VFX**: bullet impacts and the rocket mesh are simple sprite flashes for now.
- [ ] **Manager shutdown**: `GameManager.Kill()` and `LevelManager.Kill()` throw `NotImplementedException`
  ([GameManager.cs:52](Standalone/src/RipNRage.Game/Scripts/DungeonMaster/GameManager.cs:52),
  [LevelManager.cs:35](Standalone/src/RipNRage.Game/Scripts/DungeonMaster/LevelManager.cs:35)).
- [ ] **Health and armour pickups**: the HUD shows both, but only ammo pickups exist.
- [ ] **Enemy navigation**: agents steer in a straight line. Add pathfinding (a navmesh or a grid baked from the level brushes).
- [ ] **Place checkpoints in the dock level**: save/load works, but the level has no `trigger_checkpoint` volumes yet.

## Later: content

- [ ] **Katana**: sprites and animations are loaded, but there's no weapon class.
- [ ] **Sonic shotgun**: sprites and a firing sound are loaded, but there's no weapon class. The Unity version had a
  charge shot, which wasn't ported.
- [ ] **Weapons for unused ammo types**: the HUD has energy and grenade ammo icons, and `item_ammo_energy` is a registered entity, but no weapon uses them.
- [ ] **More enemy types**: the Rifleman is the only one. The behavior tree and directional sprite system are ready for more.
- [ ] **More levels**: the dock is the only playable map (`culling_test.map` is a test variant). Add level progression and an end-of-level trigger.
- [ ] **Menus**: title screen, pause menu, settings (mouse sensitivity, FOV, key bindings, volume).
- [ ] **Music**, and sound for UI, pickups and enemies.

## Later: engine

- [ ] 8-direction enemy sprites on the GPU (currently done on the CPU from 4-side sheets)
- [ ] Interlaced PNG and progressive JPEG support ([PngDecoder.cs:78](Standalone/src/RipNRage.Engine/Graphics/PngDecoder.cs:78),
  [JpegDecoder.cs:119](Standalone/src/RipNRage.Engine/Graphics/JpegDecoder.cs:119))
- [ ] Optional sokol-gfx backend behind `RenderPipeline` (all GL calls already live in `Graphics/` and `Platform/GL.cs`)
- [ ] Gamepad input
