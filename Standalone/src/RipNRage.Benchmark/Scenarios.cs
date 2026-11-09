using System;
using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;
using RipNRage.Engine.Ecs.Rendering;
using RipNRage.Engine.Ecs.Transforms;
using RipNRage.Engine.Graphics;
using RipNRage.Game.Content;

namespace RipNRage.Benchmark
{
    /// <summary>Shared scene pieces and the scenario catalogue.</summary>
    public static class BenchmarkScene
    {
        /// <summary>Every layer collides with every other (the benchmark filters by collision masks where it matters).</summary>
        public static readonly uint[] CollisionMatrix =
        {
            0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff,
            0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff, 0xffffffff,
        };

        public static List<Scenario> Select(string which)
        {
            // ragdolls (jointed bodies) and projectiles (kinematic movers) return with ECS joints (2.5) and kinematic bodies (2.6)
            var all = new List<Scenario> { new CratesScenario(), new GroundScenario() };
            if (which == "all") return all;
            var list = new List<Scenario>();
            foreach (var part in which.Split(new[] { ',', ' ', '+' }, StringSplitOptions.RemoveEmptyEntries))
                foreach (var s in all) if (s.Name.StartsWith(part, StringComparison.OrdinalIgnoreCase) && !list.Contains(s)) list.Add(s);
            return list;
        }

        public static void PlaceCamera(Camera cam, string scenario)
        {
            switch (scenario)
            {
                case "crates": Look(cam, new Vector3(0f, 22f, -42f), new Vector3(0f, 3f, 0f)); break;
                case "ragdolls": Look(cam, new Vector3(0f, 18f, -48f), new Vector3(0f, 1f, 0f)); break;
                case "projectiles": Look(cam, new Vector3(0f, 45f, -80f), new Vector3(0f, 2f, 0f)); break;
                default: Look(cam, new Vector3(0f, 40f, -120f), new Vector3(0f, 0f, 0f)); break;
            }
        }

        private static void Look(Camera cam, Vector3 from, Vector3 at)
        {
            cam.transform.position = from;
            cam.transform.rotation = Quaternion.LookRotation((at - from).normalized, Vector3.up);
        }

        /// <summary>A textured static box (floor slab, wall, pillar) with its base at <paramref name="basePos"/>.</summary>
        public static GameObject StaticBox(GameObject parent, string name, Vector3 basePos, Vector3 size, Texture2D tex, float uvScale = 0.25f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = basePos + new Vector3(0f, size.y * 0.5f, 0f);
            go.layer = Layers.Walkable;
            var mr = go.AddComponent<MeshRenderer>();
            mr.mesh = Mesh.CreateBox(Vector3.zero, size, uvScale);
            mr.texture = tex;
            Track(RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticBox(World, go.transform.position, Quaternion.identity, size, Layers.Walkable));
            return go;
        }

        /// <summary>Floor slab (top at y = 0) and four walls around a square arena.</summary>
        public static void Arena(GameObject root, float size, float wallHeight)
        {
            StaticBox(root, "Floor", new Vector3(0f, -2f, 0f), new Vector3(size, 2f, size), GameAssets.Texture13, 1f / 8f);
            if (wallHeight <= 0f) return;
            float h = size * 0.5f;
            StaticBox(root, "WallN", new Vector3(0f, 0f, h), new Vector3(size, wallHeight, 1f), GameAssets.GreenProto);
            StaticBox(root, "WallS", new Vector3(0f, 0f, -h), new Vector3(size, wallHeight, 1f), GameAssets.GreenProto);
            StaticBox(root, "WallE", new Vector3(h, 0f, 0f), new Vector3(1f, wallHeight, size), GameAssets.GreenProto);
            StaticBox(root, "WallW", new Vector3(-h, 0f, 0f), new Vector3(1f, wallHeight, size), GameAssets.GreenProto);
        }

        public static World World => Application.Current.World;
        private static readonly List<Entity> _spawned = new List<Entity>();
        /// <summary>Remembers an entity the scenario created so <see cref="DestroyEntities"/> can remove it at teardown.</summary>
        public static Entity Track(Entity e) { _spawned.Add(e); return e; }
        public static void DestroyEntities()
        {
            var w = World;
            w.Sync();
            foreach (var e in _spawned) if (w.IsAlive(e)) w.DestroyEntity(e);
            _spawned.Clear();
            w.Sync();
        }

