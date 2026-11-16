using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics;
using RipNRage.Level;

namespace RipNRage.Game.Content.Level
{
    /// <summary>Result of loading a level into the scene.</summary>
    public sealed class LoadedLevel
    {
        public MapDocument document;
        public Vector3 playerStart = new Vector3(0, 2.6f, 0);
        public Quaternion playerRotation = Quaternion.identity;
        /// <summary>World Y below which the player is respawned (worldspawn kill_y, else 1 m under the water plane, else 5 m under the lowest brush).</summary>
        public float killY = -50f;
        /// <summary>The editor's preview entities (9.6: a rebuild destroys them; children follow their parents).</summary>
        public readonly List<RipNRage.Engine.Ecs.Entity> previewEntities = new List<RipNRage.Engine.Ecs.Entity>();

        /// <summary>A named spawn and where it stood when the level was built (9.2: the connectors' pose math — the
        /// entity itself goes through the command buffer and is not alive until playback).</summary>
        public readonly Dictionary<string, (RipNRage.Engine.Ecs.Entity entity, Vector3 position, Quaternion rotation)> namedEntities =
            new Dictionary<string, (RipNRage.Engine.Ecs.Entity, Vector3, Quaternion)>(StringComparer.OrdinalIgnoreCase);

        public void RegisterNamed(string targetname, RipNRage.Engine.Ecs.Entity entity, Vector3 position, Quaternion rotation)
        {
            if (!string.IsNullOrEmpty(targetname)) namedEntities[targetname] = (entity, position, rotation);
        }
    }

    /// <summary>
    /// Builds the scene from a .map document: batched meshes per texture, box/convex colliders per
    /// brush, point entities through the EntityRegistry, and worldspawn keys into the lighting config.
    /// </summary>
    public static class LevelLoader
    {
        public static LoadedLevel Load(string path)
        {
            // the level's chunk meshes are cached per file version (LevelVisuals: the asset registry is append-only)
            _chunkKey = path + "@" + System.IO.File.GetLastWriteTimeUtc(path).Ticks;
            try { return Load(MapFile.Load(path)); }
            finally { _chunkKey = null; }
        }

        private static string _chunkKey;
        private static readonly List<RipNRage.Engine.Ecs.AssetHandle<Mesh>> _previewChunkHandles = new List<RipNRage.Engine.Ecs.AssetHandle<Mesh>>();
        private static readonly Dictionary<string, (string tex, RipNRage.Engine.Ecs.AssetHandle<Mesh> mesh)[]> _chunks =
            new Dictionary<string, (string, RipNRage.Engine.Ecs.AssetHandle<Mesh>)[]>();

        /// <summary>The level's render chunks, built once per file version (anonymous when loaded from a document).</summary>
        private static (string tex, RipNRage.Engine.Ecs.AssetHandle<Mesh> mesh)[] Chunks(MapDocument doc)
        {
            if (_chunkKey != null && _chunks.TryGetValue(_chunkKey, out var cached)) return cached;
            var built = new List<(string, RipNRage.Engine.Ecs.AssetHandle<Mesh>)>();
            foreach (var (texName, mesh) in LevelMeshBuilder.BuildChunked(doc.Brushes, MeshChunkSize))
                built.Add((texName, RipNRage.Engine.Ecs.Rendering.RenderAssets.Meshes.Intern(mesh)));
            var arr = built.ToArray();
            if (_chunkKey != null) _chunks[_chunkKey] = arr;
            return arr;
        }

        public static LoadedLevel Load(MapDocument doc, bool preview = false)
        {
            var level = new LoadedLevel { document = doc };
            BuildBrushes(doc, level, preview);
            level.killY = BakeWorldspawn(doc);

            if (!preview) GameSaveSystem.CurrentLevel = level;
            Loading = level;
            try
            {
                // two passes: everything that stands on its own, then the deferred classes (joints connecting entities by targetname)
                for (int pass = 0; pass < 2; pass++)
                    for (int index = 0; index < doc.entities.Count; index++)
                    {
                        var e = doc.entities[index];
                        if (e.classname.Equals("info_player_start", StringComparison.OrdinalIgnoreCase))
                        {
                            if (pass == 1) continue;
                            level.playerStart = e.origin + Vector3.up * 1.0f; // capsule centre above the floor
                            level.playerRotation = Quaternion.Euler(0, e.angle, 0);
                            if (!preview) continue;
                        }
                        var def = EntityRegistry.Get(e.classname);
                        if (def == null) { if (pass == 0) Debug.LogWarning("level: unknown entity class " + e.classname); continue; }
                        if (def.deferred != (pass == 1)) continue;
                        try
                        {
                            var w = Application.Current.World;
                            RipNRage.Engine.Ecs.Entity ent;
                            if (def.prefab != null)
                            {
                                var inst = SpawnPrefab(def, e, preview, w);
                                ent = inst.Root;
                                if (preview) foreach (var x in inst.All) level.previewEntities.Add(x); // detached children too
                                else level.RegisterNamed(e.Get("targetname"), ent,
                                                         w.Get<RipNRage.Engine.Ecs.Transforms.LocalTransform>(ent).Position,
                                                         w.Get<RipNRage.Engine.Ecs.Transforms.LocalTransform>(ent).Rotation);
                            }
                            else
                            {
                                ent = (preview ? (def.previewEntity ?? def.spawnEntity) : def.spawnEntity)?.Invoke(e) ?? RipNRage.Engine.Ecs.Entity.Null;
                                if (preview && !ent.IsNull) level.previewEntities.Add(ent);
                            }
                            if (!preview && w.IsAlive(ent) && w.Has<RipNRage.Game.Ecs.AI.LevelRef>(ent))
                                w.Get<RipNRage.Game.Ecs.AI.LevelRef>(ent).Index = index;
                        }
                        catch (Exception ex) { Debug.LogError($"level: failed to spawn {e.classname}: {ex.Message}"); }
                    }
            }
            finally { Loading = null; }
            return level;
        }

