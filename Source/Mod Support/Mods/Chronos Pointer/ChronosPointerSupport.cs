using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using Better_Work_Tab.UI.Settings;
using HarmonyLib;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;
using static Better_Work_Tab.UI.Settings.SettingIDs;

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
        private const string ModernPackageId = "CoolNether123.ChronosPointer";
        private const string LegacyPackageId = "CoolNether123.ChronosPointer.Legacy";
        private const int FailedResolveRetryFrames = 60;

        private static bool _resolved;
        private static int _nextResolveAttemptFrame;
        private static Type _apiType;
        private static MethodInfo _supports;
        private static MethodInfo _createGeometry;
        private static MethodInfo _drawTimeline;
        private static MethodInfo _drawEmbeddedTimeline;
        private static MethodInfo _tryDrawEmbeddedTimeline;
        private static MethodInfo _tryGetTimelineSnapshot;
        private static MethodInfo _getLatestTimelineSnapshot;
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
        private static readonly IModSettingsContributor SettingsContributor = new ChronosPointerSettingsContributor();

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

        internal static void RegisterSettings()
        {
            BWTModSettingsApi.RegisterContributor(SettingsContributor);
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

                if (!TryDrawTimeline(geometry, drawIncidentOverlay, out object timeline) || timeline == null)
                {
                    return false;
                }

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

            if (!IsChronosPointerActive())
            {
                return false;
            }

            if (Time.frameCount < _nextResolveAttemptFrame)
            {
                return false;
            }

            ClearResolvedMembers();
            _apiType = AccessTools.TypeByName("ChronosPointer.Api.ChronosPointerApi");
            if (_apiType == null)
            {
                MarkResolveFailed();
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
                _tryDrawEmbeddedTimeline = AccessTools.Method(_apiType, "TryDrawEmbeddedTimeline", new[] { typeof(Map), geometryType, typeof(bool) });
                _tryGetTimelineSnapshot = AccessTools.Method(_apiType, "TryGetTimelineSnapshot");
                _getLatestTimelineSnapshot = AccessTools.Method(_apiType, "GetLatestTimelineSnapshot");
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
                !HasTimelineDrawPath() ||
                _geometryXAtLocalHour == null ||
                _isReady == null ||
                _timelineLocalHour == null ||
                _timelineSettings == null ||
                _settingsDrawHoursBarCursor == null ||
                _settingsDrawMainCursor == null ||
                _settingsColorMainCursor == null ||
                _settingsHoursBarCursorThickness == null)
            {
                MarkResolveFailed();
                return false;
            }

            _resolved = true;
            _nextResolveAttemptFrame = 0;
            return true;
        }

        private static bool IsChronosPointerActive()
        {
            return ModListerCompat.GetActiveModWithIdentifier(ModernPackageId) != null ||
                   ModListerCompat.GetActiveModWithIdentifier(LegacyPackageId) != null;
        }

        private sealed class ChronosPointerSettingsContributor : IModSettingsContributor
        {
            public BWTModSettingsSection CreateSettingsSection()
            {
                return new BWTModSettingsSection
                {
                    Header = new SettingDefinition
                    {
                        Id = CompatChronosPointerHeader,
                        Label = "Chronos Pointer",
                        Type = SettingType.Header,
                        VisibleWhen = _ => ChronosPointerSupport.IsChronosPointerActive(),
                        ShowInSimpleView = true,
                        SortOrder = 0
                    },
                    Children = new[]
                    {
                        new SettingDefinition
                        {
                            Id = UiChronosPointerTimePriority,
                            FieldName = "enableChronosPointerTimePriorityIntegration",
                            Label = "Chronos Pointer time bar",
                            Tooltip = "When Chronos Pointer is loaded, draw its daylight/current-time bar above the Work tab time-priority hour numbers.",
                            Type = SettingType.Bool,
                            DefaultValue = DefaultSettings.enableChronosPointerTimePriorityIntegration,
                            VisibleWhen = _ => ChronosPointerSupport.IsChronosPointerActive(),
                            ControlsChildVisibility = true,
                            ShowInSimpleView = true,
                            SortOrder = 1
                        },
                        new SettingDefinition
                        {
                            Id = UiChronosPointerTimePriorityIncidents,
                            ParentId = UiChronosPointerTimePriority,
                            FieldName = "chronosPointerTimePriorityIncidentOverlay",
                            Label = "Chronos incident overlay",
                            Tooltip = "Allow Chronos Pointer to draw its incident colors, such as eclipses and auroras, on the Work tab time bar.",
                            Type = SettingType.Bool,
                            DefaultValue = DefaultSettings.chronosPointerTimePriorityIncidentOverlay,
                            VisibleWhen = _ => ChronosPointerSupport.IsChronosPointerActive(),
                            ShowInSimpleView = false,
                            SortOrder = 2
                        }
                    }
                };
            }
        }

        private static void MarkResolveFailed()
        {
            ClearResolvedMembers();
            _nextResolveAttemptFrame = Time.frameCount + FailedResolveRetryFrames;
        }

        private static void ClearResolvedMembers()
        {
            _resolved = false;
            _apiType = null;
            _supports = null;
            _createGeometry = null;
            _drawTimeline = null;
            _drawEmbeddedTimeline = null;
            _tryDrawEmbeddedTimeline = null;
            _tryGetTimelineSnapshot = null;
            _getLatestTimelineSnapshot = null;
            _getCurrentHoursBarCursorColor = null;
            _geometryXAtLocalHour = null;
            _isReady = null;
            _timelineLocalHour = null;
            _timelineSettings = null;
            _settingsDrawHoursBarCursor = null;
            _settingsDrawMainCursor = null;
            _settingsColorMainCursor = null;
            _settingsHoursBarCursorThickness = null;
        }

        private static bool HasTimelineDrawPath()
        {
            bool hasPublicEmbeddedDraw = _tryDrawEmbeddedTimeline != null &&
                _getLatestTimelineSnapshot != null;
            bool hasManualDraw = (_drawEmbeddedTimeline != null || _drawTimeline != null) &&
                _tryGetTimelineSnapshot != null;
            return hasPublicEmbeddedDraw || hasManualDraw;
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

        private static bool TryDrawTimeline(object geometry, bool drawIncidentOverlay, out object timeline)
        {
            timeline = null;

            if (_tryDrawEmbeddedTimeline != null && _getLatestTimelineSnapshot != null)
            {
                bool drawn = (bool)_tryDrawEmbeddedTimeline.Invoke(null, new object[]
                {
                    MapCompat.CurrentMap,
                    geometry,
                    drawIncidentOverlay
                });
                if (!drawn)
                {
                    return false;
                }

                timeline = _getLatestTimelineSnapshot.Invoke(null, null);
                return timeline != null;
            }

            object[] timelineArgs = { MapCompat.CurrentMap, null };
            if (_tryGetTimelineSnapshot == null ||
                !(bool)_tryGetTimelineSnapshot.Invoke(null, timelineArgs) ||
                timelineArgs[1] == null)
            {
                return false;
            }

            timeline = timelineArgs[1];
            DrawTimelineManually(timeline, geometry, drawIncidentOverlay);
            return true;
        }

        private static void DrawTimelineManually(object timeline, object geometry, bool drawIncidentOverlay)
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
