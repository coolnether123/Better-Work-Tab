using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>Single-owner coordinator for the redesigned Work-tab tutorial.</summary>
    internal static class BWTWorkTabTutorial
    {
        internal static float PreferredReserveWidth => BWTGeneralTutorial.PreferredReserveWidth;
        internal static float PreferredReserveHeight => BWTGeneralTutorial.PreferredReserveHeight;

        internal static bool TryHandleInput(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            return BWTGeneralTutorial.TryHandleInput(inRect, layout, evt);
        }

        internal static bool TryHandleAcceptKey()
        {
            return BWTGeneralTutorial.TryHandleAcceptKey();
        }

        internal static void TickAndDraw(Rect inRect, IWorkTabLayoutController layout)
        {
            BWTGeneralTutorial.TickAndDraw(inRect, layout);
        }

        internal static void ObserveInteraction(BWTTutorialInteraction interaction)
        {
            BWTGeneralTutorial.ObserveInteraction(interaction);
        }
    }
}
