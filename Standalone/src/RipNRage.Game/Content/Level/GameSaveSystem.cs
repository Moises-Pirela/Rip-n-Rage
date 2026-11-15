using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Game.Ecs.AI;
using RipNRage.Game.Ecs.Combat;
using RipNRage.Level;

namespace RipNRage.Game.Content.Level
{
    // ---- save file model (System.Text.Json, public fields) ----
    public sealed class GameSave
    {
        public int version = 1;
        public string level;
        public string time;
        public PlayerSave player = new PlayerSave();
        public List<EntitySave> entities = new List<EntitySave>();
        public List<MonsterSave> dynamicMonsters = new List<MonsterSave>();
    }

    public sealed class PlayerSave
    {
        public float x, y, z, yaw, pitch;
        public float health, armor, rage, calm; // rage = the Fury meter (the field name is the save format's)
        public int rageState, weaponIndex; // combatState was dropped in 8.5 (saves are not a compatibility promise)
        public int[] ammo = new int[5];
    }

    public sealed class EntitySave
    {
        public int index;
        public string classname;
        public bool alive = true;   // monsters
        public bool taken;          // pickups
        public int spawned;         // spawners
        public float x, y, z, health;
    }

    public sealed class MonsterSave
    {
        public string classname;
        public float x, y, z, yaw, health;
        public int spawnerIndex = -1;
    }

    /// <summary>
    /// Full game save/load (8.5: captured from and applied to the components, by hand — the SaveData /
    /// PlayerStateData static round trip is gone): level, player pose/look, health/armour/rage, inventory, every
    /// level entity's state (monsters alive + health, pickups taken, spawner progress) and monsters created at
    /// runtime. Loading rebuilds the level and applies the save the frame after the build (the facades'
    /// Start has run by then, so nothing re-initialises over it). Files: &lt;persistentDataPath&gt;/saves/&lt;slot&gt;.json.
    /// </summary>
    public static class GameSaveSystem
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

        public static string SaveDir => Path.Combine(Application.persistentDataPath, "saves");
        public static string SlotPath(string slot) => Path.Combine(SaveDir, slot + ".json");
        public static Action<string> OnMessage;

        /// <summary>The level currently played (set by the game when it builds a level).</summary>
        public static LoadedLevel CurrentLevel;
        public static string CurrentLevelPath;

        public static bool Save(string slot)
        {
            var w = Application.Current.World;
            var pe = RipNRage.Game.Ecs.PlayerLink.PlayerEntity;
            bool alive = !pe.IsNull && w.IsAlive(pe) && w.Has<Health>(pe);
            if (!alive || CurrentLevel == null) { Message("Nothing to save"); return false; }
            if (w.Has<Dead>(pe) || w.Get<Health>(pe).Current <= 0f) { Message("Cannot save: you are dead"); return false; }

            // the player, off their components (8.5)
            var save = new GameSave { level = CurrentLevelPath, time = DateTime.Now.ToString("s") };
            var p = save.player;
            var playerPos = w.Get<LocalTransform>(pe).Position;
            p.x = playerPos.x; p.y = playerPos.y; p.z = playerPos.z;
            var look = w.Has<RipNRage.Game.Ecs.PlayerLook>(pe) ? w.Get<RipNRage.Game.Ecs.PlayerLook>(pe) : default;
            p.yaw = look.Yaw; p.pitch = look.Pitch;
            p.health = w.Get<Health>(pe).Current;
            p.armor = w.Has<Armor>(pe) ? w.Get<Armor>(pe).Current : 0f;
            RipNRage.Game.Ecs.Rages.TryGet(out var rage);
            p.rage = rage.Fury; p.calm = rage.Calm; p.rageState = (int)rage.State;
            p.weaponIndex = w.Has<Weapon>(pe) ? w.Get<Weapon>(pe).Slot : 0;
            p.ammo = Weapons.AmmoArray(w.Get<Inventory>(pe));

            // level entities
            var doc = CurrentLevel.document;
            // every saved level entity is an entity now (monsters 6.6, spawners 6.7, pickups 7.5), keyed by LevelRef;
            // monsters are entities (6.6): level-placed ones keyed by their LevelRef, runtime ones saved dynamic
            var monstersByIndex = new Dictionary<int, Entity>();
            var dynamic = new List<MonsterSave>();
            var enemies = new List<Entity>();
            Enemies.Collect(w, enemies);
            foreach (var en in enemies)
            {
                var r = w.Get<LevelRef>(en);
                if (r.Index >= 0) { monstersByIndex[r.Index] = en; continue; }
                if (w.Has<Dead>(en)) continue;
                var pos = w.Get<LocalTransform>(en).Position;
                dynamic.Add(new MonsterSave
                {
                    classname = Enemies.All[w.Get<Enemy>(en).Kind].classname,
                    x = pos.x, y = pos.y, z = pos.z, yaw = w.Get<Heading>(en).Yaw,
                    health = w.Get<Health>(en).Current, spawnerIndex = r.SpawnerIndex,
                });
            }
            var pickupsByIndex = new Dictionary<int, Entity>();
            var pickups = new List<Entity>();
            RipNRage.Game.Ecs.Pickups.Collect(w, pickups);
            foreach (var pk in pickups) { int idx = w.Get<LevelRef>(pk).Index; if (idx >= 0) pickupsByIndex[idx] = pk; }
            var spawnersByIndex = new Dictionary<int, Entity>();
            var spawners = new List<Entity>();
            Spawners.Collect(w, spawners);
            foreach (var sp in spawners)
            {
                int idx = w.Get<LevelRef>(sp).Index;
                if (idx >= 0) spawnersByIndex[idx] = sp;
            }
            for (int i = 0; i < doc.entities.Count; i++)
            {
                var e = doc.entities[i];
                var es = new EntitySave { index = i, classname = e.classname };
                if (e.classname.StartsWith("monster_", StringComparison.OrdinalIgnoreCase) && !e.classname.Equals("monster_spawner", StringComparison.OrdinalIgnoreCase))
                {
                    es.alive = monstersByIndex.TryGetValue(i, out var en) && !w.Has<Dead>(en);
                    if (es.alive)
                    {
                        var pos = w.Get<LocalTransform>(en).Position;
                        es.x = pos.x; es.y = pos.y; es.z = pos.z;
                        es.health = w.Get<Health>(en).Current;
                    }
                }
                else if (e.classname.StartsWith("item_", StringComparison.OrdinalIgnoreCase)) es.taken = !pickupsByIndex.ContainsKey(i);
                else if (e.classname.Equals("monster_spawner", StringComparison.OrdinalIgnoreCase)) es.spawned = spawnersByIndex.TryGetValue(i, out var sp) ? w.Get<Spawner>(sp).Spawned : 0;
                else continue;
                save.entities.Add(es);
            }
            save.dynamicMonsters = dynamic;

            try
            {
                Directory.CreateDirectory(SaveDir);
                File.WriteAllText(SlotPath(slot), JsonSerializer.Serialize(save, Json));
                if (slot == "checkpoint") RipNRage.Game.Ecs.Flows.SetCheckpointReached();
                Message($"Game saved ({slot})");
                return true;
            }
            catch (Exception e) { Message("Save failed: " + e.Message); return false; }
        }

