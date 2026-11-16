using System;
using System.Collections.Generic;
using RipNRage.Engine.Ecs;
using RipNRage.Game.Ecs.Combat;

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

    private static readonly List<string> _names = new List<string> { Default };
    private static readonly Dictionary<string, int> _ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [Default] = 0 };

    /// <summary>The interned id of a surface name (components hold the id: plain data). 0 is <see cref="Default"/>.
    /// Names are keys, not labels: they are stored lower-case, so "Wood" and "wood" are one surface and resolve to
    /// the spelling the VFX bindings use, whichever was seen first.</summary>
    public static int Id(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        lock (_names)
        {
            if (_ids.TryGetValue(name, out int id)) return id;
            _names.Add(name.ToLowerInvariant());
            return _ids[name] = _names.Count - 1;
        }
    }

    public static string Name(int id) => id > 0 && id < _names.Count ? _names[id] : Default;
    /// <summary>Every surface name known so far (the editors' pickers).</summary>
    public static IEnumerable<string> All { get { lock (_names) return new List<string>(_names); } }

    /// <summary>The per-face tables of the level brushes, as assets the components hold handles to.</summary>
    public static readonly AssetRegistry<RipNRage.Game.Ecs.BrushFaces> FaceTables = new AssetRegistry<RipNRage.Game.Ecs.BrushFaces>();

    /// <summary>Tags a collider with the surface bullets hit — through the command buffer, because the colliders
    /// it is used on are built through it (<c>PhysicsEntities</c>), so the entity is not alive until playback.</summary>
    public static void Tag(World w, Entity collider, string surface) =>
        w.Commands.Add(collider, new RipNRage.Game.Ecs.ImpactSurface { Id = Id(surface) });

    /// <summary>Tags a brush collider with its per-face table (the hit normal picks the face).</summary>
    public static void TagFaces(World w, Entity collider, RipNRage.Game.Ecs.BrushFaces faces) =>
        w.Commands.Add(collider, new RipNRage.Game.Ecs.ImpactSurface { Id = faces.Fallback, Faces = FaceTables.Intern(faces) });

    /// <summary>
    /// What a hit landed on: a living thing (flesh), a tagged collider (its surface, or for a brush the face the
    /// normal points out of), or the default. Since 7.7 this reads the hit <b>entity</b> — the old resolver looked
    /// for a MonoBehaviour on a GameObject the physics no longer knew about, and had been stubbed to "default"
    /// since layer 2.
    /// </summary>
    public static string Of(World w, Entity e, Vector3 normal)
    {
        if (w == null || e.IsNull || !w.IsAlive(e)) return Default;
        if (w.Has<Health>(e)) return Flesh;
        if (!w.Has<RipNRage.Game.Ecs.ImpactSurface>(e)) return Default;
        var s = w.Get<RipNRage.Game.Ecs.ImpactSurface>(e);
        var faces = FaceTables.Resolve(s.Faces);
        return Name(faces != null ? faces.Resolve(normal) : s.Id);
    }
}

/// <summary>Figures out what a raycast hit (the weapons' impact effects).</summary>
public static class SurfaceResolver
{
    public static string Resolve(in RaycastHit hit) => Surfaces.Of(Application.Current?.World, hit.entity, hit.normal);
}

namespace RipNRage.Game.Ecs
{
    /// <summary>
    /// The surface a collider is made of, for the impact effects and decals (<c>vfx/bindings.json</c>): an interned
    /// surface id, or for a level brush a handle to its per-face table. Not the physics <c>SurfaceMaterial</c>
    /// (friction, bounce) — that one says how it slides, this one says what a bullet kicks up.
    /// </summary>
    public struct ImpactSurface : IComponent
    {
        public int Id;
        /// <summary>Per-face table (level brushes); None for a collider that is one material all over.</summary>
        public AssetHandle<BrushFaces> Faces;
    }

    /// <summary>A brush's faces: outward normals and the surface of each (from the level texture table).</summary>
    public sealed class BrushFaces
    {
        public readonly Vector3[] Normals;
        public readonly int[] Ids;
        public readonly int Fallback;

        public BrushFaces(Vector3[] normals, int[] ids, int fallback) { Normals = normals; Ids = ids; Fallback = fallback; }

        /// <summary>The face whose normal points most along the hit normal.</summary>
        public int Resolve(Vector3 hitNormal)
        {
            int best = Fallback; float bestDot = -2f;
            for (int i = 0; i < Normals.Length; i++)
            {
                float d = Vector3.Dot(Normals[i], hitNormal);
                if (d > bestDot) { bestDot = d; best = Ids[i]; }
            }
            return best;
        }
    }
}
