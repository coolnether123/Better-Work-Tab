using System;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
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