        /// <summary>The level being built right now (entity spawners resolve targetnames through it).</summary>
        public static LoadedLevel Loading { get; private set; }

        /// <summary>
        /// A class that is a prefab (10.3b): every map key but the universal ones is a field override, and an
        /// <c>EntityRef</c> the tree cannot answer is looked up among the level's <c>targetname</c>s. A key the prefab
        /// does not know throws, naming the class and the key — a typo in a map is caught at load, not at play.
        /// </summary>
        private static RipNRage.Engine.Ecs.PrefabInstance SpawnPrefab(EntityDefinition def, MapEntity e, bool preview, RipNRage.Engine.Ecs.World world)
        {
            var overrides = new List<KeyValuePair<string, string>>();
            foreach (var kv in e.properties)
                if (!IsUniversalKey(kv.Key)) overrides.Add(kv);
            var lib = GamePrefabs.Library;
            var prefab = overrides.Count == 0 ? lib.Get(def.prefab) : lib.Get(def.prefab, overrides);
            var rotation = Quaternion.Euler(0f, e.angle, 0f);
            return preview ? prefab.SpawnPreview(world, e.origin, rotation, LevelName)
                           : prefab.Spawn(world, e.origin, rotation, LevelName);
        }

        /// <summary>
        /// One entity of a class, outside a level load (10.3d: the save restoring a monster a spawner made). The class
        /// may be a prefab or still code; the caller gives the world because this runs when no level is loading.
        /// </summary>
        public static RipNRage.Engine.Ecs.Entity SpawnClass(RipNRage.Engine.Ecs.World world, MapEntity e)
        {
            var def = EntityRegistry.Get(e.classname);
            if (def == null) return RipNRage.Engine.Ecs.Entity.Null;
            if (def.prefab != null) return SpawnPrefab(def, e, preview: false, world).Root;
            return def.spawnEntity?.Invoke(e) ?? RipNRage.Engine.Ecs.Entity.Null;
        }

