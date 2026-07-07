using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    internal static class FluffyWorkTabCoexistenceUI
    {
        private const float WorkTabButtonWidth = 190f;
        private const float WorkTabButtonHeight = 28f;
        private const float SettingsBannerHeight = 38f;
        private const float SettingsBannerGap = 8f;

        internal static void DrawWorkTabSwitchButton(Rect inRect)
        {
            if (!FluffyWorkTabCoexistence.IsFluffyWorkTabPresent ||
                !FluffyWorkTabCoexistence.BetterWorkTabOwnsWorkTab)
            {
                return;
            }

            Rect buttonRect = new Rect(inRect.x + 155f, inRect.y + 5f, WorkTabButtonWidth, WorkTabButtonHeight);
            if (Widgets.ButtonText(buttonRect, "BWT_UseFluffyWorkTab_Button".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "BWT_UseFluffyWorkTab_Confirm".Translate(),
                    FluffyWorkTabCoexistence.SwitchToFluffyWorkTab,
                    destructive: false,
                    title: "BWT_UseFluffyWorkTab_Title".Translate()));
            }

            TooltipHandler.TipRegion(buttonRect, "BWT_UseFluffyWorkTab_Tooltip".Translate());
        }

        internal static void DrawSettingsBannerIfNeeded(ref Rect inRect)
        {
            if (!FluffyWorkTabCoexistence.IsFluffyWorkTabPresent ||
                !FluffyWorkTabCoexistence.FluffyOwnsWorkTab)
            {
                return;
            }

            Rect bannerRect = new Rect(inRect.x, inRect.y, inRect.width, SettingsBannerHeight);
            Widgets.DrawBoxSolid(bannerRect, new Color(0.18f, 0.18f, 0.18f, 0.95f));
            Widgets.DrawBox(bannerRect);

            Rect labelRect = new Rect(bannerRect.x + 10f, bannerRect.y + 8f, bannerRect.width - 260f, 24f);
            Rect buttonRect = new Rect(bannerRect.xMax - 238f, bannerRect.y + 6f, 228f, 26f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(labelRect, "BWT_FluffyActive_SettingsBanner".Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonText(buttonRect, "BWT_SwitchBackToBetterWorkTab_Button".Translate()))
            {
                FluffyWorkTabCoexistence.SwitchToBetterWorkTab();
            }

            TooltipHandler.TipRegion(buttonRect, "BWT_SwitchBackToBetterWorkTab_Tooltip".Translate());
            inRect.yMin += SettingsBannerHeight + SettingsBannerGap;
        }
    }
}
