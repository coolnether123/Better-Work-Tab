using System;
using System.Collections.Generic;

namespace Spine.Api
{
    public interface ICacheDiagnostics
    {
        int EntryCount { get; }
        long BudgetBytes { get; }
        long UsedBytes { get; }
        long Hits { get; }
        long Misses { get; }
        long Evictions { get; }
    }

    public interface IBoundedCache<TKey, TValue> : ICacheDiagnostics
    {
        bool TryGet(TKey key, out TValue value);
        bool AddOrUpdate(TKey key, TValue value, long byteSize);
        bool Remove(TKey key);
        void Reset();
    }
}

namespace Spine.Caching
{
    using Spine.Api;

    /// <summary>
    /// Main-thread LRU cache with caller-supplied byte sizes and deterministic eviction.
    /// </summary>
    public sealed class BoundedLruCache<TKey, TValue> : IBoundedCache<TKey, TValue>
    {
        private sealed class Entry
        {
            public TKey Key;
            public TValue Value;
            public long ByteSize;
        }

        private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries;
        private readonly LinkedList<Entry> _recency = new LinkedList<Entry>();
        private readonly Action<TKey, TValue> _release;
        private long _usedBytes;
        private long _hits;
        private long _misses;
        private long _evictions;

        public BoundedLruCache(long budgetBytes, IEqualityComparer<TKey> comparer = null, Action<TKey, TValue> release = null)
        {
            if (budgetBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            }

            BudgetBytes = budgetBytes;
            _entries = new Dictionary<TKey, LinkedListNode<Entry>>(comparer ?? EqualityComparer<TKey>.Default);
            _release = release;
        }

        public int EntryCount => _entries.Count;
        public long BudgetBytes { get; }
        public long UsedBytes => _usedBytes;
        public long Hits => _hits;
        public long Misses => _misses;
        public long Evictions => _evictions;

        public bool TryGet(TKey key, out TValue value)
        {
            if (_entries.TryGetValue(key, out LinkedListNode<Entry> node))
            {
                _hits++;
                _recency.Remove(node);
                _recency.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            _misses++;
            value = default(TValue);
            return false;
        }

        public bool AddOrUpdate(TKey key, TValue value, long byteSize)
        {
            if (byteSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteSize), "Cache entries must report a positive byte size.");
            }

            if (byteSize > BudgetBytes)
            {
                return false;
            }

            if (_entries.TryGetValue(key, out LinkedListNode<Entry> existing))
            {
                _entries.Remove(key);
                _recency.Remove(existing);
                _usedBytes -= existing.Value.ByteSize;
                Release(existing.Value);
            }

            var entry = new Entry { Key = key, Value = value, ByteSize = byteSize };
            LinkedListNode<Entry> node = _recency.AddFirst(entry);
            _entries.Add(key, node);
            _usedBytes += byteSize;
            EvictToBudget();
            return true;
        }

        public bool Remove(TKey key)
        {
            if (!_entries.TryGetValue(key, out LinkedListNode<Entry> node))
            {
                return false;
            }

            _entries.Remove(key);
            _recency.Remove(node);
            _usedBytes -= node.Value.ByteSize;
            Release(node.Value);
            return true;
        }

        public void Reset()
        {
            if (_release != null)
            {
                foreach (Entry entry in _recency)
                {
                    Release(entry);
                }
            }

            _entries.Clear();
            _recency.Clear();
            _usedBytes = 0;
            _hits = 0;
            _misses = 0;
            _evictions = 0;
        }

        private void EvictToBudget()
        {
            while (_usedBytes > BudgetBytes)
            {
                LinkedListNode<Entry> node = _recency.Last;
                _recency.RemoveLast();
                _entries.Remove(node.Value.Key);
                _usedBytes -= node.Value.ByteSize;
                _evictions++;
                Release(node.Value);
            }
        }

        private void Release(Entry entry)
        {
            _release?.Invoke(entry.Key, entry.Value);
        }
    }
}
