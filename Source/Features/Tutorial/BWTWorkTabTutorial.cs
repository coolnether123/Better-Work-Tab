using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
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
                BWTGeneralTutorial.ObserveWorkTabState();
            }

            BWTBetaTutorial.ObserveWorkTabState();

            if (BWTGeneralTutorial.IsActive)
            {
                BWTGeneralTutorial.TickAndDraw(inRect, layout);
                return;
            }

            BWTBetaTutorial.TickAndDraw(inRect, layout);
        }

        internal static void ObserveInteraction(BWTTutorialInteraction interaction)
        {
            if (BWTGeneralTutorial.IsActive)
            {
                BWTGeneralTutorial.ObserveInteraction(interaction);
            }
            else if (BWTBetaTutorial.IsActive)
            {
                if (!BWTBetaTutorial.ObserveInteraction(interaction))
                {
                    BWTGeneralTutorial.TryActivateForInteraction(interaction);
                }
            }
        }

        internal static void Start2TutorialAt(BWTBetaTutorialStep step)
        {
            BWTBetaTutorial.StartAt(step);
        }

        internal static void OpenRelatedSettings(Rect inRect, IWorkTabLayoutController layout, System.Collections.Generic.List<Rect> focusRects)
        {
            Vector2 focusPosition = focusRects != null && focusRects.Count > 0
                ? focusRects[0].center
                : inRect.center;

            if (!BWTWorkTabContextSettingsRouter.TryBuildFocusRequest(
                    inRect,
                    layout,
                    focusPosition,
                    skillOnly: false,
                    ctrlOnly: false,
                    out BWTSettingsFocusRequest request))
            {
                return;
            }

            BWTSettingsContextFocus.Request(request);
            MainTabWindow_BetterWork.OpenBetterWorkTabSettings(toggleExisting: false);
        }
    }
}
