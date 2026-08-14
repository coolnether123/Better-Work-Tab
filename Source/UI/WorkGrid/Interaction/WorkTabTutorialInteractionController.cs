using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Chrome;
using Better_Work_Tab.UI.WorkGrid.Layout;
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
        internal bool TryHandleInput(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            return BWTWorkTabTutorial.TryHandleInput(inRect, layout, evt);
        }

        internal bool TryHandleAcceptKey()
        {
            return BWTWorkTabTutorial.TryHandleAcceptKey();
        }

        internal void ReportInteraction(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            if (evt == null || evt.type != EventType.MouseDown)
            {
                return;
            }

            BWTTutorialInteractionKind kind = ClassifyInteraction(inRect, layout, evt.mousePosition);
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
            Rect inRect,
            IWorkTabLayoutController layout,
            Vector2 mousePosition)
        {
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

            if (layout?.Rows != null && layout.TryGetRowAt(mousePosition, out var row))
            {
                if (row.Divider != null)
                {
                    return BWTTutorialInteractionKind.Divider;
                }

                if (row.Pawn != null && layout.TryGetBodyColumnAt(mousePosition, out var bodyColumn))
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

            if (layout?.Columns != null)
            {
                for (int i = 0; i < layout.Columns.Count; i++)
                {
                    WorkTabLayoutColumn column = layout.Columns[i];
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
