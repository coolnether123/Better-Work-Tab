using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Chrome;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Layout;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities
{
    internal static class SleekWorkTabCoexistenceUI
    {
        private const float WorkTabButtonHeight = 26f;
        private const float SettingsBannerHeight = 38f;
        private const float SettingsBannerGap = 8f;

        private const float ToolbarBridgeGap = 8f;
        private const float MixedToolbarControlsWidth = 320f;
        private const float MixedToolbarControlsLeftRatio = 0.42f;

        internal static void DrawSettingsBannerIfNeeded(ref Rect inRect)
        {
            if (!SleekWorkTabGateway.SleekOwnsWorkTab)
            {
                return;
            }

            Rect bannerRect = new Rect(inRect.x, inRect.y, inRect.width, SettingsBannerHeight);
            Widgets.DrawBoxSolid(bannerRect, new Color(0.18f, 0.18f, 0.18f, 0.95f));
            Widgets.DrawBox(bannerRect);

            Rect labelRect = new Rect(
                bannerRect.x + 10f,
                bannerRect.y + 8f,
                bannerRect.width - 20f,
                24f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(
                labelRect,
                TranslateOrFallback("BWT_SleekActive_SettingsBanner",
                    "Sleek Work Priorities is running the Work tab."));
            Text.Anchor = TextAnchor.UpperLeft;

            inRect.yMin += SettingsBannerHeight + SettingsBannerGap;
        }

        internal static void DrawMixedToolbarExtras(Rect rect)
        {
            if (!SleekWorkTabGateway.BetterWorkTabHostsSleek)
            {
                return;
            }

            Rect controlsRect = GetMixedToolbarControlsRect(rect);
            Rect buttonRect = new Rect(
                controlsRect.x,
                controlsRect.y + 4f,
                190f,
                WorkTabButtonHeight);
            FluffyWorkTabGateway.DrawCenteredWorkTabSwitchButton(buttonRect);

            Rect settingsRect = new Rect(
                buttonRect.xMax + ToolbarBridgeGap,
                buttonRect.y,
                Mathf.Max(0f, controlsRect.xMax - buttonRect.xMax - ToolbarBridgeGap),
                buttonRect.height);
            if (settingsRect.width <= 1f)
            {
                return;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(
                settingsRect,
                TranslateOrFallback(
                    "BWT_Sleek_SettingsHint",
                    "Alt-click settings"));
            GUI.color = Color.white;
            TooltipHandler.TipRegion(
                settingsRect,
                TranslateOrFallback(
                    "BWT_Sleek_SettingsHint_Tooltip",
                    "Open Better Work Tab settings without leaving Sleek's Work tab."));

            Event current = Event.current;
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                current.alt &&
                settingsRect.Contains(current.mousePosition))
            {
                BetterWorkTabSettingsWindowService.Open(toggleExisting: false);
                current.Use();
            }
        }

        private static Rect GetMixedToolbarControlsRect(Rect rect)
        {
            float preferredX = rect.x + rect.width * MixedToolbarControlsLeftRatio;
            float x = Mathf.Clamp(
                preferredX,
                rect.x + 360f,
                rect.xMax - MixedToolbarControlsWidth - 6f);
            return new Rect(
                x,
                rect.y,
                Mathf.Min(MixedToolbarControlsWidth, Mathf.Max(0f, rect.xMax - x - 4f)),
                WorkTabButtonHeight + 8f);
        }

        internal static void DrawSleekOnlyBottomChrome(Rect inRect)
        {
            if (!SleekWorkTabGateway.SleekOwnsWorkTab)
            {
                return;
            }

            Rect infoRect = WorkTabChromeGeometry.GetInfoIconRect(inRect);
            HeaderButtons.DrawBottomRightGrouped(inRect, infoRect);
            if (Widgets.ButtonImage(infoRect, TexButton.Info))
            {
                BetterWorkTabSettingsWindowService.Open();
            }

            HeaderButtons.BottomButtonRects buttonRects = HeaderButtons.GetBottomButtonRects(inRect, infoRect);
            Rect footerRect = new Rect(
                inRect.x + 6f,
                inRect.y,
                Mathf.Max(0f, buttonRects.LeftEdge - inRect.x - 14f),
                inRect.height);
            if (footerRect.width > 1f)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.LowerLeft;
                GUI.color = new Color(1f, 1f, 1f, 0.72f);
                Widgets.Label(
                    footerRect,
                    TranslateOrFallback(
                        "BWT_Sleek_FooterHint",
                        "Better Work Tab: right-click colonist names for organization; Alt-click for settings."));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }

        private static string TranslateOrFallback(string key, string fallback)
        {
            return key.CanTranslate() ? key.Translate().ToString() : fallback;
        }
    }
}
