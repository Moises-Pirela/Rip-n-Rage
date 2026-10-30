using System;
using System.Collections.Generic;

/// <summary>Well-known surface names used by the VFX bindings (any string works; these are the ones the defaults cover).</summary>
public static class Surfaces
{
    public const string Default = "default";
    public const string Concrete = "concrete";
    public const string Wood = "wood";
    public const string Metal = "metal";
    public const string Glass = "glass";
    public const string Sand = "sand";
    public const string Water = "water";
    public const string Flesh = "flesh";
}

/// <summary>
/// Tags a collider (or a whole hierarchy) with the surface bullets hit: impact effects and decals are looked
/// up by this name in <c>vfx/bindings.json</c>. Put it on props (crates = wood, lamp posts = metal…);
/// level brushes get a <see cref="BrushSurface"/> instead, which knows the texture of every face.
/// </summary>
public class SurfaceMaterial : MonoBehaviour
{
    public string surface = Surfaces.Concrete;
}

/// <summary>
/// Per-face surface lookup for a level brush collider: the hit normal picks the face, the face's texture name
/// maps to a surface through the level texture table (filled in by the level loader).
/// </summary>
public class BrushSurface : MonoBehaviour
{
    public struct FaceSurface { public Vector3 normal; public string surface; }
    public readonly List<FaceSurface> faces = new List<FaceSurface>();
    public string fallback = Surfaces.Concrete;

    public string Resolve(Vector3 hitNormal)
    {
        string best = fallback; float bestDot = -2f;
        foreach (var f in faces)
        {
            float d = Vector3.Dot(f.normal, hitNormal);
            if (d > bestDot) { bestDot = d; best = f.surface; }
        }
        return best;
    }
}

/// <summary>Figures out what a raycast hit: a living thing (flesh), a tagged prop, a level brush face, or the default surface.</summary>
public static class SurfaceResolver
{
    public static string Resolve(in RaycastHit hit)
    {
        var col = hit.collider;
        if (col == null) return Surfaces.Default;
        var go = col.gameObject;
        if (go.GetComponent<EntityState>() != null) return Surfaces.Flesh;
        var brush = go.GetComponent<BrushSurface>();
        if (brush != null) return brush.Resolve(hit.normal);
        var mat = go.GetComponentInParent<SurfaceMaterial>();
        if (mat != null && !string.IsNullOrEmpty(mat.surface)) return mat.surface;
        if (go.layer == Layers.Water) return Surfaces.Water;
        return Surfaces.Default;
    }
}
