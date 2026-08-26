using UnityEngine;
using Verse;
using RimWorld;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.Columns;
using Better_Work_Tab.UI.WorkGrid.Projection;

namespace Better_Work_Tab.UI.Headers.Vanilla
{
    /// <summary>
    /// Controller for the vanilla-style staggered header system.
    /// Handles collection, solving, and interaction routing.
    /// </summary>
    public static class VanillaHeaderController
    {
        private const float StemLineXOffset = 5f;
        private const float StemLineDetectWidth = 10f;

        /// <summary>
        /// Calculates the minimum height required for the header area when staggered.
        /// Only modifies header height when the actual stagger levels exceed vanilla.
        /// </summary>
        public static void CalculateMinHeaderHeight(PawnTable table, ref int __result)
        {
            // Height calculation is always managed by BWT to ensure the content area size matches our requirements.

            // For vanilla mode, calculate height based on number of levels
            // Get the solver and check if it has a valid solution
            var solver = HeaderDrawingCoordinator.GetVanillaSolver();
            
            // If the solver does not have a valid solution yet, the height is not modified.
            // Vanilla logic is utilized until valid layout data is available.
            // This prevents premature header expansion that would shrink the content area.
            if (solver == null || !solver.HasValidSolution())
            {
                return;
            }

            int maxLevel = solver.GetMaxLevelUsed();

            // Vanilla height is sufficient; do not modify.
            if (maxLevel <= 1)
            {
                // Vanilla height is sufficient, don't modify
                return;
            }

            // We need more than vanilla height
            int minRequired = VanillaHeaderMetrics.GetRequiredHeaderHeight(maxLevel);

            if (__result < minRequired)
            {
                __result = minRequired;
            }
        }

        /// <summary>
        /// Handles the header rendering and logic for a single column in vanilla mode.
        /// </summary>
        /// <returns>True to allow vanilla execution (fallback), False to skip (handled).</returns>
        public static bool DoHeader(PawnColumnWorker_WorkPriority worker, Rect rect, PawnTable table)
        {
            HeaderPresentationPacket presentation = HeaderDrawingCoordinator.CapturePresentation();
            return DoHeader(worker, rect, table, in presentation);
        }

        internal static bool DoHeader(
            PawnColumnWorker_WorkPriority worker,
            Rect rect,
            PawnTable table,
            in HeaderPresentationPacket presentation)
        {
            // BWT takes over header rendering to provide robust staggered layout.

            // === Collect header data during Layout event ===
            if (Event.current.type == EventType.Layout)
            {
            bool isMoved = WorkColumnCustomizationService.ShouldShowColumnMarker(worker.def.workType, presentation.ShowMovedMarker);
                var solver = HeaderDrawingCoordinator.GetVanillaSolver();
                solver.CollectHeader(worker.def, rect, worker.def.workType, isMoved);
            }

            // === The custom system assumes control: ensure layout is solved ===
            // Resolved layout is necessary for interaction (MouseDown/MouseUp) and Repaint events.
            HeaderDrawingCoordinator.EnsureLayoutSolved(table);

            // Handle Input/Draw
            return HandleDrawAndInput(worker, rect, table, in presentation);
        }

