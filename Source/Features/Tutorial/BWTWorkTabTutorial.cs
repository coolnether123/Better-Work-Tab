using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>Single-owner coordinator for the redesigned Work-tab tutorial.</summary>
    internal static class BWTWorkTabTutorial
    {
        internal static bool TryHandleInput(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            return BWTGeneralTutorial.TryHandleInput(inRect, layout, evt);
        }

        internal static bool TryHandleAcceptKey()
        {
            return BWTGeneralTutorial.TryHandleAcceptKey();
        }

        internal static bool OwnsCurrentPointer => BWTGeneralTutorial.OwnsCurrentPointer;

        internal static void UpdatePointerOwnership(
            Rect inRect,
            IWorkTabLayoutController layout,
            Vector2 pointer)
        {
            BWTGeneralTutorial.UpdatePointerOwnership(inRect, layout, pointer);
        }

        /// <summary>
        /// Latches whether the tutorial band exists this frame. The Work tab must
        /// call this before deriving any layout geometry, because the reserved
        /// height has to stay constant across a whole Layout/Repaint cycle.
        /// </summary>
        internal static void RefreshStripReservation()
        {
            BWTGeneralTutorial.RefreshStripReservation();
        }

        /// <summary>
        /// Draws the tutorial band with the Work tab's other pinned bands, so
        /// header selections stop at the divider and row overlays paint over it
        /// the way they do over any divider.
        /// </summary>
        internal static void DrawBand(IWorkTabLayoutController layout)
        {
            BWTGeneralTutorial.DrawBand(layout);
        }

        internal static void TickAndDraw(Rect inRect, IWorkTabLayoutController layout)
        {
            BWTGeneralTutorial.TickAndDraw(inRect, layout);
        }

        /// <summary>
        /// The vertical lane the tutorial band occupies this frame, for column
        /// chrome that must not paint across it.
        /// </summary>
        internal static bool TryGetBandSpan(
            IWorkTabLayoutController layout,
            out float top,
            out float bottom)
        {
            return BWTTutorialStrip.TryGetBandSpan(layout, out top, out bottom);
        }

        internal static void ObserveInteraction(BWTTutorialInteraction interaction)
        {
            BWTGeneralTutorial.ObserveInteraction(interaction);
        }

        internal static void NotifyWorkTabClosed()
        {
            BWTGeneralTutorial.NotifyWorkTabClosed();
        }
    }
}
