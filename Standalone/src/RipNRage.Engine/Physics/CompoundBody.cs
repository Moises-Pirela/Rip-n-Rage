using System;
using RipNRage.Engine.Ecs;
using RipNRage.Engine.Ecs.Physics;

namespace RipNRage.Engine
{
    /// <summary>
    /// The compound half of a rigid body (V2): one body, many colliders. A hull, its cabin and its rails; a ship's
    /// deck, its superstructure and the walls of its hold. Each part is a collider slot of its own — in the
    /// broadphase, in the narrow phase, answering rays — and every one of them names this body, so whatever touches
    /// any part touches the boat, and whatever stands on any part stands on the boat.
    ///
    /// <para>The body moves as one. Its mass is shared out among the parts by volume × density, its centre of mass is
    /// theirs, and its inertia is the full tensor of all of them about that centre (each part's own, turned into the
    /// body frame and moved by the parallel-axis theorem), diagonalised into a principal frame. The solver keeps the
    /// diagonal it always kept; only the frame it is diagonal in is new (<see cref="inertiaFrame"/>).</para>
    ///
    /// <para>The entity's scale applies to its parts (V2b): uniformly, everything grows; non-uniformly, an axis-aligned box
    /// stays a box, a turned box becomes the parallelepiped it really is (an exact convex part), and a convex part is
    /// transformed exactly. Spheres and capsules, which a non-uniform scale would make ellipsoids, take the rule single
    /// shapes already use: the largest scale across them.</para>
    /// </summary>
    public sealed partial class SolverBody
    {
        /// <summary>Made of several parts (<see cref="children"/>) rather than the one shape described by isSphere / isCapsule / boxHalfExtents.</summary>
        internal bool isCompound;
        /// <summary>The parts' collider slots, in the asset's order. <see cref="collider"/> is the first of them.</summary>
        internal ColliderTable.Slot[] children = Array.Empty<ColliderTable.Slot>();
        internal CompoundChild[] parts = Array.Empty<CompoundChild>();
        /// <summary>Each part's frame relative to the entity origin: where its centre is and how it is turned.</summary>
        private Vector3[] _partCentre = Array.Empty<Vector3>();
        private Quaternion[] _partRotation = Array.Empty<Quaternion>();
        /// <summary>Convex parts: the local polytope each step's world copy is posed from (null for the other kinds).</summary>
        private ConvexShape[] _partTemplate = Array.Empty<ConvexShape>();
        /// <summary>Half extents of each part's own box — for the sweep, and for a convex part's inertia.</summary>
        private Vector3[] _partHalf = Array.Empty<Vector3>();
        /// <summary>The principal axes of the inertia tensor, in the body frame. Identity for a single shape.</summary>
        internal Quaternion inertiaFrame = Quaternion.identity;
        /// <summary>Each part's centre when its slot was last posed: a sweep between steps carries it from there.</summary>
        private Vector3[] _posedCentre = Array.Empty<Vector3>();
        /// <summary>What the body was set up from, so a change of scale can set it up again.</summary>
        private CompoundShapeAsset _source;
        private Body _sourceConfig;
        internal Vector3 compoundScale = Vector3.one;

        public bool IsCompound => isCompound;
        public int PartCount => children.Length;

        // ------------------------------------------------------------------ setup

        /// <summary>Configures an ECS-owned compound body from its <see cref="Body"/>, its parts and the entity's scale.</summary>
        internal void SetupCompound(in Body cfg, CompoundShapeAsset asset, Vector3 scale)
        {
            ApplyBodyConfig(in cfg);
            _source = asset; _sourceConfig = cfg; compoundScale = scale;
            isCompound = true;
            isSphere = isCapsule = false;
            parts = ScaledParts(asset?.Children ?? Array.Empty<CompoundChild>(), scale);
            int n = parts.Length;
            _partCentre = new Vector3[n]; _partRotation = new Quaternion[n]; _partTemplate = new ConvexShape[n]; _partHalf = new Vector3[n];
            _posedCentre = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                ref readonly var p = ref parts[i];
                var rot = p.Rotation == default ? Quaternion.identity : p.Rotation;
                _partRotation[i] = rot;
                _partCentre[i] = p.Position + rot * p.Shape.Center;
                if (p.Shape.Kind == ShapeKind.Convex)
                {
                    var hull = PhysicsAssets.Hulls.Resolve(p.Shape.Hull);
                    // the template is the hull in the part's own frame, about the part's centre
                    if (hull != null) _partTemplate[i] = ConvexShape.FromPlanes(hull.Faces, hull.Vertices);
                }
                _partHalf[i] = PartHalfExtents(i);
            }
            ComputeMassProperties();
        }

