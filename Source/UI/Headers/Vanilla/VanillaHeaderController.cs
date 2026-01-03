using UnityEngine;
using Verse;
using RimWorld;

namespace Better_Work_Tab.UI.Headers.Vanilla
{
    /// <summary>
    /// Controller for the vanilla-style staggered header system.
    /// Handles collection, solving, and interaction routing.
    /// </summary>
    public static class VanillaHeaderController
    {
        private const float StemLineGap = 2f;
        private const float StemLineXOffset = 5f;
        private const float StemLineDetectWidth = 10f;

        /// <summary>
        /// Calculates the minimum height required for the header area when staggered.
        /// </summary>
        public static void CalculateMinHeaderHeight(PawnTable table, ref int __result)
        {
            // For vanilla mode, calculate height based on number of levels
            if (HeaderUtility.CheckIfAnyColumnsAreMoved(table))
            {
                GameFont oldFont = Text.Font;
                Text.Font = GameFont.Small;
                float rowHeight = Text.LineHeight + StemLineGap;

                var solver = HeaderDrawingCoordinator.GetVanillaSolver();
                int maxLevel = solver?.GetMaxLevelUsed() ?? 1;

                // Vanilla baseline is roughly 50px.
                // MaxLevel 1 (Vanilla): (1 + 1.2) * 22 = 48.4px (Matches vanilla)
                int minRequired = Mathf.CeilToInt(rowHeight * (maxLevel + 1.2f)); 

                if (__result < minRequired)
                {
                    __result = minRequired;
                }

                Text.Font = oldFont;
            }
        }

        /// <summary>
        /// Handles the header rendering and logic for a single column in vanilla mode.
        /// </summary>
        /// <returns>True to allow vanilla execution (fallback), False to skip (handled).</returns>
        public static bool DoHeader(PawnColumnWorker_WorkPriority worker, Rect rect, PawnTable table)
        {
            // If angled headers are OFF, check if ANY columns are moved
            if (!HeaderUtility.CheckIfAnyColumnsAreMoved(table))
            {
                // If no columns moved, use standard vanilla rendering (return true to let vanilla run)
                return true; 
            }

            // === Collect header data during Layout event ===
            if (Event.current.type == EventType.Layout)
            {
                bool isMoved = MainTabWindow_BetterWork.ShouldShowColumnMarker(worker.def.workType);
                var solver = HeaderDrawingCoordinator.GetVanillaSolver();
                solver.CollectHeader(worker.def, rect, worker.def.workType, isMoved);
            }

            // === We're taking over: ensure layout is solved ===
            if (Event.current.type == EventType.Repaint)
            {
                HeaderDrawingCoordinator.EnsureLayoutSolved(table);
            }

            // Handle Input/Draw
            return HandleDrawAndInput(worker, rect, table);
        }

        private static bool HandleDrawAndInput(PawnColumnWorker_WorkPriority worker, Rect rect, PawnTable table)
        {
            var evt = Event.current;
            if (!HeaderUtility.ShouldHandleHeader(evt.type)) return false;

            bool shouldDraw = evt.type == EventType.Repaint;

            // Determine Hover
            bool isMouseOver = DetermineMouseOver(rect, worker.def);
            if (isMouseOver)
            {
                HeaderInputController.SetHoveredWorkType(worker.def.workType);
            }

            // Get Render/Layout Objects
            var solver = HeaderDrawingCoordinator.GetVanillaSolver();
            Rect interactionBounds = solver.GetBounds(worker.def);
            bool isMoved = MainTabWindow_BetterWork.ShouldShowColumnMarker(worker.def.workType);
            
            // Text Layout construction
            string label = HeaderUtility.GetHeaderText(worker.def.workType, isMoved);
            
            var interactionLayout = new Angled.AngledLabelDrawer.AngledLabelLayout(
                label,
                interactionBounds.size,
                interactionBounds.center,
                isMoved
            );

            // Use the AngledHeaderInteraction helper (it handles interaction logic via the renderer abstraction)
            var renderer = HeaderDrawingCoordinator.GetActiveRenderer();
            
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
                Renderer = renderer
            };

            Angled.AngledHeaderInteraction.HandleInteractions(ctx);

            return false; // Skip vanilla execution
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
