using Better_Work_Tab.PawnOrganizer;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.VisualFeedback
{
    /// <summary>
    /// Manages tooltip rendering for headers and cells.
    /// Single responsibility: Display contextual tooltips.
    /// </summary>
    public static class TooltipManager
    {
        public static void DrawHeaderTooltip(WorkTabLayoutColumn column, Sorting.SortState sortState)
        {
            if (column.Column == null) return;

            // Let vanilla handle the base tooltip
            // We only add extra info if sorting is active
            if (sortState.SortColumn == column.Column)
            {
                string sortInfo = sortState.Descending 
                    ? "\n\n<i>Sorted descending (click to reverse)</i>" 
                    : "\n\n<i>Sorted ascending (click to reverse)</i>";

                // Append to vanilla tooltip
                TooltipHandler.TipRegion(column.HeaderRect, 
                    column.Column.LabelCap + sortInfo);
            }
            // Otherwise, let vanilla HeaderTip show normally (don't override)
        }

        public static void DrawCellTooltip(Rect cellRect, Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null || workType == null) return;

            string tooltip = WidgetsWork.TipForPawnWorker(
                pawn, 
                workType, 
                pawn.WorkTypeIsDisabled(workType));

            TooltipHandler.TipRegion(cellRect, tooltip);
        }
    }
}