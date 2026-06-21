using Better_Work_Tab.Features.Rules;
using HarmonyLib;
using RimWorld;
using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Useful Marks compatibility module.
    /// Pulls marker info from Useful Marks' world component to feed into BWT rules or UI overlays.
    /// Reflection is used so the assembly stays optional; failures are contained and logged under DebugFeature.ModSupport.
    /// </summary>
    public sealed class UsefulMarksSupport : ModSupportModuleBase
    {
        public override string PackageId => "Andromeda.UsefulMarks";
        public override string DisplayName => "Useful Marks";

        // Cached reflection members
        private Type _worldCompType;
        private FieldInfo _instanceField;
        private MethodInfo _shouldDrawLabelMethod;
        private MethodInfo _getColorMethod;

        // Marker rendering reflection
        private Type _markerSettingsType;
        private FieldInfo _markerIconField;
        private FieldInfo _markerIconAccentField;
        private FieldInfo _markerShowModeField;
        private MethodInfo _markerColorMethod;
        private MethodInfo _markerAccentColorMethod;
        private MethodInfo _processMarkersForMethod;
        private MethodInfo _drawMarkMethod;
        private Delegate _processMarkersDelegate;

        // Cached per-draw context
        private Pawn _currentPawn;
        private Rect _currentIconRect;
        private bool _drewMarker;

        public override void OnModsDetected()
        {
            Debug("[UsefulMarks] Detected. Caching reflection handles...");

            // Type and members from UsefulMarks.PawnLabelCustomColors_WorldComponent
            _worldCompType = AccessTools.TypeByName("UsefulMarks.PawnLabelCustomColors_WorldComponent");
            if (_worldCompType == null)
            {
                Debug("[UsefulMarks] WorldComponent type not found; skipping integration.");
                return;
            }

            _instanceField = AccessTools.Field(_worldCompType, "instance");
            _shouldDrawLabelMethod = AccessTools.Method(_worldCompType, "ShouldToDrawMarkLabel", new[] { typeof(Pawn) });
            _getColorMethod = AccessTools.Method(_worldCompType, "GetPawnMarkLabelColorFor", new[] { typeof(Pawn) });

            if (_instanceField == null || _shouldDrawLabelMethod == null || _getColorMethod == null)
            {
                _instanceField = null;
                _shouldDrawLabelMethod = null;
                _getColorMethod = null;
                Debug("[UsefulMarks] Missing expected members; integration disabled.");
            }
            else
            {
                Debug("[UsefulMarks] Reflection cached successfully.");
            }

            // Marker settings + drawing bits
            _markerSettingsType = AccessTools.TypeByName("UsefulMarks.MarkerSettings");
            _markerIconField = AccessTools.Field(_markerSettingsType, "icon");
            _markerIconAccentField = AccessTools.Field(_markerSettingsType, "iconAccent");
            _markerShowModeField = AccessTools.Field(_markerSettingsType, "showMode");
            _markerColorMethod = AccessTools.Method(_markerSettingsType, "GetColorFor", new[] { typeof(Pawn) });
            _markerAccentColorMethod = AccessTools.Method(_markerSettingsType, "GetColorAccentFor", new[] { typeof(Pawn) });
            var actionType = typeof(Action<>).MakeGenericType(_markerSettingsType);
            _processMarkersForMethod = AccessTools.Method(_worldCompType, "ProcessMarkersFor", new[] { typeof(Pawn), actionType });
            var namePlatePatchesType = AccessTools.TypeByName("UsefulMarks.NamePlatePatches");
            _drawMarkMethod = AccessTools.Method(namePlatePatchesType, "DrawMark", new[] { typeof(Texture2D), typeof(Texture2D), typeof(Color), typeof(Color), typeof(Vector2), typeof(float?) });

            if (_markerSettingsType == null || _processMarkersForMethod == null || _markerIconField == null || _markerColorMethod == null)
            {
                Debug("[UsefulMarks] Marker rendering members not found; icon overlay disabled.");
            }
        }

        public override void OnPawnTableRefresh(PawnTable table)
        {
            // Placeholder: Useful Marks stores state per pawn; nothing to refresh here yet.
        }

        public override void OnPawnRowDrawn(Pawn pawn, Rect iconRect)
        {
            TryDrawMarker(pawn, iconRect);
        }

        public override void OnRulesEvaluated(Pawn pawn, WorkAssignmentParameters currentParameters)
        {
            // Example: if a pawn has a Useful Mark label, you could bias rules here.
            // Currently no-op to avoid changing behavior; hook is ready for future logic.
        }

        /// <summary>
        /// Strongly typed data we expose to the rest of BWT.
        /// </summary>
        public struct UsefulMarkInfo
        {
            public bool ShouldDrawLabel;
            public Color LabelColor;
        }

        /// <summary>
        /// Attempts to read Useful Marks data. Returns false if reflection is missing or the mod has no data for the pawn.
        /// </summary>
        public bool TryGetMarkInfo(Pawn pawn, out UsefulMarkInfo info)
        {
            info = default;

            if (_instanceField == null || _shouldDrawLabelMethod == null || _getColorMethod == null)
            {
                return false;
            }

            try
            {
                object instance = _instanceField.GetValue(null);
                if (instance == null)
                {
                    Debug($"[UsefulMarks] WorldComponent instance null; skipping pawn '{PawnLabel(pawn)}'.");
                    return false;
                }

                bool shouldDraw = (bool)_shouldDrawLabelMethod.Invoke(instance, new object[] { pawn });
                if (!shouldDraw)
                {
                    Debug($"[UsefulMarks] No icon for pawn '{PawnLabel(pawn)}'.");
                    return false;
                }

                Color color = (Color)_getColorMethod.Invoke(instance, new object[] { pawn });
                info = new UsefulMarkInfo
                {
                    ShouldDrawLabel = shouldDraw,
                    LabelColor = color
                };

                Debug($"[UsefulMarks] Pawn '{PawnLabel(pawn)}' has a Useful Marks icon (color: {color}).");
                return true;
            }
            catch (Exception ex)
            {
                Debug($"[UsefulMarks] Error reading mark info for {PawnLabel(pawn)}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Public helper used by ModSupportManager to expose data externally.
        /// </summary>
        public UsefulMarkInfo? GetMarkInfo(Pawn pawn)
        {
            if (TryGetMarkInfo(pawn, out var info))
            {
                return info;
            }
            return null;
        }

        private bool TryDrawMarker(Pawn pawn, Rect iconRect)
        {
            if (_processMarkersForMethod == null || _markerSettingsType == null || _markerIconField == null || _markerColorMethod == null)
            {
                return false;
            }

            object instance = _instanceField?.GetValue(null);
            if (instance == null)
            {
                return false;
            }

            if (_processMarkersDelegate == null)
            {
                _processMarkersDelegate = BuildProcessMarkersDelegate();
            }

            if (_processMarkersDelegate == null)
            {
                return false;
            }

            _currentPawn = pawn;
            _currentIconRect = iconRect;
            _drewMarker = false;

            try
            {
                _processMarkersDelegate.DynamicInvoke(instance, pawn);
            }
            catch (Exception ex)
            {
                Debug($"[UsefulMarks] Error while drawing marker for '{PawnLabel(pawn)}': {ex.Message}");
            }
            finally
            {
                _currentPawn = null;
            }

            return _drewMarker;
        }

        private Delegate BuildProcessMarkersDelegate()
        {
            try
            {
                var actionType = typeof(Action<>).MakeGenericType(_markerSettingsType);
                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var pawnParam = Expression.Parameter(typeof(Pawn), "pawn");
                var markerParam = Expression.Parameter(_markerSettingsType, "marker");

                // marker => DrawMarkerGeneric((object)marker)
                var drawCall = Expression.Call(Expression.Constant(this),
                    typeof(UsefulMarksSupport).GetMethod(nameof(DrawMarkerGeneric), BindingFlags.NonPublic | BindingFlags.Instance),
                    Expression.Convert(markerParam, typeof(object)));
                var drawLambda = Expression.Lambda(actionType, drawCall, markerParam);

                // (object inst, Pawn p) => ProcessMarkersFor(inst, p, drawLambda)
                var invoke = Expression.Call(
                    Expression.Convert(instanceParam, _worldCompType),
                    _processMarkersForMethod,
                    pawnParam,
                    drawLambda);

                return Expression.Lambda(invoke, instanceParam, pawnParam).Compile();
            }
            catch (Exception ex)
            {
                Debug($"[UsefulMarks] Failed to build delegate: {ex.Message}");
                return null;
            }
        }

        // Signature intentionally takes object; contravariance lets us bind to Action<MarkerSettings>
        private void DrawMarkerGeneric(object markerObj)
        {
            if (markerObj == null || _currentPawn == null)
            {
                return;
            }

            // Filter showMode (skip WorldOnly)
            if (_markerShowModeField != null)
            {
                var showModeValue = _markerShowModeField.GetValue(markerObj);
                if (showModeValue != null && showModeValue.ToString().Equals("WorldOnly", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            var icon = _markerIconField.GetValue(markerObj) as Texture2D;
            if (icon == null)
            {
                return;
            }
            var accent = _markerIconAccentField?.GetValue(markerObj) as Texture2D;

            Color baseColor = Color.white;
            Color accentColor = Color.white;
            try
            {
                baseColor = (Color)_markerColorMethod.Invoke(markerObj, new object[] { _currentPawn });
                if (_markerAccentColorMethod != null)
                {
                    accentColor = (Color)_markerAccentColorMethod.Invoke(markerObj, new object[] { _currentPawn });
                }
            }
            catch (Exception ex)
            {
                Debug($"[UsefulMarks] Failed to fetch colors: {ex.Message}");
            }

            float size = Mathf.Min(_currentIconRect.width, _currentIconRect.height) * 0.45f;
            var point = new Vector2(_currentIconRect.xMax - size * 0.5f - 1f, _currentIconRect.yMax - size * 0.5f - 1f);

            if (_drawMarkMethod != null)
            {
                _drawMarkMethod.Invoke(null, new object[] { icon, accent, baseColor, accentColor, point, (float?)size });
            }
            else
            {
                // Fallback simple draw
                Color prev = GUI.color;
                var rect = new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size);
                GUI.color = baseColor;
                GUI.DrawTexture(rect, icon);
                if (accent != null)
                {
                    GUI.color = accentColor;
                    GUI.DrawTexture(rect, accent);
                }
                GUI.color = prev;
            }

            _drewMarker = true;
        }

        private static string PawnLabel(Pawn pawn)
        {
            return pawn == null ? "null" : PawnCompat.LabelShortCap(pawn);
        }
    }
}
