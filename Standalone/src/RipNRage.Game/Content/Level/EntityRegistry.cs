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
        public Func<MapEntity, GameObject> spawn;
        /// <summary>Visual-only instance for the editor (no AI / triggers). Null = spawn is used.</summary>
        public Func<MapEntity, GameObject> preview;
        /// <summary>Half-size of the selection box around the origin (editor picking).</summary>
        public Vector3 pickExtents = new Vector3(0.5f, 0.5f, 0.5f);
    }

    /// <summary>Writes a full game save ("checkpoint" slot) when the player enters the volume (once).</summary>
    public class CheckpointAutoSave : MonoBehaviour, ITriggerListener
    {
        private bool _used;
        public void OnTriggerEnter(Collider other)
        {
            if (_used || !other.CompareTag("Player")) return;
            _used = true;
            GameSaveSystem.Save("checkpoint");
        }
        public void OnTriggerExit(Collider other) { }
    }

    /// <summary>All entity classes the level files can use (Quake-style classnames).</summary>
    public static class EntityRegistry
    {
        private static readonly List<EntityDefinition> Defs = new List<EntityDefinition>();
        private static readonly Dictionary<string, EntityDefinition> ByName = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);
        private static bool _init;

        public static IReadOnlyList<EntityDefinition> All { get { Init(); return Defs; } }
        public static EntityDefinition Get(string classname) { Init(); return ByName.TryGetValue(classname, out var d) ? d : null; }

        private static EntityDefinition Add(EntityDefinition d) { Defs.Add(d); ByName[d.classname] = d; return d; }

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
                spawn = e => null, // handled by the loader (player spawn transform)
                preview = e => Gizmo("PlayerStart", new Color(0.2f, 1f, 0.3f, 1f), new Vector3(0.8f, 1.8f, 0.8f), e),
            });

            Add(new EntityDefinition
            {
                classname = "monster_rifleman", description = "AR grunt (rifleman)",
                gizmoColor = new Color(1f, 0.3f, 0.3f, 1f), pickExtents = new Vector3(0.5f, 1.1f, 0.5f),
                defaults = { ["angle"] = "180" },
                spawn = e => Prefabs.SpawnRifleman(e.origin, Quaternion.Euler(0, e.angle, 0)),
                preview = e =>
                {
                    var go = new GameObject("monster_rifleman");
                    go.transform.position = e.origin;
                    var holder = new GameObject("Sprite"); holder.transform.SetParent(go.transform, false);
                    holder.transform.localPosition = new Vector3(0, 1.125f, 0);
                    holder.transform.localScale = Vector3.one * (2.25f / 5.12f);
                    var sr = holder.AddComponent<SpriteRenderer>();
                    sr.sprite = new DirectionalSpriteSheet(GameAssets.RiflemanIdle, 1, 4, 1).GetSprite(0, 0);
                    sr.billboard = true; sr.lit = true;
                    return go;
                },
            });

            foreach (var (cls, type, sprite) in new[]
            {
                ("item_ammo_light", AmmoType.LIGHT, "pickup_lightammo"), ("item_ammo_heavy", AmmoType.HEAVY, "pickup_heavyammo"),
                ("item_ammo_shell", AmmoType.SHELL, "pickup_shellammo"), ("item_ammo_energy", AmmoType.ENERGY, "pickup_energyammo"),
                ("item_ammo_explosive", AmmoType.EXPLOSIVE, "pickup_explosiveammo"),
            })
            {
                var t = type; var sp = sprite;
                Add(new EntityDefinition
                {
                    classname = cls, description = "Ammo pickup: " + type.ToString().ToLowerInvariant(),
                    gizmoColor = new Color(1f, 0.9f, 0.3f, 1f), pickExtents = new Vector3(0.7f, 0.7f, 0.7f),
                    spawn = e => AmmoPrefab(t).Instantiate(e.origin + Vector3.up * 0.9f, Quaternion.identity),
                    preview = e =>
                    {
                        var go = new GameObject(cls); go.transform.position = e.origin + Vector3.up * 0.9f;
                        go.transform.localScale = new Vector3(0.75f, 0.75f, 1f);
                        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = GameAssets.Pickups[sp]; sr.billboard = true; sr.lit = true;
                        return go;
                    },
                });
            }

            Add(new EntityDefinition
            {
                classname = "monster_spawner", description = "Wave spawner (count, interval, radius, activate_range)",
                gizmoColor = new Color(1f, 0.5f, 0.1f, 1f), pickExtents = new Vector3(0.8f, 0.3f, 0.8f),
                defaults = { ["monster"] = "monster_rifleman", ["count"] = "3", ["interval"] = "5", ["radius"] = "2", ["activate_range"] = "25", ["max_alive"] = "4", ["angle"] = "180" },
                numericProps = { ("count", 1f), ("interval", 0.5f) },
                spawn = e => MonsterSpawner.Create(e),
                preview = e =>
                {
                    var go = new GameObject("monster_spawner"); go.transform.position = e.origin;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.mesh = Mesh.CreateBox(new Vector3(0, 0.15f, 0), new Vector3(1.6f, 0.3f, 1.6f), 1f);
                    mr.texture = Texture2D.White; mr.color = new Color(1f, 0.5f, 0.1f, 1f); mr.emissive = 0.4f; mr.castsShadow = false;
                    var ghost = new GameObject("Ghost"); ghost.transform.SetParent(go.transform, false);
                    ghost.transform.localPosition = new Vector3(0, 1.125f, 0); ghost.transform.localScale = Vector3.one * (2.25f / 5.12f);
                    var sr = ghost.AddComponent<SpriteRenderer>();
                    sr.sprite = new DirectionalSpriteSheet(GameAssets.RiflemanIdle, 1, 4, 1).GetSprite(0, 0);
                    sr.billboard = true; sr.color = new Color(1f, 0.6f, 0.3f, 0.45f);
                    return go;
                },
            });

            Add(new EntityDefinition
            {
                classname = "light", description = "Point light (color r g b, intensity, range)",
                gizmoColor = new Color(1f, 1f, 0.6f, 1f), pickExtents = new Vector3(0.35f, 0.35f, 0.35f),
                defaults = { ["color"] = "1 0.85 0.6", ["intensity"] = "1.5", ["range"] = "18" },
                numericProps = { ("intensity", 0.25f), ("range", 1f) },
                spawn = e => PointLight(e, false),
                preview = e => PointLight(e, true),
            });

            Add(new EntityDefinition
            {
                classname = "lamp_post", description = "Lamp post with light (origin = base)",
                gizmoColor = new Color(1f, 0.8f, 0.4f, 1f), pickExtents = new Vector3(0.4f, 2.8f, 0.4f),
                defaults = { ["intensity"] = "1.4", ["range"] = "24" },
                numericProps = { ("intensity", 0.2f), ("range", 1f) },
                spawn = e => LampPost(e),
                preview = e => LampPost(e),
            });

            Add(new EntityDefinition
            {
                classname = "water_plane", description = "Animated water plane (size w l)",
                gizmoColor = new Color(0.3f, 0.6f, 1f, 1f), pickExtents = new Vector3(2f, 0.2f, 2f),
                defaults = { ["size"] = "200 200" },
                numericProps = { ("height", 0.25f) },
                spawn = e => Plane(e, "WaterPlane", Materials.Water, Texture2D.White, Color.white, 1f / 16f, transparent: true, collider: false, water: true),
                preview = e => Plane(e, "WaterPlane", Materials.Water, Texture2D.White, Color.white, 1f / 16f, transparent: true, collider: false, water: false),
            });

            Add(new EntityDefinition
            {
                classname = "floor_plane", description = "Large textured ground plane (size w l, texture)",
                gizmoColor = new Color(0.9f, 0.8f, 0.5f, 1f), pickExtents = new Vector3(2f, 0.2f, 2f),
                defaults = { ["size"] = "200 200", ["texture"] = "sand" },
                spawn = e => { var go = Plane(e, "FloorPlane", null, LevelTextures.Get(e.Get("texture", "sand")).Texture, Color.white, 1f / 4f, transparent: false, collider: true, water: false); go.AddComponent<SurfaceMaterial>().surface = LevelTextures.Get(e.Get("texture", "sand")).surface; return go; },
                preview = e => Plane(e, "FloorPlane", null, LevelTextures.Get(e.Get("texture", "sand")).Texture, Color.white, 1f / 4f, transparent: false, collider: false, water: false),
            });

            Add(new EntityDefinition
            {
                classname = "trigger_checkpoint", description = "Checkpoint volume (size w h l): saves the game",
                gizmoColor = new Color(0.4f, 1f, 1f, 1f), pickExtents = new Vector3(1f, 1f, 1f),
                defaults = { ["size"] = "4 4 4" },
                spawn = e =>
                {
                    var go = new GameObject("Checkpoint"); go.transform.position = e.origin; go.tag = "Checkpoint";
                    var col = go.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = e.GetVector("size", new Vector3(4, 4, 4));
                    go.AddComponent<Checkpoint>();
                    go.AddComponent<CheckpointAutoSave>();
                    return go;
                },
                preview = e => { var go = new GameObject("Checkpoint"); go.transform.position = e.origin; return go; },
            });
        }

        private static Prefab AmmoPrefab(AmmoType t) => t switch
        {
            AmmoType.LIGHT => Prefabs.LightAmmoPickup, AmmoType.HEAVY => Prefabs.HeavyAmmoPickup, AmmoType.SHELL => Prefabs.ShellAmmoPickup,
            AmmoType.ENERGY => Prefabs.EnergyAmmoPickup, _ => Prefabs.ExplosiveAmmoPickup,
        };

        private static GameObject Gizmo(string name, Color color, Vector3 size, MapEntity e)
        {
            var go = new GameObject(name);
            go.transform.position = e.origin + Vector3.up * size.y * 0.5f;
            go.transform.rotation = Quaternion.Euler(0, e.angle, 0);
            var mr = go.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(Vector3.zero, size, 1f);
            mr.texture = Texture2D.White; mr.color = color; mr.emissive = 0.5f; mr.castsShadow = false;
            // arrow showing the facing direction
            var arrow = new GameObject("Arrow"); arrow.transform.SetParent(go.transform, false);
            arrow.transform.localPosition = new Vector3(0, 0, size.z * 0.5f + 0.4f);
            var amr = arrow.AddComponent<MeshRenderer>();
            amr.mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.2f, 0.2f, 0.8f), 1f);
            amr.texture = Texture2D.White; amr.color = color; amr.emissive = 0.5f; amr.castsShadow = false;
            return go;
        }

        private static GameObject PointLight(MapEntity e, bool preview)
        {
            var go = new GameObject("light");
            go.transform.position = e.origin;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = e.GetColor("color", new Color(1f, 0.85f, 0.6f, 1f));
            l.intensity = e.GetFloat("intensity", 1.5f);
            l.range = e.GetFloat("range", 18f);
            if (preview)
            {
                var mr = go.AddComponent<MeshRenderer>();
                mr.mesh = Mesh.CreateBox(Vector3.zero, Vector3.one * 0.4f, 1f);
                mr.texture = Texture2D.White; mr.color = l.color; mr.emissive = 1.5f; mr.castsShadow = false;
            }
            return go;
        }

        private static GameObject LampPost(MapEntity e)
        {
            var post = new GameObject("LampPost");
            post.transform.position = e.origin;
            post.layer = Layers.Walkable;
            post.AddComponent<SurfaceMaterial>().surface = Surfaces.Metal;
            var pole = post.AddComponent<MeshRenderer>();
            pole.mesh = Mesh.CreateBox(new Vector3(0, 2.6f, 0), new Vector3(0.28f, 5.2f, 0.28f), 1f);
            pole.texture = GameAssets.Texture13; pole.color = new Color(0.35f, 0.35f, 0.38f, 1f);
            var col = post.AddComponent<BoxCollider>(); col.center = new Vector3(0, 2.6f, 0); col.size = new Vector3(0.4f, 5.2f, 0.4f);
            var head = new GameObject("LampHead"); head.transform.SetParent(post.transform, false); head.transform.localPosition = new Vector3(0, 5.4f, 0);
            var headMr = head.AddComponent<MeshRenderer>();
            headMr.mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.7f, 0.5f, 0.7f), 1f);
            headMr.texture = Texture2D.White; headMr.color = new Color(1f, 0.85f, 0.55f, 1f); headMr.castsShadow = false; headMr.material = Materials.Lamp;
            var light = head.AddComponent<Light>();
            light.type = LightType.Point; light.color = e.GetColor("color", new Color(1f, 0.78f, 0.5f, 1f));
            light.intensity = e.GetFloat("intensity", 1.4f); light.range = e.GetFloat("range", 24f);
            return post;
        }

        private static GameObject Plane(MapEntity e, string name, Material material, Texture2D texture, Color color, float uvScale, bool transparent, bool collider, bool water)
        {
            var size = e.GetVector("size", new Vector3(200, 0, 200));
            float w = size.x, l = size.y != 0 && size.z == 0 ? size.y : size.z; // "w l" or "w 0 l"
            if (l <= 0f) l = w;
            var go = new GameObject(name);
            go.transform.position = e.origin;
            go.layer = water ? Layers.Water : Layers.Walkable;
            var mr = go.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreatePlane(Vector3.zero, w, l, uvScale);
            mr.texture = texture; mr.color = color; mr.transparent = transparent; mr.castsShadow = false; mr.material = material;
            if (collider) { var c = go.AddComponent<BoxCollider>(); c.size = new Vector3(w, 2f, l); c.center = new Vector3(0, -1f, 0); }
            if (water)
            {
                var trig = go.AddComponent<BoxCollider>(); trig.isTrigger = true; trig.size = new Vector3(w, 100f, l); trig.center = new Vector3(0, -50f, 0);
                go.AddComponent<EnterWaterCollision>();
            }
            return go;
        }
    }
}
