using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.Headers.Vanilla;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Calculates the extra header ceiling needed by the full-tab sub-work drilldown.
    /// The body rows stay anchored; only the header area is allowed to grow upward.
    /// </summary>
    internal static class SubWorkDrilldownHeaderGeometry
    {
        private static float _lastNormalHeaderHeight;

        internal static void RecordNormalHeaderHeight(PawnTable table, float currentHeaderHeight)
        {
            if (SubWorkDrilldownState.IsActive)
            {
                return;
            }

            float candidate = currentHeaderHeight > 0f
                ? currentHeaderHeight
                : PawnTableCompat.GetCachedHeaderHeight(table);
            if (candidate > 0f)
            {
                _lastNormalHeaderHeight = candidate;
            }
        }

        internal static float GetBaseHeaderDrawWidth(PawnTable table, float fallback)
        {
            if (SubWorkDrilldownState.IsActive && SubWorkDrilldownState.BaseHeaderDrawWidth > 0f)
            {
                return SubWorkDrilldownState.BaseHeaderDrawWidth;
            }

            if (!SubWorkDrilldownState.IsActive && fallback > 0f)
            {
                return fallback;
            }

            if (_lastNormalHeaderHeight > 0f)
            {
                return _lastNormalHeaderHeight;
            }

            if (fallback > 0f)
            {
                return fallback;
            }

            return PawnTableCompat.GetCachedHeaderHeight(table);
        }

        internal static float GetEffectiveHeaderHeight(PawnTable table)
        {
            float baseHeight = GetNormalHeaderHeight(table);
            return baseHeight + GetHeaderHeightExpansion(table);
        }

        internal static float GetHeaderHeightExpansion(PawnTable table)
        {
            if (!SubWorkDrilldownState.IsActive || table == null)
            {
                return 0f;
            }

            float required = GetRequiredHeaderHeight(table);
            float fullExpansion = Mathf.Max(0f, Mathf.Ceil(required - GetNormalHeaderHeight(table)));
            return fullExpansion * Mathf.Clamp01(SubWorkDrilldownState.ModeVisualProgress);
        }

        private static float GetNormalHeaderHeight(PawnTable table)
        {
            if (_lastNormalHeaderHeight > 0f)
            {
                return _lastNormalHeaderHeight;
            }

            return PawnTableCompat.GetCachedHeaderHeight(table);
        }

        private static float GetRequiredHeaderHeight(PawnTable table)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.enableAngledHeaders)
            {
                var solver = HeaderDrawingCoordinator.GetVanillaSolver();
                int maxLevel = solver?.GetMaxLevelUsed() ?? 1;
                if (maxLevel <= 1)
                {
                    return PawnTableCompat.GetCachedHeaderHeight(table);
                }

                return Mathf.Max(PawnTableCompat.GetCachedHeaderHeight(table), VanillaHeaderMetrics.GetRequiredHeaderHeight(maxLevel));
            }

            float needed = AngledLabelDrawer.GetNeededHeight(table);
            int padding = HeaderUtility.IsAnyCJKVertical(table) ? 2 : 10;
            return Mathf.Ceil(needed + padding);
        }
    }
}
