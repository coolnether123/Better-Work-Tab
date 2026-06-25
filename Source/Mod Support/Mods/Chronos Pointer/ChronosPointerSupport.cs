using System;
using System.Globalization;
using System.Reflection;
using System.Text;
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
        private static MethodInfo _drawTimeline;
        private static MethodInfo _drawEmbeddedTimeline;
        private static MethodInfo _tryGetTimelineSnapshot;
        private static MethodInfo _getCurrentHoursBarCursorColor;
        private static MethodInfo _geometryXAtLocalHour;
        private static PropertyInfo _isReady;
        private static PropertyInfo _timelineLocalHour;
        private static PropertyInfo _timelineSettings;
        private static PropertyInfo _settingsDrawHoursBarCursor;
        private static PropertyInfo _settingsDrawMainCursor;
        private static PropertyInfo _settingsColorMainCursor;
        private static PropertyInfo _settingsHoursBarCursorThickness;
        private static bool _lastTimePriorityCursorDrawn;
        private static Rect _lastChronosRect;
        private static Rect _lastPriorityRowsRect;
        private static Rect _lastCursorRect;
        private static Color _lastCursorColor = Color.white;
        private static float _lastLocalHour;
        private static float _lastHourBoxWidth;

        internal static bool ShouldReserveTimePriorityTimelineHeight
        {
            get
            {
                if (!(BetterWorkTabMod.Settings?.enableChronosPointerTimePriorityIntegration ??
                      DefaultSettings.enableChronosPointerTimePriorityIntegration))
                {
                    return false;
                }

                return EnsureResolved() && IsReady() && SupportsContract();
            }
        }

        internal static bool TryDrawTimePriorityTimeline(
            Rect chronosRect,
            Rect priorityRowsRect,
            float progress,
            bool drawIncidentOverlay)
        {
            _lastTimePriorityCursorDrawn = false;
            _lastChronosRect = chronosRect;
            _lastPriorityRowsRect = priorityRowsRect;
            _lastCursorRect = Rect.zero;
            _lastCursorColor = Color.white;
            _lastLocalHour = 0f;
            _lastHourBoxWidth = 0f;

            if (!ShouldReserveTimePriorityTimelineHeight ||
                progress < 0.98f ||
                MapCompat.CurrentMap == null ||
                chronosRect.width <= 1f ||
                chronosRect.height <= 1f ||
                priorityRowsRect.width <= 1f ||
                priorityRowsRect.height <= 1f)
            {
                return false;
            }

            if (!EnsureResolved() || !IsReady() || !SupportsContract())
            {
                return false;
            }

            try
            {
                float baseOffsetX = 0.5f;
                float hourBoxWidth = Math.Max((chronosRect.width - 1f) / 24f, 0f);
                object geometry = _createGeometry.Invoke(null, new object[]
                {
                    chronosRect,
                    0f,
                    hourBoxWidth,
                    baseOffsetX,
                    1f,
                    0f,
                    chronosRect.height,
                    0f,
                    0f
                });

                if (geometry == null)
                {
                    return false;
                }

                object[] timelineArgs = { MapCompat.CurrentMap, null };
                if (!(bool)_tryGetTimelineSnapshot.Invoke(null, timelineArgs) || timelineArgs[1] == null)
                {
                    return false;
                }

                object timeline = timelineArgs[1];
                DrawTimeline(timeline, geometry, drawIncidentOverlay);

                PrepareScheduleCursor(chronosRect, priorityRowsRect, geometry, timeline, hourBoxWidth);
                return true;
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "Chronos Pointer time-priority draw failed: " + ex.Message,
                    DebugFeature.ModSupport);
                return false;
            }
        }

        internal static void DrawTimePriorityScheduleCursorOverlay()
        {
            if (!_lastTimePriorityCursorDrawn || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Widgets.DrawBoxSolid(_lastCursorRect, _lastCursorColor);
        }

        internal static void AppendTimePriorityDiagnostics(StringBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            builder.AppendLine("chronosTimePriorityCursorDrawn=" + _lastTimePriorityCursorDrawn);
            if (!_lastTimePriorityCursorDrawn)
            {
                return;
            }

            builder.AppendLine(
                "chronosTimePriority chronosRect=" + FormatRect(_lastChronosRect)
                + " priorityRowsRect=" + FormatRect(_lastPriorityRowsRect)
                + " cursorRect=" + FormatRect(_lastCursorRect)
                + " cursorCenterX=" + Format(_lastCursorRect.center.x)
                + " localHour=" + Format(_lastLocalHour)
                + " hourBoxWidth=" + Format(_lastHourBoxWidth));
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
            _isReady = AccessTools.Property(_apiType, "IsReady");
            Type timelineServiceType = AccessTools.TypeByName("ChronosPointer.Core.ChronosTimelineService");
            Type timelineType = AccessTools.TypeByName("ChronosPointer.Api.ChronosTimelineSnapshot");
            Type geometryType = AccessTools.TypeByName("ChronosPointer.Api.ChronosScheduleGeometrySnapshot");
            Type settingsType = AccessTools.TypeByName("ChronosPointer.Api.ChronosSettingsSnapshot");
            if (timelineType != null && geometryType != null)
            {
                _createGeometry = AccessTools.Method(
                    _apiType,
                    "CreateGeometry",
                    new[]
                    {
                        typeof(Rect),
                        typeof(float),
                        typeof(float),
                        typeof(float),
                        typeof(float),
                        typeof(float),
                        typeof(float),
                        typeof(float),
                        typeof(float)
                    });
                _drawTimeline = AccessTools.Method(_apiType, "DrawTimeline", new[] { timelineType, geometryType, typeof(bool), typeof(bool) });
                _drawEmbeddedTimeline = AccessTools.Method(_apiType, "DrawEmbeddedTimeline", new[] { timelineType, geometryType, typeof(bool) });
                _tryGetTimelineSnapshot = AccessTools.Method(_apiType, "TryGetTimelineSnapshot");
                _geometryXAtLocalHour = AccessTools.Method(geometryType, "XAtLocalHour", new[] { typeof(float) });
            }

            if (timelineServiceType != null)
            {
                _getCurrentHoursBarCursorColor = AccessTools.Method(timelineServiceType, "GetCurrentHoursBarCursorColor");
            }

            if (timelineType != null)
            {
                _timelineLocalHour = AccessTools.Property(timelineType, "LocalHour");
                _timelineSettings = AccessTools.Property(timelineType, "Settings");
            }

            if (settingsType != null)
            {
                _settingsDrawHoursBarCursor = AccessTools.Property(settingsType, "DrawHoursBarCursor");
                _settingsDrawMainCursor = AccessTools.Property(settingsType, "DrawMainCursor");
                _settingsColorMainCursor = AccessTools.Property(settingsType, "ColorMainCursor");
                _settingsHoursBarCursorThickness = AccessTools.Property(settingsType, "HoursBarCursorThickness");
            }
            if (_supports == null ||
                _createGeometry == null ||
                _drawTimeline == null ||
                _tryGetTimelineSnapshot == null ||
                _geometryXAtLocalHour == null ||
                _isReady == null ||
                _timelineLocalHour == null ||
                _timelineSettings == null ||
                _settingsDrawHoursBarCursor == null ||
                _settingsDrawMainCursor == null ||
                _settingsColorMainCursor == null ||
                _settingsHoursBarCursorThickness == null)
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

        private static void DrawTimeline(object timeline, object geometry, bool drawIncidentOverlay)
        {
            if (_drawEmbeddedTimeline != null)
            {
                _drawEmbeddedTimeline.Invoke(null, new object[]
                {
                    timeline,
                    geometry,
                    drawIncidentOverlay
                });
                return;
            }

            _drawTimeline.Invoke(null, new object[]
            {
                timeline,
                geometry,
                true,
                drawIncidentOverlay
            });
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

        private static void PrepareScheduleCursor(
            Rect chronosRect,
            Rect priorityRowsRect,
            object geometry,
            object timeline,
            float hourBoxWidth)
        {
            object settings = _timelineSettings.GetValue(timeline, null);
            if (settings == null)
            {
                return;
            }

            bool drawMainCursor = (bool)_settingsDrawMainCursor.GetValue(settings, null);
            bool drawHoursBarCursor = (bool)_settingsDrawHoursBarCursor.GetValue(settings, null);
            if (!drawMainCursor && !drawHoursBarCursor)
            {
                return;
            }

            float localHour = Convert.ToSingle(_timelineLocalHour.GetValue(timeline, null));
            float cursorX = Convert.ToSingle(_geometryXAtLocalHour.Invoke(geometry, new object[] { localHour }));
            float cursorThickness = Math.Max(1f, Convert.ToSingle(_settingsHoursBarCursorThickness.GetValue(settings, null)));
            Color cursorColor = drawHoursBarCursor
                ? GetHoursBarCursorColor(timeline)
                : (Color)_settingsColorMainCursor.GetValue(settings, null);
            Rect cursorRect = new Rect(cursorX, priorityRowsRect.yMin, cursorThickness, priorityRowsRect.height);

            _lastTimePriorityCursorDrawn = true;
            _lastChronosRect = chronosRect;
            _lastPriorityRowsRect = priorityRowsRect;
            _lastCursorRect = cursorRect;
            _lastCursorColor = cursorColor;
            _lastLocalHour = localHour;
            _lastHourBoxWidth = hourBoxWidth;
        }

        private static Color GetHoursBarCursorColor(object timeline)
        {
            if (_getCurrentHoursBarCursorColor == null)
            {
                return Color.white;
            }

            try
            {
                return (Color)_getCurrentHoursBarCursorColor.Invoke(null, new[] { MapCompat.CurrentMap, timeline });
            }
            catch
            {
                return Color.white;
            }
        }

        private static string FormatRect(Rect rect)
        {
            return "(" + Format(rect.x) + "," + Format(rect.y) + "," + Format(rect.width) + "," + Format(rect.height) + ")";
        }

        private static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
