using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    public struct Bounds
    {
        public Vector3 min, max;
        public Bounds(Vector3 min, Vector3 max) { this.min = min; this.max = max; }
        public Vector3 center => (min + max) * 0.5f;
        public Vector3 size => max - min;
        public bool Intersects(Bounds o) =>
            min.x <= o.max.x && max.x >= o.min.x && min.y <= o.max.y && max.y >= o.min.y && min.z <= o.max.z && max.z >= o.min.z;
        public bool Contains(Vector3 p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
        public Bounds Expanded(float r) => new Bounds(min - new Vector3(r, r, r), max + new Vector3(r, r, r));
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
    /// Base collider. The simplified physics uses world-space AABBs for boxes and spheres for
    /// characters/enemies — enough for a sprite-based boomer shooter on brush geometry.
    /// </summary>
    public abstract class Collider : Behaviour
    {
        public bool isTrigger;
        public Vector3 center;

        protected override void Awake() => Physics.Register(this);
        protected override void OnDestroy() => Physics.Unregister(this);

        public abstract Bounds bounds { get; }
        public Vector3 worldCenter => transform.TransformPoint(center);

        /// <summary>Ray/collider intersection in world space.</summary>
        public abstract bool Raycast(Ray ray, float maxDistance, out RaycastHit hit);

        /// <summary>Closest point on the collider surface/volume to a world point.</summary>
        public abstract Vector3 ClosestPoint(Vector3 p);

        public bool IsEnabledForPhysics => enabled && !destroyed && gameObject.activeInHierarchy;
    }

    public sealed class BoxCollider : Collider
    {
        public Vector3 size = Vector3.one;

        public override Bounds bounds
        {
            get
            {
                // world AABB of the (possibly rotated) box
                Matrix4x4 m = transform.localToWorldMatrix;
                Vector3 h = size * 0.5f;
                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = center + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                    Vector3 w = m.MultiplyPoint3x4(c);
                    min = Vector3.Min(min, w); max = Vector3.Max(max, w);
                }
                return new Bounds(min, max);
            }
        }

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit) => Physics.RayAabb(ray, bounds, maxDistance, this, out hit);

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

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit) => Physics.RaySphere(ray, worldCenter, worldRadius, maxDistance, this, out hit);

        public override Vector3 ClosestPoint(Vector3 p)
        {
            Vector3 c = worldCenter; float r = worldRadius;
            Vector3 d = p - c;
            if (d.sqrMagnitude <= r * r) return p;
            return c + d.normalized * r;
        }
    }

    /// <summary>Vertical capsule (enemies / player). Raycasts treat it as its bounding box with rounded ends approximated by a sphere test.</summary>
    public sealed class CapsuleCollider : Collider
    {
        public float radius = 0.5f;
        public float height = 2f;

        public override Bounds bounds
        {
            get
            {
                Vector3 s = transform.lossyScale;
                float r = radius * Mathf.Max(s.x, s.z);
                float h = Mathf.Max(height * s.y, r * 2f);
                Vector3 c = worldCenter;
                return new Bounds(c - new Vector3(r, h * 0.5f, r), c + new Vector3(r, h * 0.5f, r));
            }
        }

        public override bool Raycast(Ray ray, float maxDistance, out RaycastHit hit) => Physics.RayAabb(ray, bounds, maxDistance, this, out hit);

        public override Vector3 ClosestPoint(Vector3 p)
        {
            var b = bounds;
            return new Vector3(Mathf.Clamp(p.x, b.min.x, b.max.x), Mathf.Clamp(p.y, b.min.y, b.max.y), Mathf.Clamp(p.z, b.min.z, b.max.z));
        }
    }

    /// <summary>Static scene queries (UnityEngine.Physics subset) over all registered colliders.</summary>
    public static class Physics
    {
        public static Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        public const int DefaultRaycastLayers = ~0;

        private static readonly List<Collider> Colliders = new List<Collider>();
        private static readonly List<Collider> Movers = new List<Collider>();
        internal static IReadOnlyList<Collider> All => Colliders;

        internal static void Register(Collider c) { if (!Colliders.Contains(c)) Colliders.Add(c); }
        internal static void Unregister(Collider c) { Colliders.Remove(c); Movers.Remove(c); }

        /// <summary>Movers are the bodies checked against triggers every fixed step (characters, projectiles).</summary>
        public static void RegisterMover(Collider c) { if (!Movers.Contains(c)) Movers.Add(c); }
        public static void UnregisterMover(Collider c) => Movers.Remove(c);

        /// <summary>Called by the application after each fixed step.</summary>
        public static void Step() => StepTriggers(Movers);

        private static bool LayerAllowed(Collider c, int layerMask) => ((1 << c.gameObject.layer) & layerMask) != 0;

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

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers, bool includeTriggers = false)
        {
            var ray = new Ray(origin, direction);
            hit = default;
            float best = maxDistance;
            bool found = false;
            foreach (var c in Colliders)
            {
                if (!c.IsEnabledForPhysics || !LayerAllowed(c, layerMask)) continue;
                if (c.isTrigger && !includeTriggers) continue;
                if (c.Raycast(ray, best, out var h) && h.distance < best)
                {
                    best = h.distance; hit = h; found = true;
                }
            }
            return found;
        }

        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            Raycast(origin, direction, out _, maxDistance, layerMask);

        public static bool Raycast(Ray ray, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers) =>
            Raycast(ray.origin, ray.direction, out hit, maxDistance, layerMask);

        /// <summary>Sphere cast approximated by a ray against colliders inflated by the radius.</summary>
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hit, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers)
        {
            var ray = new Ray(origin, direction);
            hit = default;
            float best = maxDistance;
            bool found = false;
            foreach (var c in Colliders)
            {
                if (!c.IsEnabledForPhysics || c.isTrigger || !LayerAllowed(c, layerMask)) continue;
                if (RayAabb(ray, c.bounds.Expanded(radius), best, c, out var h) && h.distance < best)
                {
                    best = h.distance; hit = h; found = true;
                }
            }
            return found;
        }

        public static RaycastHit[] SphereCastAll(Vector3 origin, float radius, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers)
        {
            var list = new List<RaycastHit>();
            var ray = new Ray(origin, direction.sqrMagnitude > 0 ? direction : Vector3.forward);
            foreach (var c in Colliders)
            {
                if (!c.IsEnabledForPhysics || c.isTrigger || !LayerAllowed(c, layerMask)) continue;
                var b = c.bounds.Expanded(radius);
                if (b.Contains(origin)) list.Add(new RaycastHit { collider = c, point = c.ClosestPoint(origin), normal = Vector3.up, distance = 0f });
                else if (RayAabb(ray, b, maxDistance, c, out var h)) list.Add(h);
            }
            return list.ToArray();
        }

        public static Collider[] OverlapSphere(Vector3 position, float radius, int layerMask = DefaultRaycastLayers)
        {
            var list = new List<Collider>();
            foreach (var c in Colliders)
            {
                if (!c.IsEnabledForPhysics || !LayerAllowed(c, layerMask)) continue;
                if ((c.ClosestPoint(position) - position).sqrMagnitude <= radius * radius) list.Add(c);
            }
            return list.ToArray();
        }

        public static bool CheckSphere(Vector3 position, float radius, int layerMask = DefaultRaycastLayers) => OverlapSphere(position, radius, layerMask).Length > 0;

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

        // ---- trigger tracking (run once per fixed step by the application) ----

        private static readonly HashSet<(Collider, Collider)> _overlaps = new HashSet<(Collider, Collider)>();

        /// <summary>Detects trigger overlaps between moving bodies (CharacterControllers, projectiles) and trigger colliders.</summary>
        internal static void StepTriggers(IReadOnlyList<Collider> movers)
        {
            var current = new HashSet<(Collider, Collider)>();
            foreach (var mover in movers)
            {
                if (!mover.IsEnabledForPhysics) continue;
                var mb = mover.bounds;
                foreach (var other in Colliders)
                {
                    if (other == mover || !other.IsEnabledForPhysics) continue;
                    if (!(other.isTrigger || mover.isTrigger)) continue;
                    if (!LayersCollide(mover, other)) continue;
                    if (other.transform.IsChildOf(mover.transform) || mover.transform.IsChildOf(other.transform)) continue;
                    if (!mb.Intersects(other.bounds)) continue;
                    current.Add((mover, other));
                }
            }
            foreach (var pair in current)
                if (!_overlaps.Contains(pair)) { Dispatch(pair.Item1, pair.Item2, true); Dispatch(pair.Item2, pair.Item1, true); }
            foreach (var pair in _overlaps)
                if (!current.Contains(pair)) { Dispatch(pair.Item1, pair.Item2, false); Dispatch(pair.Item2, pair.Item1, false); }
            _overlaps.Clear();
            foreach (var p in current) _overlaps.Add(p);
        }

        private static void Dispatch(Collider self, Collider other, bool enter)
        {
            if (self.destroyed || other.destroyed) return;
            foreach (var comp in self.gameObject.Components)
            {
                if (comp is ITriggerListener l && !comp.destroyed)
                {
                    try { if (enter) l.OnTriggerEnter(other); else l.OnTriggerExit(other); }
                    catch (Exception e) { Debug.LogError($"OnTrigger handler threw: {e}"); }
                }
            }
        }
    }

    /// <summary>Implement on a Behaviour to receive trigger callbacks (replaces Unity's magic OnTriggerEnter methods).</summary>
    public interface ITriggerListener
    {
        void OnTriggerEnter(Collider other);
        void OnTriggerExit(Collider other);
    }
}
