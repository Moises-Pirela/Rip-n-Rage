using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics;
using RipNRage.Engine.Graphics.Lighting;
using RipNRage.Level;

namespace RipNRage.Game.Content.Level
{
    /// <summary>Describes a placeable entity class: how the game spawns it and how the editor previews it.</summary>
    public sealed class EntityDefinition
    {
        public string classname;
        public string description;
        public Color gizmoColor = Color.white;
        /// <summary>Default key/values written when the editor places a new instance.</summary>
        public Dictionary<string, string> defaults = new Dictionary<string, string>();
        /// <summary>Editable numeric properties (name, step) the editor exposes with +/- keys.</summary>
        public List<(string key, float step)> numericProps = new List<(string, float)>();
        /// <summary>The prefab this class is (10.3b): the loader spawns it with the map's keys as field overrides.
        /// When set, <see cref="spawnEntity"/> / <see cref="previewEntity"/> are not used.</summary>
        public string prefab;
        /// <summary>Game spawn; the loader stamps the <see cref="RipNRage.Game.Ecs.AI.LevelRef"/> when the entity carries one.</summary>
        public Func<MapEntity, RipNRage.Engine.Ecs.Entity> spawnEntity;
        /// <summary>Visual-only instance for the editor (entities since 9.6 — no AI, triggers or colliders). Null = spawnEntity.</summary>
        public Func<MapEntity, RipNRage.Engine.Ecs.Entity> previewEntity;
        /// <summary>Half-size of the selection box around the origin (editor picking).</summary>
        public Vector3 pickExtents = new Vector3(0.5f, 0.5f, 0.5f);
        /// <summary>Spawned after every other entity (joints that connect entities by targetname).</summary>
        public bool deferred;

        /// <summary>
        /// The components this class puts on its root (10.3d: from the prefab, so a reader asks what a thing *is*
        /// instead of what its name starts with — the save used to tell monsters from pickups by a classname prefix).
        /// Empty for the classes that are still code.
        /// </summary>
        public bool Builds<T>() where T : struct, RipNRage.Engine.Ecs.IComponent
        {
            if (prefab == null) return false;
            foreach (var c in GamePrefabs.Library.Get(prefab).Components)
                if (c.ComponentType == typeof(T)) return true;
            return false;
        }
    }

    /// <summary>All entity classes the level files can use (Quake-style classnames).</summary>
    public static class EntityRegistry
    {
        private static readonly List<EntityDefinition> Defs = new List<EntityDefinition>();
        private static readonly Dictionary<string, EntityDefinition> ByName = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);
        private static bool _init;

        public static IReadOnlyList<EntityDefinition> All { get { Init(); return Defs; } }
        public static EntityDefinition Get(string classname) { Init(); return ByName.TryGetValue(classname, out var d) ? d : null; }

        private static EntityDefinition Add(EntityDefinition d)
        {
            if (ByName.TryGetValue(d.classname, out var old)) Defs[Defs.IndexOf(old)] = d; else Defs.Add(d);
            ByName[d.classname] = d;
            return d;
        }

        /// <summary>A prefab class registered or re-read after its file changed (10.6: the prefab window saved it).</summary>
        public static EntityDefinition Refresh(string classname)
        {
            Init();
            var old = Get(classname);
            return AddPrefab(classname, old?.deferred ?? false);
        }

