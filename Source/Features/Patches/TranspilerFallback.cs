using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Better_Work_Tab.Features.Patches
{
    internal static class TranspilerFallback
    {
        public static IEnumerable<CodeInstruction> ReturnOriginalWithWarning(
            List<CodeInstruction> codes,
            MethodBase original,
            Exception exception,
            string patchName)
        {
            string methodName = original?.DeclaringType != null
                ? $"{original.DeclaringType.FullName}.{original.Name}"
                : original?.Name ?? "<unknown method>";
            string message =
                $"[BWT] {patchName} transpiler skipped for {methodName}: " +
                $"{exception.GetType().Name}: {exception.Message}. Leaving the original IL unchanged.";

            try
            {
                Log.WarningOnce(message, message.GetHashCode());
            }
            catch (Exception loggingException)
            {
                // Returning the original instruction stream is the safety contract.
                // Diagnostics must not turn a skipped transpiler into a patch failure.
                try
                {
                    Console.Error.WriteLine(
                        message + " Warning logging also failed: " +
                        loggingException.Message);
                }
                catch
                {
                    // Some Unity hosts do not expose a usable process console.
                }
            }
            return codes;
        }
    }
}