        public static bool Exists(string slot) => File.Exists(SlotPath(slot));

        /// <summary>Level path stored in a slot (as written by <see cref="Save"/>), or null when the slot is missing or unreadable.</summary>
        public static string PeekLevel(string slot)
        {
            try
            {
                string path = SlotPath(slot);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<GameSave>(File.ReadAllText(path), Json)?.level;
            }
            catch { return null; }
        }

        /// <summary>
        /// True when the "checkpoint" slot exists and belongs to <paramref name="levelPath"/> (default: the
        /// level being played), i.e. dying can respawn the player at the last checkpoint instead of the level start.
        /// </summary>
        public static bool CheckpointAvailable(string levelPath = null)
        {
            levelPath ??= CurrentLevelPath;
            string saved = PeekLevel("checkpoint");
            if (string.IsNullOrEmpty(saved) || string.IsNullOrEmpty(levelPath)) return false;
            try
            {
                string a = Path.GetFullPath(LevelLoader.Locate(saved) ?? saved);
                string b = Path.GetFullPath(LevelLoader.Locate(levelPath) ?? levelPath);
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>The slot "Continue" should load: the checkpoint when one exists, otherwise the quick save; null when there is nothing.</summary>
        public static string ContinueSlot()
        {
            if (Exists("checkpoint")) return "checkpoint";
            if (Exists("quick")) return "quick";
            return null;
        }

        /// <summary>Rebuilds the level and restores the saved state. Returns false if the slot does not exist.</summary>
        public static bool Load(string slot)
        {
            string path = SlotPath(slot);
            if (!File.Exists(path)) { Message($"No save in slot '{slot}'"); return false; }
            GameSave save;
            try { save = JsonSerializer.Deserialize<GameSave>(File.ReadAllText(path), Json); }
            catch (Exception e) { Message("Load failed: " + e.Message); return false; }

            string levelPath = LevelLoader.Locate(save.level) ?? save.level;
            if (!File.Exists(levelPath)) { Message("Level of the save not found: " + save.level); return false; }

            GameSession.Rebuild(levelPath, () => // now: boot (--load) or the session carrying out a flow move
            {
                RipNRage.Game.Ecs.Flows.SetCheckpointReached(); // the run continues from a save: dying goes back to the checkpoint
                GameSession.RunAfterBuild(ApplyWhenReady(save));
            });
            return true;
        }

        private static IEnumerator ApplyWhenReady(GameSave save)
        {
            // the player and its components exist when the build returns (9.4: nothing re-initialises after it);
            // one yield keeps the apply on the frame it always landed on
            yield return null;
            var world = Application.Current.World;
            var pe = RipNRage.Game.Ecs.PlayerLink.PlayerEntity;
            var p = save.player;
            // the character entity owns the position: a GameObject write was overwritten by the next sync (the load put
            // the player at the spawn since layer 4). Teleport writes the entity and zeroes the pitch; the pitch follows.
            RipNRage.Game.Ecs.PlayerLink.Teleport(new Vector3(p.x, p.y, p.z), p.yaw);
            if (world.Has<RipNRage.Game.Ecs.PlayerLook>(pe))
                world.Get<RipNRage.Game.Ecs.PlayerLook>(pe).Pitch = Mathf.Clamp(p.pitch, -90f, 90f);
            if (world.Has<Health>(pe)) { ref var h = ref world.Get<Health>(pe); h.Current = Mathf.Clamp(p.health, 0f, h.Max); }
            if (world.Has<Armor>(pe)) { ref var a = ref world.Get<Armor>(pe); a.Current = Mathf.Clamp(p.armor, 0f, a.Max); }
            RipNRage.Game.Ecs.Rages.Restore(p.rage, p.calm, (RipNRage.Game.Ecs.RageState)p.rageState);
            Weapons.LoadAmmo(ref world.Get<Inventory>(pe), p.ammo);
            CombatController.EquipWeaponIndex(p.weaponIndex); // retargets the viewmodel sprite entity

            var level = CurrentLevel;
            if (level != null)
            {
                // the freshly rebuilt level spawned every level-placed monster alive at its origin
                var w = Application.Current.World;
                var monstersByIndex = new Dictionary<int, Entity>();
                var enemies = new List<Entity>();
                Enemies.Collect(w, enemies);
                foreach (var en in enemies)
                {
                    var r = w.Get<LevelRef>(en);
                    if (r.Index >= 0) monstersByIndex[r.Index] = en;
                }
                var pickupsByIndex = new Dictionary<int, Entity>();
                var pickupEnts = new List<Entity>();
                RipNRage.Game.Ecs.Pickups.Collect(w, pickupEnts);
                foreach (var pk in pickupEnts) { int idx = w.Get<LevelRef>(pk).Index; if (idx >= 0) pickupsByIndex[idx] = pk; }
                var spawnersByIndex = new Dictionary<int, Entity>();
                var spawnerEnts = new List<Entity>();
                Spawners.Collect(w, spawnerEnts);
                foreach (var sp in spawnerEnts)
                {
                    int idx = w.Get<LevelRef>(sp).Index;
                    if (idx >= 0) spawnersByIndex[idx] = sp;
                }
                foreach (var es in save.entities)
                {
                    if (es.classname.StartsWith("monster_", StringComparison.OrdinalIgnoreCase) && !es.classname.Equals("monster_spawner", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!monstersByIndex.TryGetValue(es.index, out var en) || !w.IsAlive(en)) continue;
                        if (!es.alive) { w.DestroyEntity(en); continue; } // the sprite child follows through the hierarchy
                        w.Get<LocalTransform>(en).Position = new Vector3(es.x, es.y, es.z); // a teleport to the physics sync
                        if (es.health > 0f) { ref var h = ref w.Get<Health>(en); h.Current = Mathf.Min(es.health, h.Max); }
                        continue;
                    }
                    if (es.classname.Equals("monster_spawner", StringComparison.OrdinalIgnoreCase))
                    {
                        if (spawnersByIndex.TryGetValue(es.index, out var sp)) w.Get<Spawner>(sp).Spawned = es.spawned;
                        continue;
                    }
                    if (es.classname.StartsWith("item_", StringComparison.OrdinalIgnoreCase))
                    {
                        if (es.taken && pickupsByIndex.TryGetValue(es.index, out var pk) && w.IsAlive(pk)) w.DestroyEntity(pk); // the sprite child follows
                        continue;
                    }
                }
                foreach (var m in save.dynamicMonsters)
                {
                    var def = EntityRegistry.Get(m.classname);
                    if (def?.spawnEntity == null) continue;
                    var e = new MapEntity { classname = m.classname, origin = new Vector3(m.x, m.y, m.z) };
                    e.angle = m.yaw;
                    var en = def.spawnEntity(e);
                    if (!w.IsAlive(en)) continue;
                    w.Get<LocalTransform>(en).Position = new Vector3(m.x, m.y, m.z); // the saved pose is the body centre
                    w.Get<LevelRef>(en).SpawnerIndex = m.spawnerIndex;
                    if (m.health > 0f) { ref var h = ref w.Get<Health>(en); h.Current = Mathf.Min(m.health, h.Max); }
                }
            }
            Debug.LogWarning($"Loaded: player at {world.Get<LocalTransform>(pe).Position}, hp {world.Get<Health>(pe).Current}, ammo {CombatController.AmmoOf(AmmoType.LIGHT)}, weapon {CombatController.CurrentWeaponIndex}");
            Message("Game loaded");
        }

        public static string[] ListSlots()
        {
            if (!Directory.Exists(SaveDir)) return Array.Empty<string>();
            var files = Directory.GetFiles(SaveDir, "*.json");
            for (int i = 0; i < files.Length; i++) files[i] = Path.GetFileNameWithoutExtension(files[i]);
            Array.Sort(files);
            return files;
        }

        private static void Message(string m)
        {
            Debug.LogWarning(m);
            OnMessage?.Invoke(m);
        }
    }
}