        /// <summary>
        /// The entity's scale changed: set the parts up again at the new one, keeping the body where it is - its origin
        /// stays put and its centre of mass moves with the parts. Velocities carry over.
        /// </summary>
        internal void Rescale(Vector3 scale)
        {
            if (_source == null) return;
            Vector3 origin = position - rotation * comOffsetLocal;
            Vector3 v = linearVelocity, w = angularVelocity;
            SetupCompound(in _sourceConfig, _source, scale);
            position = origin + rotation * comOffsetLocal;
            linearVelocity = v; angularVelocity = w;
            for (int i = 0; i < children.Length && i < parts.Length; i++) if (children[i] != null) { children[i].ecsShape = parts[i].Shape; children[i].polytope = null; }
            UpdateShape();
            TouchBroadphase();
        }

        // ------------------------------------------------------------------ scale

        /// <summary>
        /// The parts as the entity's scale makes them. Every part ends up described in the body's frame: its centre is
        /// the scale applied to where it sat, and its shape is the scale applied to what it was - exactly, wherever the
        /// result is still a shape a part can be.
        /// </summary>
        internal static CompoundChild[] ScaledParts(CompoundChild[] src, Vector3 scale)
        {
            if (src.Length == 0 || ((scale - Vector3.one).sqrMagnitude < 1E-10f)) return src;
            var result = new CompoundChild[src.Length];
            float ax = MathF.Abs(scale.x), ay = MathF.Abs(scale.y), az = MathF.Abs(scale.z);
            float most = MathF.Max(ax, MathF.Max(ay, az));
            bool uniform = scale.x > 0f && MathF.Abs(scale.x - scale.y) < 1E-5f && MathF.Abs(scale.x - scale.z) < 1E-5f;
            for (int i = 0; i < src.Length; i++)
            {
                var p = src[i];
                var rot = p.Rotation == default ? Quaternion.identity : p.Rotation;
                var q = p;
                q.Shape.Center = Vector3.zero;
                q.Position = Vector3.Scale(scale, p.Position + rot * p.Shape.Center); // the part's centre, scaled
                q.Rotation = rot;
                switch (p.Shape.Kind)
                {
                    case ShapeKind.Sphere:
                        q.Shape.Radius = p.Shape.Radius * (uniform ? scale.x : most);
                        break;
                    case ShapeKind.Capsule:
                    {
                        if (uniform) { q.Shape.Radius = p.Shape.Radius * scale.x; q.Shape.Height = p.Shape.Height * scale.x; break; }
                        // along a body axis the capsule stays a capsule: its length takes that axis, its girth the larger of the others
                        Vector3 axis = rot * Vector3.up;
                        int k = AxisOf(axis);
                        if (k < 0) { q.Shape.Radius = p.Shape.Radius * most; q.Shape.Height = p.Shape.Height * most; break; }
                        float along = k == 0 ? ax : k == 1 ? ay : az;
                        float across = k == 0 ? MathF.Max(ay, az) : k == 1 ? MathF.Max(ax, az) : MathF.Max(ax, ay);
                        q.Shape.Radius = p.Shape.Radius * across;
                        q.Shape.Height = MathF.Max(p.Shape.Height * along, 2f * q.Shape.Radius);
                        break;
                    }
                    case ShapeKind.Box:
                    {
                        if (uniform) { q.Shape.Size = p.Shape.Size * scale.x; break; }
                        // a box turned onto the body's axes stays a box, its sides taking the scale of the axes they lie on
                        int kx = AxisOf(rot * Vector3.right), ky = AxisOf(rot * Vector3.up), kz = AxisOf(rot * Vector3.forward);
                        if (kx >= 0 && ky >= 0 && kz >= 0)
                        {
                            q.Shape.Size = new Vector3(p.Shape.Size.x * Pick(kx, ax, ay, az), p.Shape.Size.y * Pick(ky, ax, ay, az), p.Shape.Size.z * Pick(kz, ax, ay, az));
                            break;
                        }
                        // any other box becomes a parallelepiped: convex, and exact
                        var h = p.Shape.Size * 0.5f;
                        var corners = new Vector3[8];
                        for (int c = 0; c < 8; c++)
                            corners[c] = new Vector3((c & 1) == 0 ? -h.x : h.x, (c & 2) == 0 ? -h.y : h.y, (c & 4) == 0 ? -h.z : h.z);
                        var faces = new[]
                        {
                            new Plane(Vector3.right, h.x), new Plane(Vector3.left, h.x), new Plane(Vector3.up, h.y),
                            new Plane(Vector3.down, h.y), new Plane(Vector3.forward, h.z), new Plane(Vector3.back, h.z),
                        };
                        q.Shape.Kind = ShapeKind.Convex; q.Shape.Size = Vector3.zero;
                        q.Shape.Hull = TransformedHull(faces, corners, rot, scale);
                        q.Rotation = Quaternion.identity;
                        break;
                    }
                    case ShapeKind.Convex:
                    {
                        var hull = PhysicsAssets.Hulls.Resolve(p.Shape.Hull);
                        if (hull == null) break;
                        q.Shape.Hull = TransformedHull(hull.Faces, hull.Vertices, rot, scale);
                        q.Rotation = Quaternion.identity;
                        break;
                    }
                }
                result[i] = q;
            }
            return result;
        }

