using System;
using System.Collections.Generic;
using Verse;

namespace Spine.Harmony.Infrastructure
{
    internal static class MMLog
    {
        private const string Prefix = "[Spine.Harmony] ";

        internal static void Write(string message) =>
            Log.Message(Prefix + (message ?? string.Empty));

        /// <summary>
        /// Informational output. Gated behind dev mode: a player's log should
        /// carry problems, not progress reports.
        /// </summary>
        internal static void WriteInfo(string message)
        {
            if (Prefs.DevMode)
            {
                Write(message);
            }
        }

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
                string.Join(Environment.NewLine, block.ToArray()));
        }

        internal static void WarnOnce(string key, string message)
        {
            string normalizedKey = "Spine.Harmony:" + (key ?? string.Empty);
#if RWT_HAS_WARNING_ONCE
            Log.WarningOnce(
                Prefix + (message ?? string.Empty),
                normalizedKey.GetHashCode());
#else
            Log.Warning(Prefix + (message ?? string.Empty));
#endif
        }
    }
}
