using System;
using System.Collections;
using System.Collections.Generic;

namespace Spine.Collections
{
    /// <summary>
    /// Read-only contiguous snapshot storage. CopyOf prevents later writes through the source array.
    /// </summary>
    public sealed class ImmutableSnapshotArray<T> : IReadOnlyList<T>
    {
        private static readonly ImmutableSnapshotArray<T> EmptyInstance =
            new ImmutableSnapshotArray<T>(Array.Empty<T>());

        private readonly T[] _items;

        private ImmutableSnapshotArray(T[] ownedItems)
        {
            _items = ownedItems;
        }

        public static ImmutableSnapshotArray<T> Empty => EmptyInstance;

        public static ImmutableSnapshotArray<T> CopyOf(T[] items)
        {
            if (items == null || items.Length == 0)
            {
                return Empty;
            }

            var copy = new T[items.Length];
            Array.Copy(items, copy, items.Length);
            return new ImmutableSnapshotArray<T>(copy);
        }

        internal static ImmutableSnapshotArray<T> TakeOwnership(T[] items)
        {
            return items == null || items.Length == 0
                ? Empty
                : new ImmutableSnapshotArray<T>(items);
        }

        public int Count => _items.Length;

        public T this[int index] => _items[index];

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }

    /// <summary>
    /// Lifecycle-owned publication slot. Clearing releases the complete snapshot graph at teardown.
    /// </summary>
    public sealed class SnapshotSlot<T> where T : class
    {
        public T Current { get; private set; }

        public void Publish(T snapshot)
        {
            Current = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public void Clear()
        {
            Current = null;
        }
    }
}
