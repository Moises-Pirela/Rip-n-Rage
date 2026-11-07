using System;
using System.Collections.Generic;

namespace RipNRage.Engine.Ecs
{
    /// <summary>A typed column of one archetype (an array of one component type, row-aligned with the entities).</summary>
    internal abstract class Column
    {
        public abstract void Resize(int capacity);
        public abstract void MoveRow(int from, int to);                 // within the column (swap-remove)
        public abstract void CopyRowTo(int row, Column dst, int dstRow); // between archetypes
        public abstract void ClearRow(int row);
        public abstract Array Raw { get; }
    }

    internal sealed class Column<T> : Column where T : struct
    {
        public T[] Data = new T[8];
        public override void Resize(int capacity) => Array.Resize(ref Data, capacity);
        public override void MoveRow(int from, int to) => Data[to] = Data[from];
        public override void CopyRowTo(int row, Column dst, int dstRow) => ((Column<T>)dst).Data[dstRow] = Data[row];
        public override void ClearRow(int row) => Data[row] = default;
        public override Array Raw => Data;
    }

    /// <summary>
    /// A table of every entity with exactly this set of (non-sparse) components: one dense column per component.
    /// Iteration is linear over contiguous arrays; adding or removing a table component moves the entity to another
    /// archetype (sparse components never cause a move).
    /// </summary>
    public sealed class Archetype
    {
        public readonly Signature Signature;
        internal readonly int[] ComponentIds;
        internal readonly Column[] Columns;
        private readonly int[] _columnOf = new int[Signature.Capacity]; // component id -> column index (-1)
        internal Entity[] Entities = new Entity[8];
        public int Count { get; internal set; }
        internal readonly Dictionary<int, Archetype> AddEdge = new Dictionary<int, Archetype>(), RemoveEdge = new Dictionary<int, Archetype>();
        /// <summary>Bumped on every structural change: jobs assert the table did not change under them.</summary>
        internal int Version;

        internal Archetype(Signature sig, int[] ids)
        {
            Signature = sig; ComponentIds = ids;
            Array.Fill(_columnOf, -1);
            Columns = new Column[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var t = ComponentRegistry.Get(ids[i]).Type;
                Columns[i] = (Column)Activator.CreateInstance(typeof(Column<>).MakeGenericType(t));
                _columnOf[ids[i]] = i;
            }
        }

        internal int ColumnOf(int componentId) => _columnOf[componentId];

        internal T[] Data<T>(int componentId) where T : struct
        {
            int c = _columnOf[componentId];
            if (c < 0) throw new InvalidOperationException($"archetype has no {ComponentRegistry.Get(componentId).Type.Name}");
            return ((Column<T>)Columns[c]).Data;
        }

        internal int AddRow(Entity e)
        {
            if (Count == Entities.Length)
            {
                int cap = Entities.Length * 2;
                Array.Resize(ref Entities, cap);
                foreach (var col in Columns) col.Resize(cap);
            }
            Entities[Count] = e;
            Version++;
            return Count++;
        }

        /// <summary>Swap-removes a row; returns the entity that moved into it (or null if it was the last row).</summary>
        internal Entity RemoveRow(int row)
        {
            int last = Count - 1;
            Entity moved = Entity.Null;
            if (row != last)
            {
                foreach (var col in Columns) col.MoveRow(last, row);
                Entities[row] = Entities[last];
                moved = Entities[row];
            }
            foreach (var col in Columns) col.ClearRow(last);
            Entities[last] = Entity.Null;
            Count--; Version++;
            return moved;
        }

        public override string ToString() => $"Archetype[{string.Join(",", Array.ConvertAll(ComponentIds, i => ComponentRegistry.Get(i).Type.Name))}] x{Count}";
    }

    internal abstract class SparseSetBase
    {
        public abstract bool Remove(int entityIndex);
        public abstract int Count { get; }
    }

    /// <summary>Sparse set for one <see cref="SparseComponentAttribute"/> type: O(1) add / remove / lookup by entity index.</summary>
    internal sealed class SparseSet<T> : SparseSetBase where T : struct
    {
        private int[] _sparse = new int[64];     // entity index -> dense index + 1 (0 = absent)
        internal T[] Dense = new T[16];
        internal int[] DenseEntity = new int[16];
        private int _count;
        public override int Count => _count;

        public bool Has(int e) => e < _sparse.Length && _sparse[e] != 0;
        public ref T Get(int e)
        {
            if (!Has(e)) throw new InvalidOperationException($"entity {e} has no {typeof(T).Name}");
            return ref Dense[_sparse[e] - 1];
        }

        public void Set(int e, in T value)
        {
            if (e >= _sparse.Length) Array.Resize(ref _sparse, Math.Max(e + 1, _sparse.Length * 2));
            if (_sparse[e] != 0) { Dense[_sparse[e] - 1] = value; return; }
            if (_count == Dense.Length) { Array.Resize(ref Dense, _count * 2); Array.Resize(ref DenseEntity, _count * 2); }
            Dense[_count] = value; DenseEntity[_count] = e;
            _sparse[e] = ++_count;
        }

        public override bool Remove(int e)
        {
            if (!Has(e)) return false;
            int d = _sparse[e] - 1, last = _count - 1;
            Dense[d] = Dense[last]; DenseEntity[d] = DenseEntity[last];
            _sparse[DenseEntity[d]] = d + 1;
            Dense[last] = default;
            _sparse[e] = 0; _count--;
            return true;
        }
    }
}
