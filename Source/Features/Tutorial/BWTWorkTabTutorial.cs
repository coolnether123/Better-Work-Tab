using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    internal static class BWTWorkTabTutorial
    {
        internal static bool TryHandleInput(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            if (BWTGeneralTutorial.IsActive)
            {
                return BWTGeneralTutorial.TryHandleInput(inRect, layout, evt);
            }

            return BWTBetaTutorial.TryHandleInput(inRect, layout, evt);
        }

        internal static bool TryHandleAcceptKey()
        {
            if (BWTGeneralTutorial.IsActive)
            {
                return BWTGeneralTutorial.TryHandleAcceptKey();
            }

            return BWTBetaTutorial.TryHandleAcceptKey();
        }

        internal static void TickAndDraw(Rect inRect, IWorkTabLayoutController layout)
        {
            if (BWTGeneralTutorial.IsActive)
            {
                BWTGeneralTutorial.TickAndDraw(inRect, layout);
                return;
            }

            BWTBetaTutorial.TickAndDraw(inRect, layout);
        }
    }
}
