using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Util
{
    /// <summary>
    /// Structured, leveled, and throttled logging for Better Work Tab.
    /// - Levels: Off(0), Error(1), Warn(2), Info(3), Debug(4), Trace(5)
    /// - Categories (e.g., DragColumn, DragRow) have separate token buckets to avoid spam.
    /// - Suppression summary: when logs are throttled, the next successful log includes a count.
    ///
    /// Settings are read from BetterWorkTabMod.Settings.*
    /// </summary>
    internal static class WorkTabLogger
    {
        internal enum Level { Off = 0, Error = 1, Warn = 2, Info = 3, Debug = 4, Trace = 5 }

        private class Bucket
        {
            public float tokens;
            public float lastUpdate;
            public int suppressed;
        }

        private static readonly Dictionary<string, Bucket> buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        private static float Now => Time.realtimeSinceStartup;

        private static Level ConfigLevel => (Level)Mathf.Clamp(BetterWorkTabMod.Settings.debugLogLevel, 0, 5);
        private static int MaxPerSec => Mathf.Max(0, BetterWorkTabMod.Settings.debugLogMaxPerSecond);

        private static bool DragColsEnabled => BetterWorkTabMod.Settings.debugLogDragColumns;
        private static bool DragRowsEnabled => BetterWorkTabMod.Settings.debugLogDragRows;

        internal static bool IsEnabled(Level level, string category)
        {
            if (level == Level.Off || ConfigLevel == Level.Off)
                return false;
            if (level > ConfigLevel)
                return false;

            // Category filters for high-traffic drag logs
            if (category == Categories.DragColumn && !DragColsEnabled && level >= Level.Debug)
                return false;
            if (category == Categories.DragRow && !DragRowsEnabled && level >= Level.Debug)
                return false;

            return true;
        }

        private static bool ConsumeToken(string category)
        {
            if (MaxPerSec <= 0)
                return false; // throttling disabled -> allow unlimited

            if (!buckets.TryGetValue(category, out var b))
            {
                b = new Bucket { tokens = MaxPerSec, lastUpdate = Now, suppressed = 0 };
                buckets[category] = b;
            }

            float t = Now;
            float dt = Mathf.Max(0f, t - b.lastUpdate);
            b.lastUpdate = t;
            // Refill tokens at rate MaxPerSec per second, cap at 2*MaxPerSec to allow small bursts.
            b.tokens = Mathf.Min(b.tokens + dt * MaxPerSec, MaxPerSec * 2f);

            if (b.tokens >= 1f)
            {
                b.tokens -= 1f;
                // If we suppressed before, attach a summary to the next log
                if (b.suppressed > 0)
                {
                    Log.Message($"[Better Work Tab] (category={category}) Suppressed {b.suppressed} log(s) due to throttling.");
                    b.suppressed = 0;
                }
                return true;
            }
            else
            {
                b.suppressed++;
                return false;
            }
        }

        internal static class Categories
        {
            public const string DragColumn = "DragColumn";
            public const string DragRow = "DragRow";
            public const string Init = "Init";
            public const string Error = "Error";
        }

        internal static void Trace(string category, string msg) => Write(Level.Trace, category, msg);
        internal static void Debug(string category, string msg) => Write(Level.Debug, category, msg);
        internal static void Info(string category, string msg) => Write(Level.Info, category, msg);
        internal static void Warn(string category, string msg) => Write(Level.Warn, category, msg);
        internal static void Error(string category, string msg) => Write(Level.Error, category, msg);

        private static void Write(Level level, string category, string msg)
        {
            if (!IsEnabled(level, category))
                return;

            // Throttle high-frequency logs (Debug/Trace); always allow Error/Warn/Info
            bool throttle = level >= Level.Debug; 
            if (throttle && !ConsumeToken(category))
                return;

            string prefix = $"[Better Work Tab/{category}/{level}] ";
            switch (level)
            {
                case Level.Error: Log.Error(prefix + msg); break;
                case Level.Warn: Log.Warning(prefix + msg); break;
                default: Log.Message(prefix + msg); break;
            }
        }
    }
}

