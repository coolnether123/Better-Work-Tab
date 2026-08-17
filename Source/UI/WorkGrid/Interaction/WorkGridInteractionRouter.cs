using Better_Work_Tab.Features;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
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
    /// established semantics while this class owns dispatch order and cross-frame gesture state.
    /// </summary>
    internal sealed class WorkGridInteractionRouter
    {
        private readonly WorkTabTutorialInteractionController _tutorialInteractionController;
        private readonly WorkTabPriorityInputHandler _priorityInputHandler;
        private readonly RuleBuilder2WorkTabInteractionController _ruleBuilder2InteractionController;
        private readonly WorkTabContextSettingsInteractionController _contextSettingsInteractionController;
        private readonly SubWorkInteractionController _subWorkInteractionController;
        private readonly WorkGridContextActionController _contextActionController;

        internal WorkGridInteractionRouter(
            WorkTabTutorialInteractionController tutorialInteractionController,
            WorkTabPriorityInputHandler priorityInputHandler,
            RuleBuilder2WorkTabInteractionController ruleBuilder2InteractionController,
            WorkTabContextSettingsInteractionController contextSettingsInteractionController,
            SubWorkInteractionController subWorkInteractionController,
            WorkGridContextActionController contextActionController)
        {
            _tutorialInteractionController = tutorialInteractionController;
            _priorityInputHandler = priorityInputHandler;
            _ruleBuilder2InteractionController = ruleBuilder2InteractionController;
            _contextSettingsInteractionController = contextSettingsInteractionController;
            _subWorkInteractionController = subWorkInteractionController;
            _contextActionController = contextActionController;
        }

        internal bool ShiftOverlayActive { get; private set; }
        internal bool ControlGestureActive { get; private set; }
        internal bool PointerGestureActive { get; private set; }

        internal void Route(Rect inRect, PawnOrganizerSystem organizer, Event evt)
        {
            if (evt == null)
            {
                return;
            }

            UpdateSessionState(evt);
            IWorkTabLayoutController layout = organizer?.Layout;
            if (TryHandleHistoryShortcut(evt))
            {
                return;
            }

            // Contextual settings owns Alt-click before any gameplay handler.
            // It must still run on Repaint so Spine can register the binding
            // lease before the corresponding MouseDown arrives.
            bool handledContextSettings =
                _contextSettingsInteractionController.TryHandleInput(inRect, layout, evt);

            if (evt.type == EventType.Repaint)
            {
                // Repaint is registration-only. Gameplay handlers must not see
                // the same event that establishes the contextual binding.
                return;
            }

            bool handledTutorial = !handledContextSettings &&
                _tutorialInteractionController.TryHandleInput(inRect, layout, evt);
            if (!handledTutorial)
            {
                _tutorialInteractionController.ReportInteraction(inRect, layout, evt);
            }

            // This order is the compatibility contract for Work-tab input.
            bool handled = handledContextSettings
                || handledTutorial
                || _priorityInputHandler.TryHandlePriorityCellInput(layout, evt)
                || HeaderButtons.TryHandleTopRightFluffyStyleInput(layout, inRect, evt)
                || FluffyTimeScheduleAssigner.TryHandleInput(evt)
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

        internal void ResetSessions()
        {
            ShiftOverlayActive = false;
            ControlGestureActive = false;
            PointerGestureActive = false;
        }

        private void UpdateSessionState(Event evt)
        {
            ShiftOverlayActive = evt.shift;
            ControlGestureActive = evt.control;
            if (evt.type == EventType.MouseDown)
            {
                PointerGestureActive = true;
            }
            else if (evt.type == EventType.MouseUp || evt.type == EventType.MouseLeaveWindow)
            {
                PointerGestureActive = false;
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
