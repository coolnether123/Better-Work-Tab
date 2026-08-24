using System;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Commands;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    /// <summary>
    /// Owns Rule Builder 2's Work-tab target selection and hover preview policy.
    /// </summary>
    internal sealed class RuleBuilder2WorkTabInteractionController
    {
        private readonly WorkTabBodyRenderer _bodyRenderer;

        internal RuleBuilder2WorkTabInteractionController(WorkTabBodyRenderer bodyRenderer)
        {
            _bodyRenderer = bodyRenderer ?? throw new ArgumentNullException(nameof(bodyRenderer));
        }

        internal bool TryHandleInput(in WorkTabView view, Event evt)
        {
            if (SubWorkDrilldownInput.MatchesGesture(evt))
            {
                return false;
            }

            if (!RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab ||
                view.Layout == null ||
                evt == null ||
                evt.type != EventType.MouseDown ||
                evt.button != 0)
            {
                return false;
            }

            if (TryGetPriorityCellTarget(in view, evt.mousePosition, out WorkTypeDef workType, out WorkGiverDef workGiver, out Pawn pawn, out int priority, out Rect priorityBoxRect))
            {
                WorkPriorityCommandGateway.SelectRuleTarget(
                    workType,
                    workGiver,
                    pawn,
                    priority,
                    priorityBoxRect,
                    header: false);
                evt.Use();
                return true;
            }

            if (TryGetHeaderTarget(in view, evt.mousePosition, out workType, out workGiver, out Rect headerBounds))
            {
                WorkPriorityCommandGateway.SelectRuleTarget(
                    workType,
                    workGiver,
                    pawn: null,
                    priority: WorkPrioritySystem.DisabledPriority,
                    bounds: headerBounds,
                    header: true);
                evt.Use();
                return true;
            }

            return false;
        }

        internal void UpdateHover(in WorkTabView view)
        {
            if (!RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab ||
                view.Layout == null ||
                TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                !Mouse.IsOver(view.WindowRect) ||
                BWTWorkTabTutorial.OwnsCurrentPointer ||
                RuleBuilderGateway.RuleBuilder2BlocksWorkTabHover())
            {
                RuleBuilderGateway.ClearRuleBuilder2WorkTabPreview();
                return;
            }

            Vector2 mousePosition = Event.current.mousePosition;
            if (TryGetPriorityCellTarget(in view, mousePosition, out WorkTypeDef workType, out WorkGiverDef workGiver, out Pawn pawn, out int priority, out Rect priorityBoxRect))
            {
                RuleBuilderGateway.PreviewPriorityCellForRuleBuilder2(
                    workType,
                    workGiver,
                    pawn,
                    priority,
                    priorityBoxRect);
                return;
            }

            if (TryGetHeaderTarget(in view, mousePosition, out workType, out workGiver, out Rect headerBounds))
            {
                RuleBuilderGateway.PreviewHeaderForRuleBuilder2(workType, workGiver, headerBounds);
                return;
            }

            RuleBuilderGateway.ClearRuleBuilder2WorkTabPreview();
        }

        private bool TryGetPriorityCellTarget(
            in WorkTabView view,
            Vector2 mousePosition,
            out WorkTypeDef workType,
            out WorkGiverDef workGiver,
            out Pawn pawn,
            out int priority,
            out Rect priorityBoxRect)
        {
            workType = null;
            workGiver = null;
            pawn = null;
            priority = WorkPrioritySystem.DisabledPriority;
            priorityBoxRect = Rect.zero;

            if (!_bodyRenderer.TryGetRowAt(in view, mousePosition, out WorkTabLayoutRow row) ||
                row.Pawn == null ||
                !_bodyRenderer.TryGetBodyColumnAt(in view, mousePosition, out WorkTabLayoutColumn bodyColumn) ||
                !(bodyColumn.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                !_bodyRenderer.TryGetPriorityBoxHit(in view, row, bodyColumn, mousePosition, out priorityBoxRect))
            {
                return false;
            }

            workType = RuleBuilder2WorkTabOverlay.ResolveWorkType(bodyColumn);
            pawn = row.Pawn;
            priority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            if (SubWorkDrilldownState.TryGetWorkGiverForColumn(bodyColumn, out WorkGiver activeWorkGiver, out _, out _))
            {
                workGiver = activeWorkGiver.def;
                priority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, priority);
            }

            return true;
        }

        private bool TryGetHeaderTarget(
            in WorkTabView view,
            Vector2 mousePosition,
            out WorkTypeDef workType,
            out WorkGiverDef workGiver,
            out Rect headerBounds)
        {
            workType = null;
            workGiver = null;
            headerBounds = Rect.zero;

            IWorkTabLayoutController layout = view.Layout;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                Rect headerRect = FluffyWorkTabGateway.GetHostedHeaderLaneRect(
                    column.Column,
                    layout.Table,
                    WorkGridInteractionGeometry.GetAnimatedHeaderRect(column));
                if (!RuleBuilder2WorkTabOverlay.TryGetHeaderHighlight(column, headerRect, layout.Table, out var headerHighlight) ||
                    !headerHighlight.Contains(mousePosition) ||
                    !(column.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                    column.Column.workType == null)
                {
                    continue;
                }

                workType = RuleBuilder2WorkTabOverlay.ResolveWorkType(column);
                if (SubWorkDrilldownState.TryGetWorkGiverForColumn(column, out WorkGiver activeWorkGiver, out _, out _))
                {
                    workGiver = activeWorkGiver.def;
                }

                headerBounds = headerHighlight.Bounds;
                return true;
            }

            return false;
        }
    }
}
