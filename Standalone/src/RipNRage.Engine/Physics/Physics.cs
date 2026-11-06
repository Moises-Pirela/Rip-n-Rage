using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    public struct Bounds
    {
        public Vector3 min, max;
        public Bounds(Vector3 min, Vector3 max) { this.min = min; this.max = max; }
        public static Bounds FromCenterExtents(Vector3 center, Vector3 halfExtents) => new Bounds(center - halfExtents, center + halfExtents);
        public Vector3 center => (min + max) * 0.5f;
        public Vector3 size => max - min;
        /// <summary>Half size.</summary>
        public Vector3 extents => (max - min) * 0.5f;
        public bool Intersects(Bounds o) =>
            min.x <= o.max.x && max.x >= o.min.x && min.y <= o.max.y && max.y >= o.min.y && min.z <= o.max.z && max.z >= o.min.z;
        public bool Contains(Vector3 p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
        public Bounds Expanded(float r) => new Bounds(min - new Vector3(r, r, r), max + new Vector3(r, r, r));
        public Bounds Expanded(Vector3 r) => new Bounds(min - r, max + r);
        public Bounds Translated(Vector3 v) => new Bounds(min + v, max + v);
        public void Encapsulate(Vector3 p) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        public void Encapsulate(Bounds b) { min = Vector3.Min(min, b.min); max = Vector3.Max(max, b.max); }
        /// <summary>Smallest box containing this box at its start and end of a translation (the swept volume's bounds).</summary>
        public Bounds Swept(Vector3 motion) => new Bounds(Vector3.Min(min, min + motion), Vector3.Max(max, max + motion));
        public override string ToString() => $"Bounds({min} - {max})";
    }

    /// <summary>Result of a raycast (UnityEngine.RaycastHit subset).</summary>
    public struct RaycastHit
    {
        public Collider collider;
        public Vector3 point;
        public Vector3 normal;
        public float distance;
        public Transform transform => collider?.transform;
    }

    /// <summary>
    /// Base collider. Solids are convex hulls (brushes, boxes) or, for the round shapes, their bounding boxes —
    /// every mover in the engine is an axis-aligned box, which is all a sprite shooter on brush geometry needs.
    /// Colliders are either <see cref="isStatic"/> (level geometry: cached bounds in the broadphase grid, must not
    /// move) or dynamic (anything that moves: characters, projectiles, pickups).
    /// </summary>
    public abstract class Collider : Behaviour
    {
        public bool isTrigger;
        public Vector3 center;
        /// <summary>Surface properties (friction, bounciness); null = the rigid body's values or the solver defaults.</summary>
        public PhysicsMaterial material;
        private bool _isStatic;

        /// <summary>Broadphase bookkeeping (null while not registered).</summary>
        internal PhysicsWorld.Entry broadphase;

        /// <summary>
        /// Static colliders never move: their bounds are cached in the spatial grid when they register. Brush
        /// colliders are static by default; set it on any level prop. Call <see cref="RefreshBounds"/> after moving one.
        /// </summary>
        public bool isStatic
        {
            get => _isStatic;
            set
            {
                if (_isStatic == value) return;
                _isStatic = value;
                if (broadphase != null) Physics.World.Refresh(this);
            }
        }

        protected override void Awake() => Physics.Register(this);
        protected override void OnDestroy() => Physics.Unregister(this);

        public abstract Bounds bounds { get; }
        public Vector3 worldCenter => transform.TransformPoint(center);

        /// <summary>Convex hull of the solid for the fast axis-aligned box sweeps; null when the collider is not a polytope.</summary>
        public virtual ConvexHull hull => null;

        /// <summary>The collider's exact world-space shape (sphere, capsule, oriented box, brush); meshes answer with their bounds.</summary>
        public virtual CollisionShape shape => CollisionShape.FromBounds(bounds);

        /// <summary>Raised when a mover or rigid body starts / keeps / stops overlapping this trigger (or this mover a trigger).</summary>
        public event Action<Collider> TriggerEnter, TriggerStay, TriggerExit;
        internal void RaiseTrigger(Collider other, int phase)
        {
            try
            {
                if (phase == 0) TriggerEnter?.Invoke(other);
                else if (phase == 1) TriggerStay?.Invoke(other);
                else TriggerExit?.Invoke(other);
            }
            catch (Exception e) { Debug.LogError($"Trigger event handler on '{name}' threw: {e}"); }
        }

        /// <summary>Ray/collider intersection in world space.</summary>
        public abstract bool Raycast(Ray ray, float maxDistance, out RaycastHit hit);

        /// <summary>Closest point on the collider surface/volume to a world point.</summary>
        public abstract Vector3 ClosestPoint(Vector3 p);

        public bool IsEnabledForPhysics => enabled && !destroyed && gameObject.activeInHierarchy;

        /// <summary>Re-caches the bounds of a static collider after it was moved or resized.</summary>
        public virtual void RefreshBounds() { if (broadphase != null) Physics.World.Refresh(this); }

        private RigidBody _body;
        private bool _bodyLooked;
        /// <summary>The rigid body this collider belongs to (null for static geometry and kinematic movers).</summary>
        public RigidBody attachedRigidbody
        {
            get
            {
                if (!_bodyLooked || (_body != null && _body.destroyed)) { _body = gameObject.GetComponent<RigidBody>(); _bodyLooked = true; }
                return _body;
            }
        }
        internal void InvalidateBody() => _bodyLooked = false;
    }

    public sealed class BoxCollider : Collider
    {
        public Vector3 size = Vector3.one;

        private ConvexHull _hull;
        private Matrix4x4 _hullMatrix;
        private Vector3 _hullCenter, _hullSize;

        public override Bounds bounds
        {
            get
            {
                // AABB of the oriented box: |R| applied to the half extents (exact, no corner loop)
                Matrix4x4 m = transform.localToWorldMatrix;
                Vector3 h = size * 0.5f;
                Vector3 c = m.MultiplyPoint3x4(center);
                Vector3 e = new Vector3(
                    MathF.Abs(m.m00) * h.x + MathF.Abs(m.m01) * h.y + MathF.Abs(m.m02) * h.z,
                    MathF.Abs(m.m10) * h.x + MathF.Abs(m.m11) * h.y + MathF.Abs(m.m12) * h.z,
                    MathF.Abs(m.m20) * h.x + MathF.Abs(m.m21) * h.y + MathF.Abs(m.m22) * h.z);
                return new Bounds(c - e, c + e);
            }
        }

        /// <summary>True when the transform rotates the box (its bounds are then only a conservative envelope).</summary>
        public bool isRotated
        {
            get
            {
                Matrix4x4 m = transform.localToWorldMatrix;
                const float eps = 1E-04f;
                return MathF.Abs(m.m01) > eps || MathF.Abs(m.m02) > eps || MathF.Abs(m.m10) > eps ||
                       MathF.Abs(m.m12) > eps || MathF.Abs(m.m20) > eps || MathF.Abs(m.m21) > eps;
            }
        }

        /// <summary>Exact hull for rotated boxes (cached until the transform or the shape changes); null when axis-aligned.</summary>
        public override ConvexHull hull
        {
            get
            {
                if (!isRotated) return null;
                Matrix4x4 m = transform.localToWorldMatrix;
                if (_hull == null || !SameMatrix(m, _hullMatrix) || _hullCenter != center || _hullSize != size)
                {
                    _hull = ConvexHull.FromBox(m, center, size);
                    _hullMatrix = m; _hullCenter = center; _hullSize = size;
                }
                return _hull;
            }
        }

        private ConvexShape _shape;
        private Matrix4x4 _shapeMatrix;
        private Vector3 _shapeCenter, _shapeSize;

        /// <summary>World-space polytope of the box (cached until the transform or the shape changes) for the rigid-body narrow phase.</summary>
        public ConvexShape Shape
        {
            get
            {
                Matrix4x4 m = transform.localToWorldMatrix;
                if (_shape == null || !SameMatrix(m, _shapeMatrix) || _shapeCenter != center || _shapeSize != size)
                {
                    _shape ??= ConvexShape.CreateBox();
                    Vector3 scale = transform.lossyScale;
                    _shape.SetBox(m.MultiplyPoint3x4(center), Vector3.Max(Vector3.Scale(size, new Vector3(MathF.Abs(scale.x), MathF.Abs(scale.y), MathF.Abs(scale.z))) * 0.5f, new Vector3(1E-04f, 1E-04f, 1E-04f)), transform.rotation);
                    _shapeMatrix = m; _shapeCenter = center; _shapeSize = size;
                }
                return _shape;
            }
        }

        private static bool SameMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            const float eps = 1E-06f;
            return MathF.Abs(a.m00 - b.m00) < eps && MathF.Abs(a.m01 - b.m01) < eps && MathF.Abs(a.m02 - b.m02) < eps && MathF.Abs(a.m03 - b.m03) < eps &&
                   MathF.Abs(a.m10 - b.m10) < eps && MathF.Abs(a.m11 - b.m11) < eps && MathF.Abs(a.m12 - b.m12) < eps && MathF.Abs(a.m13 - b.m13) < eps &&
                   MathF.Abs(a.m20 - b.m20) < eps && MathF.Abs(a.m21 - b.m21) < eps && MathF.Abs(a.m22 - b.m22) < eps && MathF.Abs(a.m23 - b.m23) < eps;
        }

        public override CollisionShape shape
        {
            get
            {
                Matrix4x4 m = transform.localToWorldMatrix;
                Vector3 scale = transform.lossyScale;
                Vector3 h = Vector3.Scale(size, new Vector3(MathF.Abs(scale.x), MathF.Abs(scale.y), MathF.Abs(scale.z))) * 0.5f;
                return CollisionShape.Box(m.MultiplyPoint3x4(center), transform.rotation, h);
            }
        }

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit)
        {
            var h = hull;
            if (h == null) return Physics.RayAabb(ray, bounds, maxDistance, this, out hit);
            hit = default;
            if (!h.Raycast(ray, maxDistance, out float t, out Vector3 n)) return false;
            hit = new RaycastHit { collider = this, point = ray.GetPoint(t), normal = n, distance = t };
            return true;
        }

        public override Vector3 ClosestPoint(Vector3 p)
        {
            var b = bounds;
            return new Vector3(Mathf.Clamp(p.x, b.min.x, b.max.x), Mathf.Clamp(p.y, b.min.y, b.max.y), Mathf.Clamp(p.z, b.min.z, b.max.z));
        }
    }

    public sealed class SphereCollider : Collider
    {
        public float radius = 0.5f;

        public float worldRadius
        {
            get { Vector3 s = transform.lossyScale; return radius * Mathf.Max(s.x, Mathf.Max(s.y, s.z)); }
        }

        public override Bounds bounds { get { float r = worldRadius; Vector3 c = worldCenter; return new Bounds(c - new Vector3(r, r, r), c + new Vector3(r, r, r)); } }
        public override CollisionShape shape => CollisionShape.Sphere(worldCenter, worldRadius);

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit) => Physics.RaySphere(ray, worldCenter, worldRadius, maxDistance, this, out hit);

        public override Vector3 ClosestPoint(Vector3 p)
        {
            Vector3 c = worldCenter; float r = worldRadius;
            Vector3 d = p - c;
            if (d.sqrMagnitude <= r * r) return p;
            return c + d.normalized * r;
        }
    }

    /// <summary>Capsule along the local Y axis (enemies / player): a segment of <see cref="height"/> − 2·<see cref="radius"/> rounded by <see cref="radius"/>.</summary>
    public sealed class CapsuleCollider : Collider
    {
        public float radius = 0.5f;
        public float height = 2f;

        public float worldRadius { get { Vector3 s = transform.lossyScale; return radius * Mathf.Max(MathF.Abs(s.x), MathF.Abs(s.z)); } }
        /// <summary>Half length of the core segment in world units.</summary>
        public float worldHalfHeight { get { Vector3 s = transform.lossyScale; return Mathf.Max(height * MathF.Abs(s.y) * 0.5f - worldRadius, 0f); } }

        public override CollisionShape shape => CollisionShape.Capsule(worldCenter, transform.rotation, worldRadius, worldHalfHeight);
        public override Bounds bounds => shape.bounds;

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit)
        {
            var s = shape;
            return Physics.RayCapsule(ray, s.center - s.Axis * s.halfHeight, s.center + s.Axis * s.halfHeight, s.radius, maxDistance, this, out hit);
        }

        public override Vector3 ClosestPoint(Vector3 p)
        {
            var s = shape;
            Vector3 a = s.center - s.Axis * s.halfHeight, b = s.center + s.Axis * s.halfHeight;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1E-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            Vector3 q = a + ab * t;
            Vector3 d = p - q;
            if (d.sqrMagnitude <= s.radius * s.radius) return p;
            return q + d.normalized * s.radius;
        }
    }

    /// <summary>
    /// Which colliders a query may see. <see cref="self"/> (and everything under its transform) is never returned;
    /// with <see cref="useLayerMatrix"/> the layer collision matrix row of <see cref="self"/> is applied on top of
    /// <see cref="layerMask"/>. <see cref="predicate"/> is an optional final veto (return false to skip a collider).
    /// </summary>
    public struct QueryFilter
    {
        public int layerMask;
        public Collider self;
        public Transform ignore;
        public bool useLayerMatrix;
        public bool includeTriggers;
        public Func<Collider, bool> predicate;

        public static QueryFilter Everything => new QueryFilter { layerMask = ~0 };
        public static QueryFilter Solids(int layerMask = ~0) => new QueryFilter { layerMask = layerMask };
        /// <summary>What a collider-bearing mover should collide with: the layer matrix row of its own collider, never itself.</summary>
        public static QueryFilter For(Collider self, int layerMask = ~0) => new QueryFilter { layerMask = layerMask, self = self, useLayerMatrix = true };

        public bool Accepts(Collider c)
        {
            if (!c.IsEnabledForPhysics) return false;
            if (c.isTrigger && !includeTriggers) return false;
            if (((1 << c.gameObject.layer) & layerMask) == 0) return false;
            if (self != null)
            {
                if (c == self || c.transform.IsChildOf(self.transform)) return false;
                if (useLayerMatrix && !Physics.LayersCollide(self, c)) return false;
            }
            if (ignore != null && c.transform.IsChildOf(ignore)) return false;
            if (predicate != null && !predicate(c)) return false;
            return true;
        }
    }

    /// <summary>Result of a shape sweep: the first collider the moving box touches.</summary>
    public struct ShapeHit
    {
        public Collider collider;
        /// <summary>Fraction of the motion completed before the contact (0..1); 1 when nothing was hit.</summary>
        public float fraction;
        /// <summary>Surface normal at the contact (points away from the collider).</summary>
        public Vector3 normal;
        /// <summary>Point on the surface that was touched.</summary>
        public Vector3 point;
        /// <summary>The box already overlapped the collider at the start of the sweep (fraction 0, normal = the way out).</summary>
        public bool startSolid;
        /// <summary>The plane that was hit (world space).</summary>
        public Plane plane;
        public bool hit => collider != null;
    }

    /// <summary>
    /// Scene queries (UnityEngine.Physics subset) plus the engine's own movement API, all served by <see cref="World"/>.
    /// Movers are axis-aligned boxes given as a centre and half extents.
    /// </summary>
    public static class Physics
    {
        public static Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        public const int DefaultRaycastLayers = ~0;
        /// <summary>Gap a sweep leaves between a mover and the surface it stops at (metres). Tunable at runtime through <see cref="defaultSkin"/>.</summary>
        public const float DefaultSkin = 0.01f;
        /// <summary>The skin used when a query passes 0 / omits it (tunables.cfg [collision] skin).</summary>
        public static float defaultSkin = DefaultSkin;
        private static float Skin(float skin) => skin > 0f ? skin : defaultSkin;
        /// <summary>Cap for "infinite" shape casts.</summary>
        public const float MaxCastDistance = 1000f;

        public static PhysicsWorld World { get; } = new PhysicsWorld();
        internal static IReadOnlyList<Collider> All => World.Colliders;

        internal static void Register(Collider c) => World.Add(c);
        internal static void Unregister(Collider c) => World.Remove(c);

        /// <summary>Movers are the bodies checked against triggers every step (characters, projectiles).</summary>
        public static void RegisterMover(Collider c) => World.AddMover(c);
        public static void UnregisterMover(Collider c) => World.RemoveMover(c);

        /// <summary>Called by the application once per frame after the scene update: per-frame bodies, then trigger enter/exit.</summary>
        public static void Step() => World.StepFrame(Time.deltaTime);
        /// <summary>Called by the scene after the FixedUpdate callbacks of every fixed step: fixed-step bodies.</summary>
        public static void FixedStep(float dt) => World.StepFixed(dt);

        // ---- layer collision matrix (Unity's Physics settings) ----
        private static readonly uint[] CollisionMatrix = InitMatrix();

        private static uint[] InitMatrix()
        {
            var m = new uint[32];
            for (int i = 0; i < 32; i++) m[i] = 0xFFFFFFFFu;
            return m;
        }

        /// <summary>Sets the whole matrix: row i is the bitmask of layers that layer i collides with.</summary>
        public static void SetCollisionMatrix(uint[] rows)
        {
            for (int i = 0; i < 32 && i < rows.Length; i++) CollisionMatrix[i] = rows[i];
        }

        public static void IgnoreLayerCollision(int layer1, int layer2, bool ignore = true)
        {
            if (ignore) { CollisionMatrix[layer1] &= ~(1u << layer2); CollisionMatrix[layer2] &= ~(1u << layer1); }
            else { CollisionMatrix[layer1] |= 1u << layer2; CollisionMatrix[layer2] |= 1u << layer1; }
        }

        public static bool GetIgnoreLayerCollision(int layer1, int layer2) => (CollisionMatrix[layer1] & (1u << layer2)) == 0;

        /// <summary>True when the two colliders' layers are allowed to interact.</summary>
        public static bool LayersCollide(Collider a, Collider b) => !GetIgnoreLayerCollision(a.gameObject.layer, b.gameObject.layer);

        /// <summary>Bitmask of the layers that <paramref name="layer"/> collides with.</summary>
        public static int CollisionMaskOf(int layer) => unchecked((int)CollisionMatrix[layer & 31]);

        // ---- ray queries ----

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers, bool includeTriggers = false)
        {
            var filter = new QueryFilter { layerMask = layerMask, includeTriggers = includeTriggers };
            return World.Raycast(new Ray(origin, direction), maxDistance, filter, out hit);
        }

        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            Raycast(origin, direction, out _, maxDistance, layerMask);

        public static bool Raycast(Ray ray, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            Raycast(ray.origin, ray.direction, out hit, maxDistance, layerMask);

        public static bool Raycast(Ray ray, float maxDistance, QueryFilter filter, out RaycastHit hit) => World.Raycast(ray, maxDistance, filter, out hit);

        // ---- shape casts (all continuous: the exact time of impact along the whole motion) ----

        private static float CastDistance(float maxDistance) => float.IsInfinity(maxDistance) || maxDistance > MaxCastDistance ? MaxCastDistance : maxDistance;

        /// <summary>Sweeps any convex shape; <paramref name="hit"/> is the first solid it touches.</summary>
        public static bool ShapeCast(in CollisionShape shape, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers)
        {
            hit = default;
            if (direction.sqrMagnitude < 1E-12f) return false;
            float dist = CastDistance(maxDistance);
            if (!World.Sweep(in shape, direction.normalized * dist, QueryFilter.Solids(layerMask), defaultSkin, out ShapeHit sh)) return false;
            hit = new RaycastHit { collider = sh.collider, point = sh.point, normal = sh.normal, distance = sh.fraction * dist };
            return true;
        }

        public static bool ShapeCast(in CollisionShape shape, Vector3 direction, QueryFilter filter, out RaycastHit hit, float maxDistance = float.PositiveInfinity)
        {
            hit = default;
            if (direction.sqrMagnitude < 1E-12f) return false;
            float dist = CastDistance(maxDistance);
            if (!World.Sweep(in shape, direction.normalized * dist, filter, defaultSkin, out ShapeHit sh)) return false;
            hit = new RaycastHit { collider = sh.collider, point = sh.point, normal = sh.normal, distance = sh.fraction * dist };
            return true;
        }

        /// <summary>Sweeps an axis-aligned box (Unity's BoxCast for unrotated boxes).</summary>
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            ShapeCast(CollisionShape.AxisAlignedBox(center, halfExtents), direction, out hit, maxDistance, layerMask);

        /// <summary>Sweeps an oriented box.</summary>
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            ShapeCast(CollisionShape.Box(center, orientation, halfExtents), direction, out hit, maxDistance, layerMask);

        /// <summary>Exact sphere cast.</summary>
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            ShapeCast(CollisionShape.Sphere(origin, radius), direction, out hit, maxDistance, layerMask);

        /// <summary>Exact capsule cast; <paramref name="point1"/> / <paramref name="point2"/> are the centres of the end spheres (Unity's CapsuleCast).</summary>
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            ShapeCast(CapsuleShape(point1, point2, radius), direction, out hit, maxDistance, layerMask);

        /// <summary>Capsule shape from its two end-sphere centres.</summary>
        public static CollisionShape CapsuleShape(Vector3 point1, Vector3 point2, float radius)
        {
            Vector3 axis = point2 - point1;
            float len = axis.magnitude;
            Quaternion rot = len > 1E-06f ? Quaternion.FromToRotation(Vector3.up, axis / len) : Quaternion.identity;
            return CollisionShape.Capsule((point1 + point2) * 0.5f, rot, radius, len * 0.5f);
        }

        /// <summary>Every solid the sphere overlaps at the origin (distance 0) or that its swept bounding box would run into.</summary>
        public static RaycastHit[] SphereCastAll(Vector3 origin, float radius, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers)
        {
            var list = new List<RaycastHit>();
            var ray = new Ray(origin, direction.sqrMagnitude > 0 ? direction : Vector3.forward);
            float dist = CastDistance(maxDistance);
            Bounds region = new Bounds(origin, origin); region.Encapsulate(ray.GetPoint(dist)); region = region.Expanded(radius);
            var candidates = World.RentList();
            World.QueryBounds(region, QueryFilter.Solids(layerMask), candidates);
            foreach (var c in candidates)
            {
                var b = c.bounds.Expanded(radius);
                if (b.Contains(origin)) list.Add(new RaycastHit { collider = c, point = c.ClosestPoint(origin), normal = Vector3.up, distance = 0f });
                else if (RayAabb(ray, b, dist, c, out var h)) list.Add(h);
            }
            World.ReturnList(candidates);
            return list.ToArray();
        }

        // ---- overlap queries (exact shapes) ----

        /// <summary>Colliders (triggers included) an exact shape overlaps.</summary>
        public static int OverlapShape(in CollisionShape shape, List<Collider> results, QueryFilter filter) => World.Overlap(in shape, filter, results);

        public static Collider[] OverlapSphere(Vector3 position, float radius, int layerMask = DefaultRaycastLayers)
        {
            var list = new List<Collider>();
            World.Overlap(CollisionShape.Sphere(position, radius), new QueryFilter { layerMask = layerMask, includeTriggers = true }, list);
            return list.ToArray();
        }

        public static bool CheckSphere(Vector3 position, float radius, int layerMask = DefaultRaycastLayers) =>
            World.Overlap(CollisionShape.Sphere(position, radius), QueryFilter.Solids(layerMask), null) > 0;

        /// <summary>Solids an axis-aligned box overlaps (exact against hulls).</summary>
        public static int OverlapBox(Vector3 center, Vector3 halfExtents, List<Collider> results, QueryFilter filter) => World.Overlap(center, halfExtents, filter, results);

        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents, int layerMask = DefaultRaycastLayers)
        {
            var list = new List<Collider>();
            World.Overlap(center, halfExtents, QueryFilter.Solids(layerMask), list);
            return list.ToArray();
        }

        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion orientation, int layerMask = DefaultRaycastLayers)
        {
            var list = new List<Collider>();
            World.Overlap(CollisionShape.Box(center, orientation, halfExtents), QueryFilter.Solids(layerMask), list);
            return list.ToArray();
        }

        public static Collider[] OverlapCapsule(Vector3 point1, Vector3 point2, float radius, int layerMask = DefaultRaycastLayers)
        {
            var list = new List<Collider>();
            World.Overlap(CapsuleShape(point1, point2, radius), QueryFilter.Solids(layerMask), list);
            return list.ToArray();
        }

        public static bool CheckBox(Vector3 center, Vector3 halfExtents, int layerMask = DefaultRaycastLayers) => World.Overlap(center, halfExtents, QueryFilter.Solids(layerMask), null) > 0;
        public static bool CheckBox(Vector3 center, Vector3 halfExtents, QueryFilter filter) => World.Overlap(center, halfExtents, filter, null) > 0;
        public static bool CheckCapsule(Vector3 point1, Vector3 point2, float radius, int layerMask = DefaultRaycastLayers) =>
            World.Overlap(CapsuleShape(point1, point2, radius), QueryFilter.Solids(layerMask), null) > 0;
        public static bool CheckShape(in CollisionShape shape, QueryFilter filter) => World.Overlap(in shape, filter, null) > 0;

        /// <summary>Exact distance / closest points between two shapes (negative distance = overlapping).</summary>
        public static bool ComputeDistance(in CollisionShape a, in CollisionShape b, out DistanceResult result) => ConvexDistance.Distance(in a, in b, out result);

        /// <summary>Direction and distance that moves <paramref name="shape"/> out of <paramref name="collider"/> (Unity's ComputePenetration).</summary>
        public static bool ComputePenetration(in CollisionShape shape, Collider collider, out Vector3 direction, out float distance) =>
            PhysicsWorld.Penetration(collider, in shape, out direction, out distance);

        // ---- movement API ----

        /// <summary>Sweeps a box along <paramref name="motion"/> and reports the first contact (no sliding).</summary>
        public static bool Sweep(Vector3 center, Vector3 halfExtents, Vector3 motion, QueryFilter filter, out ShapeHit hit, float skin = 0f) =>
            World.Sweep(center, halfExtents, motion, filter, Skin(skin), out hit);

        /// <summary>Sweeps any convex shape along <paramref name="motion"/> and reports the first contact (no sliding).</summary>
        public static bool Sweep(in CollisionShape shape, Vector3 motion, QueryFilter filter, out ShapeHit hit, float skin = 0f) =>
            World.Sweep(in shape, motion, filter, Skin(skin), out hit);

        /// <summary>
        /// Moves a box along <paramref name="motion"/> until it touches something; returns the centre it ends at and the
        /// contact in <paramref name="hit"/> (fraction 1 / no collider when the whole motion was free).
        /// </summary>
        public static Vector3 MoveAndCollide(Vector3 center, Vector3 halfExtents, Vector3 motion, QueryFilter filter, out ShapeHit hit, float skin = 0f)
        {
            World.Sweep(center, halfExtents, motion, filter, Skin(skin), out hit);
            return center + motion * hit.fraction;
        }

        public static Vector3 MoveAndCollide(in CollisionShape shape, Vector3 motion, QueryFilter filter, out ShapeHit hit, float skin = 0f)
        {
            World.Sweep(in shape, motion, filter, Skin(skin), out hit);
            return shape.center + motion * hit.fraction;
        }

        /// <summary>
        /// Moves a box along <paramref name="motion"/>, sliding along whatever it touches (collide-and-slide with
        /// floor / wall / ceiling classification, step-up and ground snapping — see <see cref="MoveSettings"/>).
        /// The <paramref name="result"/> instance is reused when given (no allocation per call).
        /// </summary>
        public static MoveResult MoveAndSlide(Vector3 center, Vector3 halfExtents, Vector3 motion, MoveSettings settings, MoveResult result = null)
        {
            result ??= new MoveResult();
            KinematicSolver.MoveAndSlide(World, center, halfExtents, motion, settings, result);
            return result;
        }

        public static MoveResult MoveAndSlide(Bounds box, Vector3 motion, MoveSettings settings, MoveResult result = null) =>
            MoveAndSlide(box.center, box.extents, motion, settings, result);

        /// <summary>Collide-and-slide for any convex mover (sphere, capsule, box).</summary>
        public static MoveResult MoveAndSlide(in CollisionShape shape, Vector3 motion, MoveSettings settings, MoveResult result = null)
        {
            result ??= new MoveResult();
            KinematicSolver.MoveAndSlide(World, in shape, motion, settings, result);
            return result;
        }

        /// <summary>
        /// Lets a kinematic mover shove the rigid bodies it ran into during a move: each body touched gets its velocity
        /// along the push brought up to the mover's speed into it, fully when it weighs less than
        /// <paramref name="pushMass"/> kilograms, proportionally less when heavier (0 = never push).
        /// </summary>
        public static void PushRigidBodies(MoveResult result, Vector3 moverVelocity, float pushMass)
        {
            if (pushMass <= 0f || result == null) return;
            var contacts = result.contacts;
            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                if (c.kind == ContactKind.Floor) continue; // standing on a body is not a push
                var rb = c.collider != null ? c.collider.attachedRigidbody : null;
                if (rb == null || rb.destroyed || rb.isKinematic || rb.isSleeping && moverVelocity.sqrMagnitude < 0.01f) continue;
                Vector3 dir = -c.normal; // into the body
                float into = Vector3.Dot(moverVelocity, dir);
                if (into <= 0.05f) continue;
                float current = Vector3.Dot(rb.GetPointVelocity(c.point), dir);
                float dv = (into - current) * MathF.Min(1f, pushMass / MathF.Max(rb.mass, 1E-03f));
                if (dv <= 0f) continue;
                rb.AddImpulseAtPosition(dir * (dv * rb.mass), c.point);
            }
        }

        /// <summary>All rigid bodies in the world.</summary>
        public static IReadOnlyList<RigidBody> RigidBodies => World.RigidBodies;

        /// <summary>Explosion impulse on every rigid body within <paramref name="radius"/> (linear falloff, see <see cref="RigidBody.AddExplosionForce"/>).</summary>
        public static void AddExplosionForce(float impulse, Vector3 origin, float radius, float upwardsModifier = 0f, int layerMask = DefaultRaycastLayers)
        {
            var bodies = World.RigidBodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b.destroyed || !b.isActiveAndEnabled || ((1 << b.gameObject.layer) & layerMask) == 0) continue;
                if ((b.worldCenterOfMass - origin).sqrMagnitude > (radius + 5f) * (radius + 5f)) continue;
                b.AddExplosionForce(impulse, origin, radius, upwardsModifier);
            }
        }

        /// <summary>Pushes a box out of every solid it overlaps; returns the distance it had to move.</summary>
        public static float Depenetrate(ref Vector3 center, Vector3 halfExtents, QueryFilter filter, float skin = 0f)
        {
            var candidates = World.RentList();
            skin = Skin(skin);
            World.QueryBounds(Bounds.FromCenterExtents(center, halfExtents).Expanded(skin + 0.05f), filter, candidates);
            float moved = KinematicSolver.Depenetrate(candidates, ref center, halfExtents, skin, out _);
            World.ReturnList(candidates);
            return moved;
        }

        /// <summary>Pushes any convex shape out of every solid it overlaps; returns the distance it had to move.</summary>
        public static float Depenetrate(ref CollisionShape shape, QueryFilter filter, float skin = 0f)
        {
            var candidates = World.RentList();
            skin = Skin(skin);
            World.QueryBounds(shape.bounds.Expanded(skin + 0.05f), filter, candidates);
            float moved = KinematicSolver.Depenetrate(candidates, ref shape, skin, out _);
            World.ReturnList(candidates);
            return moved;
        }

        // ---- primitives ----

        public static bool RayAabb(Ray ray, Bounds b, float maxDistance, Collider c, out RaycastHit hit)
        {
            hit = default;
            float tmin = 0f, tmax = maxDistance;
            int hitAxis = -1; float hitSign = 0f;
            for (int i = 0; i < 3; i++)
            {
                float o = ray.origin[i], d = ray.direction[i], mn = b.min[i], mx = b.max[i];
                if (Mathf.Abs(d) < 1E-08f)
                {
                    if (o < mn || o > mx) return false;
                }
                else
                {
                    float inv = 1f / d;
                    float t1 = (mn - o) * inv, t2 = (mx - o) * inv;
                    float sign = -1f;
                    if (t1 > t2) { var t = t1; t1 = t2; t2 = t; sign = 1f; }
                    if (t1 > tmin) { tmin = t1; hitAxis = i; hitSign = sign; }
                    if (t2 < tmax) tmax = t2;
                    if (tmin > tmax) return false;
                }
            }
            if (hitAxis < 0)
            {
                // origin inside the box
                hit = new RaycastHit { collider = c, point = ray.origin, normal = -ray.direction, distance = 0f };
                return true;
            }
            Vector3 n = Vector3.zero; n[hitAxis] = hitSign;
            hit = new RaycastHit { collider = c, point = ray.GetPoint(tmin), normal = n, distance = tmin };
            return true;
        }

        /// <summary>Ray against a capsule with core segment a-b: the cylinder body and the two end spheres.</summary>
        public static bool RayCapsule(Ray ray, Vector3 a, Vector3 b, float radius, float maxDistance, Collider c, out RaycastHit hit)
        {
            hit = default;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1E-12f) return RaySphere(ray, a, radius, maxDistance, c, out hit);
            Vector3 axis = ab / MathF.Sqrt(len2);
            // infinite cylinder: components perpendicular to the axis
            Vector3 m = ray.origin - a;
            Vector3 dPerp = ray.direction - axis * Vector3.Dot(ray.direction, axis);
            Vector3 mPerp = m - axis * Vector3.Dot(m, axis);
            float best = maxDistance; bool found = false; Vector3 bestPoint = Vector3.zero, bestNormal = Vector3.up;
            float qa = dPerp.sqrMagnitude, qb = 2f * Vector3.Dot(dPerp, mPerp), qc = mPerp.sqrMagnitude - radius * radius;
            if (qa > 1E-12f)
            {
                float disc = qb * qb - 4f * qa * qc;
                if (disc >= 0f)
                {
                    float t = (-qb - MathF.Sqrt(disc)) / (2f * qa);
                    if (t < 0f && qc <= 0f) t = 0f; // origin inside the cylinder
                    if (t >= 0f && t < best)
                    {
                        Vector3 p = ray.GetPoint(t);
                        float along = Vector3.Dot(p - a, axis);
                        if (along >= 0f && along * along <= len2) { best = t; found = true; bestPoint = p; bestNormal = (p - (a + axis * along)).normalized; }
                    }
                }
            }
            if (RaySphere(ray, a, radius, best, null, out var ha) && ha.distance < best) { best = ha.distance; found = true; bestPoint = ha.point; bestNormal = ha.normal; }
            if (RaySphere(ray, b, radius, best, null, out var hb) && hb.distance < best) { best = hb.distance; found = true; bestPoint = hb.point; bestNormal = hb.normal; }
            if (!found) return false;
            hit = new RaycastHit { collider = c, point = bestPoint, normal = bestNormal, distance = best };
            return true;
        }

        internal static bool RaySphere(Ray ray, Vector3 center, float radius, float maxDistance, Collider c, out RaycastHit hit)
        {
            hit = default;
            Vector3 m = ray.origin - center;
            float bq = Vector3.Dot(m, ray.direction);
            float cq = Vector3.Dot(m, m) - radius * radius;
            if (cq > 0f && bq > 0f) return false;
            float disc = bq * bq - cq;
            if (disc < 0f) return false;
            float t = -bq - Mathf.Sqrt(disc);
            if (t < 0f) t = 0f;
            if (t > maxDistance) return false;
            Vector3 p = ray.GetPoint(t);
            hit = new RaycastHit { collider = c, point = p, normal = (p - center).normalized, distance = t };
            return true;
        }
    }

    /// <summary>Implement on a Behaviour to receive trigger callbacks (replaces Unity's magic OnTriggerEnter methods).</summary>
    public interface ITriggerListener
    {
        void OnTriggerEnter(Collider other);
        void OnTriggerExit(Collider other);
    }

    /// <summary>Implement on a Behaviour to be told every frame an overlap persists (Unity's OnTriggerStay).</summary>
    public interface ITriggerStayListener
    {
        void OnTriggerStay(Collider other);
    }
}
