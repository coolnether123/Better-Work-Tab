using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using RimWorld;
using UnityEngine;

namespace Better_Work_Tab.UI.WorkGrid.Layout
{
    /// <summary>
    /// Owns priority-cell hit geometry without coupling input controllers to rendering.
    /// The formulas intentionally match the historical renderer and global sub-work paths.
    /// </summary>
    internal static class WorkGridPriorityHitGeometry
    {
        internal static bool TryGetPriorityBoxHit(
            IWorkTabLayoutController layout,
            WorkTabLayoutRow row,
            WorkTabLayoutColumn column,
            Vector2 mousePosition,
            out Rect priorityBoxRect)
        {
            priorityBoxRect = default;
            if (layout == null ||
                row.Pawn == null ||
                !(column.Column?.Worker is PawnColumnWorker_WorkPriority))
            {
                return false;
            }

            Rect rowRect = layout.GetScreenRect(row);
            Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyScreenRect(column, rowRect);
            priorityBoxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            return priorityBoxRect.Contains(mousePosition);
        }

        internal static bool TryGetGlobalPriorityBoxHit(
            IWorkTabLayoutController layout,
            Rect globalRowRect,
            Vector2 mousePosition,
            out WorkTabLayoutColumn column,
            out Rect priorityBoxRect)
        {
            column = default;
            priorityBoxRect = default;
            if (layout?.Columns == null)
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var candidate = layout.Columns[i];
                if (!(candidate.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                    !SubWorkDrilldownState.TryGetWorkGiverForColumn(candidate, out _, out _, out _))
                {
                    continue;
                }

                Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyScreenRect(candidate, globalRowRect);
                float boxSize = Mathf.Min(SubWorkDrilldownState.GlobalPriorityBoxSize, Mathf.Max(0f, cellRect.height - 4f));
                if (boxSize <= 6f)
                {
                    continue;
                }

                Rect boxRect = WorkPriorityCellGeometry.GetCenteredBoxRect(cellRect, boxSize);
                if (!boxRect.Contains(mousePosition))
                {
                    continue;
                }

                column = candidate;
                priorityBoxRect = boxRect;
                return true;
            }

            return false;
        }
    }
}