        private static float Pick(int k, float x, float y, float z) => k == 0 ? x : k == 1 ? y : z;

        /// <summary>Which body axis a unit vector lies along (0 x, 1 y, 2 z), or -1 if none.</summary>
        private static int AxisOf(Vector3 v)
        {
            const float tol = 1E-4f;
            if (MathF.Abs(MathF.Abs(v.x) - 1f) < tol) return 0;
            if (MathF.Abs(MathF.Abs(v.y) - 1f) < tol) return 1;
            if (MathF.Abs(MathF.Abs(v.z) - 1f) < tol) return 2;
            return -1;
        }

        /// <summary>
        /// A hull carried through the part's turn and the body's scale, M = S·R: vertices by M, face normals by its
        /// inverse transpose S⁻¹·R (R is orthonormal), each plane through the image of a point it held. Exact for any
        /// scale, mirroring included, and uncapped - deriving faces again from the points would be neither.
        /// </summary>
        private static AssetHandle<ConvexColliderAsset> TransformedHull(Plane[] faces, Vector3[] vertices, Quaternion rot, Vector3 scale)
        {
            var verts = new Vector3[vertices.Length];
            for (int i = 0; i < verts.Length; i++) verts[i] = Vector3.Scale(scale, rot * vertices[i]);
            var inv = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            var planes = new Plane[faces.Length];
            for (int i = 0; i < planes.Length; i++)
            {
                Vector3 n = Vector3.Scale(inv, rot * faces[i].normal).normalized;
                Vector3 onPlane = Vector3.Scale(scale, rot * (faces[i].normal * faces[i].dist));
                planes[i] = new Plane(n, Vector3.Dot(n, onPlane));
            }
            return PhysicsAssets.Hulls.Intern(new ConvexColliderAsset(planes, verts), "scaled-part-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>Half extents of a part's own box (its frame).</summary>
        private Vector3 PartHalfExtents(int i)
        {
            ref readonly var s = ref parts[i].Shape;
            switch (s.Kind)
            {
                case ShapeKind.Sphere: return new Vector3(s.Radius, s.Radius, s.Radius);
                case ShapeKind.Capsule: return new Vector3(s.Radius, MathF.Max(s.Height * 0.5f, s.Radius), s.Radius);
                case ShapeKind.Convex:
                {
                    var t = _partTemplate[i];
                    if (t == null || t.vertices.Length == 0) return new Vector3(0.05f, 0.05f, 0.05f);
                    Vector3 mn = t.vertices[0], mx = mn;
                    foreach (var v in t.vertices) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
                    return Vector3.Max((mx - mn) * 0.5f, new Vector3(0.01f, 0.01f, 0.01f));
                }
                default: return Vector3.Max(s.Size * 0.5f, new Vector3(0.01f, 0.01f, 0.01f));
            }
        }

        // ------------------------------------------------------------------ mass

        /// <summary>
        /// Mass, centre of mass and inertia from the parts. Each part's share of <see cref="mass"/> is its volume ×
        /// density over the whole; its inertia about its own centre is turned into the body frame (R·D·Rᵀ) and moved
        /// to the body's centre of mass (m·(|d|²·E − d·dᵀ)); the sum is diagonalised. A convex part's own inertia is
        /// its bounding box's — its mass and its centre are exact.
        /// </summary>
        private void ComputeCompoundMassProperties()
        {
            int n = parts.Length;
            float total = MathF.Max(mass, 1E-04f);
            inverseMass = isKinematic ? 0f : 1f / total;
            if (n == 0) { comOffsetLocal = Vector3.zero; inverseInertiaLocal = Vector3.zero; inertiaFrame = Quaternion.identity; initialised = true; return; }

            var weight = new float[n];
            var centre = new Vector3[n];
            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                PartVolume(i, out float volume, out Vector3 localCentroid);
                float density = parts[i].Density > 0f ? parts[i].Density : 1f;
                weight[i] = MathF.Max(volume, 1E-06f) * density;
                centre[i] = _partCentre[i] + _partRotation[i] * localCentroid;
                sum += weight[i];
            }

            Vector3 com = Vector3.zero;
            for (int i = 0; i < n; i++) com += centre[i] * (weight[i] / sum);
            comOffsetLocal = com;

            // the tensor, accumulated as a symmetric matrix
            float xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
            for (int i = 0; i < n; i++)
            {
                float m = total * weight[i] / sum;
                var t = PartTensor(i, m); // the part's own inertia, its frame, about its centroid
                var r = Matrix3x3.FromQuaternion(_partRotation[i]);
                // R · T · Rᵀ
                for (int a = 0; a < 3; a++)
                for (int b = a; b < 3; b++)
                {
                    float v = 0f;
                    for (int k = 0; k < 3; k++)
                    for (int l = 0; l < 3; l++)
                        v += Get(r, a, k) * Get(t, k, l) * Get(r, b, l);
                    Accumulate(ref xx, ref yy, ref zz, ref xy, ref xz, ref yz, a, b, v);
                }
                // parallel axis to the body's centre of mass
                Vector3 o = centre[i] - com;
                float o2 = o.sqrMagnitude;
                xx += m * (o2 - o.x * o.x); yy += m * (o2 - o.y * o.y); zz += m * (o2 - o.z * o.z);
                xy -= m * o.x * o.y; xz -= m * o.x * o.z; yz -= m * o.y * o.z;
            }

            Diagonalise(xx, yy, zz, xy, xz, yz, out Vector3 moments, out Quaternion frame);
            inertiaFrame = frame;
            inverseInertiaLocal = new Vector3(
                moments.x > 1E-09f ? 1f / moments.x : 0f, moments.y > 1E-09f ? 1f / moments.y : 0f, moments.z > 1E-09f ? 1f / moments.z : 0f);
            if (isKinematic || freezeRotation) inverseInertiaLocal = Vector3.zero;

            // the box the body as a whole fits in, about its centre of mass (what the single-shape paths still read)
            Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), mx = -mn;
            for (int i = 0; i < n; i++)
            {
                var r = _partRotation[i];
                Vector3 h = _partHalf[i];
                Vector3 c = _partCentre[i] - com;
                Vector3 e = new Vector3(
                    MathF.Abs((r * Vector3.right).x) * h.x + MathF.Abs((r * Vector3.up).x) * h.y + MathF.Abs((r * Vector3.forward).x) * h.z,
                    MathF.Abs((r * Vector3.right).y) * h.x + MathF.Abs((r * Vector3.up).y) * h.y + MathF.Abs((r * Vector3.forward).y) * h.z,
                    MathF.Abs((r * Vector3.right).z) * h.x + MathF.Abs((r * Vector3.up).z) * h.y + MathF.Abs((r * Vector3.forward).z) * h.z);
                mn = Vector3.Min(mn, c - e); mx = Vector3.Max(mx, c + e);
            }
            boxHalfExtents = Vector3.Max(Vector3.Max(-mn, mx), new Vector3(0.01f, 0.01f, 0.01f));
            shape ??= ConvexShape.CreateBox();
            initialised = true;
        }

