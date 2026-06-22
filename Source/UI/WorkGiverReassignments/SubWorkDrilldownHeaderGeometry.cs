using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
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
        internal static float GetBaseHeaderDrawWidth(PawnTable table, float fallback)
        {
            float baseHeight = table?.cachedHeaderHeight ?? 0f;
            return baseHeight > 0f ? baseHeight : fallback;
        }

        internal static float GetEffectiveHeaderHeight(PawnTable table)
        {
            float baseHeight = table?.cachedHeaderHeight ?? 0f;
            return baseHeight + GetHeaderHeightExpansion(table);
        }

        internal static float GetHeaderHeightExpansion(PawnTable table)
        {
            if (!SubWorkDrilldownState.IsActive || table == null)
            {
                return 0f;
            }

            float required = GetRequiredHeaderHeight(table);
            return Mathf.Max(0f, Mathf.Ceil(required - table.cachedHeaderHeight));
        }

        private static float GetRequiredHeaderHeight(PawnTable table)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.enableAngledHeaders)
            {
                return table.cachedHeaderHeight;
            }

            float needed = AngledLabelDrawer.GetNeededHeight(table);
            int padding = HeaderUtility.IsAnyCJKVertical(table) ? 2 : 10;
            return Mathf.Ceil(needed + padding);
        }
    }
}
