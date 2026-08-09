using Better_Work_Tab;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Settings;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGrid.Interaction
{
    /// <summary>
    /// Owns Alt-click context-settings focus routing and its one-time hint update.
    /// </summary>
    internal sealed class WorkTabContextSettingsInteractionController
    {
        internal bool TryHandleInput(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            if (evt == null ||
                evt.type != EventType.MouseDown ||
                evt.button != 0 ||
                !evt.alt ||
                !inRect.Contains(evt.mousePosition))
            {
                return false;
            }

            if (!BWTWorkTabContextSettingsRouter.TryBuildFocusRequest(
                    inRect,
                    layout,
                    evt.mousePosition,
                    evt.shift,
                    evt.control,
                    out BWTSettingsFocusRequest request))
            {
                return false;
            }

            BWTSettingsContextFocus.Request(request);
            bool opened = BetterWorkTabSettingsWindowService.Open(toggleExisting: false);
            if (opened)
            {
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                if (settings?.showContextSettingsHint ?? false)
                {
                    settings.showContextSettingsHint = false;
                    settings.Write();
                }

                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            evt.Use();
            return true;
        }
    }
}
