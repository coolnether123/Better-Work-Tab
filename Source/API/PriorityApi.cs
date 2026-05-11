using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Verse;

namespace Better_Work_Tab.API
{
    /// <summary>
    /// Immutable snapshot of Better Work Tab's current priority configuration.
    /// Designed to be easy to consume over reflection so other mods do not need
    /// a hard assembly reference just to read max-priority settings.
    /// </summary>
    public readonly struct PriorityApiSnapshot
    {
        public readonly int ApiVersion;
        public readonly int MaxPriority;
        public readonly int DefaultEnabledPriority;

        public PriorityApiSnapshot(int apiVersion, int maxPriority, int defaultEnabledPriority)
        {
            ApiVersion = apiVersion;
            MaxPriority = maxPriority;
            DefaultEnabledPriority = defaultEnabledPriority;
        }
    }

    /// <summary>
    /// Public compatibility surface for Better Work Tab's extended manual priorities.
    /// Other mods should prefer this API over hardcoding the vanilla max priority of 4.
    ///
    /// Reflection integration:
    /// - Assembly: "Better Work Tab"
    /// - Type: "Better_Work_Tab.API.PriorityApi"
    /// - Read methods such as <see cref="GetMaxPriority"/> or <see cref="GetSnapshot"/>
    ///
    /// Example reflection flow for mods that do not want a hard dependency:
    /// 1. Find the loaded assembly named "Better Work Tab".
    /// 2. Resolve type "Better_Work_Tab.API.PriorityApi".
    /// 3. Invoke static method "GetMaxPriority" or "GetSnapshot".
    /// </summary>
    public static class PriorityApi
    {
        private static readonly object LogLock = new object();
        private static readonly HashSet<string> LoggedFallbacks = new HashSet<string>();

        /// <summary>
        /// Stable API version for reflection-based callers.
        /// Increment only when the public contract changes incompatibly.
        /// </summary>
        public const int ApiVersion = 1;

        /// <summary>
        /// Stable assembly name for reflection-based lookups.
        /// </summary>
        public const string AssemblyName = "Better Work Tab";

        /// <summary>
        /// Stable fully-qualified type name for reflection-based lookups.
        /// </summary>
        public const string TypeName = "Better_Work_Tab.API.PriorityApi";

        /// <summary>
        /// Returns the configured upper bound for manual priorities.
        /// Guaranteed to return at least 1.
        /// </summary>
        public static int GetMaxPriority()
        {
            try
            {
                return MaxPriorityLogic.GetMaxPriority();
            }
            catch (Exception ex)
            {
                LogFallbackOnce(nameof(GetMaxPriority), ex);
                return Math.Max(1, DefaultSettings.maxPriority);
            }
        }

        /// <summary>
        /// Returns the default priority used when enabling a work type outside manual-priority mode.
        /// </summary>
        public static int GetDefaultEnabledPriority()
        {
            try
            {
                return MaxPriorityLogic.GetDefaultEnabledPriority();
            }
            catch (Exception ex)
            {
                LogFallbackOnce(nameof(GetDefaultEnabledPriority), ex);
                return Clamp(3, 1, GetMaxPriority());
            }
        }

        /// <summary>
        /// Maps an extended stored priority back into RimWorld's vanilla-style 1..4 display buckets.
        /// Returns 0 for disabled priorities.
        /// </summary>
        public static int MapPriorityToVanillaDisplay(int priority)
        {
            try
            {
                return MaxPriorityLogic.MapPriorityToVanillaDisplay(priority);
            }
            catch (Exception ex)
            {
                LogFallbackOnce(nameof(MapPriorityToVanillaDisplay), ex);
                if (priority <= 0)
                {
                    return 0;
                }

                int maxPriority = GetMaxPriority();
                if (maxPriority <= 1)
                {
                    return 1;
                }

                float remapped = Spine.Utils.SpineUtils.Remap(priority, 1, maxPriority, 1, 4);
                return Clamp((int)Math.Round(remapped), 1, 4);
            }
        }

        /// <summary>
        /// True when the priority value represents "disabled" for a work type.
        /// </summary>
        public static bool IsDisabledPriority(int priority)
        {
            return priority <= 0;
        }

        /// <summary>
        /// Clamps a stored priority into Better Work Tab's supported range.
        /// Values below 0 become 0, where 0 means disabled.
        /// </summary>
        public static int ClampPriority(int priority)
        {
            return Clamp(priority, 0, GetMaxPriority());
        }

        /// <summary>
        /// True when a value can be stored as a Better Work Tab work priority.
        /// </summary>
        public static bool IsValidPriority(int priority)
        {
            return priority >= 0 && priority <= GetMaxPriority();
        }

        /// <summary>
        /// Returns a small versioned snapshot for reflection-friendly integration.
        /// This is the preferred call for mods that want one round-trip instead of multiple method calls.
        /// </summary>
        public static PriorityApiSnapshot GetSnapshot()
        {
            return new PriorityApiSnapshot(
                ApiVersion,
                GetMaxPriority(),
                GetDefaultEnabledPriority());
        }

        /// <summary>
        /// Safe reflection-friendly wrapper that never throws.
        /// Returns false only if the API could not produce a valid snapshot.
        /// </summary>
        public static bool TryGetSnapshot(out PriorityApiSnapshot snapshot)
        {
            try
            {
                snapshot = GetSnapshot();
                return true;
            }
            catch (Exception ex)
            {
                LogFallbackOnce(nameof(TryGetSnapshot), ex);
                int fallbackMaxPriority = Math.Max(1, DefaultSettings.maxPriority);
                snapshot = new PriorityApiSnapshot(ApiVersion, fallbackMaxPriority, Clamp(3, 1, fallbackMaxPriority));
                return false;
            }
        }

        private static void LogFallbackOnce(string methodName, Exception exception)
        {
            lock (LogLock)
            {
                if (!LoggedFallbacks.Add(methodName))
                {
                    return;
                }
            }

            Log.Warning($"[BWT] PriorityApi.{methodName} used fallback values after {exception.GetType().Name}: {exception.Message}");
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
