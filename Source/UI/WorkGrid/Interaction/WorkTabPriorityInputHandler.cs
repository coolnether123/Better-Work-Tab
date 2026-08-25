using System;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Application;
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
using Better_Work_Tab.UI.WorkGrid.Commands;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Interaction
{
    /// <summary>
    /// Owns priority-cell input and the global-priority hit geometry used by sub-work exit input.
    /// </summary>
    internal sealed class WorkTabPriorityInputHandler
    {
        private readonly WorkTabBodyRenderer _bodyRenderer;
        private readonly Func<WorkTabApplication> _application;

        internal WorkTabPriorityInputHandler(
            WorkTabBodyRenderer bodyRenderer,
            Func<WorkTabApplication> application)
        {
            _bodyRenderer = bodyRenderer ?? throw new ArgumentNullException(nameof(bodyRenderer));
            _application = application ?? throw new ArgumentNullException(nameof(application));
        }

        internal bool TryHandlePriorityCellInput(in WorkTabView view, Event evt)
        {
            if (!view.HasMatchingLayoutRevision || evt == null ||
                (evt.type != EventType.MouseDown && evt.type != EventType.ScrollWheel))
            {
                return false;
            }

            if (RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab)
            {
                return false;
            }

            if (FluffyTimeScheduleAssigner.IsOpen)
            {
                if (WorkTabEffectiveStateRuntime.IsPreviewActive)
                {
                    WorkTabEffectiveStateRuntime.ReportBlocked(
                        WorkTabEffectiveStateDimension.Schedule,
                        "BWT_Workload_FluffyScheduleUnavailable".Translate());
                    evt.Use();
                    return true;
                }

                return false;
            }

            if (SubWorkDrilldownInput.MatchesGesture(evt))
            {
                return false;
            }

            if (TimePriorityScheduleEditor.OwnsMousePosition(evt.mousePosition))
            {
                return false;
            }

            // In mixed mode Sleek owns the visible priority cell and its normal click/wheel
            // semantics. Let its PawnColumnWorker_WorkPriority.DoCell prefix receive the event
            // after BWT has already given RuleBuilder, scheduling, and sub-work gestures first
            // refusal. BWT still owns the surrounding rows, dividers, headers, and fallback
            // interactions later in this router.
            if (SleekWorkTabGateway.BetterWorkTabHostsSleek)
            {
                if (WorkTabEffectiveStateRuntime.IsPreviewActive)
                {
                    WorkTabEffectiveStateRuntime.ReportBlocked(
                        WorkTabEffectiveStateDimension.ParentPriority,
                        "BWT_Preview_SleekCellUnavailable".Translate());
                    evt.Use();
                    return true;
                }

                return false;
            }

            if (!_bodyRenderer.TryGetRowAt(in view, evt.mousePosition, out WorkTabLayoutRow row))
            {
                return false;
            }

            if (row.Pawn == null)
            {
                return false;
            }

            if (!_bodyRenderer.TryGetBodyColumnAt(in view, evt.mousePosition, out WorkTabLayoutColumn column))
            {
                return false;
            }

            if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
            {
                return false;
            }

            if (!_bodyRenderer.TryGetPriorityBoxHit(in view, row, column, evt.mousePosition, out Rect priorityBoxRect))
            {
                return false;
            }

            if (SubWorkDrilldownState.TryGetWorkGiverForColumn(
                    column,
                    out WorkGiver workGiver,
                    out WorkTypeDef parentWorkType,
                    out _))
            {
                if (!WorkPriorityCommandGateway.CanHandleSpecificJobInput(
                        row.Pawn,
                        parentWorkType,
                        workGiver?.def))
                {
                    evt.Use();
                    return true;
                }

                if (!EnsurePreviewMembershipForMutation(view.Preview, row.Pawn, parentWorkType, evt))
                {
                    return true;
                }

                int parentPriority = ParentPriorityRead.GetObserved(
                    row.Pawn,
                    parentWorkType);
                bool handled = WorkGiverPriorityBoxRenderer.TryHandleRootInput(
                    workGiver,
                    parentWorkType,
                    row.Pawn,
                    priorityBoxRect,
                    _application(),
                    parentPriority);
                return handled;
            }

            WorkTypeDef workType = column.Column.workType;
            if (workType == null)
            {
                return false;
            }

            if (!WorkPriorityCommandGateway.CanHandleParentPriorityInput(row.Pawn, workType))
            {
                evt.Use();
                return true;
            }

            if (!EnsurePreviewMembershipForMutation(view.Preview, row.Pawn, workType, evt))
            {
                return true;
            }

            Rect rowRect = view.Geometry.GetRowScreenRect(row.VisualIndex, view.Table.scrollPosition);
            Rect rootCellRect = WorkGridInteractionGeometry.GetAnimatedBodyScreenRect(column, rowRect);
            bool parentHandled = Patch_WorkPriority_DoCell_Unified.TryHandleRootPriorityInput(
                rootCellRect,
                row.Pawn,
                workType,
                _application());
            return parentHandled;
        }

        private static bool EnsurePreviewMembershipForMutation(
            IWorkGridPreviewPort preview,
            Pawn pawn,
            WorkTypeDef workType,
            Event evt)
        {
            if (preview?.IsActive != true)
            {
                return true;
            }

            bool mutatesPriority = evt.type == EventType.ScrollWheel
                ? BetterWorkTabMod.Settings?.enableScrollWheelPriority ?? false
                : ParentPriorityRead.GetObservedManualMode(
                      pawn,
                      workType,
                      true)
                    ? evt.button == 0 || evt.button == 1
                    : evt.button == 0;
            return !mutatesPriority || preview.EnsurePawnIncludedForPriorityEdit(pawn);
        }

        internal bool TryGetGlobalPriorityBoxHit(
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
