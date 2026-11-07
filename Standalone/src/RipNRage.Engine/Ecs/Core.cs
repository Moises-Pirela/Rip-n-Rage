using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RipNRage.Engine.Ecs
{
    /// <summary>Runtime component data: an unmanaged-friendly struct stored in archetype columns or a sparse set.</summary>
    public interface IComponent { }

    /// <summary>
    /// Authoring data for exactly one runtime component (a class, serialisable to JSON prefabs and editable by
    /// tools). <see cref="Bake"/> produces the struct an entity starts with.
    /// </summary>
    public interface IComponentConfig
    {
        Type ComponentType { get; }
        /// <summary>Adds the baked component to <paramref name="entity"/> through <paramref name="commands"/>.</summary>
        void BakeInto(CommandBuffer commands, Entity entity);
    }

    /// <summary>Typed config: implement <see cref="Bake"/>; the plumbing is provided.</summary>
    public abstract class ComponentConfig<T> : IComponentConfig where T : struct, IComponent
    {
        public Type ComponentType => typeof(T);
        public abstract T Bake();
        public void BakeInto(CommandBuffer commands, Entity entity) => commands.Add(entity, Bake());
    }

    /// <summary>
    /// Stored in a per-type sparse set instead of the archetype tables: adding / removing it never moves the entity
    /// between tables. Use for tags and frequently toggled state (stunned, grabbed, dying).
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class SparseComponentAttribute : Attribute { }

    /// <summary>An entity handle: index into the world's entity table plus a generation that detects stale handles.</summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly int Index;
        public readonly int Generation;
        public Entity(int index, int generation) { Index = index; Generation = generation; }
        public static readonly Entity Null = default;
        public bool IsNull => Generation == 0;
        public bool Equals(Entity o) => Index == o.Index && Generation == o.Generation;
        public override bool Equals(object o) => o is Entity e && Equals(e);
        public override int GetHashCode() => HashCode.Combine(Index, Generation);
        public static bool operator ==(Entity a, Entity b) => a.Equals(b);
        public static bool operator !=(Entity a, Entity b) => !a.Equals(b);
        public override string ToString() => IsNull ? "Entity(null)" : $"Entity({Index}:{Generation})";
    }

    /// <summary>A fixed 256-bit component signature (one bit per registered component type).</summary>
    public struct Signature : IEquatable<Signature>
    {
        public const int Capacity = 256;
        public ulong A, B, C, D;

        public static Signature Of(int bit) { var s = new Signature(); s.Set(bit); return s; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int bit)
        {
            if ((uint)bit >= Capacity) throw new ArgumentOutOfRangeException(nameof(bit), $"component index {bit} exceeds the {Capacity}-bit signature");
            ulong m = 1UL << (bit & 63);
            switch (bit >> 6) { case 0: A |= m; break; case 1: B |= m; break; case 2: C |= m; break; default: D |= m; break; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear(int bit)
        {
            ulong m = ~(1UL << (bit & 63));
            switch (bit >> 6) { case 0: A &= m; break; case 1: B &= m; break; case 2: C &= m; break; default: D &= m; break; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Has(int bit)
        {
            ulong m = 1UL << (bit & 63);
            return (bit >> 6) switch { 0 => (A & m) != 0, 1 => (B & m) != 0, 2 => (C & m) != 0, _ => (D & m) != 0 };
        }

        /// <summary>Every bit of <paramref name="required"/> is set here.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ContainsAll(in Signature required) =>
            (A & required.A) == required.A && (B & required.B) == required.B && (C & required.C) == required.C && (D & required.D) == required.D;

        /// <summary>At least one bit in common (an empty mask counts as "any" = true).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Intersects(in Signature other) => ((A & other.A) | (B & other.B) | (C & other.C) | (D & other.D)) != 0;

        public readonly bool IsEmpty => (A | B | C | D) == 0;
        public readonly int Count => BitOperations.PopCount(A) + BitOperations.PopCount(B) + BitOperations.PopCount(C) + BitOperations.PopCount(D);

        public static Signature operator |(Signature x, Signature y) => new Signature { A = x.A | y.A, B = x.B | y.B, C = x.C | y.C, D = x.D | y.D };
        public static Signature operator &(Signature x, Signature y) => new Signature { A = x.A & y.A, B = x.B & y.B, C = x.C & y.C, D = x.D & y.D };
        public static Signature operator ~(Signature x) => new Signature { A = ~x.A, B = ~x.B, C = ~x.C, D = ~x.D };

        public bool Equals(Signature o) => A == o.A && B == o.B && C == o.C && D == o.D;
        public override bool Equals(object o) => o is Signature s && Equals(s);
        public override int GetHashCode() => HashCode.Combine(A, B, C, D);
        public static bool operator ==(Signature x, Signature y) => x.Equals(y);
        public static bool operator !=(Signature x, Signature y) => !x.Equals(y);
    }
}
