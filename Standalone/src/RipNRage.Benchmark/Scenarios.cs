using System;
using System.Collections.Generic;
using RipNRage.Engine;
using RipNRage.Engine.Destruction;
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
            var col = go.AddComponent<BoxCollider>(); col.size = size; col.isStatic = true;
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

    /// <summary>1000+ one-metre crates dropped in a loose block so they tumble into a heap; an explosion every few seconds keeps them colliding.</summary>
    public sealed class CratesScenario : Scenario
    {
        public override string Name => "crates";
        public override string Description => "colliding rigid-body crates (box/box contacts, stacking, sleeping; a blast every 4 s)";
        public override int Target(BenchmarkConfig cfg) => cfg.Count(cfg.crates);
        private readonly List<RigidBody> _bodies = new List<RigidBody>();
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
                        var go = new GameObject("Crate" + placed);
                        go.transform.SetParent(root.transform, false);
                        go.transform.position = new Vector3((x - side * 0.5f + 0.5f) * spacing + _rng.Range(-0.1f, 0.1f), 1f + layer * spacing + _rng.Range(0f, 0.2f), (z - side * 0.5f + 0.5f) * spacing + _rng.Range(-0.1f, 0.1f));
                        go.transform.rotation = Quaternion.Euler(0f, _rng.Range(0f, 90f), 0f);
                        go.layer = Layers.Default;
                        var mr = go.AddComponent<MeshRenderer>(); mr.mesh = mesh; mr.texture = (placed & 1) == 0 ? GameAssets.OrangeProto : GameAssets.PurpleProto;
                        var col = go.AddComponent<BoxCollider>(); col.size = Vector3.one;
                        var rb = go.AddComponent<RigidBody>();
                        rb.mass = 12f; rb.friction = 0.55f; rb.restitution = 0.05f;
                        _bodies.Add(rb);
                    }
        }

        public override void Tick(float dt, float elapsed)
        {
            if (elapsed < _nextBlast) return;
            _nextBlast = elapsed + 4f;
            Vector3 at = new Vector3(_rng.Range(-6f, 6f), 0.5f, _rng.Range(-6f, 6f));
            Physics.AddExplosionForce(260f, at, 14f, 0.8f);
            _sleeping = 0;
            foreach (var b in _bodies) if (b != null && !b.IsDestroyed && b.isSleeping) _sleeping++;
        }

        public override int Alive => _bodies.Count;
        public override string Extra => $"sleeping(at last blast) {_sleeping}";
    }

    // ==================================================================== 2. ragdolls

    /// <summary>500+ sprite entities sliced into ragdolls (box / capsule pieces on ball constraints, piece/piece and piece/floor contacts).</summary>
    public sealed class RagdollsScenario : Scenario
    {
        public override string Name => "ragdolls";
        public override string Description => "sprite destruction: billboard enemies sliced into jointed ragdoll pieces (a blast every 5 s)";
        public override int Target(BenchmarkConfig cfg) => cfg.Count(cfg.ragdolls);
        private readonly List<GameObject> _entities = new List<GameObject>();
        private readonly List<SpriteRagdoll> _ragdolls = new List<SpriteRagdoll>();
        private GameObject _root;
        private Sprite _sprite;
        private SpriteSkeleton _skeleton;
        private int _sliced;
        private float _nextBlast = 6f;
        private readonly BenchmarkScene.Rng _rng = new BenchmarkScene.Rng(11);
        private int _parts;

        public override void Build(GameObject root, BenchmarkConfig cfg)
        {
            _root = root;
            int n = Target(cfg);
            BenchmarkScene.Arena(root, 70f, 6f);
            _skeleton = SkeletonLibrary.Get("rifleman") ?? DefaultSkeletons.Rifleman();
            _sprite = new DirectionalSpriteSheet(GameAssets.RiflemanIdle, 1, 4, 1f).GetSprite(0, 0);
            int side = Math.Max(2, (int)MathF.Ceiling(MathF.Sqrt(n)));
            float spacing = 2.2f;
            const float S = 2.25f;
            for (int i = 0; i < n; i++)
            {
                int x = i % side, z = i / side;
                var go = new GameObject("Grunt" + i);
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3((x - side * 0.5f + 0.5f) * spacing, 0.5f * S + 0.35f, (z - side * 0.5f + 0.5f) * spacing);
                go.layer = Layers.Default;
                var holder = new GameObject("Sprite"); holder.transform.SetParent(go.transform, false);
                holder.transform.localPosition = new Vector3(0f, -0.35f, 0f);
                holder.transform.localScale = Vector3.one * (S / 5.12f);
                var sr = holder.AddComponent<SpriteRenderer>(); sr.sprite = _sprite; sr.billboard = true; sr.lit = true;
                var capsule = go.AddComponent<CapsuleCollider>(); capsule.radius = 0.15f * S; capsule.height = S - 0.35f; capsule.center = new Vector3(0f, -0.175f, 0f);
                var rb = go.AddComponent<RigidBody>(); rb.mass = 85f; rb.freezeRotation = true;
                _entities.Add(go);
            }
        }

        public override void Tick(float dt, float elapsed)
        {
            // slice the standing entities over the first second or so of the warm-up
            if (_sliced < _entities.Count)
            {
                int perFrame = Math.Max(1, _entities.Count / 45);
                for (int k = 0; k < perFrame && _sliced < _entities.Count; k++, _sliced++) Slice(_entities[_sliced]);
            }
            if (elapsed >= _nextBlast)
            {
                _nextBlast = elapsed + 5f;
                Vector3 at = new Vector3(_rng.Range(-10f, 10f), 0.3f, _rng.Range(-10f, 10f));
                Physics.AddExplosionForce(120f, at, 12f, 0.8f);
            }
            _parts = 0;
            foreach (var r in _ragdolls) if (r != null && !r.IsDestroyed) _parts += r.parts.Count;
        }

        private void Slice(GameObject entity)
        {
            var sr = entity.GetComponentInChildren<SpriteRenderer>();
            var options = RagdollOptions.Default;
            options.lifetime = 100000f; options.fadeOut = false;
            Vector3 dir = _rng.OnCircle(); dir.y = 0.3f;
            options.impulse = dir.normalized * _rng.Range(30f, 90f);
            options.impulsePoint = sr.transform.position + new Vector3(0f, _rng.Range(-0.5f, 0.8f), 0f);
            var rag = SpriteRagdoll.Spawn(_skeleton, sr.sprite, sr.transform.position, sr.transform.lossyScale, options, sr);
            if (rag != null) { rag.transform.SetParent(_root.transform, true); _ragdolls.Add(rag); }
            GameObject.Destroy(entity);
        }

        public override int Alive { get { int n = 0; foreach (var r in _ragdolls) if (r != null && !r.IsDestroyed) n++; return n; } }
        public override string Extra => $"pieces {_parts}";
    }

    // ==================================================================== 3. projectiles

    /// <summary>1000+ swept kinematic projectiles in flight at once, fired from ring emitters into a field of pillars and walls.</summary>
    public sealed class ProjectilesScenario : Scenario
    {
        public override string Name => "projectiles";
        public override string Description => "swept KinematicBody projectiles (Stop mode, trigger volumes) flying through a pillar field";
        public override int Target(BenchmarkConfig cfg) => cfg.Count(cfg.projectiles);
        private GameObject _root;
        private Mesh _mesh;
        private int _target;
        private float _spawnAcc;
        private readonly BenchmarkScene.Rng _rng = new BenchmarkScene.Rng(23);
        public static int Alive_;
        public static int Spawned, Hits;
        private const float Speed = 40f, Life = 3.2f;

        public override void Build(GameObject root, BenchmarkConfig cfg)
        {
            _root = root; _target = Target(cfg);
            Alive_ = 0; Spawned = 0; Hits = 0;
            BenchmarkScene.Arena(root, 160f, 10f);
            var pr = new BenchmarkScene.Rng(5);
            for (int i = 0; i < 80; i++)
                BenchmarkScene.StaticBox(root, "Pillar" + i, new Vector3(pr.Range(-60f, 60f), 0f, pr.Range(-60f, 60f)), new Vector3(pr.Range(0.8f, 2.5f), pr.Range(2f, 7f), pr.Range(0.8f, 2.5f)), GameAssets.PurpleProto);
            _mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.16f, 0.16f, 0.5f), 1f);
        }

        public override void Tick(float dt, float elapsed)
        {
            // keep the target number in the air: each projectile lives Life seconds (or less)
            float rate = _target / Life * 1.15f;
            _spawnAcc += rate * dt;
            int n = (int)_spawnAcc;
            if (Alive_ + n > _target + _target / 10) n = Math.Max(0, _target + _target / 10 - Alive_);
            _spawnAcc -= (int)_spawnAcc;
            for (int i = 0; i < n; i++) Fire();
        }

        private void Fire()
        {
            Vector3 from = _rng.OnCircle() * _rng.Range(55f, 70f); from.y = _rng.Range(1f, 6f);
            Vector3 to = new Vector3(_rng.Range(-30f, 30f), _rng.Range(0.5f, 5f), _rng.Range(-30f, 30f));
            var go = new GameObject("Bullet");
            go.transform.SetParent(_root.transform, false);
            go.transform.position = from;
            go.transform.rotation = Quaternion.LookRotation((to - from).normalized, Vector3.up);
            go.layer = Layers.Projectile;
            var mr = go.AddComponent<MeshRenderer>(); mr.mesh = _mesh; mr.texture = GameAssets.RedProto; mr.castsShadow = false;
            var col = go.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = new Vector3(0.16f, 0.16f, 0.16f);
            var body = go.AddComponent<KinematicBody>();
            body.simulation = SimulationMode.PerFrame; body.motionMode = MotionMode.Stop; body.useGravity = false; body.terminalSpeed = 0f;
            body.halfExtents = new Vector3(0.08f, 0.08f, 0.08f); body.useLayerMatrix = false;
            body.collisionMask = ~(1 << Layers.Projectile); body.collideWithTriggers = false;
            body.velocity = go.transform.forward * Speed;
            var p = go.AddComponent<BenchProjectile>(); p.life = Life;
            Spawned++;
        }

        public override int Alive => Alive_;
        public override string Extra => $"fired {Spawned} hit {Hits}";
    }

    /// <summary>Flies until it hits something or its life runs out.</summary>
    public sealed class BenchProjectile : Behaviour
    {
        public float life = 3f;
        private KinematicBody _body;
        private float _age;
        private bool _dead;

        protected override void Awake()
        {
            ProjectilesScenario.Alive_++;
            _body = GetComponent<KinematicBody>();
            if (_body != null) _body.Collided += OnCollided;
        }

        private void OnCollided(KinematicBody b, Contact c)
        {
            if (_dead) return;
            ProjectilesScenario.Hits++;
            Kill();
        }

        protected override void Update()
        {
            if (_dead) return;
            _age += Time.deltaTime;
            if (_age >= life) Kill();
        }

        private void Kill() { _dead = true; GameObject.Destroy(gameObject); }

        protected override void OnDestroy() { if (_body != null) _body.Collided -= OnCollided; ProjectilesScenario.Alive_--; }
    }

    // ==================================================================== 4. ground

    /// <summary>Fast force-driven characters (CharacterBody capsules) sprinting over a rough 30k-triangle mesh terrain.</summary>
    public sealed class GroundScenario : Scenario
    {
        public override string Name => "ground";
        public override string Description => "fast CharacterBody runners (12–16 m/s) on a hilly MeshCollider terrain (~29k triangles)";
        public override int Target(BenchmarkConfig cfg) => cfg.Count(cfg.runners);
        private readonly List<Runner> _runners = new List<Runner>();
        private int _grounded; private float _speed;

        public override void Build(GameObject root, BenchmarkConfig cfg)
        {
            int n = Target(cfg);
            BuildTerrain(root, 200f, 120);
            var mesh = Mesh.CreateBox(Vector3.zero, new Vector3(0.6f, 1.6f, 0.6f), 1f);
            var rng = new BenchmarkScene.Rng(31);
            int side = Math.Max(2, (int)MathF.Ceiling(MathF.Sqrt(n)));
            for (int i = 0; i < n; i++)
            {
                int x = i % side, z = i / side;
                var go = new GameObject("Runner" + i);
                go.transform.SetParent(root.transform, false);
                Vector3 p = new Vector3((x - side * 0.5f + 0.5f) * 4f, 0f, (z - side * 0.5f + 0.5f) * 4f);
                p.y = TerrainHeight(p.x, p.z) + 1.6f;
                go.transform.position = p;
                go.layer = Layers.Default;
                var mr = go.AddComponent<MeshRenderer>(); mr.mesh = mesh; mr.texture = GameAssets.RedProto;
                var capsule = go.AddComponent<CapsuleCollider>(); capsule.radius = 0.3f; capsule.height = 1.6f;
                var rb = go.AddComponent<RigidBody>(); rb.mass = 85f;
                var motor = go.AddComponent<CharacterBody>(); motor.profile = CharacterProfileKind.Enemy;
                motor.groundMask = 1 << Layers.Walkable;
                var r = go.AddComponent<Runner>(); r.rng = new BenchmarkScene.Rng((uint)(100 + i)); r.speed = rng.Range(12f, 16f);
                _runners.Add(r);
            }
        }

        public override void Tick(float dt, float elapsed)
        {
            _grounded = 0; _speed = 0f; int n = 0;
            foreach (var r in _runners)
            {
                if (r == null || r.IsDestroyed || r.motor == null) continue;
                n++; if (r.motor.isGrounded) _grounded++; _speed += r.motor.velocity.magnitude;
            }
            if (n > 0) _speed /= n;
        }

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
            var mc = go.AddComponent<MeshCollider>(); mc.mesh = mesh;
        }
    }

    /// <summary>Steers a CharacterBody at a constant speed, turning now and then and away from the edge of the terrain.</summary>
    public sealed class Runner : Behaviour
    {
        public BenchmarkScene.Rng rng;
        public float speed = 14f;
        public CharacterBody motor;
        private Vector3 _dir = Vector3.forward;
        private float _turnIn;

        protected override void Awake()
        {
            motor = GetComponent<CharacterBody>();
            _dir = rng.OnCircle(); _turnIn = rng.Range(1f, 3f);
        }

        protected override void Update()
        {
            if (motor == null) return;
            _turnIn -= Time.deltaTime;
            Vector3 p = transform.position;
            if (MathF.Abs(p.x) > 88f || MathF.Abs(p.z) > 88f) { _dir = (-new Vector3(p.x, 0f, p.z)).normalized; _turnIn = rng.Range(2f, 4f); }
            else if (_turnIn <= 0f) { _dir = Quaternion.Euler(0f, rng.Range(-70f, 70f), 0f) * _dir; _turnIn = rng.Range(1f, 3f); }
            motor.Move(_dir * speed);
            if (rng.Next01() < 0.002f) motor.Jump(6f);
        }
    }
}