        private static float Get(in Matrix3x3 m, int r, int c) => r switch
        {
            0 => c == 0 ? m.m00 : c == 1 ? m.m01 : m.m02,
            1 => c == 0 ? m.m10 : c == 1 ? m.m11 : m.m12,
            _ => c == 0 ? m.m20 : c == 1 ? m.m21 : m.m22,
        };

        private static void Accumulate(ref float xx, ref float yy, ref float zz, ref float xy, ref float xz, ref float yz, int a, int b, float v)
        {
            if (a == 0 && b == 0) xx += v; else if (a == 1 && b == 1) yy += v; else if (a == 2 && b == 2) zz += v;
            else if (a == 0 && b == 1) xy += v; else if (a == 0 && b == 2) xz += v; else yz += v;
        }

        /// <summary>A part's volume, and where its centroid is in its own frame (exact for every kind).</summary>
        private void PartVolume(int i, out float volume, out Vector3 centroid)
        {
            ref readonly var s = ref parts[i].Shape;
            centroid = Vector3.zero;
            switch (s.Kind)
            {
                case ShapeKind.Sphere: volume = 4f / 3f * MathF.PI * s.Radius * s.Radius * s.Radius; return;
                case ShapeKind.Capsule:
                {
                    float r = s.Radius, h = MathF.Max(s.Height - 2f * r, 0f);
                    volume = MathF.PI * r * r * h + 4f / 3f * MathF.PI * r * r * r;
                    return;
                }
                case ShapeKind.Convex:
                {
                    var t = _partTemplate[i];
                    if (t == null) { volume = 1E-06f; return; }
                    // tetrahedra from the centre to every face's fan: exact volume and centroid
                    double vol = 0; Vector3 acc = Vector3.zero;
                    Vector3 o = t.center;
                    foreach (var f in t.faces)
                    {
                        if (f.indices == null || f.indices.Length < 3) continue;
                        Vector3 a = t.vertices[f.indices[0]];
                        for (int k = 1; k + 1 < f.indices.Length; k++)
                        {
                            Vector3 b = t.vertices[f.indices[k]], c = t.vertices[f.indices[k + 1]];
                            float v6 = MathF.Abs(Vector3.Dot(a - o, Vector3.Cross(b - o, c - o)));
                            vol += v6 / 6.0;
                            acc += (o + a + b + c) * (v6 / 24f); // centroid of a tetrahedron is the mean of its corners
                        }
                    }
                    volume = (float)vol;
                    centroid = vol > 1E-12 ? acc / (float)vol : o;
                    return;
                }
                default: volume = MathF.Abs(s.Size.x * s.Size.y * s.Size.z); return;
            }
        }

