using System;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>How tangential friction is modelled.</summary>
    public enum FrictionModel
    {
        /// <summary>One coefficient (the dynamic one) bounds the friction impulse.</summary>
        Coulomb,
        /// <summary>Static friction holds a contact still up to μs·N; once it slips, dynamic friction μd·N applies.</summary>
        StaticDynamic,
    }

    /// <summary>
    /// The contact constraint solver: prepares every manifold (effective masses, tangent frame, restitution),
    /// carries impulses over from the previous step through the contact cache (warm start, matched by feature id
    /// and falling back to anchor proximity), solves the non-penetration rows with the chosen position-correction
    /// mode (soft spring, Baumgarte bias or NGS position pass), solves friction with a static / dynamic Coulomb
    /// cone, applies restitution after the velocity passes, and stores the results back into the cache.
    /// </summary>
    public sealed class ContactSolver
    {
        // ---- cache ----
        private struct CachedPoint { public int id; public Vector3 localA; public float n, t0, t1; }
        private sealed class Cached
        {
            public readonly CachedPoint[] points = new CachedPoint[Manifold.MaxPoints];
            public int count;
            public int stamp;      // last step the manifold existed
            public int born;       // step it first appeared
        }
        private readonly Dictionary<long, Cached> _cache = new Dictionary<long, Cached>();
        private readonly Stack<Cached> _pool = new Stack<Cached>();
        private readonly List<long> _stale = new List<long>();
        private int _stamp;

        /// <summary>Steps a manifold's impulses are kept after the shapes stop touching (a brief separation does not lose the warm start).</summary>
        public int cacheLifetime = 3;
        public FrictionModel frictionModel = FrictionModel.StaticDynamic;
        /// <summary>
        /// Relative tangential speed (m/s) at the start of the step below which a contact point counts as sticking
        /// and is held by static friction; faster points slide under dynamic friction.
        /// </summary>
        public float slipSpeed = 0.05f;
        public int CachedManifolds => _cache.Count;
        private int _matched, _total;
        public int MatchedPoints => _matched;
        public int TotalPoints => _total;

        public void BeginStep() { _stamp++; _matched = 0; _total = 0; }

        public void EndStep()
        {
            _stale.Clear();
            foreach (var kv in _cache) if (_stamp - kv.Value.stamp > cacheLifetime) _stale.Add(kv.Key);
            foreach (var k in _stale) { _pool.Push(_cache[k]); _cache.Remove(k); }
        }

        public void Clear() { foreach (var kv in _cache) _pool.Push(kv.Value); _cache.Clear(); }

        // ---- prepare ----

        /// <summary>Per-manifold setup for the step; <paramref name="p"/> carries the solver settings. Safe to run on several threads at once (the cache is only read).</summary>
        public void Prepare(Manifold m, in SolverParams p)
        {
            SolverBody a = m.bodyA, b = m.bodyB;
            Vector3 n = m.normal;
            m.tangent0 = Vector3.Cross(n, MathF.Abs(n.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
            m.tangent1 = Vector3.Cross(n, m.tangent0);
            m.posA0 = a.position; m.rotA0 = a.rotation;
            m.posB0 = b != null ? b.position : Vector3.zero; m.rotB0 = b != null ? b.rotation : Quaternion.identity;
            float imA = a.inverseMass, imB = Constraint.Dynamic(b) ? b.inverseMass : 0f;

            // soft constraint coefficients (Catto): a spring-damper in impulse form
            float zeta = m.dampingRatio;
            float omega = 2f * MathF.PI * MathF.Min(m.hertz, 0.25f / p.h);
            float hw = p.h * omega;
            m.biasRate = omega / (2f * zeta + hw);
            m.massScale = hw * (2f * zeta + hw) / (1f + hw * (2f * zeta + hw));
            m.impulseScale = 1f / (1f + hw * (2f * zeta + hw));

            _cache.TryGetValue(m.key, out Cached cached);
            bool haveCache = p.warmStart && cached != null && cached.count > 0;
            m.age = cached != null ? _stamp - cached.born : 0;

            for (int i = 0; i < m.count; i++)
            {
                ref ManifoldPoint pt = ref m.points[i];
                pt.rA = pt.point - a.position;
                pt.rB = b != null ? pt.point - b.position : Vector3.zero;
                pt.localA = a.RotateToLocal(pt.rA);
                pt.localB = b != null ? b.RotateToLocal(pt.rB) : Vector3.zero;
                pt.anchorA0 = pt.point; pt.anchorB0 = pt.point;

                Vector3 rnA = Vector3.Cross(pt.rA, n), rnB = Vector3.Cross(pt.rB, n);
                float k = imA + imB + Vector3.Dot(rnA, a.ApplyInverseInertia(rnA)) + (Constraint.Dynamic(b) ? Vector3.Dot(rnB, b.ApplyInverseInertia(rnB)) : 0f);
                pt.normalMass = k > 0f ? 1f / k : 0f;
                Vector3 rtA0 = Vector3.Cross(pt.rA, m.tangent0), rtB0 = Vector3.Cross(pt.rB, m.tangent0);
                float k0 = imA + imB + Vector3.Dot(rtA0, a.ApplyInverseInertia(rtA0)) + (Constraint.Dynamic(b) ? Vector3.Dot(rtB0, b.ApplyInverseInertia(rtB0)) : 0f);
                pt.tangentMass0 = k0 > 0f ? 1f / k0 : 0f;
                Vector3 rtA1 = Vector3.Cross(pt.rA, m.tangent1), rtB1 = Vector3.Cross(pt.rB, m.tangent1);
                float k1 = imA + imB + Vector3.Dot(rtA1, a.ApplyInverseInertia(rtA1)) + (Constraint.Dynamic(b) ? Vector3.Dot(rtB1, b.ApplyInverseInertia(rtB1)) : 0f);
                pt.tangentMass1 = k1 > 0f ? 1f / k1 : 0f;

                pt.baseSeparation = -pt.depth;
                Vector3 vRel = RelativeVelocity(m, in pt);
                pt.relativeVelocity = Vector3.Dot(vRel, n);
                Vector3 vTangent = vRel - n * pt.relativeVelocity;
                pt.slipping = vTangent.sqrMagnitude > slipSpeed * slipSpeed;
                pt.normalImpulse = pt.tangentImpulse0 = pt.tangentImpulse1 = 0f;
                pt.maxNormalImpulse = 0f; pt.positionImpulse = 0f;
                pt.persistent = false; pt.age = 0;
                System.Threading.Interlocked.Increment(ref _total);
                if (haveCache && Match(ref pt, cached)) { System.Threading.Interlocked.Increment(ref _matched); pt.persistent = true; }
            }
        }

        /// <summary>Feature id first, then the nearest anchor within 2 cm.</summary>
        private static bool Match(ref ManifoldPoint p, Cached c)
        {
            for (int i = 0; i < c.count; i++)
            {
                if (c.points[i].id != p.id) continue;
                p.normalImpulse = c.points[i].n; p.tangentImpulse0 = c.points[i].t0; p.tangentImpulse1 = c.points[i].t1;
                return true;
            }
            const float tol = 0.02f * 0.02f;
            int best = -1; float bd = tol;
            for (int i = 0; i < c.count; i++)
            {
                float d = (p.localA - c.points[i].localA).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return false;
            p.normalImpulse = c.points[best].n; p.tangentImpulse0 = c.points[best].t0; p.tangentImpulse1 = c.points[best].t1;
            return true;
        }

        /// <summary>Remembers the manifold's impulses for the next step.</summary>
        public void Store(Manifold m)
        {
            if (!_cache.TryGetValue(m.key, out Cached c))
            {
                c = _pool.Count > 0 ? _pool.Pop() : new Cached();
                c.born = _stamp;
                _cache[m.key] = c;
            }
            c.count = m.count; c.stamp = _stamp;
            for (int i = 0; i < m.count; i++)
            {
                ref ManifoldPoint pt = ref m.points[i];
                pt.age = _stamp - c.born;
                c.points[i] = new CachedPoint { id = pt.id, localA = pt.localA, n = pt.normalImpulse, t0 = pt.tangentImpulse0, t1 = pt.tangentImpulse1 };
            }
        }

        // ---- velocity ----

        public static Vector3 RelativeVelocity(Manifold m, in ManifoldPoint p)
        {
            SolverBody a = m.bodyA, b = m.bodyB;
            Vector3 va = a.linearVelocity + Vector3.Cross(a.angularVelocity, p.rA);
            Vector3 vb = b != null && !b.sleeping ? b.linearVelocity + Vector3.Cross(b.angularVelocity, p.rB) : Vector3.zero;
            return vb - va;
        }

        /// <summary>Impulse <paramref name="impulse"/> pushes B and pulls A (the manifold normal runs from A to B).</summary>
        private static void ApplyImpulse(Manifold m, in ManifoldPoint p, Vector3 impulse)
        {
            SolverBody a = m.bodyA, b = m.bodyB;
            a.linearVelocity -= impulse * a.inverseMass;
            a.angularVelocity -= a.ApplyInverseInertia(Vector3.Cross(p.rA, impulse));
            if (Constraint.Dynamic(b))
            {
                b.linearVelocity += impulse * b.inverseMass;
                b.angularVelocity += b.ApplyInverseInertia(Vector3.Cross(p.rB, impulse));
            }
        }

        public void WarmStart(Manifold m)
        {
            for (int i = 0; i < m.count; i++)
            {
                ref ManifoldPoint p = ref m.points[i];
                if (p.normalImpulse == 0f && p.tangentImpulse0 == 0f && p.tangentImpulse1 == 0f) continue;
                ApplyImpulse(m, in p, m.normal * p.normalImpulse + m.tangent0 * p.tangentImpulse0 + m.tangent1 * p.tangentImpulse1);
            }
        }

        /// <summary>Current separation of a point: the anchors have moved with their bodies since the start of the step.</summary>
        public static float Separation(Manifold m, in ManifoldPoint p)
        {
            SolverBody a = m.bodyA, b = m.bodyB;
            // how far each anchor moved since the step started (A's motion into B closes the gap)
            Vector3 d = p.anchorA0 - (a.position + a.RotateToWorld(p.localA));
            if (b != null) d += (b.position + b.RotateToWorld(p.localB)) - p.anchorB0;
            return p.baseSeparation + Vector3.Dot(d, m.normal);
        }

        public void SolveVelocity(Manifold m, bool useBias, in SolverParams p)
        {
            Vector3 n = m.normal;
            for (int i = 0; i < m.count; i++)
            {
                ref ManifoldPoint pt = ref m.points[i];
                float s = Separation(m, in pt);
                float bias, massScale = 1f, impulseScale = 0f;
                if (s > 0f) bias = s * p.invH; // speculative: may approach enough to touch
                else if (!useBias) bias = 0f;
                else switch (p.mode)
                {
                    case PositionCorrectionMode.Soft: bias = MathF.Max(m.biasRate * s, -p.maxBiasVelocity); massScale = m.massScale; impulseScale = m.impulseScale; break;
                    case PositionCorrectionMode.Baumgarte: bias = MathF.Max(p.baumgarte * p.invH * MathF.Min(s + p.linearSlop, 0f), -p.maxBiasVelocity); break;
                    default: bias = 0f; break; // NGS: positions are corrected afterwards
                }
                float vn = Vector3.Dot(RelativeVelocity(m, in pt), n);
                float impulse = -pt.normalMass * massScale * (vn + bias) - impulseScale * pt.normalImpulse;
                float newImpulse = MathF.Max(pt.normalImpulse + impulse, 0f);
                impulse = newImpulse - pt.normalImpulse;
                pt.normalImpulse = newImpulse;
                if (newImpulse > pt.maxNormalImpulse) pt.maxNormalImpulse = newImpulse;
                if (impulse != 0f) ApplyImpulse(m, in pt, n * impulse);
            }
            SolveFriction(m);
        }

        /// <summary>
        /// Coulomb friction with a static / dynamic coefficient: a point that was (nearly) at rest tangentially at
        /// the start of the step is held inside the static cone μs·N — a crate on a slope below the static angle
        /// does not creep — while a point that was already sliding is bounded by the dynamic cone μd·N. The
        /// accumulated impulse is clamped to the cone as usual, so warm starting converges to the holding
        /// distribution across the manifold.
        /// </summary>
        private void SolveFriction(Manifold m)
        {
            bool staticDynamic = frictionModel == FrictionModel.StaticDynamic;
            for (int i = 0; i < m.count; i++)
            {
                ref ManifoldPoint pt = ref m.points[i];
                if (pt.normalImpulse <= 0f)
                {
                    if (pt.tangentImpulse0 != 0f || pt.tangentImpulse1 != 0f) { ApplyImpulse(m, in pt, -(m.tangent0 * pt.tangentImpulse0 + m.tangent1 * pt.tangentImpulse1)); pt.tangentImpulse0 = pt.tangentImpulse1 = 0f; }
                    continue;
                }
                float mu = staticDynamic && !pt.slipping ? m.staticFriction : m.dynamicFriction;
                float maxFriction = mu * pt.normalImpulse;
                Vector3 vRel = RelativeVelocity(m, in pt);
                float vt0 = Vector3.Dot(vRel, m.tangent0), vt1 = Vector3.Dot(vRel, m.tangent1);
                float want0 = pt.tangentImpulse0 - pt.tangentMass0 * vt0;
                float want1 = pt.tangentImpulse1 - pt.tangentMass1 * vt1;
                float mag = MathF.Sqrt(want0 * want0 + want1 * want1);
                if (mag > maxFriction && mag > 1E-12f) { float k = maxFriction / mag; want0 *= k; want1 *= k; }
                float d0 = want0 - pt.tangentImpulse0, d1 = want1 - pt.tangentImpulse1;
                pt.tangentImpulse0 = want0; pt.tangentImpulse1 = want1;
                if (d0 != 0f || d1 != 0f) ApplyImpulse(m, in pt, m.tangent0 * d0 + m.tangent1 * d1);
            }
        }

        /// <summary>Restitution after the velocity passes: contacts that were approaching fast enough bounce.</summary>
        public void ApplyRestitution(Manifold m, float threshold)
        {
            if (m.restitution <= 0f) return;
            Vector3 n = m.normal;
            for (int i = 0; i < m.count; i++)
            {
                ref ManifoldPoint pt = ref m.points[i];
                if (pt.relativeVelocity > -threshold || pt.maxNormalImpulse == 0f) continue;
                float vn = Vector3.Dot(RelativeVelocity(m, in pt), n);
                float impulse = -pt.normalMass * (vn + m.restitution * pt.relativeVelocity);
                float newImpulse = MathF.Max(pt.normalImpulse + impulse, 0f);
                impulse = newImpulse - pt.normalImpulse;
                pt.normalImpulse = newImpulse;
                if (impulse != 0f) ApplyImpulse(m, in pt, n * impulse);
            }
        }

        // ---- position (NGS) ----

        /// <summary>One position pass: pushes the poses apart along the normal where the penetration exceeds the slop.</summary>
        public void SolvePosition(Manifold m, in SolverParams p)
        {
            SolverBody a = m.bodyA, b = m.bodyB;
            Vector3 n = m.normal;
            bool da = Constraint.Dynamic(a), db = Constraint.Dynamic(b);
            for (int i = 0; i < m.count; i++)
            {
                ref ManifoldPoint pt = ref m.points[i];
                float s = Separation(m, in pt);
                float c = Mathf.Clamp(p.baumgarte * (s + p.linearSlop), -p.maxLinearCorrection, 0f);
                if (c >= 0f) continue;
                Vector3 rA = a.RotateToWorld(pt.localA), rB = b != null ? b.RotateToWorld(pt.localB) : Vector3.zero;
                float k = 0f;
                if (da) { Vector3 rn = Vector3.Cross(rA, n); k += a.inverseMass + Vector3.Dot(rn, a.ApplyInverseInertia(rn)); }
                if (db) { Vector3 rn = Vector3.Cross(rB, n); k += b.inverseMass + Vector3.Dot(rn, b.ApplyInverseInertia(rn)); }
                if (k <= 0f) continue;
                float impulse = -c / k;
                pt.positionImpulse += impulse;
                Vector3 P = n * impulse;
                if (da) a.ApplyPositionImpulse(-P, rA);
                if (db) b.ApplyPositionImpulse(P, rB);
            }
        }
    }
}
