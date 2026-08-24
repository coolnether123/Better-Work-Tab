using Better_Work_Tab.Features;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.RuleBuilderV2;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Projection;
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

        // Footer Alt-clicks are routed before the normal footer actions. The
        // window uses this narrow entry point to register and consume the
        // clipped footer rectangles without replaying the Work-grid router.
        internal bool TryHandleFooterContextSettings(
            Rect inRect,
            Event evt)
        {
            if (evt == null)
            {
                return false;
            }

            return _contextSettingsInteractionController.TryHandleFooterInput(inRect, evt);
        }

        internal void Route(in WorkTabView view, PawnOrganizerSystem organizer, Event evt)
        {
            if (evt == null)
            {
                return;
            }

            UpdateSessionState(evt);
            IWorkTabLayoutController layout = view.Layout;
            if (TryHandleHistoryShortcut(view.Preview, evt))
            {
                return;
            }

            // Contextual settings owns Alt-click before any gameplay handler.
            // It must still run on Repaint so Spine can register the binding
            // lease before the corresponding MouseDown arrives.
            bool handledContextSettings =
                _contextSettingsInteractionController.TryHandleInput(in view, evt);

            if (evt.type == EventType.Repaint)
            {
                // Repaint is registration-only. Gameplay handlers must not see
                // the same event that establishes the contextual binding.
                return;
            }

            bool handledTutorial = !handledContextSettings &&
                _tutorialInteractionController.TryHandleInput(in view, evt);
            if (!handledTutorial)
            {
                _tutorialInteractionController.ReportInteraction(in view, evt);
            }

            // This order is the compatibility contract for Work-tab input.
            bool handled = handledContextSettings
                || handledTutorial
                || _priorityInputHandler.TryHandlePriorityCellInput(in view, evt)
                || HeaderButtons.TryHandleTopRightFluffyStyleInput(layout, view.Viewport, evt)
                || FluffyTimeScheduleAssigner.TryHandleInput(evt)
                || _ruleBuilder2InteractionController.TryHandleInput(in view, evt)
                || TimePriorityScheduleEditor.TryHandleInput(in view, evt)
                || _subWorkInteractionController.TryHandleSubWorkBackButtonClick(in view)
                || _subWorkInteractionController.TryHandleSubWorkExitGesture(in view)
                || _subWorkInteractionController.TryHandleSubWorkHeaderOpen(in view);
            if (handled)
            {
                return;
            }

            _contextActionController.ProcessRightClicks(in view);
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

        private static bool TryHandleHistoryShortcut(
            IWorkGridPreviewPort preview,
            Event evt)
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
            if (!undo && !redo)
            {
                return false;
            }

            if (preview?.TryHandleHistoryShortcut(evt) == true)
            {
                return true;
            }

            if (WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    "BWT_Workload_OrderUndoUnavailable".Translate());
                evt.Use();
                return true;
            }

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
