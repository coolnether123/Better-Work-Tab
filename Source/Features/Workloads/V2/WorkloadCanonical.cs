using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Better_Work_Tab.Features.Workloads.V2
{
    internal static class WorkloadCanonical
    {
        public static string Encode(string value)
        {
            string safe = value ?? string.Empty;
            return safe.Length.ToString(CultureInfo.InvariantCulture) + ":" + safe;
        }

        public static string Pair(string first, string second)
        {
            return Encode(first) + Encode(second);
        }

        public static string Triple(string first, string second, string third)
        {
            return Encode(first) + Encode(second) + Encode(third);
        }

        public static string Boolean(bool value)
        {
            return value ? "1" : "0";
        }

        public static string Integer(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Fingerprint(string canonical)
        {
            string safe = canonical ?? string.Empty;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(safe));
                var builder = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }
    }
}
