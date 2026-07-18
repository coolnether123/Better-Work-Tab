using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Interaction
{
    internal interface IWorkGridInteractionHost
    {
        bool TryHandleHistoryShortcut(Event evt);
        bool TryHandleTutorial(Rect inRect, IWorkTabLayoutController layout, Event evt);
        void ReportTutorial(Rect inRect, IWorkTabLayoutController layout, Event evt);
        bool TryHandlePriorityCell(IWorkTabLayoutController layout, Event evt);
        bool TryHandleTopButtons(IWorkTabLayoutController layout, Rect inRect, Event evt);
        bool TryHandleFluffySchedule(Event evt);
        bool TryHandleContextSettings(Rect inRect, IWorkTabLayoutController layout, Event evt);
        bool TryHandleRuleTarget(IWorkTabLayoutController layout, Event evt);
        bool TryHandleSchedule(IWorkTabLayoutController layout, Event evt);
        bool TryHandleSubWorkBadge(IWorkTabLayoutController layout);
        bool TryHandleSubWorkExit(IWorkTabLayoutController layout);
        bool TryHandleSubWorkOpen(IWorkTabLayoutController layout);
        void ProcessRightClicks(IWorkTabLayoutController layout);
        void HandleOrganizerInput(PawnOrganizerSystem organizer, Event evt);
    }

    /// <summary>
    /// Single ordered entry point for Work-grid interaction. Specialized handlers retain their
    /// established semantics while this class owns dispatch order and cross-frame gesture state.
    /// </summary>
    internal sealed class WorkGridInteractionRouter
    {
        private readonly IWorkGridInteractionHost _host;

        internal WorkGridInteractionRouter(IWorkGridInteractionHost host)
        {
            _host = host;
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
            if (_host.TryHandleHistoryShortcut(evt))
            {
                return;
            }

            bool handledTutorial = _host.TryHandleTutorial(inRect, layout, evt);
            if (!handledTutorial)
            {
                _host.ReportTutorial(inRect, layout, evt);
            }

            // This order is the compatibility contract from the former RouteWorkTabInput.
            bool handled = handledTutorial
                || _host.TryHandlePriorityCell(layout, evt)
                || _host.TryHandleTopButtons(layout, inRect, evt)
                || _host.TryHandleFluffySchedule(evt)
                || _host.TryHandleContextSettings(inRect, layout, evt)
                || _host.TryHandleRuleTarget(layout, evt)
                || _host.TryHandleSchedule(layout, evt)
                || _host.TryHandleSubWorkBadge(layout)
                || _host.TryHandleSubWorkExit(layout)
                || _host.TryHandleSubWorkOpen(layout);
            if (handled)
            {
                return;
            }

            _host.ProcessRightClicks(layout);
            if (evt.type != EventType.Used)
            {
                _host.HandleOrganizerInput(organizer, evt);
            }
        }

        internal bool TryGetRowAt(IWorkTabLayoutController layout, Vector2 position, out WorkTabLayoutRow row)
        {
            row = default;
            return layout?.GeometrySnapshot != null && layout.TryGetRowAt(position, out row);
        }

        internal bool TryGetBodyColumnAt(
            IWorkTabLayoutController layout,
            Vector2 position,
            out WorkTabLayoutColumn column)
        {
            column = default;
            return layout?.GeometrySnapshot != null && layout.TryGetBodyColumnAt(position, out column);
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
            else if (evt.type == EventType.MouseUp || EventCompat.IsMouseLeaveWindow(evt.type))
            {
                PointerGestureActive = false;
            }
        }
    }
}
