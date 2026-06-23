using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Optional reflection adapter for Chronos Pointer. BWT must not hard-reference
    /// Chronos because BWT supports many RimWorld versions and Chronos is optional.
    /// </summary>
    internal static class ChronosPointerSupport
    {
        private const int RequiredMajor = 1;
        private const int RequiredMinor = 0;

        private static bool _resolved;
        private static Type _apiType;
        private static MethodInfo _supports;
        private static MethodInfo _createGeometry;
        private static MethodInfo _tryDrawTimeline;
        private static PropertyInfo _isReady;

        internal static bool TryDrawTimePriorityTimeline(
            Rect timelineRect,
            float progress,
            bool drawIncidentOverlay)
        {
            if (!(BetterWorkTabMod.Settings?.enableChronosPointerTimePriorityIntegration ??
                  DefaultSettings.enableChronosPointerTimePriorityIntegration) ||
                progress < 0.98f ||
                Find.CurrentMap == null ||
                timelineRect.width <= 1f ||
                timelineRect.height <= 1f)
            {
                return false;
            }

            if (!EnsureResolved() || !IsReady() || !SupportsContract())
            {
                return false;
            }

            try
            {
                float hourBoxWidth = timelineRect.width / 24f;
                object geometry = _createGeometry.Invoke(null, new object[]
                {
                    timelineRect,
                    timelineRect.height,
                    hourBoxWidth,
                    0f,
                    1f,
                    0f,
                    5f,
                    0f,
                    0f
                });

                if (geometry == null)
                {
                    return false;
                }

                return (bool)_tryDrawTimeline.Invoke(null, new object[]
                {
                    Find.CurrentMap,
                    geometry,
                    true,
                    drawIncidentOverlay
                });
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "Chronos Pointer time-priority draw failed: " + ex.Message,
                    DebugFeature.ModSupport);
                return false;
            }
        }

        private static bool EnsureResolved()
        {
            if (_resolved)
            {
                return _apiType != null;
            }

            _resolved = true;
            _apiType = AccessTools.TypeByName("ChronosPointer.Api.ChronosPointerApi");
            if (_apiType == null)
            {
                return false;
            }

            _supports = AccessTools.Method(_apiType, "Supports");
            _createGeometry = AccessTools.Method(_apiType, "CreateGeometry");
            _tryDrawTimeline = AccessTools.Method(_apiType, "TryDrawTimeline");
            _isReady = AccessTools.Property(_apiType, "IsReady");
            if (_supports == null || _createGeometry == null || _tryDrawTimeline == null || _isReady == null)
            {
                _apiType = null;
                return false;
            }

            return true;
        }

        private static bool SupportsContract()
        {
            try
            {
                return (bool)_supports.Invoke(null, new object[] { RequiredMajor, RequiredMinor });
            }
            catch
            {
                return false;
            }
        }

        private static bool IsReady()
        {
            try
            {
                return (bool)_isReady.GetValue(null, null);
            }
            catch
            {
                return false;
            }
        }
    }
}
