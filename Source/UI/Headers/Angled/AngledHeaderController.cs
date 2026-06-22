using UnityEngine;
using Verse;
using RimWorld;

namespace Better_Work_Tab.UI.Headers.Angled
{
    /// <summary>
    /// Controller for the angled header system.
    /// Handles layout fetching, interaction routing, and height calculation.
    /// </summary>
    public static class AngledHeaderController
    {

        /// <summary>
        /// Calculates the minimum height required for the header area when angled.
        /// Based on the maximum width of the header labels and the current rotation.
        /// </summary>
        public static void CalculateMinHeaderHeight(PawnTable table, ref int __result)
        {
            float needed = AngledLabelDrawer.GetNeededHeight(table);
            
            // Padding Logic:
            // CJK Vertical stacking looks best with minimal padding (compact bottom-anchor).
            // Standard angled headers require more padding to prevent visual collision with the tab's upper edge.
            int padding = HeaderUtility.IsAnyCJKVertical(table) ? 2 : 10;
            int angledRequired = Mathf.CeilToInt(needed + padding);
            
            if (__result < angledRequired)
            {
                __result = angledRequired;
            }
        }

        /// <summary>
        /// Handles the header rendering and logic for a single column in angled mode.
        /// </summary>
        /// <returns>True to allow vanilla execution (fallback), False to skip (handled).</returns>
        public static bool DoHeader(PawnColumnWorker_WorkPriority worker, Rect rect, PawnTable table)
        {
            var evt = Event.current;
            if (!HeaderUtility.ShouldHandleHeader(evt.type)) return false;

            bool shouldDraw = evt.type == EventType.Repaint;

            float rot = AngledLabelDrawer.CurrentRotation;
            float rotCos = Mathf.Cos(rot * Mathf.Deg2Rad);
            float rotSin = Mathf.Sin(rot * Mathf.Deg2Rad);

            if (!AngledHeaderCache.TryGetLayout(
                    rect,
                    worker.def.workType,
                    rotCos,
                    rotSin,
                    AngledLabelDrawer.STEM_BOTTOM_GAP,
                    AngledLabelDrawer.EffectiveHorizontalOffset,
                    out var cached))
            {
                return false;
            }

            bool isMouseOver = DetermineMouseOver(rect, cached);
            
            if (isMouseOver)
            {
                HeaderInputController.SetHoveredWorkType(worker.def.workType, cached.Bounds);
            }

            var renderer = HeaderDrawingCoordinator.GetActiveRenderer();

            // Group parameters into interaction context
            var ctx = new HeaderInteractionContext
            {
                Worker = worker,
                Table = table,
                Layout = cached.Layout,
                Bounds = cached.Bounds,
                Quad = cached.Quad,
                IsMouseOver = isMouseOver,
                ShouldDraw = shouldDraw,
                HeaderRect = rect,
                Renderer = renderer
            };

            AngledHeaderInteraction.HandleInteractions(ctx);

            return false; // Skip vanilla
        }

        private static bool DetermineMouseOver(Rect rect, AngledHeaderCache.CachedHeaderData cached)
        {
            Vector2 mousePos = HeaderInputController.MousePosition;
            return AngledHeaderCache.IsMouseOver(cached.Quad, mousePos);
        }
    }
}
