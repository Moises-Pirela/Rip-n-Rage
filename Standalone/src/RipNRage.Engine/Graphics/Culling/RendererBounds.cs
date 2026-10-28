using System;

namespace RipNRage.Engine.Graphics.Culling
{
    /// <summary>World-space axis-aligned bounds for renderers (used by frustum and occlusion culling).</summary>
    public static class RendererBounds
    {
        /// <summary>Transforms the mesh's local bounds by the renderer's localToWorld matrix and returns the enclosing AABB.</summary>
        public static bool GetWorldAABB(MeshRenderer mr, out Vector3 min, out Vector3 max)
        {
            min = max = Vector3.zero;
            var mesh = mr.mesh;
            if (mesh == null) return false;
            if (mesh.boundsMin == mesh.boundsMax && mesh.vertices.Length > 0) mesh.RecalculateBounds();
            TransformAABB(mr.transform.localToWorldMatrix, mesh.boundsMin, mesh.boundsMax, out min, out max);
            return true;
        }

        /// <summary>
        /// Bounds of a sprite quad. Billboards rotate around Y so a cylinder-ish box (max of width/height extents) is used;
        /// static sprites use their rotated quad corners.
        /// </summary>
        public static bool GetWorldAABB(SpriteRenderer sr, out Vector3 min, out Vector3 max)
        {
            min = max = Vector3.zero;
            var sp = sr.sprite;
            if (sp == null) return false;
            sp.GetLocalCorners(out Vector2 lmin, out Vector2 lmax);
            if (sr.billboard)
            {
                Vector3 s = sr.transform.lossyScale;
                Vector3 p = sr.transform.position;
                float w = MathF.Max(MathF.Abs(lmin.x), MathF.Abs(lmax.x)) * MathF.Abs(s.x);
                float r = w; // any yaw: the quad sweeps a cylinder of radius = half width
                float y0 = lmin.y * s.y, y1 = lmax.y * s.y;
                if (y0 > y1) { float t = y0; y0 = y1; y1 = t; }
                min = new Vector3(p.x - r, p.y + y0, p.z - r);
                max = new Vector3(p.x + r, p.y + y1, p.z + r);
                return true;
            }
            TransformAABB(sr.transform.localToWorldMatrix, new Vector3(lmin.x, lmin.y, 0f), new Vector3(lmax.x, lmax.y, 0f), out min, out max);
            return true;
        }

        /// <summary>AABB of a local box after an affine transform (Arvo's method: |M| * extents).</summary>
        public static void TransformAABB(in Matrix4x4 m, in Vector3 lmin, in Vector3 lmax, out Vector3 min, out Vector3 max)
        {
            Vector3 c = (lmin + lmax) * 0.5f;
            Vector3 e = (lmax - lmin) * 0.5f;
            Vector3 wc = m.MultiplyPoint3x4(c);
            float ex = MathF.Abs(m.m00) * e.x + MathF.Abs(m.m01) * e.y + MathF.Abs(m.m02) * e.z;
            float ey = MathF.Abs(m.m10) * e.x + MathF.Abs(m.m11) * e.y + MathF.Abs(m.m12) * e.z;
            float ez = MathF.Abs(m.m20) * e.x + MathF.Abs(m.m21) * e.y + MathF.Abs(m.m22) * e.z;
            min = new Vector3(wc.x - ex, wc.y - ey, wc.z - ez);
            max = new Vector3(wc.x + ex, wc.y + ey, wc.z + ez);
        }
    }

    /// <summary>Per-frame culling counters shown by the debug overlay.</summary>
    public sealed class CullingStats
    {
        public int meshes, meshesFrustumCulled, meshesOccluded;
        public int sprites, spritesFrustumCulled, spritesOccluded;
        public int lights, lightsCulled;
        public int occluderTriangles;
        public double occlusionMs;

        public int Total => meshes + sprites;
        public int Culled => meshesFrustumCulled + meshesOccluded + spritesFrustumCulled + spritesOccluded;

        public void Reset()
        {
            meshes = meshesFrustumCulled = meshesOccluded = 0;
            sprites = spritesFrustumCulled = spritesOccluded = 0;
            lights = lightsCulled = 0;
            occluderTriangles = 0; occlusionMs = 0;
        }

        public override string ToString() =>
            $"MESHES {meshes - meshesFrustumCulled - meshesOccluded}/{meshes} (F{meshesFrustumCulled} O{meshesOccluded})  SPRITES {sprites - spritesFrustumCulled - spritesOccluded}/{sprites} (F{spritesFrustumCulled} O{spritesOccluded})  LIGHTS {lights - lightsCulled}/{lights}  OCCLUDER TRIS {occluderTriangles} {occlusionMs:0.0}MS";
    }
}
