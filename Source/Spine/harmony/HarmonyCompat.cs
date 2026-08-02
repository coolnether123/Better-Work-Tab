using System;
using System.Collections.Generic;
using Verse;

namespace Spine.Harmony.Infrastructure
{
    internal static class MMLog
    {
        private const string Prefix = "[Spine.Harmony] ";
        private static readonly HashSet<string> WarnedKeys =
            new HashSet<string>(StringComparer.Ordinal);

        internal static void Write(string message) =>
            Log.Message(Prefix + (message ?? string.Empty));

        internal static void WriteInfo(string message) => Write(message);

        internal static void WriteWarning(string message) =>
            Log.Warning(Prefix + (message ?? string.Empty));

        internal static void WriteError(string message) =>
            Log.Error(Prefix + (message ?? string.Empty));

        internal static void WriteDebug(string message)
        {
            if (Prefs.DevMode)
            {
                Log.Message(Prefix + "[Debug] " + (message ?? string.Empty));
            }
        }

        internal static void WriteDebugBlock(
            string heading,
            IEnumerable<string> lines)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            var block = new List<string> { heading ?? string.Empty };
            if (lines != null)
            {
                block.AddRange(lines);
            }

            Log.Message(
                Prefix + "[Debug] " +
                string.Join(Environment.NewLine, block));
        }

        internal static void WarnOnce(string key, string message)
        {
            lock (WarnedKeys)
            {
                if (!string.IsNullOrEmpty(key) && !WarnedKeys.Add(key))
                {
                    return;
                }
            }

            WriteWarning(message);
        }
    }

    internal static class ModPrefs
    {
        internal static bool DebugTranspilers => Prefs.DevMode;
        internal static bool TranspilerSafeMode => true;
        internal static bool TranspilerForcePreserveInstructionCount => true;
        internal static bool TranspilerFailFastCritical => true;
        internal static bool TranspilerLogValidationWarnings => false;
    }
}
