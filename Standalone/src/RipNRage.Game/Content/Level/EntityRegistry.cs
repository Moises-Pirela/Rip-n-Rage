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

            Add(new EntityDefinition
            {
                classname = "monster_rifleman", description = "AR grunt (rifleman)",
                gizmoColor = new Color(1f, 0.3f, 0.3f, 1f), pickExtents = new Vector3(0.5f, 1.1f, 0.5f),
                defaults = { ["angle"] = "180" },
                spawnEntity = e => RipNRage.Game.Ecs.AI.Enemies.Spawn(
                    Application.Current.World, RipNRage.Game.Ecs.AI.Enemies.KindOf(GameAssets.RiflemanData), e.origin, e.angle),
                previewEntity = e => GhostSprite(e.origin, Color.white),
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
                    spawnEntity = e => RipNRage.Game.Ecs.Pickups.Spawn(Application.Current.World, t, e.origin), // 7.5: an entity, keyed by LevelRef
                    previewEntity = e => Billboard(e.origin + Vector3.up * 0.9f, new Vector3(0.75f, 0.75f, 1f), GameAssets.Pickups[sp], Color.white),
                });
            }

            Add(new EntityDefinition
            {
                classname = "monster_spawner", description = "Wave spawner (count, interval, radius, activate_range)",
                gizmoColor = new Color(1f, 0.5f, 0.1f, 1f), pickExtents = new Vector3(0.8f, 0.3f, 0.8f),
                defaults = { ["monster"] = "monster_rifleman", ["count"] = "3", ["interval"] = "5", ["radius"] = "2", ["activate_range"] = "25", ["max_alive"] = "4", ["angle"] = "180" },
                numericProps = { ("count", 1f), ("interval", 0.5f) },
                spawnEntity = e =>
                {
                    var w = Application.Current.World;
                    string monster = e.Get("monster", "monster_rifleman");
                    int kind = RipNRage.Game.Ecs.AI.Enemies.KindByClassname(monster);
                    if (kind < 0) Debug.LogWarning("monster_spawner: unknown monster " + monster);
                    var ent = w.CreateEntity();
                    w.Add(ent, new RipNRage.Engine.Ecs.Transforms.LocalTransform { Position = e.origin, Rotation = Quaternion.identity, Scale = Vector3.one });
                    w.Add(ent, new RipNRage.Game.Ecs.AI.Spawner
                    {
                        Kind = kind, Count = e.GetInt("count", 3), Interval = e.GetFloat("interval", 5f),
                        Radius = e.GetFloat("radius", 2f), ActivateRange = e.GetFloat("activate_range", 0f),
                        MaxAlive = e.GetInt("max_alive", 4), Yaw = e.angle,
                    });
                    w.Add(ent, new RipNRage.Game.Ecs.AI.LevelRef { Index = -1, SpawnerIndex = -1 });
                    return ent;
                },
                previewEntity = e =>
                {
                    var pad = LevelVisuals.Spawn(e.origin, LevelVisuals.Render(LevelVisuals.Box(new Vector3(0, 0.15f, 0), new Vector3(1.6f, 0.3f, 1.6f)),
                                                                              Texture2D.White, new Color(1f, 0.5f, 0.1f, 1f), emissive: 0.4f, castsShadow: false));
                    GhostSprite(e.origin, new Color(1f, 0.6f, 0.3f, 0.45f), pad);
                    return pad;
                },
            });

            Add(new EntityDefinition
            {
                classname = "prop_physics", description = "Rigid-body prop (size w h l, mass, texture; shape box|sphere|capsule; targetname for joints)",
                gizmoColor = new Color(1f, 0.6f, 0.2f, 1f), pickExtents = new Vector3(0.5f, 0.5f, 0.5f),
                defaults = { ["size"] = "1 1 1", ["mass"] = "10", ["texture"] = "orange", ["shape"] = "box" },
                numericProps = { ("mass", 1f) },
                spawnEntity = e => PhysicsEntities.PropEntity(e), // 9.2
                previewEntity = e => PhysicsEntities.PropPreview(e),
            });

            PhysicsEntities.Register(Add);

            Add(new EntityDefinition
            {
                classname = "light", description = "Point light (color r g b, intensity, range)",
                gizmoColor = new Color(1f, 1f, 0.6f, 1f), pickExtents = new Vector3(0.35f, 0.35f, 0.35f),
                defaults = { ["color"] = "1 0.85 0.6", ["intensity"] = "1.5", ["range"] = "18" },
                numericProps = { ("intensity", 0.25f), ("range", 1f) },
                spawnEntity = e => LevelVisuals.PointLight(e.origin, e.GetColor("color", new Color(1f, 0.85f, 0.6f, 1f)),
                                                           e.GetFloat("intensity", 1.5f), e.GetFloat("range", 18f)), // 9.1
                previewEntity = e =>
                {
                    var l = LevelVisuals.PointLight(e.origin, e.GetColor("color", new Color(1f, 0.85f, 0.6f, 1f)),
                                                    e.GetFloat("intensity", 1.5f), e.GetFloat("range", 18f));
                    LevelVisuals.Spawn(Vector3.zero, LevelVisuals.Render(LevelVisuals.Box(Vector3.zero, Vector3.one * 0.4f),
                                                                         Texture2D.White, e.GetColor("color", new Color(1f, 0.85f, 0.6f, 1f)), emissive: 1.5f, castsShadow: false), l);
                    return l;
                },
            });

            Add(new EntityDefinition
            {
                classname = "lamp_post", description = "Lamp post with light (origin = base)",
                gizmoColor = new Color(1f, 0.8f, 0.4f, 1f), pickExtents = new Vector3(0.4f, 2.8f, 0.4f),
                defaults = { ["intensity"] = "1.4", ["range"] = "24" },
                numericProps = { ("intensity", 0.2f), ("range", 1f) },
                spawnEntity = e => LampPostEntity(e), // 9.1 (the preview is the same build minus the collider)
                previewEntity = e => LampPostEntity(e, preview: true),
            });

            Add(new EntityDefinition
            {
                classname = "water_plane", description = "Animated water plane (size w l)",
                gizmoColor = new Color(0.3f, 0.6f, 1f, 1f), pickExtents = new Vector3(2f, 0.2f, 2f),
                defaults = { ["size"] = "200 200" },
                numericProps = { ("height", 0.25f) },
                spawnEntity = e => PlaneEntity(e, Materials.Water, Texture2D.White, 1f / 16f, transparent: true, collider: false, water: true), // 9.1
                previewEntity = e => PlaneEntity(e, Materials.Water, Texture2D.White, 1f / 16f, transparent: true, collider: false, water: false),
            });

            Add(new EntityDefinition
            {
                classname = "floor_plane", description = "Large textured ground plane (size w l, texture)",
                gizmoColor = new Color(0.9f, 0.8f, 0.5f, 1f), pickExtents = new Vector3(2f, 0.2f, 2f),
                defaults = { ["size"] = "200 200", ["texture"] = "sand" },
                spawnEntity = e => PlaneEntity(e, null, LevelTextures.Get(e.Get("texture", "sand")).Texture, 1f / 4f, transparent: false, collider: true, water: false,
                                               surface: LevelTextures.Get(e.Get("texture", "sand")).surface), // 9.1
                previewEntity = e => PlaneEntity(e, null, LevelTextures.Get(e.Get("texture", "sand")).Texture, 1f / 4f, transparent: false, collider: false, water: false),
            });

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

        /// <summary>The rifleman stand-in sprite (the monster and spawner previews).</summary>
        private static RipNRage.Engine.Ecs.Entity GhostSprite(Vector3 origin, Color color, RipNRage.Engine.Ecs.Entity parent = default)
        {
            var sprite = new RipNRage.Engine.Graphics.DirectionalSpriteSheet(GameAssets.RiflemanIdle, 1, 4, 1).GetSprite(0, 0);
            var pos = parent.IsNull ? origin + new Vector3(0, 1.125f, 0) : new Vector3(0, 1.125f, 0);
            return Billboard(pos, Vector3.one * (2.25f / 5.12f), sprite, color, parent);
        }

        /// <summary>A lit billboard sprite entity (the pickup and monster previews).</summary>
        private static RipNRage.Engine.Ecs.Entity Billboard(Vector3 position, Vector3 scale, Sprite sprite, Color color, RipNRage.Engine.Ecs.Entity parent = default)
        {
            var w = Application.Current.World;
            var ent = w.CreateEntity();
            w.Add(ent, new RipNRage.Engine.Ecs.Transforms.LocalTransform { Position = position, Rotation = Quaternion.identity, Scale = scale });
            if (!parent.IsNull) w.Add(ent, new RipNRage.Engine.Ecs.Transforms.Parent { Value = parent });
            w.Add(ent, new RipNRage.Engine.Ecs.Rendering.SpriteRender
            {
                Sprite = RipNRage.Engine.Ecs.Rendering.RenderAssets.Sprites.Intern(sprite),
                Color = color, Billboard = true, Lit = true, Layer = RipNRage.Engine.Graphics.RenderLayer.World,
            });
            return ent;
        }

        /// <summary>A plane's width and length: "w l" (what the maps write) or "w h l". MapEntity.GetVector wants three
        /// numbers and falls back to its default otherwise — which is how dock.map's 1732 x 1000 water and sand were built
        /// 200 x 200 (found in 7.6: the water volume did not reach where the player goes in).</summary>
        private static (float w, float l) PlaneSize(MapEntity e)
        {
            var parts = e.Get("size", "200 200").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            float Num(int i) => i < parts.Length && float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
            float w = Num(0), l = parts.Length >= 3 ? Num(2) : Num(1);
            if (w <= 0f) w = 200f;
            if (l <= 0f) l = w;
            return (w, l);
        }

        /// <summary>The game's plane (9.1): a render entity, plus its collider (floor) or water volume (water).</summary>
        private static RipNRage.Engine.Ecs.Entity PlaneEntity(MapEntity e, Material material, Texture2D texture, float uvScale, bool transparent, bool collider, bool water, string surface = null)
        {
            var (w, l) = PlaneSize(e);
            var ent = LevelVisuals.Spawn(e.origin, LevelVisuals.Render(LevelVisuals.Plane(w, l, uvScale), texture, Color.white, material,
                                                                    castsShadow: false, transparent: transparent));
            if (collider)
            {
                var floor = RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(Application.Current.World, e.origin + new Vector3(0, -1f, 0), Quaternion.identity, new Vector3(w, 2f, l), Layers.Walkable);
                if (surface != null) Surfaces.Tag(Application.Current.World, floor, surface); // what a bullet kicks up off it
            }
            if (water) RipNRage.Game.Ecs.Waters.Spawn(Application.Current.World, e.origin, new Vector3(w, 100f, l)); // the view under it hides the sky
            return ent;
        }

        /// <summary>The game's lamp post (9.1): the pole and its collider, the head (lamp material), the light in it.
        /// The editor's preview is the same build without the collider.</summary>
        private static RipNRage.Engine.Ecs.Entity LampPostEntity(MapEntity e, bool preview = false)
        {
            var post = LevelVisuals.Spawn(e.origin, LevelVisuals.Render(LevelVisuals.Box(new Vector3(0, 2.6f, 0), new Vector3(0.28f, 5.2f, 0.28f)),
                                                                     GameAssets.Texture13, new Color(0.35f, 0.35f, 0.38f, 1f)));
            if (!preview)
            {
                var poleCollider = RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(Application.Current.World, e.origin + new Vector3(0, 2.6f, 0), Quaternion.identity, new Vector3(0.4f, 5.2f, 0.4f), Layers.Walkable);
                Surfaces.Tag(Application.Current.World, poleCollider, Surfaces.Metal);
            }
            var head = LevelVisuals.Spawn(new Vector3(0, 5.4f, 0), LevelVisuals.Render(LevelVisuals.Box(Vector3.zero, new Vector3(0.7f, 0.5f, 0.7f)),
                                                                                    Texture2D.White, new Color(1f, 0.85f, 0.55f, 1f), Materials.Lamp, castsShadow: false), post);
            LevelVisuals.PointLight(Vector3.zero, e.GetColor("color", new Color(1f, 0.78f, 0.5f, 1f)), e.GetFloat("intensity", 1.4f), e.GetFloat("range", 24f), head);
            return post;
        }

    }
}
