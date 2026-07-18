using System;
using System.Collections;
using System.Collections.Generic;

namespace Spine.Collections
{
    /// <summary>
    /// Read-only contiguous snapshot storage. CopyOf prevents later writes through the source array.
    /// </summary>
    public sealed class ImmutableSnapshotArray<T> : IList<T>
    {
        private static readonly ImmutableSnapshotArray<T> EmptyInstance =
            new ImmutableSnapshotArray<T>(ArrayCompat.Empty<T>());

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

        public bool IsReadOnly => true;

        public T this[int index]
        {
            get => _items[index];
            set => throw new NotSupportedException("Immutable snapshots cannot be modified.");
        }

        public int IndexOf(T item) => Array.IndexOf(_items, item);

        public bool Contains(T item) => IndexOf(item) >= 0;

        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public void Add(T item) => throw new NotSupportedException("Immutable snapshots cannot be modified.");

        public void Clear() => throw new NotSupportedException("Immutable snapshots cannot be modified.");

        public void Insert(int index, T item) => throw new NotSupportedException("Immutable snapshots cannot be modified.");

        public bool Remove(T item) => throw new NotSupportedException("Immutable snapshots cannot be modified.");

        public void RemoveAt(int index) => throw new NotSupportedException("Immutable snapshots cannot be modified.");

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
