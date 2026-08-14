using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Diagnostics;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Layout;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Interaction
{
    /// <summary>
    /// Owns priority-cell input.
    /// </summary>
    internal sealed class WorkTabPriorityInputHandler
    {
        internal bool TryHandlePriorityCellInput(IWorkTabLayoutController layout, Event evt)
        {
            if (layout == null || evt == null ||
                (evt.type != EventType.MouseDown && evt.type != EventType.ScrollWheel))
            {
                return false;
            }

            if (RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab)
            {
                WorkTabDiagnostics.RecordPriorityInput("rule-builder owner", evt);
                return false;
            }

            if (FluffyTimeScheduleAssigner.IsOpen)
            {
                WorkTabDiagnostics.RecordPriorityInput("fluffy scheduler owner", evt);
                return false;
            }

            if (SubWorkDrilldownInput.MatchesGesture(evt))
            {
                WorkTabDiagnostics.RecordPriorityInput("sub-work gesture", evt);
                return false;
            }

            if (TimePriorityScheduleEditor.OwnsMousePosition(evt.mousePosition))
            {
                WorkTabDiagnostics.RecordPriorityInput("time-priority owner", evt);
                return false;
            }

            // In mixed mode Sleek owns the visible priority cell and its normal click/wheel
            // semantics. Let its PawnColumnWorker_WorkPriority.DoCell prefix receive the event
            // after BWT has already given RuleBuilder, scheduling, and sub-work gestures first
            // refusal. BWT still owns the surrounding rows, dividers, headers, and fallback
            // interactions later in this router.
            if (SleekWorkTabGateway.BetterWorkTabHostsSleek)
            {
                WorkTabDiagnostics.RecordPriorityInput("sleek cell owner", evt);
                return false;
            }

            if (!layout.TryGetRowAt(evt.mousePosition, out WorkTabLayoutRow row))
            {
                WorkTabDiagnostics.RecordPriorityInput("row miss", evt);
                return false;
            }

            if (row.Pawn == null)
            {
                WorkTabDiagnostics.RecordPriorityInput("non-pawn row", evt);
                return false;
            }

            if (!layout.TryGetBodyColumnAt(evt.mousePosition, out WorkTabLayoutColumn column))
            {
                WorkTabDiagnostics.RecordPriorityInput("column miss", evt);
                return false;
            }

            if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
            {
                WorkTabDiagnostics.RecordPriorityInput("non-priority column", evt);
                return false;
            }

            if (!WorkGridPriorityHitGeometry.TryGetPriorityBoxHit(
                    layout,
                    row,
                    column,
                    evt.mousePosition,
                    out Rect priorityBoxRect))
            {
                WorkTabDiagnostics.RecordPriorityInput("priority-box miss", evt);
                return false;
            }

            if (SubWorkDrilldownState.TryGetWorkGiverForColumn(
                    column,
                    out WorkGiver workGiver,
                    out WorkTypeDef parentWorkType,
                    out _))
            {
                int parentPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(row.Pawn, parentWorkType);
                bool handled = WorkGiverPriorityBoxRenderer.TryHandleRootInput(
                    workGiver,
                    parentWorkType,
                    row.Pawn,
                    priorityBoxRect,
                    parentPriority);
                WorkTabDiagnostics.RecordPriorityInput(
                    handled ? "sub-work handled" : "sub-work rejected",
                    evt);
                return handled;
            }

            WorkTypeDef workType = column.Column.workType;
            if (workType == null)
            {
                return false;
            }

            Rect rowRect = layout.GetScreenRect(row);
            Rect rootCellRect = WorkGridInteractionGeometry.GetAnimatedBodyScreenRect(column, rowRect);
            bool parentHandled = Patch_WorkPriority_DoCell_Unified.TryHandleRootPriorityInput(
                rootCellRect,
                row.Pawn,
                workType);
            WorkTabDiagnostics.RecordPriorityInput(
                parentHandled ? "parent handled" : "parent rejected",
                evt);
            return parentHandled;
        }

    }
}