        /// <summary>
        /// A part's inertia tensor in its own frame, about its centroid, for mass <paramref name="m"/>. Exact for every kind:
        /// a convex part's comes from the same tetrahedra as its volume - each one's ∫x·xᵀ dV is V/20·(s·sᵀ + Σvᵢ·vᵢᵀ)
        /// over its corners - moved to the centroid and turned into I = tr(C)·E − C. (It was the bounding box's, which
        /// makes a V-bottomed hull roll like a box.)
        /// </summary>
        private Matrix3x3 PartTensor(int i, float m)
        {
            if (parts[i].Shape.Kind == ShapeKind.Convex && _partTemplate[i] != null)
            {
                var t = _partTemplate[i];
                Vector3 o = t.center;
                double cxx = 0, cyy = 0, czz = 0, cxy = 0, cxz = 0, cyz = 0, vol = 0;
                Vector3 first = Vector3.zero;
                foreach (var f in t.faces)
                {
                    if (f.indices == null || f.indices.Length < 3) continue;
                    Vector3 a = t.vertices[f.indices[0]] - o;
                    for (int k = 1; k + 1 < f.indices.Length; k++)
                    {
                        Vector3 b = t.vertices[f.indices[k]] - o, c = t.vertices[f.indices[k + 1]] - o;
                        double det = Math.Abs(Vector3.Dot(a, Vector3.Cross(b, c)));
                        double k120 = det / 120.0;
                        Vector3 sum = a + b + c;
                        cxx += k120 * (a.x * a.x + b.x * b.x + c.x * c.x + sum.x * sum.x);
                        cyy += k120 * (a.y * a.y + b.y * b.y + c.y * c.y + sum.y * sum.y);
                        czz += k120 * (a.z * a.z + b.z * b.z + c.z * c.z + sum.z * sum.z);
                        cxy += k120 * (a.x * a.y + b.x * b.y + c.x * c.y + sum.x * sum.y);
                        cxz += k120 * (a.x * a.z + b.x * b.z + c.x * c.z + sum.x * sum.z);
                        cyz += k120 * (a.y * a.z + b.y * b.z + c.y * c.z + sum.y * sum.z);
                        double v = det / 6.0;
                        vol += v;
                        first += sum * (float)(v / 4.0); // the tetrahedron's centroid is a quarter of its corners' sum
                    }
                }
                if (vol > 1E-12)
                {
                    // about the centroid, then scaled from volume to mass
                    Vector3 cen = first / (float)vol;
                    cxx -= vol * cen.x * cen.x; cyy -= vol * cen.y * cen.y; czz -= vol * cen.z * cen.z;
                    cxy -= vol * cen.x * cen.y; cxz -= vol * cen.x * cen.z; cyz -= vol * cen.y * cen.z;
                    double rho = m / vol, tr = cxx + cyy + czz;
                    return new Matrix3x3
                    {
                        m00 = (float)(rho * (tr - cxx)), m11 = (float)(rho * (tr - cyy)), m22 = (float)(rho * (tr - czz)),
                        m01 = (float)(-rho * cxy), m10 = (float)(-rho * cxy), m02 = (float)(-rho * cxz), m20 = (float)(-rho * cxz),
                        m12 = (float)(-rho * cyz), m21 = (float)(-rho * cyz),
                    };
                }
            }
            Vector3 d = PartInertia(i, m);
            return new Matrix3x3 { m00 = d.x, m11 = d.y, m22 = d.z };
        }

