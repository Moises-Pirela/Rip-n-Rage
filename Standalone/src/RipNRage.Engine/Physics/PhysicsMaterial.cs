using System;

namespace RipNRage.Engine
{
    /// <summary>How the values of two touching materials are combined (Unity's PhysicMaterialCombine).</summary>
    public enum CombineMode { Average, Minimum, Maximum, Multiply }

    /// <summary>
    /// Surface properties of a collider: friction while at rest (static) and while sliding (dynamic), bounciness,
    /// and how each combines with the other surface. Assign to <see cref="Collider.material"/>; colliders without
    /// one use their rigid body's values (<see cref="RigidBody.staticFriction"/> / <see cref="RigidBody.friction"/>
    /// / <see cref="RigidBody.restitution"/>) or, for level geometry, the solver's defaults.
    /// </summary>
    public sealed class PhysicsMaterial
    {
        public string name = "material";
        /// <summary>Friction coefficient that has to be overcome to start sliding.</summary>
        public float staticFriction = 0.6f;
        /// <summary>Friction coefficient while sliding.</summary>
        public float dynamicFriction = 0.5f;
        public float restitution = 0f;
        public CombineMode frictionCombine = CombineMode.Average;
        public CombineMode restitutionCombine = CombineMode.Maximum;

        public PhysicsMaterial() { }
        public PhysicsMaterial(string name, float staticFriction, float dynamicFriction, float restitution = 0f)
        {
            this.name = name; this.staticFriction = staticFriction; this.dynamicFriction = dynamicFriction; this.restitution = restitution;
        }

        public static float Combine(float a, float b, CombineMode mode)
        {
            switch (mode)
            {
                case CombineMode.Minimum: return MathF.Min(a, b);
                case CombineMode.Maximum: return MathF.Max(a, b);
                case CombineMode.Multiply: return a * b;
                default: return (a + b) * 0.5f;
            }
        }

        /// <summary>The stricter of two combine modes decides (Maximum &gt; Multiply &gt; Minimum &gt; Average), like Unity.</summary>
        public static CombineMode Pick(CombineMode a, CombineMode b)
        {
            int Rank(CombineMode m) => m == CombineMode.Maximum ? 3 : m == CombineMode.Multiply ? 2 : m == CombineMode.Minimum ? 1 : 0;
            return Rank(a) >= Rank(b) ? a : b;
        }
    }
}
