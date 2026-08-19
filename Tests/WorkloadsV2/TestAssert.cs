using System;
using System.Collections.Generic;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class TestAssert
    {
        public static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        public static void False(bool value, string message)
        {
            True(!value, message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    message + " Expected=" + (expected == null ? "<null>" : expected.ToString()) +
                    " Actual=" + (actual == null ? "<null>" : actual.ToString()));
            }
        }

        public static void NotNull(object value, string message)
        {
            True(value != null, message);
        }

        public static void Contains(string value, string expected, string message)
        {
            True(
                value != null && expected != null &&
                value.IndexOf(expected, StringComparison.Ordinal) >= 0,
                message);
        }

        public static void Sequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
        {
            NotNull(expected, message + " expected sequence is null.");
            NotNull(actual, message + " actual sequence is null.");
            Equal(expected.Count, actual.Count, message + " count mismatch.");
            for (var i = 0; i < expected.Count; i++)
            {
                Equal(expected[i], actual[i], message + " element " + i + " mismatch.");
            }
        }

        public static void Throws<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(message + " Expected " + typeof(TException).Name + ".");
        }
    }
}