        /// <summary>
        /// A class that is a prefab file (10.3b): what the editor needs comes from the prefab itself — its
        /// <c>description</c> and its <c>editor</c> block (<c>gizmoColor</c>, <c>pickExtents</c>, <c>defaults</c> the
        /// editor writes when placing one, <c>numeric</c> the + / - keys step).
        /// </summary>
        private static EntityDefinition AddPrefab(string classname, bool deferred = false)
        {
            var p = GamePrefabs.Library.Get(classname);
            var d = new EntityDefinition { classname = classname, prefab = classname, description = p.Description ?? classname, deferred = deferred };
            if (p.Extra?["editor"] is System.Text.Json.Nodes.JsonObject ed)
            {
                if (ed["gizmoColor"] is System.Text.Json.Nodes.JsonArray c && c.Count >= 3)
                    d.gizmoColor = new Color((float)c[0], (float)c[1], (float)c[2], c.Count > 3 ? (float)c[3] : 1f);
                if (ed["pickExtents"] is System.Text.Json.Nodes.JsonArray x && x.Count >= 3)
                    d.pickExtents = new Vector3((float)x[0], (float)x[1], (float)x[2]);
                if (ed["defaults"] is System.Text.Json.Nodes.JsonObject defs)
                    foreach (var kv in defs) d.defaults[kv.Key] = (string)kv.Value;
                if (ed["numeric"] is System.Text.Json.Nodes.JsonArray nums)
                    foreach (var n in nums)
                        if (n is System.Text.Json.Nodes.JsonObject o && o["key"] != null)
                            d.numericProps.Add(((string)o["key"], o["step"] != null ? (float)o["step"] : 1f));
            }
            return Add(d);
        }

        private static void Init()
        {
            if (_init) return;
            _init = true;
            GameAssets.Load();
            Materials.Load();

            Add(new EntityDefinition
            {
                classname = "info_player_start", description = "Player start (angle = yaw)",
                gizmoColor = new Color(0.2f, 1f, 0.3f, 1f), pickExtents = new Vector3(0.5f, 0.9f, 0.5f),
                defaults = { ["angle"] = "180" },
                spawnEntity = e => RipNRage.Engine.Ecs.Entity.Null, // handled by the loader (player spawn transform)
                previewEntity = e => Gizmo(new Color(0.2f, 1f, 0.3f, 1f), new Vector3(0.8f, 1.8f, 0.8f), e),
            });

            AddPrefab("monster_rifleman");

            foreach (var cls in new[] { "item_ammo_light", "item_ammo_heavy", "item_ammo_shell", "item_ammo_energy", "item_ammo_explosive" })
                AddPrefab(cls);

            AddPrefab("monster_spawner");

            AddPrefab("prop_physics");

            foreach (var cls in new[] { "phys_fixed", "phys_door", "phys_hinge", "phys_slider", "phys_pendulum", "phys_spring_prop" })
                AddPrefab(cls);
            foreach (var cls in new[] { "joint_hinge", "joint_slider", "joint_fixed", "joint_ball", "joint_distance", "joint_spring" })
                AddPrefab(cls, deferred: true); // a connector needs the props it joins to exist

            AddPrefab("light");

            AddPrefab("lamp_post");

            AddPrefab("water_plane");

            AddPrefab("floor_plane");

            AddPrefab("trigger_checkpoint"); // 10.3b: prefabs/level/trigger_checkpoint.json
        }

        /// <summary>An emissive box at the entity's origin with a facing arrow (the editor's stand-in gizmo).</summary>
        private static RipNRage.Engine.Ecs.Entity Gizmo(Color color, Vector3 size, MapEntity e)
        {
            var box = LevelVisuals.Spawn(e.origin + Vector3.up * size.y * 0.5f,
                LevelVisuals.Render(LevelVisuals.Box(Vector3.zero, size), Texture2D.White, color, emissive: 0.5f, castsShadow: false, transparent: color.a < 1f));
            Application.Current.World.Get<RipNRage.Engine.Ecs.Transforms.LocalTransform>(box).Rotation = Quaternion.Euler(0, e.angle, 0);
            LevelVisuals.Spawn(new Vector3(0, 0, size.z * 0.5f + 0.4f),
                LevelVisuals.Render(LevelVisuals.Box(Vector3.zero, new Vector3(0.2f, 0.2f, 0.8f)), Texture2D.White, color, emissive: 0.5f, castsShadow: false), box);
            return box;
        }


    }
}
