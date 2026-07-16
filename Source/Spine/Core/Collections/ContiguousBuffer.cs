using System;

namespace Spine.Collections
{
    /// <summary>
    /// Reusable contiguous storage for snapshot builders. Published snapshots are copied arrays.
    /// </summary>
    public sealed class ContiguousBuffer<T>
    {
        private T[] _items;

        public ContiguousBuffer(int initialCapacity = 0)
        {
            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _items = initialCapacity == 0 ? Array.Empty<T>() : new T[initialCapacity];
        }

        public int Count { get; private set; }
        public int Capacity => _items.Length;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _items[index];
            }
            set
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                _items[index] = value;
            }
        }

        public void Add(T item)
        {
            EnsureCapacity(Count + 1);
            _items[Count++] = item;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, Count);
            Count = 0;
        }

        public T[] ToArray()
        {
            if (Count == 0)
            {
                return Array.Empty<T>();
            }

            var snapshot = new T[Count];
            Array.Copy(_items, snapshot, Count);
            return snapshot;
        }

        public ImmutableSnapshotArray<T> ToSnapshot()
        {
            return ImmutableSnapshotArray<T>.TakeOwnership(ToArray());
        }

        private void EnsureCapacity(int required)
        {
            if (_items.Length >= required)
            {
                return;
            }

            int capacity = _items.Length == 0 ? 4 : _items.Length * 2;
            if (capacity < required)
            {
                capacity = required;
            }

            Array.Resize(ref _items, capacity);
        }
    }
}