        /// <summary>A part's principal moments of inertia, in its own frame, for mass <paramref name="m"/> (box, sphere, capsule).</summary>
        private Vector3 PartInertia(int i, float m)
        {
            ref readonly var s = ref parts[i].Shape;
            switch (s.Kind)
            {
                case ShapeKind.Sphere: { float v = 0.4f * m * s.Radius * s.Radius; return new Vector3(v, v, v); }
                case ShapeKind.Capsule:
                {
                    float r = s.Radius, hgt = MathF.Max(s.Height - 2f * r, 0f);
                    float mc = m * hgt / (hgt + 4f * r / 3f), ms = m - mc;
                    float iAxis = mc * r * r * 0.5f + ms * 0.4f * r * r;
                    float iPerp = mc * (r * r * 0.25f + hgt * hgt / 12f) + ms * (0.4f * r * r + hgt * hgt * 0.25f + 3f * hgt * r / 8f);
                    return new Vector3(iPerp, iAxis, iPerp);
                }
                default:
                {
                    Vector3 sz = _partHalf[i] * 2f;
                    return new Vector3(m / 12f * (sz.y * sz.y + sz.z * sz.z), m / 12f * (sz.x * sz.x + sz.z * sz.z), m / 12f * (sz.x * sz.x + sz.y * sz.y));
                }
            }
        }

