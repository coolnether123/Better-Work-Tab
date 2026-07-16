#if v1_0
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace System
{
    public static class ArrayCompat
    {
        public static T[] Empty<T>()
        {
            return new T[0];
        }
    }

    public static class StringCompat
    {
        public static bool IsNullOrWhiteSpace(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public delegate TResult Func<in T1, in T2, in T3, in T4, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);

    public sealed class Lazy<T>
    {
        private readonly Func<T> valueFactory;
        private bool hasValue;
        private T value;

        public Lazy(Func<T> valueFactory)
        {
            if (valueFactory == null)
                throw new ArgumentNullException("valueFactory");

            this.valueFactory = valueFactory;
        }

        public T Value
        {
            get
            {
                if (!hasValue)
                {
                    value = valueFactory();
                    hasValue = true;
                }

                return value;
            }
        }

        public bool IsValueCreated
        {
            get { return hasValue; }
        }
    }

    public class Tuple<T1, T2>
    {
        public Tuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        public T1 Item1 { get; private set; }
        public T2 Item2 { get; private set; }
    }

    public static class Tuple
    {
        public static Tuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2)
        {
            return new Tuple<T1, T2>(item1, item2);
        }
    }

    public struct ValueTuple<T1, T2>
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
    }

    public struct ValueTuple<T1, T2, T3>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }
    }
}

namespace System.Collections.Generic
{
    public interface IReadOnlyCollection<T> : IEnumerable<T>
    {
        int Count { get; }
    }

    public interface IReadOnlyList<T> : IReadOnlyCollection<T>
    {
        T this[int index] { get; }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public sealed class TupleElementNamesAttribute : Attribute
    {
        public TupleElementNamesAttribute(string[] transformNames)
        {
            TransformNames = transformNames;
        }

        public IList TransformNames { get; private set; }
    }

    public sealed class ConditionalWeakTable<TKey, TValue>
        where TKey : class
        where TValue : class
    {
        private readonly Dictionary<TKey, TValue> entries = new Dictionary<TKey, TValue>();

        public void Add(TKey key, TValue value)
        {
            entries.Add(key, value);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return entries.TryGetValue(key, out value);
        }

        public bool Remove(TKey key)
        {
            return entries.Remove(key);
        }
    }
}

namespace Better_Work_Tab
{
    public static class LegacyVolatile
    {
        private static readonly object SyncRoot = new object();

        public static int Read(ref int location)
        {
            lock (SyncRoot)
            {
                return location;
            }
        }

        public static T Read<T>(ref T location) where T : class
        {
            lock (SyncRoot)
            {
                return location;
            }
        }

        public static void Write(ref int location, int value)
        {
            lock (SyncRoot)
            {
                location = value;
            }
        }

        public static void Write<T>(ref T location, T value) where T : class
        {
            lock (SyncRoot)
            {
                location = value;
            }
        }
    }
}

namespace System.Threading.Tasks
{
    public class Task
    {
    }

    public class Task<TResult> : Task
    {
    }
}

namespace System.Reflection
{
    public static class Net35ReflectionExtensions
    {
        public static T GetCustomAttribute<T>(this MemberInfo memberInfo) where T : Attribute
        {
            if (memberInfo == null)
                return null;

            object[] attributes = memberInfo.GetCustomAttributes(typeof(T), true);
            return attributes != null && attributes.Length > 0 ? (T)attributes[0] : null;
        }
    }
}

namespace System.Linq
{
    public static class Net35LinqExtensions
    {
        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer)
        {
            return new HashSet<T>(source, comparer);
        }
    }
}
#endif
