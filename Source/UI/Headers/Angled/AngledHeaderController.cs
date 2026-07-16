using UnityEngine;
using Verse;
using RimWorld;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGiverReassignments;

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

            if (!SubWorkDrilldownState.IsActive)
            {
                SubWorkDrilldownHeaderGeometry.RecordNormalHeaderHeight(table, rect.height);
            }

            if (SubWorkDrilldownState.IsDrawingExpandBesideChild)
            {
                return DoExpandBesideChildHeader(worker, rect, table, evt);
            }

            bool shouldDraw = evt.type == EventType.Repaint;

            float rot = AngledLabelDrawer.CurrentRotation;
            float rotCos = Mathf.Cos(rot * Mathf.Deg2Rad);
            float rotSin = Mathf.Sin(rot * Mathf.Deg2Rad);
            float stableDrawWidth = SubWorkDrilldownState.HasAnyDrilldown
                ? SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(table, rect.height)
                : -1f;

            if (!AngledHeaderCache.TryGetLayout(
                    rect,
                    worker.def.workType,
                    rotCos,
                    rotSin,
                    AngledLabelDrawer.STEM_BOTTOM_GAP,
                    AngledLabelDrawer.EffectiveHorizontalOffset,
                    out var cached,
                    stableDrawWidth))
            {
                return false;
            }

            bool isMouseOver = !TimePriorityScheduleEditor.OwnsCurrentMousePosition &&
                DetermineMouseOver(rect, cached);
            
            if (isMouseOver)
            {
                HeaderInputController.SetHoveredWorkType(worker.def.workType, GetVisualBounds(cached));
            }

            var renderer = HeaderDrawingCoordinator.GetActiveRenderer();

            // Group parameters into interaction context
            var ctx = new HeaderInteractionContext
            {
                Worker = worker,
                Table = table,
                Layout = cached.Layout,
                Bounds = GetVisualBounds(cached),
                Quad = GetVisualQuad(cached),
                IsMouseOver = isMouseOver,
                ShouldDraw = shouldDraw,
                HeaderRect = rect,
                Renderer = renderer
            };

            AngledHeaderInteraction.HandleInteractions(ctx);

            return false; // Skip vanilla
        }

        private static bool DoExpandBesideChildHeader(
            PawnColumnWorker_WorkPriority worker,
            Rect rect,
            PawnTable table,
            Event evt)
        {
            bool shouldDraw = evt.type == EventType.Repaint;
            string label = HeaderUtility.GetHeaderText(worker.def.workType);
            Vector2 size = Text.CalcSize(label);
            float stableDrawWidth = SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(table, rect.height);
            Rect drawRect = new Rect(rect.x, rect.y, Mathf.Max(rect.width, stableDrawWidth), size.y)
            {
                center = rect.center
            };
            drawRect.x += AngledLabelDrawer.EffectiveHorizontalOffset;
            drawRect.position += SubWorkDrilldownHeaderGeometry.GetExpandBesideAngledAnchorOffset(
                table,
                rect.height,
                drawRect.width,
                drawRect.height,
                AngledLabelDrawer.CurrentRotation);

            var layout = new AngledLabelDrawer.AngledLabelLayout(
                label,
                size,
                drawRect.center,
                showMarker: MainTabWindow_BetterWork.ShouldShowColumnMarker(worker.def.workType),
                isCJKVertical: false,
                customDrawRect: drawRect);
            bool isMouseOver = !TimePriorityScheduleEditor.OwnsCurrentMousePosition && rect.Contains(HeaderInputController.MousePosition);
            if (isMouseOver)
            {
                HeaderInputController.SetHoveredWorkType(worker.def.workType, rect);
            }

            var ctx = new HeaderInteractionContext
            {
                Worker = worker,
                Table = table,
                Layout = layout,
                Bounds = rect,
                Quad = null,
                IsMouseOver = isMouseOver,
                ShouldDraw = shouldDraw,
                HeaderRect = rect,
                Renderer = HeaderDrawingCoordinator.GetActiveRenderer()
            };

            AngledHeaderInteraction.HandleInteractions(ctx);
            return false;
        }

        private static bool DetermineMouseOver(Rect rect, AngledHeaderCache.CachedHeaderData cached)
        {
            Vector2 mousePos = HeaderInputController.MousePosition;
            return AngledHeaderCache.IsMouseOver(GetVisualQuad(cached), mousePos);
        }

        private static Rect GetVisualBounds(AngledHeaderCache.CachedHeaderData cached)
        {
            float offsetY = GetVisualOffsetY(cached);
            if (Mathf.Abs(offsetY) <= 0.001f)
            {
                return cached.Bounds;
            }

            Rect bounds = cached.Bounds;
            bounds.y += offsetY;
            return bounds;
        }

        private static Vector2[] GetVisualQuad(AngledHeaderCache.CachedHeaderData cached)
        {
            float offsetY = GetVisualOffsetY(cached);
            if (Mathf.Abs(offsetY) <= 0.001f || cached.Quad == null)
            {
                return cached.Quad;
            }

            Vector2[] quad = new Vector2[cached.Quad.Length];
            for (int i = 0; i < cached.Quad.Length; i++)
            {
                quad[i] = new Vector2(cached.Quad[i].x, cached.Quad[i].y + offsetY);
            }

            return quad;
        }

        /// <summary>
        /// Supplies the exact rendered polygon to consumers that must follow an
        /// angled label instead of treating its column header as an axis-aligned box.
        /// </summary>
        internal static bool TryGetVisualGeometry(
            WorkTypeDef workType,
            out Rect bounds,
            out Vector2[] quad)
        {
            if (AngledHeaderCache.TryGetLatest(workType, out AngledHeaderCache.CachedHeaderData cached))
            {
                bounds = GetVisualBounds(cached);
                quad = GetVisualQuad(cached);
                return bounds.width > 0f && bounds.height > 0f && quad != null && quad.Length >= 3;
            }

            bounds = Rect.zero;
            quad = null;
            return false;
        }

        private static float GetVisualOffsetY(AngledHeaderCache.CachedHeaderData cached)
        {
            return SubWorkDrilldownState.IsActive && !cached.Layout.IsCJKVertical
                ? SubWorkDrilldownState.HeaderAnchorVisualOffsetY
                : 0f;
        }
    }
}
