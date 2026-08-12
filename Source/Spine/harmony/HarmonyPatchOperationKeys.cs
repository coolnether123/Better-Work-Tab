using System;

namespace Spine.Harmony
{
    internal static class HarmonyPatchOperationKeys
    {
        internal const string Assembly = "assembly";

        internal static string ForType(string patchName) =>
            ForNamedOperation("type", patchName);

        internal static string ForMethod(string patchName) =>
            ForNamedOperation("method", patchName);

        private static string ForNamedOperation(
            string operation,
            string patchName)
        {
            if (string.IsNullOrWhiteSpace(patchName))
            {
                throw new ArgumentException(
                    "A stable patch name is required for idempotent installation.",
                    nameof(patchName));
            }

            return operation + ":" + patchName.Trim();
        }
    }
}
