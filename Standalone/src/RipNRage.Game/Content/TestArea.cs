using System.Collections.Generic;
using System.IO;
using RipNRage.Engine.Graphics.Lighting;
using RipNRage.Game.Content.Level;

namespace RipNRage.Game.Content
{
    /// <summary>
    /// Rebuilds the TestArea scene: the dock level (the TrenchBroom export <c>dock.obj</c> that the
    /// scene's Dock.blend came from, imported like Unity does: X mirrored, 0.25 units per map unit,
    /// placed at y = -3.09), the sand/water planes, the ---DungeonMaster--- managers, the sky rotator,
    /// ammo pickups and a few AR_Grunt riflemen.
    /// </summary>
    public static class TestArea
    {
        public const float MapScale = 0.25f;
        public static readonly Vector3 DockOffset = new Vector3(0f, -3.09f, 0f);

        /// <summary>ProjectSettings/DynamicsManager.asset m_LayerCollisionMatrix (row = layer, bit = other layer).</summary>
        private static readonly uint[] LayerCollisionMatrix =
        {
            0xffffffff, 0xffff3fbf, 0xffff3fbf, 0xffff7bf7, 0xffff3fff, 0xffff3fbf, 0xffff1119, 0xffff303f,
            0xffff3f7f, 0xffff3f3f, 0xffff3f37, 0xffff3f3f, 0xffff3fff, 0xffff5fbf, 0xffff2009, 0xffff0001,
        };

        /// <summary>Builds the scene from a .map level; with no level the legacy hard-coded arena is used.</summary>
        public static void Build(string levelPath = null)
        {
            GameAssets.Load();
            Materials.Load();
            Physics.SetCollisionMatrix(LayerCollisionMatrix);
            // sun, sky, fog and shadows come from tunables.cfg ([lighting] / [sky] / [fog] / [shadows] / [volumetric_fog])

            if (levelPath != null)
            {
                var level = LevelLoader.Load(levelPath);
                Debug.LogWarning($"Level: {levelPath} ({level.document.Brushes.Count} brushes, {level.document.entities.Count} entities)");
                new GameObject("SkyRotator").AddComponent<SkyboxRotator>();
                BuildDungeonMaster(level.playerStart, level.playerRotation);
                return;
            }

            BuildEnvironment();
            BuildProps();
            BuildDungeonMaster(new Vector3(0f, 2.6f, 8f), Quaternion.Euler(0f, 180f, 0f));
            BuildPickups();
            BuildEnemies();
        }

        // ------------------------------------------------------------------ environment

        private static void BuildEnvironment()
        {
            var env = new GameObject("---ENV---");

            // DOCK (Dock.blend instance at (0, -3.09, 0))
            string objPath = Application.AssetPath("Levels/dock.obj");
            var dock = new GameObject("DOCK");
            dock.transform.SetParent(env.transform, false);
            dock.transform.localPosition = DockOffset;
            dock.layer = Layers.Walkable;
            var mr = dock.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.LoadObj(objPath, MapScale, 1f / 64f, mirrorX: true);
            mr.texture = ProceduralTextures.WoodPlanks(); // Dock.blend's materials were not exported; generated planks instead
            mr.uvScale = new Vector2(2f, 2f);             // one 64-unit brush face = 2 texture tiles (16 planks)
            mr.color = Color.white;
            BuildDockCollision(objPath, dock);

            // SkyRotator
            new GameObject("SkyRotator").AddComponent<SkyboxRotator>();

            // WaterPlane (x 474.6, y -0.5) scale 173.21 x 100 of Unity's 10x10 plane, SandPlane 15 below.
            var water = new GameObject("WaterPlane");
            water.transform.SetParent(env.transform, false);
            water.transform.localPosition = new Vector3(474.6f, -0.5f, 0f);
            water.layer = Layers.Water;
            var waterMr = water.AddComponent<MeshRenderer>();
            waterMr.mesh = Mesh.CreatePlane(Vector3.zero, 1732.1f, 1000f, 1f / 16f);
            waterMr.texture = Texture2D.White;
            waterMr.color = Color.white;
            waterMr.transparent = true;
            waterMr.castsShadow = false;
            waterMr.material = Materials.Water; // animated water shader (shaders/water.glsl)
            var waterTrigger = water.AddComponent<BoxCollider>();
            waterTrigger.isTrigger = true;
            waterTrigger.size = new Vector3(1732.1f, 100f, 1000f);
            waterTrigger.center = new Vector3(0, -50f, 0);
            water.AddComponent<EnterWaterCollision>();

            var sand = new GameObject("SandPlane");
            sand.transform.SetParent(water.transform, false);
            sand.transform.localPosition = new Vector3(0f, -15f, 0f);
            sand.layer = Layers.Walkable;
            var sandMr = sand.AddComponent<MeshRenderer>();
            sandMr.mesh = Mesh.CreatePlane(Vector3.zero, 1732.1f, 1000f, 1f / 4f);
            sandMr.texture = ProceduralTextures.Sand();
            sandMr.color = Color.white;
            sandMr.castsShadow = false;
            var sandCol = sand.AddComponent<BoxCollider>();
            sandCol.size = new Vector3(1732.1f, 2f, 1000f);
            sandCol.center = new Vector3(0, -1f, 0);
        }