        /// <summary>A dynamic ECS body that renders through the ECS (mesh + texture).</summary>
        public static Entity DynamicBody(Vector3 position, Quaternion rotation, ColliderShape shape, float mass, float friction, float restitution, Mesh mesh, Texture2D tex)
        {
            var w = World;
            var e = Track(RipNRage.Engine.Ecs.Physics.PhysicsEntities.Dynamic(w, new LocalTransform { Position = position, Rotation = rotation, Scale = Vector3.one }, shape, mass));
            var cmd = w.Commands;
            cmd.Add(e, new RipNRage.Engine.Ecs.Physics.SurfaceMaterial { StaticFriction = friction, DynamicFriction = friction, Restitution = restitution, FrictionCombine = CombineMode.Average, RestitutionCombine = CombineMode.Maximum });
            cmd.Add(e, new MeshRender { Mesh = RenderAssets.Meshes.Intern(mesh), Texture = RenderAssets.Textures.Intern(tex ?? Texture2D.White), Color = Color.white, UvScale = Vector2.one, CastsShadow = true, Layer = RenderLayer.World });
            return e;
        }

        /// <summary>Deterministic pseudo-random numbers so runs compare.</summary>
        public sealed class Rng
        {
            private uint _s;
            public Rng(uint seed) { _s = seed == 0 ? 1u : seed; }
            public float Next01() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s & 0xFFFFFF) / 16777216f; }
            public float Range(float a, float b) => a + (b - a) * Next01();
            public Vector3 InUnitSphere() { Vector3 v; do { v = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f)); } while (v.sqrMagnitude > 1f); return v; }
            public Vector3 OnCircle() { float a = Range(0f, MathF.PI * 2f); return new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)); }
        }
    }

    // ==================================================================== 1. crates

    /// <summary>1000+ one-metre ECS crates dropped in a loose block so they tumble into a heap; an explosion every few seconds keeps them colliding.</summary>
    public sealed class CratesScenario : Scenario
    {
        public override string Name => "crates";
        public override string Description => "colliding ECS rigid-body crates (box/box contacts, stacking, sleeping; a blast every 4 s)";
        public override int Target(BenchmarkConfig cfg) => cfg.Count(cfg.crates);
        private readonly List<Entity> _bodies = new List<Entity>();
        private float _nextBlast = 4f;
        private BenchmarkScene.Rng _rng = new BenchmarkScene.Rng(7);
        private int _sleeping;

        public override void Build(GameObject root, BenchmarkConfig cfg)
        {
            int n = Target(cfg);
            BenchmarkScene.Arena(root, 60f, 8f);
            var mesh = Mesh.CreateBox(Vector3.zero, Vector3.one, 1f);
            int side = Math.Max(2, (int)MathF.Ceiling(MathF.Sqrt(n / 6f)));
            float spacing = 1.25f;
            int placed = 0;
            for (int layer = 0; placed < n; layer++)
                for (int x = 0; x < side && placed < n; x++)
                    for (int z = 0; z < side && placed < n; z++, placed++)
                    {
                        var pos = new Vector3((x - side * 0.5f + 0.5f) * spacing + _rng.Range(-0.1f, 0.1f), 1f + layer * spacing + _rng.Range(0f, 0.2f), (z - side * 0.5f + 0.5f) * spacing + _rng.Range(-0.1f, 0.1f));
                        _bodies.Add(BenchmarkScene.DynamicBody(pos, Quaternion.Euler(0f, _rng.Range(0f, 90f), 0f), new ColliderShape { Kind = ShapeKind.Box, Size = Vector3.one },
                            12f, 0.55f, 0.05f, mesh, (placed & 1) == 0 ? GameAssets.OrangeProto : GameAssets.PurpleProto));
                    }
        }

        public override void Tick(float dt, float elapsed)
        {
            if (elapsed < _nextBlast) return;
            _nextBlast = elapsed + 4f;
            Vector3 at = new Vector3(_rng.Range(-6f, 6f), 0.5f, _rng.Range(-6f, 6f));
            Physics.AddExplosionForce(260f, at, 14f, 0.8f);
            _sleeping = 0;
            var w = BenchmarkScene.World;
            foreach (var b in _bodies) if (w.IsAlive(b) && w.Has<Sleeping>(b)) _sleeping++;
        }

        public override void Teardown() => _bodies.Clear();
        public override int Alive => _bodies.Count;
        public override string Extra => $"sleeping(at last blast) {_sleeping}";
    }

    // ==================================================================== 2. ground

    /// <summary>
    /// Fast ECS spheres rolling over a rough 30k-triangle mesh terrain, re-aimed every few seconds (the force-driven
    /// CharacterBody runners this used to drive return with the ECS character body, step 2.6; numbers from before
    /// layer 2's legacy removal are not comparable).
    /// </summary>
    public sealed class GroundScenario : Scenario
    {
        public override string Name => "ground";
        public override string Description => "fast ECS rolling spheres (12–16 m/s) on a hilly static mesh collider (~29k triangles)";
        public override int Target(BenchmarkConfig cfg) => cfg.Count(cfg.runners);
        private readonly List<Entity> _rollers = new List<Entity>();
        private readonly List<float> _speeds = new List<float>();
        private BenchmarkScene.Rng _rng = new BenchmarkScene.Rng(31);
        private float _nextAim = 0.5f;
        private int _grounded; private float _speed;

        public override void Build(GameObject root, BenchmarkConfig cfg)
        {
            int n = Target(cfg);
            BuildTerrain(root, 200f, 120);
            var mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.8f, 0.8f, 0.8f), 1f);
            int side = Math.Max(2, (int)MathF.Ceiling(MathF.Sqrt(n)));
            for (int i = 0; i < n; i++)
            {
                int x = i % side, z = i / side;
                Vector3 p = new Vector3((x - side * 0.5f + 0.5f) * 4f, 0f, (z - side * 0.5f + 0.5f) * 4f);
                p.y = TerrainHeight(p.x, p.z) + 1.0f;
                _rollers.Add(BenchmarkScene.DynamicBody(p, Quaternion.identity, new ColliderShape { Kind = ShapeKind.Sphere, Radius = 0.4f }, 20f, 0.6f, 0.1f, mesh, GameAssets.RedProto));
                _speeds.Add(_rng.Range(12f, 16f));
            }
        }

        public override void Tick(float dt, float elapsed)
        {
            var w = BenchmarkScene.World;
            bool aim = elapsed >= _nextAim;
            if (aim) _nextAim = elapsed + 2.5f;
            _grounded = 0; _speed = 0f; int n = 0;
            for (int i = 0; i < _rollers.Count; i++)
            {
                var e = _rollers[i];
                if (!w.IsAlive(e) || !w.Has<BodyVelocity>(e)) continue;
                ref var v = ref w.Get<BodyVelocity>(e);
                var p = w.Get<LocalTransform>(e).Position;
                if (aim)
                {
                    // head inward from the edge, otherwise a new random heading at this roller's speed
                    Vector3 dir = MathF.Abs(p.x) > 85f || MathF.Abs(p.z) > 85f ? (-new Vector3(p.x, 0f, p.z)).normalized : _rng.OnCircle();
                    v.Linear = new Vector3(dir.x * _speeds[i], v.Linear.y, dir.z * _speeds[i]);
                }
                n++; _speed += v.Linear.magnitude;
                if (p.y - TerrainHeight(p.x, p.z) < 0.6f) _grounded++;
            }
            if (n > 0) _speed /= n;
        }

        public override void Teardown() { _rollers.Clear(); _speeds.Clear(); }
        public override int Alive => _rollers.Count;
        public override string Extra => $"grounded {_grounded} avg speed {_speed:0.0} m/s";

        // ---- terrain: hills from a few sines plus a hash wobble, so every triangle is a different slope

        public static float TerrainHeight(float x, float z)
        {
            float h = MathF.Sin(x * 0.11f) * 2.2f + MathF.Cos(z * 0.09f + 1.3f) * 2.0f + MathF.Sin((x + z) * 0.05f) * 3f;
            h += MathF.Sin(x * 0.43f + z * 0.37f) * 0.45f + MathF.Cos(x * 0.71f - z * 0.53f) * 0.3f;
            return h;
        }

        private static void BuildTerrain(GameObject root, float size, int cells)
        {
            var mesh = new Mesh();
            int vn = cells + 1;
            var verts = new Vector3[vn * vn]; var uv = new Vector2[vn * vn];
            for (int j = 0; j < vn; j++)
                for (int i = 0; i < vn; i++)
                {
                    float x = (i / (float)cells - 0.5f) * size, z = (j / (float)cells - 0.5f) * size;
                    verts[j * vn + i] = new Vector3(x, TerrainHeight(x, z), z);
                    uv[j * vn + i] = new Vector2(x * 0.25f, z * 0.25f);
                }
            var tris = new int[cells * cells * 6];
            int t = 0;
            for (int j = 0; j < cells; j++)
                for (int i = 0; i < cells; i++)
                {
                    int a = j * vn + i, b = a + 1, c = a + vn, d = c + 1;
                    if (((i + j) & 1) == 0) { tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d; }
                    else { tris[t++] = a; tris[t++] = d; tris[t++] = b; tris[t++] = a; tris[t++] = c; tris[t++] = d; }
                }
            mesh.vertices = verts; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Terrain");
            go.transform.SetParent(root.transform, false);
            go.layer = Layers.Walkable;
            var mr = go.AddComponent<MeshRenderer>(); mr.mesh = mesh; mr.texture = GameAssets.Texture13;
            BenchmarkScene.Track(RipNRage.Engine.Ecs.Physics.PhysicsEntities.StaticMesh(BenchmarkScene.World, PhysicsAssets.AddMesh("bench-terrain", verts, tris), LocalTransform.At(Vector3.zero), Layers.Walkable));
        }
    }
}
