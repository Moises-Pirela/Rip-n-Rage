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
        public int rageState, combatState, weaponIndex; // combatState: unused since the two meters replaced VIOLENT
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
    /// Full game save/load on top of the ported SaveData/SaveManager: level, player transform/look,
    /// health/armour/rage, inventory, every level entity's state (monsters alive + health, pickups
    /// taken, spawner progress) and monsters created at runtime. Loading rebuilds the level and
    /// re-applies the state once the player exists. Files: &lt;persistentDataPath&gt;/saves/&lt;slot&gt;.json.
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
            var player = PlayerState.Current;
            var combat = EnragedInjector<CombatController>.GetInstance();
            var camCtrl = FPSCameraController.instance;
            if (player == null || combat == null || CurrentLevel == null) { Message("Nothing to save"); return false; }
            if (player.IsDead) { Message("Cannot save: you are dead"); return false; }

            var save = new GameSave { level = CurrentLevelPath, time = DateTime.Now.ToString("s") };
            var p = save.player;
            p.x = player.transform.position.x; p.y = player.transform.position.y; p.z = player.transform.position.z;
            p.yaw = camCtrl != null ? camCtrl.Yaw : 0f; p.pitch = camCtrl != null ? camCtrl.Pitch : 0f;
            p.health = player.Health; p.armor = player.Armor;
            RipNRage.Game.Ecs.Rages.TryGet(out var rage);
            p.rage = rage.Fury; p.calm = rage.Calm; p.rageState = (int)rage.State;
            p.weaponIndex = combat.CurrentWeaponIndex;
            p.ammo = combat.AmmoArray();

            // level entities
            var doc = CurrentLevel.document;
            // every saved level entity is an entity now (monsters 6.6, spawners 6.7, pickups 7.5), keyed by LevelRef;
            // monsters are entities (6.6): level-placed ones keyed by their LevelRef, runtime ones saved dynamic
            var w = Application.Current.World;
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
                // keep the original checkpoint data in sync
                SaveData.Current.playerStateData = new PlayerStateData(player.transform.position, player.transform.rotation, player.Health, player.Armor);
                SaveData.Current.inventoryData = new InventoryData(combat.AmmoArray(), combat.CurrentWeaponIndex);
                if (slot == "checkpoint") GameSession.CheckpointReached = true;
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

            GameSession.RestartLevel(levelPath, () =>
            {
                // The ported PlayerState/Inventory read their initial values from SaveData.Current in Start();
                // GameManager.Init (run during the build) replaces SaveData.Current, so set it after the build.
                SaveData.Current = new SaveData
                {
                    playerStateData = new PlayerStateData(new Vector3(save.player.x, save.player.y, save.player.z), Quaternion.identity, save.player.health, save.player.armor),
                    inventoryData = new InventoryData((int[])save.player.ammo.Clone(), save.player.weaponIndex),
                };
                GameSession.CheckpointReached = true; // the run continues from a save: dying goes back to the checkpoint
                GameSession.Behaviour.StartCoroutine(ApplyWhenReady(save));
            });
            return true;
        }

        private static IEnumerator ApplyWhenReady(GameSave save)
        {
            // wait for the player (spawned by the GameManager/LevelManager coroutines)
            while (PlayerState.Current == null || EnragedInjector<CombatController>.GetInstance()?.CurrentWeaponData == null) yield return null;
            yield return null;
            var player = PlayerState.Current;
            var combat = EnragedInjector<CombatController>.GetInstance();
            var p = save.player;
            // the character entity owns the position: a GameObject write was overwritten by the next sync (the load put
            // the player at the spawn since layer 4). Teleport writes the entity and zeroes the pitch; the look follows.
            RipNRage.Game.Ecs.PlayerLink.Current?.Teleport(new Vector3(p.x, p.y, p.z), p.yaw);
            FPSCameraController.instance?.SetLook(p.yaw, p.pitch);
            player.RestoreState(p.health, p.armor);
            RipNRage.Game.Ecs.Rages.Restore(p.rage, p.calm, (RipNRage.Game.Ecs.RageState)p.rageState);
            // GameManager.Init re-reads the legacy "Save1" file during the build, so apply the inventory explicitly
            combat.LoadAmmo(p.ammo);
            combat.EquipWeaponIndex(p.weaponIndex);
            combat.OnAmmoChanged?.Invoke();

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
            Debug.LogWarning($"Loaded: player at {player.transform.position}, hp {player.Health}, ammo {combat.AmmoOf(AmmoType.LIGHT)}, weapon {combat.CurrentWeaponIndex}");
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
