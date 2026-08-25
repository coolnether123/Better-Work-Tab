using Better_Work_Tab.Foundation.Canonicalization;

namespace Better_Work_Tab.Features.Workloads.V2
{
    internal static class WorkloadCanonical
    {
        public static string Encode(string value)
        {
            return DeterministicCanonical.Encode(value);
        }

        public static string Pair(string first, string second)
        {
            return DeterministicCanonical.Pair(first, second);
        }

        public static string Triple(string first, string second, string third)
        {
            return DeterministicCanonical.Triple(first, second, third);
        }

        public static string Boolean(bool value)
        {
            return DeterministicCanonical.Boolean(value);
        }

        public static string Integer(int value)
        {
            return DeterministicCanonical.Integer(value);
        }

        public static string Fingerprint(string canonical)
        {
            return DeterministicCanonical.Fingerprint(canonical);
        }
    }
}