        /// <summary>
        /// One AABB collider per brush of the TrenchBroom export. Rotated brushes (e.g. the slanted pier)
        /// are approximated by a grid of small boxes covering their convex footprint.
        /// </summary>
        private static void BuildDockCollision(string objPath, GameObject dock)
        {
            var verts = new List<Vector3>();
            var groups = new List<(string name, HashSet<int> idx)>();
            (string name, HashSet<int> idx) cur = (null, null);
            foreach (var raw in File.ReadLines(objPath))
            {
                var p = raw.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "v")
                    verts.Add(new Vector3(-float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) * MapScale,
                        float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) * MapScale,
                        float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture) * MapScale));
                else if (p[0] == "o") { cur = (p[1], new HashSet<int>()); groups.Add(cur); }
                else if (p[0] == "f" && cur.idx != null)
                    for (int i = 1; i < p.Length; i++) cur.idx.Add(int.Parse(p[i].Split('/')[0]) - 1);
            }

            foreach (var (name, idx) in groups)
            {
                var vs = new List<Vector3>();
                foreach (var i in idx) vs.Add(verts[i]);
                Vector3 min = vs[0], max = vs[0];
                var xs = new HashSet<float>(); var zs = new HashSet<float>();
                foreach (var v in vs) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); xs.Add(Mathf.Round(v.x * 100f)); zs.Add(Mathf.Round(v.z * 100f)); }
                bool axisAligned = xs.Count <= 2 && zs.Count <= 2;
                if (axisAligned)
                {
                    AddBox(dock, name, (min + max) * 0.5f, max - min);
                    continue;
                }
                // Convex footprint (XZ) → grid of boxes whose centre lies inside the hull.
                var hull = ConvexHullXZ(vs);
                float cell = 64f * MapScale * 0.5f; // 8 units
                for (float x = min.x; x < max.x; x += cell)
                    for (float z = min.z; z < max.z; z += cell)
                    {
                        float cx = x + cell * 0.5f, cz = z + cell * 0.5f;
                        if (!InsideHull(hull, cx, cz)) continue;
                        AddBox(dock, name + "_cell", new Vector3(cx, (min.y + max.y) * 0.5f, cz), new Vector3(cell, max.y - min.y, cell));
                    }
            }
        }

        private static void AddBox(GameObject parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = center;
            go.layer = Layers.Walkable;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
        }

        private static List<Vector2> ConvexHullXZ(List<Vector3> pts)
        {
            var p = new List<Vector2>();
            foreach (var v in pts) p.Add(new Vector2(v.x, v.z));
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            foreach (var pt in p) { while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], pt) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(pt); }
            int lower = hull.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--) { while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p[i]) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p[i]); }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static bool InsideHull(List<Vector2> hull, float x, float z)
        {
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % hull.Count];
                if ((b.x - a.x) * (z - a.y) - (b.y - a.y) * (x - a.x) < 0f) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ props & lights

        /// <summary>Lamp posts (point lights) and crates along the pier — shadow casters and light sources for the new lighting.</summary>
        private static void BuildProps()
        {
            float floor = DockOffset.y + 16f * MapScale; // 0.91
            var props = new GameObject("---PROPS---");
            foreach (float z in new[] { -28f, -62f, -96f })
                foreach (float x in new[] { -13.5f, 13.5f })
                    LampPost(props, new Vector3(x, floor, z));

            var crateTex = GameAssets.OrangeProto;
            Crate(props, new Vector3(6f, floor, -34f), 2.2f, crateTex);
            Crate(props, new Vector3(8.4f, floor, -35.5f), 1.6f, crateTex);
            Crate(props, new Vector3(7.2f, floor + 2.2f, -34.6f), 1.4f, crateTex);
            Crate(props, new Vector3(-9f, floor, -52f), 2.6f, GameAssets.GreenProto);
            Crate(props, new Vector3(-5f, floor, -80f), 2f, GameAssets.PurpleProto);
            Crate(props, new Vector3(10f, floor, -100f), 2.4f, GameAssets.RedProto);
        }

        private static void LampPost(GameObject parent, Vector3 basePos)
        {
            var post = new GameObject("LampPost");
            post.transform.SetParent(parent.transform, false);
            post.transform.position = basePos;
            post.layer = Layers.Walkable;
            var pole = post.AddComponent<MeshRenderer>();
            pole.mesh = Mesh.CreateBox(new Vector3(0, 2.6f, 0), new Vector3(0.28f, 5.2f, 0.28f), 1f);
            pole.texture = GameAssets.Texture13;
            pole.color = new Color(0.35f, 0.35f, 0.38f, 1f);
            var poleCol = post.AddComponent<BoxCollider>();
            poleCol.center = new Vector3(0, 2.6f, 0); poleCol.size = new Vector3(0.4f, 5.2f, 0.4f);

            var head = new GameObject("LampHead");
            head.transform.SetParent(post.transform, false);
            head.transform.localPosition = new Vector3(0, 5.4f, 0);
            var headMr = head.AddComponent<MeshRenderer>();
            headMr.mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.7f, 0.5f, 0.7f), 1f);
            headMr.texture = Texture2D.White;
            headMr.color = new Color(1f, 0.85f, 0.55f, 1f);
            headMr.emissive = 0f;
            headMr.castsShadow = false;
            headMr.material = Materials.Lamp; // pulsing emissive (shaders/mesh_emissive_pulse.glsl)
            var light = head.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.5f, 1f);
            light.intensity = 1.4f;
            light.range = 24f;
        }

        private static void Crate(GameObject parent, Vector3 basePos, float size, Texture2D tex)
        {
            var go = new GameObject("Crate");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = basePos + new Vector3(0, size * 0.5f, 0);
            go.layer = Layers.Walkable;
            var mr = go.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(Vector3.zero, Vector3.one * size, 1f / size);
            mr.texture = tex;
            mr.material = Materials.Crate;
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one * size;
        }

        // ------------------------------------------------------------------ managers

        private static void BuildDungeonMaster(Vector3 start, Quaternion rotation)
        {
            var root = new GameObject("---DungeonMaster---");
            var spawn = new GameObject("PlayerSpawn");
            spawn.transform.position = start;
            spawn.transform.rotation = rotation;

            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(root.transform, false);
            var lmGo = new GameObject("LevelManager");
            lmGo.transform.SetParent(gmGo.transform, false);
            lmGo.AddComponent<LevelManager>().playerStart = spawn.transform;
            // GameManager.Awake starts the init coroutine, so the children must exist first.
            var gm = gmGo.AddComponent<GameManager>();
            gm.playerPrefab = PlayerPrefab.Player;
        }

        // ------------------------------------------------------------------ pickups & enemies

        private static void BuildPickups()
        {
            float y = DockOffset.y + 16f * MapScale + 0.9f;
            Prefabs.ShellAmmoPickup.Instantiate(new Vector3(-3f, y, -20f), Quaternion.identity);
            Prefabs.HeavyAmmoPickup.Instantiate(new Vector3(3f, y, -40f), Quaternion.identity);
            Prefabs.LightAmmoPickup.Instantiate(new Vector3(-4f, y, -60f), Quaternion.identity);
            Prefabs.ExplosiveAmmoPickup.Instantiate(new Vector3(4f, y, -90f), Quaternion.identity);
        }

        private static void BuildEnemies()
        {
            float y = DockOffset.y + 16f * MapScale;
            Prefabs.SpawnRifleman(new Vector3(-5f, y, -16f), Quaternion.Euler(0, 0, 0));
            Prefabs.SpawnRifleman(new Vector3(2f, y, -75f), Quaternion.Euler(0, 180, 0));
            Prefabs.SpawnRifleman(new Vector3(-6f, y, -110f), Quaternion.Euler(0, 180, 0));
            Prefabs.SpawnRifleman(new Vector3(60f, y, -105f), Quaternion.Euler(0, 90, 0));
        }
    }
}
