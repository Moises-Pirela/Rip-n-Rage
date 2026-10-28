using System;
using System.Collections.Generic;
using RipNRage.Engine.Graphics;
using RipNRage.Level;

namespace RipNRage.Game.Content.Level
{
    /// <summary>Result of loading a level into the scene.</summary>
    public sealed class LoadedLevel
    {
        public GameObject root;
        public MapDocument document;
        public Vector3 playerStart = new Vector3(0, 2.6f, 0);
        public Quaternion playerRotation = Quaternion.identity;
        public readonly List<GameObject> entityObjects = new List<GameObject>();
    }

    /// <summary>
    /// Builds the scene from a .map document: batched meshes per texture, box/convex colliders per
    /// brush, point entities through the EntityRegistry, and worldspawn keys into the lighting config.
    /// </summary>
    public static class LevelLoader
    {
        public static LoadedLevel Load(string path) => Load(MapFile.Load(path));

        public static LoadedLevel Load(MapDocument doc, bool preview = false)
        {
            var level = new LoadedLevel { document = doc, root = new GameObject("Level") };
            BuildBrushes(doc, level.root, preview);
            ApplyWorldspawn(doc.worldspawn);

            if (!preview) GameSaveSystem.CurrentLevel = level;
            for (int index = 0; index < doc.entities.Count; index++)
            {
                var e = doc.entities[index];
                if (e.classname.Equals("info_player_start", StringComparison.OrdinalIgnoreCase))
                {
                    level.playerStart = e.origin + Vector3.up * 1.0f; // capsule centre above the floor
                    level.playerRotation = Quaternion.Euler(0, e.angle, 0);
                    if (!preview) continue;
                }
                var def = EntityRegistry.Get(e.classname);
                if (def == null) { Debug.LogWarning("level: unknown entity class " + e.classname); continue; }
                try
                {
                    var go = preview ? (def.preview ?? def.spawn)?.Invoke(e) : def.spawn?.Invoke(e);
                    if (go != null)
                    {
                        go.transform.SetParent(level.root.transform, true);
                        level.entityObjects.Add(go);
                        if (!preview)
                        {
                            var r = go.AddComponent<LevelEntityRef>(); r.index = index; r.classname = e.classname;
                            var sp = go.GetComponent<MonsterSpawner>(); if (sp != null) sp.levelIndex = index;
                        }
                    }
                }
                catch (Exception ex) { Debug.LogError($"level: failed to spawn {e.classname}: {ex.Message}"); }
            }
            return level;
        }

        /// <summary>Level meshes are split into cubes of this size (metres) for culling.</summary>
        public static float MeshChunkSize = 16f;

        /// <summary>Brush geometry + collision only (used by the editor when rebuilding after edits).</summary>
        public static GameObject BuildBrushes(MapDocument doc, GameObject parent, bool preview)
        {
            var geo = new GameObject("Brushes");
            geo.transform.SetParent(parent.transform, false);
            geo.layer = Layers.Walkable;

            // chunked so the renderer can frustum/occlusion-cull parts of the level; every opaque chunk is an occluder
            foreach (var (texName, mesh) in LevelMeshBuilder.BuildChunked(doc.Brushes, MeshChunkSize))
            {
                var tex = LevelTextures.Get(texName);
                if (!tex.visible && !preview) continue;
                var go = new GameObject("mesh_" + texName);
                go.transform.SetParent(geo.transform, false);
                go.layer = Layers.Walkable;
                var mr = go.AddComponent<MeshRenderer>();
                mr.mesh = mesh;
                mr.texture = tex.Texture;
                mr.color = tex.tint;
                mr.uvScale = Vector2.one;
                if (tex.material != null) mr.material = tex.material();
                mr.occluder = tex.visible; // transparent materials (water) are skipped by the renderer
                if (!tex.visible) { mr.color = new Color(1f, 0.5f, 1f, 0.35f); mr.transparent = true; mr.castsShadow = false; }
            }

            if (!preview)
            {
                foreach (var b in doc.Brushes)
                {
                    if (!b.IsValid) continue;
                    if (b.IsSky) continue;
                    var go = new GameObject("brush");
                    go.transform.SetParent(geo.transform, false);
                    go.transform.position = b.Center;
                    go.layer = Layers.Walkable;
                    if (b.IsAxisAligned)
                    {
                        var col = go.AddComponent<BoxCollider>();
                        col.size = b.boundsMax - b.boundsMin;
                    }
                    else
                    {
                        var col = go.AddComponent<ConvexCollider>();
                        var planes = new Plane[b.faces.Count];
                        var verts = new List<Vector3>();
                        for (int i = 0; i < b.faces.Count; i++) { planes[i] = b.faces[i].plane; verts.AddRange(b.faces[i].polygon); }
                        col.SetShape(planes, verts.ToArray());
                    }
                }
            }
            return geo;
        }

        /// <summary>Optional worldspawn keys override the lighting config: sun_elevation, sun_azimuth, sun_color, sky_zenith, sky_horizon, fog_color.</summary>
        private static void ApplyWorldspawn(MapEntity ws)
        {
            var lt = Application.Current?.Renderer?.Lighting;
            if (lt == null) return;
            if (ws.properties.ContainsKey("sun_elevation")) lt.sunElevation = ws.GetFloat("sun_elevation");
            if (ws.properties.ContainsKey("sun_azimuth")) lt.sunAzimuth = ws.GetFloat("sun_azimuth");
            if (ws.properties.ContainsKey("sun_color")) lt.sunColor = ws.GetColor("sun_color", lt.sunColor);
            if (ws.properties.ContainsKey("sky_zenith")) lt.skyZenith = ws.GetColor("sky_zenith", lt.skyZenith);
            if (ws.properties.ContainsKey("sky_horizon")) lt.skyHorizon = ws.GetColor("sky_horizon", lt.skyHorizon);
            if (ws.properties.ContainsKey("fog_color")) lt.fogColor = ws.GetColor("fog_color", lt.fogColor);
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
