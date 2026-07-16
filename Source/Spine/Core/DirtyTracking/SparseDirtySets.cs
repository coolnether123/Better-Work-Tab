using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Spine.Api
{
    public readonly struct DirtyRegion : IEquatable<DirtyRegion>
    {
        public DirtyRegion(int start, int length)
        {
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            if (length <= 0 || start > int.MaxValue - length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            Start = start;
            Length = length;
        }

        public int Start { get; }
        public int Length { get; }
        public int EndExclusive => Start + Length;

        public bool Equals(DirtyRegion other)
        {
            return Start == other.Start && Length == other.Length;
        }

        public override bool Equals(object obj)
        {
            return obj is DirtyRegion other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Start * 397) ^ Length;
            }
        }

        public override string ToString()
        {
            return "[" + Start + ", " + EndExclusive + ")";
        }
    }

    public interface IDirtyRegionSource
    {
        int DirtyRegionCount { get; }
        IReadOnlyList<DirtyRegion> DirtyRegions { get; }
    }
}

namespace Spine.DirtyTracking
{
    using Spine.Api;

    /// <summary>
    /// Maintains sorted, non-overlapping integer regions and coalesces adjacent writes.
    /// </summary>
    public sealed class SparseDirtyRegionSet : IDirtyRegionSource
    {
        private readonly List<DirtyRegion> _regions = new List<DirtyRegion>();
        private readonly ReadOnlyCollection<DirtyRegion> _readOnlyRegions;

        public SparseDirtyRegionSet()
        {
            _readOnlyRegions = _regions.AsReadOnly();
        }

        public int DirtyRegionCount => _regions.Count;
        public IReadOnlyList<DirtyRegion> DirtyRegions => _readOnlyRegions;

        public void Add(int start, int length)
        {
            Add(new DirtyRegion(start, length));
        }

        public void Add(DirtyRegion region)
        {
            int mergedStart = region.Start;
            int mergedEnd = region.EndExclusive;
            int index = 0;

            while (index < _regions.Count && _regions[index].EndExclusive < mergedStart)
            {
                index++;
            }

            int removeStart = index;
            while (index < _regions.Count && _regions[index].Start <= mergedEnd)
            {
                mergedStart = Math.Min(mergedStart, _regions[index].Start);
                mergedEnd = Math.Max(mergedEnd, _regions[index].EndExclusive);
                index++;
            }

            int removeCount = index - removeStart;
            if (removeCount > 0)
            {
                _regions.RemoveRange(removeStart, removeCount);
            }

            _regions.Insert(removeStart, new DirtyRegion(mergedStart, mergedEnd - mergedStart));
        }

        public void Clear()
        {
            _regions.Clear();
        }
    }

    /// <summary>
    /// Sparse identity set for invalidation targets that do not form numeric ranges.
    /// </summary>
    public sealed class SparseDirtySet<T>
    {
        private readonly HashSet<T> _items;

        public SparseDirtySet(IEqualityComparer<T> comparer = null)
        {
            _items = new HashSet<T>(comparer ?? EqualityComparer<T>.Default);
        }

        public int Count => _items.Count;
        public bool Add(T item) => _items.Add(item);
        public bool Contains(T item) => _items.Contains(item);
        public bool Remove(T item) => _items.Remove(item);
        public void Clear() => _items.Clear();
        public void CopyTo(T[] destination, int destinationIndex) => _items.CopyTo(destination, destinationIndex);
    }
}
