using Better_Work_Tab.Features;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.RuleBuilderV2;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGrid.Interaction
{
    /// <summary>
    /// Single ordered entry point for Work-grid interaction. Specialized handlers retain their
    /// established semantics while this class owns dispatch order.
    /// </summary>
    internal sealed class WorkGridInteractionRouter
    {
        private readonly WorkTabTutorialInteractionController _tutorialInteractionController;
        private readonly WorkTabPriorityInputHandler _priorityInputHandler;
        private readonly RuleBuilder2WorkTabInteractionController _ruleBuilder2InteractionController;
        private readonly SubWorkInteractionController _subWorkInteractionController;
        private readonly WorkGridContextActionController _contextActionController;

        internal WorkGridInteractionRouter(
            WorkTabTutorialInteractionController tutorialInteractionController,
            WorkTabPriorityInputHandler priorityInputHandler,
            RuleBuilder2WorkTabInteractionController ruleBuilder2InteractionController,
            SubWorkInteractionController subWorkInteractionController,
            WorkGridContextActionController contextActionController)
        {
            _tutorialInteractionController = tutorialInteractionController;
            _priorityInputHandler = priorityInputHandler;
            _ruleBuilder2InteractionController = ruleBuilder2InteractionController;
            _subWorkInteractionController = subWorkInteractionController;
            _contextActionController = contextActionController;
        }

        internal void Route(Rect inRect, PawnOrganizerSystem organizer, Event evt)
        {
            if (evt == null)
            {
                return;
            }

            IWorkTabLayoutController layout = organizer?.Layout;
            if (TryHandleHistoryShortcut(evt))
            {
                return;
            }

            bool handledTutorial = _tutorialInteractionController.TryHandleInput(inRect, layout, evt);
            if (!handledTutorial)
            {
                _tutorialInteractionController.ReportInteraction(inRect, layout, evt);
            }

            // This order is the compatibility contract for Work-tab input.
            bool handled = handledTutorial
                || _priorityInputHandler.TryHandlePriorityCellInput(layout, evt)
                || HeaderButtons.TryHandleTopRightFluffyStyleInput(layout, inRect, evt)
                || FluffyTimeScheduleAssigner.TryHandleInput(evt)
                || BWTWorkTabContextSettingsRouter.TryHandleInput(inRect, layout, evt)
                || _ruleBuilder2InteractionController.TryHandleInput(layout, evt)
                || TimePriorityScheduleEditor.TryHandleInput(layout, evt)
                || _subWorkInteractionController.TryHandleSubWorkBackButtonClick(layout)
                || _subWorkInteractionController.TryHandleSubWorkExitGesture(layout)
                || _subWorkInteractionController.TryHandleSubWorkHeaderOpen(layout);
            if (handled)
            {
                return;
            }

            _contextActionController.ProcessRightClicks(layout);
            if (evt.type != EventType.Used)
            {
                organizer?.HandleInput(evt);
            }
        }

        private static bool TryHandleHistoryShortcut(Event evt)
        {
            if (evt.type != EventType.KeyDown ||
                !evt.control ||
                GUIUtility.keyboardControl != 0 ||
                BetterWorkTabLocalState.IsHeaderDragging)
            {
                return false;
            }

            bool redo = evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shift);
            bool undo = evt.keyCode == KeyCode.Z && !evt.shift;
            bool changed = redo
                ? WorkGiverReassignmentManager.TryRedoWorkGiverLayout()
                : undo && WorkGiverReassignmentManager.TryUndoWorkGiverLayout();
            if (!changed)
            {
                return false;
            }

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            evt.Use();
            return true;
        }
    }
}