        private static bool HandleDrawAndInput(
            PawnColumnWorker_WorkPriority worker,
            Rect rect,
            PawnTable table,
            in HeaderPresentationPacket presentation)
        {
            var evt = Event.current;
            if (!HeaderUtility.ShouldHandleHeader(evt.type)) return false;

            if (!WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked &&
                SubWorkDrilldownState.IsDrawingExpandBesideChild)
            {
                return HandleExpandBesideChildDrawAndInput(worker, rect, table, evt, in presentation);
            }

            bool shouldDraw = evt.type == EventType.Repaint;

            var vanillaSolver = HeaderDrawingCoordinator.GetVanillaSolver();

            // Determine Hover
            bool isMouseOver = !TimePriorityScheduleEditor.OwnsCurrentMousePosition &&
                DetermineMouseOver(rect, worker.def);
            if (isMouseOver)
            {
                HeaderInputController.SetHoveredWorkType(worker.def.workType, vanillaSolver?.GetBounds(worker.def));
            }

            // Get Render/Layout Objects
            Rect interactionBounds = vanillaSolver.GetBounds(worker.def);
            bool isMoved = WorkColumnCustomizationService.ShouldShowColumnMarker(worker.def.workType, presentation.ShowMovedMarker);
            
            // Text Layout construction
            string label = HeaderUtility.GetHeaderText(
                worker.def.workType,
                isMoved,
                WorkGiverHeaderLabelStyle.VanillaStaggered,
                in presentation);
            
            var interactionLayout = new Angled.AngledLabelDrawer.AngledLabelLayout(
                label,
                interactionBounds.size,
                interactionBounds.center,
                isMoved
            );

            // Use the AngledHeaderInteraction helper (it handles interaction logic via the renderer abstraction)
            var renderer = presentation.ActiveRenderer;
            
            // Group parameters into interaction context
            var ctx = new Angled.HeaderInteractionContext
            {
                Worker = worker,
                Table = table,
                Layout = interactionLayout,
                Bounds = interactionBounds,
                Quad = null, // Not used for vanilla interaction
                IsMouseOver = isMouseOver,
                ShouldDraw = shouldDraw,
                HeaderRect = rect,
                Renderer = renderer,
                IsVanillaStaggered = true,
            };

            Angled.AngledHeaderInteraction.HandleInteractions(in ctx, in presentation);

            return false; // Skip vanilla execution
        }

        private static bool HandleExpandBesideChildDrawAndInput(
            PawnColumnWorker_WorkPriority worker,
            Rect rect,
            PawnTable table,
            Event evt,
            in HeaderPresentationPacket presentation)
        {
            bool shouldDraw = evt.type == EventType.Repaint;
            string label = HeaderUtility.GetHeaderText(
                worker.def.workType,
                false,
                WorkGiverHeaderLabelStyle.VanillaStaggered,
                in presentation);
            Vector2 size = Text.CalcSize(label);
            Rect bounds = new Rect(
                rect.x + 1f,
                rect.yMax - Mathf.Min(rect.height, size.y + 4f),
                Mathf.Max(1f, rect.width - 2f),
                Mathf.Min(rect.height, size.y + 4f));

            var layout = new Angled.AngledLabelDrawer.AngledLabelLayout(
                label,
                bounds.size,
                bounds.center,
                showMarker: WorkColumnCustomizationService.ShouldShowColumnMarker(worker.def.workType, presentation.ShowMovedMarker),
                isCJKVertical: false);

            bool isMouseOver = !TimePriorityScheduleEditor.OwnsCurrentMousePosition && bounds.Contains(HeaderInputController.MousePosition);
            if (isMouseOver)
            {
                HeaderInputController.SetHoveredWorkType(worker.def.workType, bounds);
            }

            var ctx = new Angled.HeaderInteractionContext
            {
                Worker = worker,
                Table = table,
                Layout = layout,
                Bounds = bounds,
                Quad = null,
                IsMouseOver = isMouseOver,
                ShouldDraw = shouldDraw,
                HeaderRect = rect,
                Renderer = presentation.ActiveRenderer,
                IsVanillaStaggered = true,
            };

            Angled.AngledHeaderInteraction.HandleInteractions(in ctx, in presentation);
            return false;
        }

        private static bool DetermineMouseOver(Rect rect, PawnColumnDef columnDef)
        {
             // Vanilla mode: use the actual staggered bounds from the solver!
            var solver = HeaderDrawingCoordinator.GetVanillaSolver();
            Rect staggeredBounds = solver.GetBounds(columnDef);
            Vector2 mousePos = HeaderInputController.MousePosition;
            
            // Allow hover if within the staggered label bounds
            if (staggeredBounds.Contains(mousePos))
            {
                return true;
            }

            // Also allow hover if within the horizontal center strip of the column (for the stem line)
            // but only above the pawn row
            float centerX = staggeredBounds.center.x;
            Rect stemChannel = new Rect(centerX - StemLineXOffset, staggeredBounds.yMax, StemLineDetectWidth, rect.yMax - staggeredBounds.yMax);
            
            return stemChannel.Contains(mousePos);
        }
    }
}