        /// <summary>
        /// Eigen-decomposition of a symmetric 3×3 (cyclic Jacobi): the principal moments, and the rotation whose
        /// columns are the principal axes. Converges in a handful of sweeps for any inertia tensor.
        /// </summary>
        internal static void Diagonalise(float xx, float yy, float zz, float xy, float xz, float yz, out Vector3 moments, out Quaternion frame)
        {
            double[,] a = { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } };
            double[,] v = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 32; sweep++)
            {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off < 1E-12 * (Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]) + 1E-30)) break;
                for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1E-30) continue;
                    double theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                    if (theta == 0) t = 1.0;
                    double c = 1.0 / Math.Sqrt(t * t + 1.0), s = t * c;
                    for (int k = 0; k < 3; k++)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double apk = a[p, k], aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq;
                    }
                }
            }
            moments = new Vector3((float)a[0, 0], (float)a[1, 1], (float)a[2, 2]);
            Vector3 c0 = new Vector3((float)v[0, 0], (float)v[1, 0], (float)v[2, 0]);
            Vector3 c1 = new Vector3((float)v[0, 1], (float)v[1, 1], (float)v[2, 1]);
            Vector3 c2 = Vector3.Cross(c0, c1); // right-handed whatever the sweeps left
            frame = FromAxes(c0.normalized, c1.normalized, c2.normalized);
        }

        /// <summary>The rotation whose columns are the given orthonormal axes.</summary>
        private static Quaternion FromAxes(Vector3 x, Vector3 y, Vector3 z)
        {
            float m00 = x.x, m10 = x.y, m20 = x.z, m01 = y.x, m11 = y.y, m21 = y.z, m02 = z.x, m12 = z.y, m22 = z.z;
            float trace = m00 + m11 + m22;
            Quaternion q;
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1f) * 2f;
                q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
                q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
                q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
                q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
            }
            return Quaternion.Normalize(q);
        }

        // ------------------------------------------------------------------ world shapes

        /// <summary>Where a part is at the body's current pose: its centre and its rotation.</summary>
        internal void PartPose(int i, out Vector3 centre, out Quaternion rot)
        {
            Vector3 origin = position - rotation * comOffsetLocal;
            centre = origin + rotation * _partCentre[i];
            rot = rotation * _partRotation[i];
        }

        /// <summary>Every part's slot posed at the body's current pose, and the body's bounds as their union.</summary>
        private void UpdateCompoundShapes()
        {
            shape ??= ConvexShape.CreateBox();
            shape.SetBox(position, boxHalfExtents, rotation);
            bool any = false; Bounds union = default;
            for (int i = 0; i < children.Length; i++)
            {
                var slot = children[i];
                if (slot == null) continue;
                PartPose(i, out Vector3 c, out Quaternion r);
                _posedCentre[i] = c;
                ref readonly var s = ref parts[i].Shape;
                switch (s.Kind)
                {
                    case ShapeKind.Sphere:
                        slot.worldShape = CollisionShape.Sphere(c, s.Radius);
                        slot.worldBounds = slot.worldShape.bounds;
                        break;
                    case ShapeKind.Capsule:
                        slot.worldShape = CollisionShape.Capsule(c, r, s.Radius, MathF.Max(s.Height * 0.5f - s.Radius, 0f));
                        slot.worldBounds = slot.worldShape.bounds;
                        break;
                    case ShapeKind.Convex when _partTemplate[i] != null:
                    {
                        // the template's vertices are about the part's centre (its Position plus its Shape.Center)
                        slot.polytope ??= ConvexShape.PosedCopy(_partTemplate[i]);
                        slot.polytope.SetPose(_partTemplate[i], c, r);
                        slot.worldShape = CollisionShape.Polytope(slot.polytope);
                        Vector3 mn = slot.polytope.vertices[0], mx = mn;
                        foreach (var v in slot.polytope.vertices) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
                        slot.worldBounds = new Bounds(mn, mx);
                        break;
                    }
                    default:
                        slot.worldShape = CollisionShape.Box(c, r, _partHalf[i]);
                        slot.polytope ??= ConvexShape.CreateBox();
                        slot.polytope.SetBox(c, _partHalf[i], r);
                        slot.worldBounds = slot.worldShape.bounds;
                        break;
                }
                slot.hull = null; // queries rebuild a moving part's plane hull on demand (PhysicsWorld.HullOf)
                slot.poseStamp++;
                if (any) union.Encapsulate(slot.worldBounds); else { union = slot.worldBounds; any = true; }
            }
            worldBounds = any ? union : Bounds.FromCenterExtents(position, boxHalfExtents);
        }

        /// <summary>
        /// A part's shape at the body's pose <i>right now</i>, for the continuous sweep between steps (the slots are
        /// posed once a step). Exact for every kind: a convex part is its posed polytope carried to where the body has
        /// moved it since (the sweep is translational, as every sweep here is, so its turn since the pose is not
        /// swept - README, Known gaps).
        /// </summary>
        internal CollisionShape PartSweepShape(int i)
        {
            PartPose(i, out Vector3 c, out Quaternion r);
            ref readonly var s = ref parts[i].Shape;
            var slot = i < children.Length ? children[i] : null;
            if (s.Kind == ShapeKind.Convex && slot?.polytope != null)
            {
                var shape = CollisionShape.Polytope(slot.polytope);
                shape.offset = c - _posedCentre[i];
                shape.center += shape.offset;
                return shape;
            }
            switch (s.Kind)
            {
                case ShapeKind.Sphere: return CollisionShape.Sphere(c, s.Radius);
                case ShapeKind.Capsule: return CollisionShape.Capsule(c, r, s.Radius, MathF.Max(s.Height * 0.5f - s.Radius, 0f));
                default: return CollisionShape.Box(c, r, _partHalf[i]);
            }
        }

        /// <summary>
        /// The point of the body nearest <paramref name="p"/>, over its parts: what a blast strikes. (It was the box the
        /// whole body fits in, which strikes air above an open deck and pushes the boat by a point it does not have.)
        /// </summary>
        internal Vector3 ClosestPointOnParts(Vector3 p)
        {
            Vector3 best = position; float bestD = float.MaxValue;
            for (int i = 0; i < children.Length; i++)
            {
                var slot = children[i];
                if (slot == null) continue;
                Vector3 q;
                var w = slot.worldShape;
                if (w.type == ShapeType.Sphere) q = w.center + Vector3.ClampMagnitude(p - w.center, w.radius);
                else if (w.type == ShapeType.Capsule)
                {
                    Vector3 axis = w.rotation * Vector3.up;
                    float t = Mathf.Clamp(Vector3.Dot(p - w.center, axis), -w.halfHeight, w.halfHeight);
                    Vector3 core = w.center + axis * t;
                    q = core + Vector3.ClampMagnitude(p - core, w.radius);
                }
                else if (slot.polytope != null)
                {
                    Vector3 onSurface = slot.polytope.ClosestPoint(p, out _, out _, out bool inside);
                    q = inside ? p : onSurface; // a blast inside a part strikes it where it is
                }
                else continue;
                float d = (q - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = q; }
            }
            return best;
        }

        private void TouchCompoundBroadphase()
        {
            for (int i = 0; i < children.Length; i++)
            {
                var slot = children[i];
                if (slot != null && slot.alive) Physics.World.Touch(slot, slot.worldBounds);
            }
        }
    }
}