        /// <summary>Keys the loader owns: where a thing stands, which way it faces, and what it is called.</summary>
        private static bool IsUniversalKey(string key) =>
            key.Equals("classname", StringComparison.OrdinalIgnoreCase) || key.Equals("origin", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("angle", StringComparison.OrdinalIgnoreCase) || key.Equals("targetname", StringComparison.OrdinalIgnoreCase);

        /// <summary>Resolves an <c>EntityRef</c> against the level's named entities (a prefab's own children come first).</summary>
        private static RipNRage.Engine.Ecs.Entity LevelName(string name) =>
            Loading != null && Loading.namedEntities.TryGetValue(name, out var n) ? n.entity : RipNRage.Engine.Ecs.Entity.Null;

        /// <summary>The kill height a level implies when its worldspawn gives none: the water surface minus one metre, else five metres under the lowest brush.</summary>
        private static float ComputeKillY(MapDocument doc)
        {
            foreach (var e in doc.entities)
                if (e.classname.Equals("water_plane", StringComparison.OrdinalIgnoreCase)) return e.origin.y - 1f;
            var (min, _) = doc.Bounds;
            return doc.Brushes.Count > 0 ? min.y - 5f : -50f;
        }

        /// <summary>Level meshes are split into cubes of this size (metres) for culling.</summary>
        public static float MeshChunkSize = 16f;

        /// <summary>Brush geometry + collision (the editor's preview: visuals only, invisible textures shown pink,
        /// chunk mesh handles reused per rebuild — an edit must not intern fresh copies).</summary>
        public static void BuildBrushes(MapDocument doc, LoadedLevel level, bool preview)
        {
            // chunked so the renderer can frustum/occlusion-cull parts of the level; every opaque chunk is an occluder
            if (!preview)
            {
                foreach (var (texName, handle) in Chunks(doc))
                {
                    var tex = LevelTextures.Get(texName);
                    if (!tex.visible) continue;
                    var r = LevelVisuals.Render(handle, tex.Texture, tex.tint, tex.material?.Invoke(),
                                              occluder: !tex.transparent, transparent: tex.transparent, castsShadow: !tex.transparent);
                    LevelVisuals.Spawn(Vector3.zero, in r);
                }
            }
            else
            {
                int i = 0;
                foreach (var (texName, mesh) in LevelMeshBuilder.BuildChunked(doc.Brushes, MeshChunkSize))
                {
                    RipNRage.Engine.Ecs.AssetHandle<Mesh> handle;
                    if (i < _previewChunkHandles.Count)
                    {
                        handle = _previewChunkHandles[i];
                        RipNRage.Engine.Ecs.Rendering.RenderAssets.Meshes.Replace(handle, mesh);
                    }
                    else _previewChunkHandles.Add(handle = RipNRage.Engine.Ecs.Rendering.RenderAssets.Meshes.Intern(mesh));
                    i++;
                    var tex = LevelTextures.Get(texName);
                    bool hiddenTex = !tex.visible;
                    var r = LevelVisuals.Render(handle, tex.Texture, hiddenTex ? new Color(1f, 0.5f, 1f, 0.35f) : tex.tint,
                                              tex.material?.Invoke(), transparent: tex.transparent || hiddenTex,
                                              castsShadow: !(tex.transparent || hiddenTex));
                    level.previewEntities.Add(LevelVisuals.Spawn(Vector3.zero, in r));
                }
            }

            if (!preview)
            {
                foreach (var b in doc.Brushes)
                {
                    if (!b.IsValid) continue;
                    if (b.IsSky) continue;
                    // collision: an ECS static collider, tagged with the surface of every face for the impact effects
                    // (hit normal → face → texture → surface)
                    var world = Application.Current.World;
                    RipNRage.Engine.Ecs.Entity collider;
                    if (b.IsAxisAligned)
                        collider = RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(world, (b.boundsMin + b.boundsMax) * 0.5f, Quaternion.identity, b.boundsMax - b.boundsMin, Layers.Walkable);
                    else
                    {
                        var planes = new Plane[b.faces.Count];
                        var verts = new List<Vector3>();
                        for (int i = 0; i < b.faces.Count; i++) { planes[i] = b.faces[i].plane; verts.AddRange(b.faces[i].polygon); }
                        collider = RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticConvex(world, planes, verts.ToArray(), Layers.Walkable);
                    }
                    var normals = new Vector3[b.faces.Count]; var ids = new int[b.faces.Count];
                    for (int i = 0; i < b.faces.Count; i++) { normals[i] = b.faces[i].plane.normal; ids[i] = Surfaces.Id(LevelTextures.Get(b.faces[i].texture).surface); }
                    Surfaces.TagFaces(world, collider, new RipNRage.Game.Ecs.BrushFaces(normals, ids, ids.Length > 0 ? ids[0] : 0));
                }
            }
        }

        private static int _worldspawnVersion;

        /// <summary>
        /// The worldspawn is a prefab like every other class (10.5), baked onto the <c>Worldspawn</c> component
        /// singleton rather than spawned: its keys override the <c>worldspawn</c> file's fields, <c>kill_y</c> is
        /// derived from the level when absent, and a key the prefab does not know is a warning, not an error — a map
        /// editor writes its own there (<c>mapversion</c>, <c>wad</c>). Returns the kill height.
        /// </summary>
        private static float BakeWorldspawn(MapDocument doc)
        {
            var lib = GamePrefabs.Library;
            var overrides = new List<KeyValuePair<string, string>>();
            foreach (var kv in doc.worldspawn.properties)
            {
                if (kv.Key.Equals("classname", StringComparison.OrdinalIgnoreCase)) continue;
                if (lib.ResolveKey("worldspawn", kv.Key) != null) overrides.Add(kv);
                else Debug.LogWarning($"level: worldspawn key '{kv.Key}' is not one the worldspawn prefab knows; ignored");
            }
            if (!doc.worldspawn.properties.ContainsKey("kill_y"))
                overrides.Add(new KeyValuePair<string, string>("kill_y", ComputeKillY(doc).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
            var baked = lib.Get("worldspawn", overrides).Get<RipNRage.Engine.Ecs.Levels.WorldspawnConfig>().Bake();
            baked.Version = ++_worldspawnVersion; // every bake is a new one, so the system re-applies after a scene reset too
            Application.Current.World.Singleton<RipNRage.Engine.Ecs.Levels.Worldspawn>() = baked;
            return baked.KillY;
        }

        /// <summary>Finds levels/&lt;name&gt; next to the executable / cwd / parents.</summary>
        public static string Locate(string nameOrPath)
        {
            if (System.IO.File.Exists(nameOrPath)) return System.IO.Path.GetFullPath(nameOrPath);
            foreach (var start in new[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new System.IO.DirectoryInfo(start);
                while (dir != null)
                {
                    string c = System.IO.Path.Combine(dir.FullName, "levels", nameOrPath);
                    if (System.IO.File.Exists(c)) return c;
                    c = System.IO.Path.Combine(dir.FullName, "levels", nameOrPath + ".map");
                    if (System.IO.File.Exists(c)) return c;
                    dir = dir.Parent;
                }
            }
            return null;
        }
    }
}
