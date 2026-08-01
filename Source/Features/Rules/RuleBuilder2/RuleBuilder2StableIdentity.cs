using System;
using System.Security.Cryptography;
using System.Text;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal static class RuleBuilder2StableIdentity
    {
        internal static string FromSeed(string seed)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(seed ?? string.Empty);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash, 0, 16)
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }
    }
}
