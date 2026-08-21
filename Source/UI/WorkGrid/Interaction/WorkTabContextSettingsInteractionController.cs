using Better_Work_Tab;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Chrome;
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

        internal bool TryHandleFooterInput(Rect inRect, Event evt)
        {
            if (evt == null || !inRect.Contains(evt.mousePosition))
            {
                return false;
            }

            HeaderButtons.BottomButtonRects rects = HeaderButtons.GetBottomButtonRects(
                inRect,
                WorkTabChromeGeometry.GetInfoIconRect(inRect));
            if (!rects.ContainsWorkloadFooter(evt.mousePosition) ||
                !BWTWorkTabContextSettingsRouter.TryBuildFocusRequest(
                    inRect,
                    null,
                    evt.mousePosition,
                    evt.shift,
                    evt.control,
                    out BWTSettingsFocusRequest request))
            {
                return false;
            }

            IContextualSettingsLease contextualSettings =
                BetterWorkTabSettingsUI.ContextualSettings;
            bool handled = false;
            handled |= BindFooterRect(
                contextualSettings,
                rects.HasWorkload ? rects.WorkloadMain : Rect.zero,
                request.TargetSettingId);
            handled |= BindFooterRect(
                contextualSettings,
                rects.HasWorkloadMenu ? rects.WorkloadMenu : Rect.zero,
                request.TargetSettingId);
            handled |= BindFooterRect(
                contextualSettings,
                rects.HasWorkloadSaveAs ? rects.WorkloadSaveAs : Rect.zero,
                request.TargetSettingId);
            handled |= BindFooterRect(
                contextualSettings,
                rects.HasWorkloadUpdate ? rects.WorkloadUpdate : Rect.zero,
                request.TargetSettingId);
            handled |= BindFooterRect(
                contextualSettings,
                rects.WorkloadCancel,
                request.TargetSettingId);
            handled |= BindFooterRect(
                contextualSettings,
                rects.WorkloadApply,
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

        private static bool BindFooterRect(
            IContextualSettingsLease contextualSettings,
            Rect rect,
            string settingId)
        {
            return rect.width > 0f &&
                   rect.height > 0f &&
                   contextualSettings.BindSetting(rect, settingId);
        }
    }
}
