using System;
using Better_Work_Tab.UI.WorkGrid.Invalidation;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    internal enum WorkGridRenderLayer : byte
    {
        RowBackground,
        BasePriority,
        SkillBackground,
        PassionWarningOverlay,
        FeatureOverlay
    }

    internal static class WorkGridLayerDependencies
    {
        internal static WorkGridInvalidationCategory GetDependencies(WorkGridRenderLayer layer)
        {
            switch (layer)
            {
                case WorkGridRenderLayer.RowBackground:
                    return WorkGridInvalidationCategory.PawnListOrder |
                           WorkGridInvalidationCategory.SettingsThemeLanguageScale;
                case WorkGridRenderLayer.BasePriority:
                    return WorkGridInvalidationCategory.Priority |
                           WorkGridInvalidationCategory.CapabilitySkill |
                           WorkGridInvalidationCategory.SettingsThemeLanguageScale;
                case WorkGridRenderLayer.SkillBackground:
                case WorkGridRenderLayer.PassionWarningOverlay:
                    return WorkGridInvalidationCategory.CapabilitySkill |
                           WorkGridInvalidationCategory.SettingsThemeLanguageScale;
                case WorkGridRenderLayer.FeatureOverlay:
                    return WorkGridInvalidationCategory.Priority |
                           WorkGridInvalidationCategory.CapabilitySkill |
                           WorkGridInvalidationCategory.SubWorkOverride |
                           WorkGridInvalidationCategory.HoverInteraction |
                           WorkGridInvalidationCategory.Animation;
                default:
                    return 0;
            }
        }
    }

    internal static class WorkGridCullingMath
    {
        internal static void ResolveVisibleBounds(
            float viewportStart,
            float viewportExtent,
            float scroll,
            float viewportBuffer,
            out float visibleStart,
            out float visibleEnd)
        {
            float extent = Math.Max(0f, viewportExtent);
            float buffer = Math.Max(0f, viewportBuffer);
            visibleStart = viewportStart + scroll - buffer;
            visibleEnd = viewportStart + scroll + extent + buffer;
        }

        internal static void ResolveVisibleRange(
            float[] starts,
            float[] extents,
            float viewportStart,
            float viewportExtent,
            float scroll,
            out int start,
            out int count)
        {
            ResolveVisibleRange(
                starts,
                extents,
                viewportStart,
                viewportExtent,
                scroll,
                0f,
                out start,
                out count);
        }

        internal static void ResolveVisibleRange(
            float[] starts,
            float[] extents,
            float viewportStart,
            float viewportExtent,
            float scroll,
            float viewportBuffer,
            out int start,
            out int count)
        {
            start = 0;
            count = 0;
            if (starts == null || extents == null || starts.Length == 0 || starts.Length != extents.Length)
            {
                return;
            }

            ResolveVisibleBounds(
                viewportStart,
                viewportExtent,
                scroll,
                viewportBuffer,
                out float visibleStart,
                out float visibleEnd);
            int first = -1;
            int last = -1;
            for (int index = 0; index < starts.Length; index++)
            {
                float itemStart = starts[index];
                float itemEnd = itemStart + Math.Max(0f, extents[index]);
                if (itemEnd < visibleStart || itemStart > visibleEnd)
                {
                    continue;
                }

                if (first < 0)
                {
                    first = index;
                }
                last = index;
            }

            if (first >= 0)
            {
                start = first;
                count = last - first + 1;
            }
        }
    }

    /// <summary>
    /// Pure table-origin policy shared by the window's pre-layout and post-layout geometry passes.
    /// </summary>
    internal static class WorkGridViewportOriginMath
    {
        internal static float ResolveTableOriginY(
            float contentRectMinY,
            float contentRectMaxY,
            float extraTopSpace,
            float bottomReservedSpace,
            float scrollViewFitAllowance,
            float headerHeight,
            float pinnedRowsHeight,
            float contentHeight)
        {
            float bottomAnchoredOrigin = contentRectMaxY -
                Math.Max(0f, bottomReservedSpace) -
                Math.Max(0f, scrollViewFitAllowance) -
                Math.Max(0f, headerHeight) -
                Math.Max(0f, pinnedRowsHeight) -
                Math.Max(0f, contentHeight);
            float visibleContentTop = contentRectMinY + Math.Max(0f, extraTopSpace);
            return Math.Max(visibleContentTop, bottomAnchoredOrigin);
        }
    }
}
