using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RipNRage.Level;

namespace RipNRage.Game.Content.Level
{
    /// <summary>Marks a scene object that came from a level entity (index into the level file) or from a spawner.</summary>
    public class LevelEntityRef : MonoBehaviour
    {
        public int index = -1;
        public string classname;
        public int spawnerIndex = -1;
    }

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
        public float health, armor, rage;
        public int rageState, combatState, weaponIndex;
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
            p.health = player.Health; p.armor = player.Armor; p.rage = player.Rage;
            p.rageState = (int)player.RageState; p.combatState = (int)player.CombatState;
            p.weaponIndex = combat.inventory.currentWeaponIndex;
            p.ammo = (int[])combat.inventory.ammo.Clone();

            // level entities
            var doc = CurrentLevel.document;
            var byIndex = new Dictionary<int, GameObject>();
            var dynamic = new List<MonsterSave>();
            foreach (var go in Scene.Current.GameObjects)
            {
                if (go.IsDestroyed) continue;
                var r = go.GetComponent<LevelEntityRef>();
                if (r == null) continue;
                if (r.index >= 0) byIndex[r.index] = go;
                else
                {
                    var st = go.GetComponent<AIState>();
                    if (st != null && st.HealthState != EntityHealthState.DEAD)
                        dynamic.Add(new MonsterSave { classname = r.classname, x = go.transform.position.x, y = go.transform.position.y, z = go.transform.position.z, yaw = go.transform.eulerAngles.y, health = st.Health, spawnerIndex = r.spawnerIndex });
                }
            }
            for (int i = 0; i < doc.entities.Count; i++)
            {
                var e = doc.entities[i];
                var es = new EntitySave { index = i, classname = e.classname };
                byIndex.TryGetValue(i, out var go);
                if (e.classname.StartsWith("monster_", StringComparison.OrdinalIgnoreCase) && !e.classname.Equals("monster_spawner", StringComparison.OrdinalIgnoreCase))
                {
                    var st = go?.GetComponent<AIState>();
                    es.alive = go != null && st != null && st.HealthState != EntityHealthState.DEAD;
                    if (es.alive) { es.x = go.transform.position.x; es.y = go.transform.position.y; es.z = go.transform.position.z; es.health = st.Health; }
                }
                else if (e.classname.StartsWith("item_", StringComparison.OrdinalIgnoreCase)) es.taken = go == null;
                else if (e.classname.Equals("monster_spawner", StringComparison.OrdinalIgnoreCase)) es.spawned = go?.GetComponent<MonsterSpawner>()?.Spawned ?? 0;
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
                SaveData.Current.inventoryData = new InventoryData((int[])combat.inventory.ammo.Clone(), combat.inventory.currentWeaponIndex);
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
            while (PlayerState.Current == null || EnragedInjector<CombatController>.GetInstance()?.inventory.equippedWeapon == null) yield return null;
            yield return null;
            var player = PlayerState.Current;
            var combat = EnragedInjector<CombatController>.GetInstance();
            var cc = player.GetComponent<CharacterController>();
            var p = save.player;
            if (cc != null) cc.Warp(new Vector3(p.x, p.y, p.z)); else player.transform.position = new Vector3(p.x, p.y, p.z);
            FPSCameraController.instance?.SetLook(p.yaw, p.pitch);
            player.RestoreState(p.rage, (PlayerRageState)p.rageState, (PlayerCombatState)p.combatState, p.health, p.armor);
            // GameManager.Init re-reads the legacy "Save1" file during the build, so apply the inventory explicitly
            Array.Copy(p.ammo, combat.inventory.ammo, Math.Min(p.ammo.Length, combat.inventory.ammo.Length));
            combat.EquipWeaponIndex(p.weaponIndex);
            combat.OnAmmoChanged?.Invoke();

            var level = CurrentLevel;
            if (level != null)
            {
                var byIndex = new Dictionary<int, GameObject>();
                foreach (var go in Scene.Current.GameObjects)
                {
                    if (go.IsDestroyed) continue;
                    var r = go.GetComponent<LevelEntityRef>();
                    if (r != null && r.index >= 0) byIndex[r.index] = go;
                }
                foreach (var es in save.entities)
                {
                    if (!byIndex.TryGetValue(es.index, out var go)) continue;
                    if (es.classname.StartsWith("item_", StringComparison.OrdinalIgnoreCase)) { if (es.taken) GameObject.Destroy(go); }
                    else if (es.classname.Equals("monster_spawner", StringComparison.OrdinalIgnoreCase)) { var sp = go.GetComponent<MonsterSpawner>(); if (sp != null) sp.Spawned = es.spawned; }
                    else if (es.classname.StartsWith("monster_", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!es.alive) { GameObject.Destroy(go); continue; }
                        go.transform.position = new Vector3(es.x, es.y, es.z);
                        var st = go.GetComponent<AIState>();
                        if (st != null && es.health > 0f) st.Health = es.health;
                    }
                }
                foreach (var m in save.dynamicMonsters)
                {
                    var def = EntityRegistry.Get(m.classname);
                    if (def?.spawn == null) continue;
                    var e = new MapEntity { classname = m.classname, origin = new Vector3(m.x, m.y, m.z) };
                    e.angle = m.yaw;
                    var go = def.spawn(e);
                    if (go == null) continue;
                    go.transform.position = new Vector3(m.x, m.y, m.z);
                    var r = go.AddComponent<LevelEntityRef>(); r.classname = m.classname; r.spawnerIndex = m.spawnerIndex;
                    var st = go.GetComponent<AIState>();
                    if (st != null && m.health > 0f) st.Health = m.health; // applied after Awake set the defaults
                }
            }
            Debug.LogWarning($"Loaded: player at {player.transform.position}, hp {player.Health}, ammo {combat.inventory.ammo[0]}, weapon {combat.inventory.currentWeaponIndex}");
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
