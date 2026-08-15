using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

#if RWT_LEGACY_BCL
namespace Spine
{
    internal sealed class LegacyMonitorScope : IDisposable
    {
        private readonly object sync;

        internal LegacyMonitorScope(object sync)
        {
            this.sync = sync;
            Monitor.Enter(sync);
        }

        public void Dispose()
        {
            Monitor.Exit(sync);
        }
    }

    internal static class LegacyBcl
    {
        internal static IDisposable Enter(object sync)
        {
            return new LegacyMonitorScope(sync);
        }

        public static bool IsNull<T>(T value) => object.ReferenceEquals(value, null);
        public static bool IsNotNull<T>(T value) => !object.ReferenceEquals(value, null);
        public static bool IsNullOrWhiteSpace(string value)
        {
            if (value == null) return true;
            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i])) return false;
            }

            return true;
        }

        public static string TrimEndWhitespace(string value)
        {
            if (value == null) return null;
            int end = value.Length;
            while (end > 0 && char.IsWhiteSpace(value[end - 1])) end--;
            return end == value.Length ? value : value.Substring(0, end);
        }
    }
#else
namespace Spine
{
    internal static class LegacyBcl
    {
        internal static IDisposable Enter(object sync)
        {
            return new LegacyMonitorScopeModern(sync);
        }

        public static bool IsNull<T>(T value) => object.ReferenceEquals(value, null);
        public static bool IsNotNull<T>(T value) => !object.ReferenceEquals(value, null);
        public static bool IsNullOrWhiteSpace(string value) => string.IsNullOrWhiteSpace(value);
        public static string TrimEndWhitespace(string value) => value == null ? null : value.TrimEnd();
    }

    internal sealed class LegacyMonitorScopeModern : IDisposable
    {
        private readonly object sync;

        internal LegacyMonitorScopeModern(object sync)
        {
            this.sync = sync;
            Monitor.Enter(sync);
        }

        public void Dispose()
        {
            Monitor.Exit(sync);
        }
    }
#endif

#if RWT_LEGACY_BCL
    // Mono's RimWorld 1.0 mscorlib stops at Func<T1..T5>.
    public delegate TResult LegacyFunc6<T1, T2, T3, T4, T5, TResult>(
        T1 arg1,
        T2 arg2,
        T3 arg3,
        T4 arg4,
        T5 arg5);

    // RimWorld 1.0 runs on a Mono profile that cannot resolve the BCL
    // IReadOnlyCollection/IReadOnlyList contracts from a mod assembly. Keep
    // the settings capability surface stable without defining types in the
    // System namespace; nested Spine namespaces resolve these contracts.
    public interface IReadOnlyCollection<T> : System.Collections.Generic.IEnumerable<T>, IEnumerable
    {
        int Count { get; }
    }

    public interface IReadOnlyList<T> : IReadOnlyCollection<T>
    {
        T this[int index] { get; }
    }

    internal sealed class ReadOnlyListAdapter<T> : IReadOnlyList<T>
    {
        private readonly System.Collections.Generic.List<T> items;

        public ReadOnlyListAdapter(System.Collections.Generic.IEnumerable<T> source)
        {
            items = source == null
                ? new System.Collections.Generic.List<T>()
                : new System.Collections.Generic.List<T>(source);
        }

        public int Count { get { return items.Count; } }

        public T this[int index] { get { return items[index]; } }

        public System.Collections.Generic.IEnumerator<T> GetEnumerator() { return items.GetEnumerator(); }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
#endif

    internal static class LegacyReadOnlyCollections
    {
#if RWT_LEGACY_BCL
        public static IReadOnlyList<T> WrapList<T>(System.Collections.Generic.IEnumerable<T> source)
        {
            var existing = source as IReadOnlyList<T>;
            return existing ?? new ReadOnlyListAdapter<T>(source);
        }

        public static IReadOnlyList<T> EmptyList<T>()
        {
            return new ReadOnlyListAdapter<T>(null);
        }

        public static IReadOnlyCollection<T> WrapCollection<T>(System.Collections.Generic.IEnumerable<T> source)
        {
            return WrapList(source);
        }
#else
        public static System.Collections.Generic.IReadOnlyList<T> WrapList<T>(System.Collections.Generic.IEnumerable<T> source)
        {
            var existing = source as System.Collections.Generic.IReadOnlyList<T>;
            return existing ?? new System.Collections.Generic.List<T>(source).AsReadOnly();
        }

        public static System.Collections.Generic.IReadOnlyList<T> EmptyList<T>()
        {
            return new System.Collections.Generic.List<T>().AsReadOnly();
        }

        public static System.Collections.Generic.IReadOnlyCollection<T> WrapCollection<T>(System.Collections.Generic.IEnumerable<T> source)
        {
            var existing = source as System.Collections.Generic.IReadOnlyCollection<T>;
            return existing ?? new System.Collections.Generic.List<T>(source).AsReadOnly();
        }
#endif
    }
}
