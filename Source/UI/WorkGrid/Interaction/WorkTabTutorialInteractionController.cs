using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Chrome;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Interaction
{
    /// <summary>
    /// Owns the Work-tab tutorial's input and interaction telemetry boundary.
    /// Presentation and lesson state remain with the tutorial feature itself.
    /// </summary>
    internal sealed class WorkTabTutorialInteractionController
    {
        private readonly WorkTabBodyRenderer _bodyRenderer;

        internal WorkTabTutorialInteractionController(WorkTabBodyRenderer bodyRenderer)
        {
            _bodyRenderer = bodyRenderer;
        }

        internal bool TryHandleInput(in WorkTabView view, Event evt)
        {
            return BWTWorkTabTutorial.TryHandleInput(view.WindowRect, view.Layout, evt);
        }

        internal bool TryHandleAcceptKey()
        {
            return BWTWorkTabTutorial.TryHandleAcceptKey();
        }

        internal void ReportInteraction(in WorkTabView view, Event evt)
        {
            if (evt == null || evt.type != EventType.MouseDown)
            {
                return;
            }

            BWTTutorialInteractionKind kind = ClassifyInteraction(in view, evt.mousePosition);
            if (kind == BWTTutorialInteractionKind.None)
            {
                return;
            }

            BWTWorkTabTutorial.ObserveInteraction(new BWTTutorialInteraction(
                kind,
                evt.mousePosition,
                evt.button,
                evt.control,
                evt.shift,
                evt.alt));
        }

        private BWTTutorialInteractionKind ClassifyInteraction(
            in WorkTabView view,
            Vector2 mousePosition)
        {
            Rect inRect = view.WindowRect;
            if (!inRect.Contains(mousePosition))
            {
                return BWTTutorialInteractionKind.OutsideWorkTab;
            }

            if (WorkTabChromeGeometry.GetInfoIconRect(inRect).Contains(mousePosition))
            {
                return BWTTutorialInteractionKind.InfoButton;
            }

            Rect infoRect = WorkTabChromeGeometry.GetInfoIconRect(inRect);
            HeaderButtons.BottomButtonRects buttons = HeaderButtons.GetBottomButtonRects(inRect, infoRect);
            if (buttons.ContainsWorkload(mousePosition))
            {
                return BWTTutorialInteractionKind.WorkloadButton;
            }

            if (buttons.ContainsRuleset(mousePosition))
            {
                return BWTTutorialInteractionKind.RulesetButton;
            }

            if (new Rect(5f, 5f, 220f, 62f).ExpandedBy(4f).Contains(mousePosition))
            {
                return BWTTutorialInteractionKind.ManualPriorities;
            }

            if (WorkTabChromeGeometry.GetContextSettingsHintRect(
                    inRect,
                    WorkTabContextSettingsHintArea.TutorialInteraction).Contains(mousePosition))
            {
                return BWTTutorialInteractionKind.ContextSettingsHint;
            }

            if (TimePriorityScheduleEditor.OwnsMousePosition(mousePosition))
            {
                return BWTTutorialInteractionKind.TimePriorityCell;
            }

            if (view.Layout?.Rows != null && _bodyRenderer.TryGetRowAt(in view, mousePosition, out var row))
            {
                if (row.Divider != null)
                {
                    return BWTTutorialInteractionKind.Divider;
                }

                if (row.Pawn != null && _bodyRenderer.TryGetBodyColumnAt(in view, mousePosition, out var bodyColumn))
                {
                    if (bodyColumn.Column?.Worker is PawnColumnWorker_Label)
                    {
                        return BWTTutorialInteractionKind.PawnName;
                    }

                    if (bodyColumn.Column?.Worker is PawnColumnWorker_WorkPriority)
                    {
                        return BWTTutorialInteractionKind.PriorityCell;
                    }
                }

                if (row.Pawn != null)
                {
                    return BWTTutorialInteractionKind.PawnRow;
                }
            }

            if (view.Layout?.Columns != null)
            {
                for (int i = 0; i < view.Layout.Columns.Count; i++)
                {
                    WorkTabLayoutColumn column = view.Layout.Columns[i];
                    if (!WorkGridInteractionGeometry.GetAnimatedHeaderRect(column).Contains(mousePosition))
                    {
                        continue;
                    }

                    if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                    {
                        return BWTTutorialInteractionKind.None;
                    }

                    return SubWorkDrilldownState.IsActive
                        ? BWTTutorialInteractionKind.SubWorkHeader
                        : BWTTutorialInteractionKind.WorkHeader;
                }
            }

            return BWTTutorialInteractionKind.None;
        }
    }
}
