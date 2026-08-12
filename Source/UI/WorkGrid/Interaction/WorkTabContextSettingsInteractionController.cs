using Better_Work_Tab;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Settings;
using RimWorld;
using Spine.UI.ContextualSettings;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Interaction
{
    /// <summary>
    /// Owns Alt-click context-settings focus routing and its one-time hint update.
    /// </summary>
    internal sealed class WorkTabContextSettingsInteractionController
    {
        internal bool TryHandleInput(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            if (evt == null || !inRect.Contains(evt.mousePosition))
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

            bool handled = BetterWorkTabSettingsUI.ContextualSettings.BindSetting(
                inRect,
                request.TargetSettingId);
            if (handled)
            {
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                if (settings?.showContextSettingsHint ?? false)
                {
                    settings.showContextSettingsHint = false;
                    settings.Write();
                }
            }

            return handled;
        }
    }
}
