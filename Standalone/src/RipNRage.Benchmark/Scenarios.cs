using System;
using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Destruction;
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
            var all = new List<Scenario> { new CratesScenario(), new RagdollsScenario(), new ProjectilesScenario(), new GroundScenario() };
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
        public static Entity DynamicBody(Vector3 position, Quaternion rotation, ColliderShape shape, float mass, float friction, float restitution, Mesh mesh, Texture2D tex, int group = 0)
        {
            var w = World;
            var e = Track(RipNRage.Engine.Ecs.Physics.PhysicsEntities.Dynamic(w, new LocalTransform { Position = position, Rotation = rotation, Scale = Vector3.one }, shape, mass));
            var cmd = w.Commands;
            cmd.Add(e, new RipNRage.Engine.Ecs.Physics.SurfaceMaterial { StaticFriction = friction, DynamicFriction = friction, Restitution = restitution, FrictionCombine = CombineMode.Average, RestitutionCombine = CombineMode.Maximum });
            if (group != 0) cmd.Add(e, new CollisionFilter { Mask = ~0, UseLayerMatrix = true, Group = group });
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

    // ==================================================================== 2. ragdolls

    /// <summary>
    /// 500+ rifleman sprites sliced along skeletons/rifleman.json into jointed ECS ragdolls (cut-sprite pieces on
    /// ball joints, piece/piece and piece/floor contacts), sliced over the first second like a massacre; a blast
    /// every 5 s.
    /// </summary>
    public sealed class RagdollsScenario : Scenario
    {
        public override string Name => "ragdolls";
        public override string Description => "sprite destruction: rifleman sprites sliced into jointed ECS ragdoll pieces (a blast every 5 s)";
        public override int Target(BenchmarkConfig cfg) => cfg.Count(cfg.ragdolls);
        private readonly List<Vector3> _spots = new List<Vector3>();
        private readonly List<Entity> _pieces = new List<Entity>();
        private Sprite _sprite;
        private SpriteSkeleton _skeleton;
        private int _sliced, _ragdolls;
        private float _nextBlast = 6f;
        private readonly BenchmarkScene.Rng _rng = new BenchmarkScene.Rng(11);
        private const float S = 2.25f;

        public override void Build(GameObject root, BenchmarkConfig cfg)
        {
            int n = Target(cfg);
            BenchmarkScene.Arena(root, 70f, 6f);
            _skeleton = SkeletonLibrary.Get("rifleman") ?? DefaultSkeletons.Rifleman();
            _sprite = new DirectionalSpriteSheet(GameAssets.RiflemanIdle, 1, 4, 1f).GetSprite(0, 0);
            int side = Math.Max(2, (int)MathF.Ceiling(MathF.Sqrt(n)));
            for (int i = 0; i < n; i++)
            {
                int x = i % side, z = i / side;
                _spots.Add(new Vector3((x - side * 0.5f + 0.5f) * 2.2f, 0.5f * S, (z - side * 0.5f + 0.5f) * 2.2f));
            }
        }

        public override void Tick(float dt, float elapsed)
        {
            // slice the "standing" enemies over the first second or so of the warm-up
            if (_sliced < _spots.Count)
            {
                int perFrame = Math.Max(1, _spots.Count / 45);
                for (int k = 0; k < perFrame && _sliced < _spots.Count; k++, _sliced++) Slice(_spots[_sliced]);
            }
            if (elapsed >= _nextBlast)
            {
                _nextBlast = elapsed + 5f;
                Physics.AddExplosionForce(120f, new Vector3(_rng.Range(-10f, 10f), 0.3f, _rng.Range(-10f, 10f)), 12f, 0.8f);
            }
        }

        private void Slice(Vector3 at)
        {
            var options = RipNRage.Engine.Ecs.Destruction.RagdollOptions.Default;
            options.lifetime = 100000f; options.fadeOut = false;
            Vector3 dir = _rng.OnCircle(); dir.y = 0.3f;
            options.impulse = dir.normalized * _rng.Range(30f, 90f);
            options.impulsePoint = at + new Vector3(0f, _rng.Range(-0.5f, 0.8f), 0f);
            var cam = Camera.main;
            var pieces = RipNRage.Engine.Ecs.Destruction.SpriteRagdolls.Spawn(BenchmarkScene.World, _skeleton, _sprite, at, Vector3.one * (S / 5.12f),
                cam != null ? cam.transform.position : at + Vector3.back, options);
            foreach (var p in pieces) _pieces.Add(BenchmarkScene.Track(p));
            if (pieces.Count > 0) _ragdolls++;
        }

        public override void Teardown() { _spots.Clear(); _pieces.Clear(); _sliced = _ragdolls = 0; }
        public override int Alive => _ragdolls;
        public override string Extra => $"pieces {_pieces.Count}";
    }

    // ==================================================================== 3. projectiles

    /// <summary>1000+ swept kinematic movers (Stop mode) in flight at once, fired from a ring into a field of pillars and walls.</summary>
    public sealed class ProjectilesScenario : Scenario
    {
        public override string Name => "projectiles";
        public override string Description => "swept ECS kinematic movers (Stop mode, collision reports) flying through a pillar field";
        public override int Target(BenchmarkConfig cfg) => cfg.Count(cfg.projectiles);
        private readonly List<(Entity e, float age)> _live = new List<(Entity, float)>();
        private Mesh _mesh;
        private int _target, _spawned, _hits;
        private float _spawnAcc;
        private readonly BenchmarkScene.Rng _rng = new BenchmarkScene.Rng(23);
        private const float Speed = 40f, Life = 3.2f;

        public override void Build(GameObject root, BenchmarkConfig cfg)
        {
            _target = Target(cfg);
            BenchmarkScene.Arena(root, 160f, 10f);
            var pr = new BenchmarkScene.Rng(5);
            for (int i = 0; i < 80; i++)
                BenchmarkScene.StaticBox(root, "Pillar" + i, new Vector3(pr.Range(-60f, 60f), 0f, pr.Range(-60f, 60f)), new Vector3(pr.Range(0.8f, 2.5f), pr.Range(2f, 7f), pr.Range(0.8f, 2.5f)), GameAssets.PurpleProto);
            _mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.16f, 0.16f, 0.5f), 1f);
        }

        public override void Tick(float dt, float elapsed)
        {
            var w = BenchmarkScene.World;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var (e, age) = _live[i];
                age += dt;
                bool hit = w.IsAlive(e) && w.Get<KinematicMover>(e).Stopped;
                if (hit) _hits++;
                if (hit || age >= Life || !w.IsAlive(e)) { if (w.IsAlive(e)) w.Commands.DestroyEntity(e); _live.RemoveAt(i); continue; }
                _live[i] = (e, age);
            }
            float rate = _target / Life * 1.15f;
            _spawnAcc += rate * dt;
            int n = (int)_spawnAcc;
            if (_live.Count + n > _target + _target / 10) n = Math.Max(0, _target + _target / 10 - _live.Count);
            _spawnAcc -= (int)_spawnAcc;
            for (int i = 0; i < n; i++) Fire(w);
        }

        private void Fire(World w)
        {
            Vector3 from = _rng.OnCircle() * _rng.Range(55f, 70f); from.y = _rng.Range(1f, 6f);
            Vector3 to = new Vector3(_rng.Range(-30f, 30f), _rng.Range(0.5f, 5f), _rng.Range(-30f, 30f));
            Vector3 dir = (to - from).normalized;
            var cmd = w.Commands;
            var e = BenchmarkScene.Track(cmd.CreateEntity());
            cmd.Add(e, new LocalTransform { Position = from, Rotation = Quaternion.LookRotation(dir, Vector3.up), Scale = Vector3.one });
            var m = new KinematicMoverConfig { useGravity = false, terminalSpeed = 0f, mode = MotionMode.Stop, useLayerMatrix = false, halfExtents = new[] { 0.08f, 0.08f, 0.08f } }.Bake();
            m.Velocity = dir * Speed;
            cmd.Add(e, m);
            cmd.Add(e, new MeshRender { Mesh = RenderAssets.Meshes.Intern(_mesh), Texture = RenderAssets.Textures.Intern(GameAssets.RedProto), Color = Color.white, UvScale = Vector2.one, Layer = RenderLayer.World });
            _live.Add((e, 0f));
            _spawned++;
        }

        public override void Teardown() { _live.Clear(); _spawned = _hits = 0; _spawnAcc = 0f; }
        public override int Alive => _live.Count;
        public override string Extra => $"fired {_spawned} hit {_hits}";
    }

    // ==================================================================== 4. ground

    /// <summary>Fast force-driven characters (ECS CharacterMotor capsules) sprinting over a rough 30k-triangle mesh terrain.</summary>
    public sealed class GroundScenario : Scenario
    {
        public override string Name => "ground";
        public override string Description => "fast ECS CharacterMotor runners (12–16 m/s) on a hilly static mesh collider (~29k triangles)";
        public override int Target(BenchmarkConfig cfg) => cfg.Count(cfg.runners);
        private readonly List<(Entity e, BenchmarkScene.Rng rng, float speed, Vector3 dir, float turnIn)> _runners = new List<(Entity, BenchmarkScene.Rng, float, Vector3, float)>();
        private int _grounded; private float _speed;

        public override void Build(GameObject root, BenchmarkConfig cfg)
        {
            int n = Target(cfg);
            BuildTerrain(root, 200f, 120);
            var mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.6f, 1.6f, 0.6f), 1f);
            var rng = new BenchmarkScene.Rng(31);
            int side = Math.Max(2, (int)MathF.Ceiling(MathF.Sqrt(n)));
            var w = BenchmarkScene.World;
            for (int i = 0; i < n; i++)
            {
                int x = i % side, z = i / side;
                Vector3 p = new Vector3((x - side * 0.5f + 0.5f) * 4f, 0f, (z - side * 0.5f + 0.5f) * 4f);
                p.y = TerrainHeight(p.x, p.z) + 1.6f;
                var e = BenchmarkScene.DynamicBody(p, Quaternion.identity, new ColliderShape { Kind = ShapeKind.Capsule, Radius = 0.3f, Height = 1.6f }, 85f, 0.05f, 0f, mesh, GameAssets.RedProto);
                w.Commands.Add(e, new CharacterMotorConfig { profile = CharacterProfileKind.Enemy, groundMask = 1 << Layers.Walkable }.Bake());
                var r = new BenchmarkScene.Rng((uint)(100 + i));
                _runners.Add((e, r, rng.Range(12f, 16f), r.OnCircle(), r.Range(1f, 3f)));
            }
        }

        public override void Tick(float dt, float elapsed)
        {
            var w = BenchmarkScene.World;
            _grounded = 0; _speed = 0f; int n = 0;
            for (int i = 0; i < _runners.Count; i++)
            {
                var (e, rng, speed, dir, turnIn) = _runners[i];
                if (!w.IsAlive(e) || !w.Has<CharacterMotor>(e)) continue;
                ref var m = ref w.Get<CharacterMotor>(e);
                Vector3 p = w.Get<LocalTransform>(e).Position;
                turnIn -= dt;
                if (MathF.Abs(p.x) > 88f || MathF.Abs(p.z) > 88f) { dir = (-new Vector3(p.x, 0f, p.z)).normalized; turnIn = rng.Range(2f, 4f); }
                else if (turnIn <= 0f) { dir = Quaternion.Euler(0f, rng.Range(-70f, 70f), 0f) * dir; turnIn = rng.Range(1f, 3f); }
                m.Desired = dir * speed;
                if (m.IsGrounded && rng.Next01() < 0.002f) m.PendingJump = 6f; // only from the ground (the legacy runner air-jumped and drifted into the sky)
                _runners[i] = (e, rng, speed, dir, turnIn);
                n++; if (m.IsGrounded) _grounded++; _speed += w.Get<BodyVelocity>(e).Linear.magnitude;
            }
            if (n > 0) _speed /= n;
        }

        public override void Teardown() => _runners.Clear();
        public override int Alive => _runners.Count;
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
