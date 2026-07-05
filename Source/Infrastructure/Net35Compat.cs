#if (v1_0 || v0_19 || v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4)
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace System
{
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

namespace Verse
{
    public static class Net35VerseStringExtensions
    {
        public static string Formatted(this string text, params object[] args)
        {
            return args != null && args.Length > 0
                ? string.Format(text ?? string.Empty, args)
                : text ?? string.Empty;
        }
    }
}
#endif
