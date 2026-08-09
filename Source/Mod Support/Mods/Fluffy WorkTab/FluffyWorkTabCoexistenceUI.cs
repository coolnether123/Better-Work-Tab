using RimWorld;
using System.Collections.Generic;
using Better_Work_Tab.UI;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    internal static class FluffyWorkTabCoexistenceUI
    {
        private const float WorkTabButtonWidth = 190f;
        private const float WorkTabButtonHeight = 28f;
        private const float SettingsBannerHeight = 38f;
        private const float SettingsBannerGap = 8f;

        internal static void DrawWorkTabSwitchButton(Rect inRect)
        {
            if (!FluffyWorkTabGateway.BetterWorkTabOwnsWorkTab ||
                (!FluffyWorkTabGateway.IsPresent && !SleekWorkTabGateway.IsPresent))
            {
                return;
            }

            DrawOwnerButton(new Rect(
                inRect.x + 155f,
                inRect.y + 5f,
                WorkTabButtonWidth,
                WorkTabButtonHeight));
        }

        internal static void DrawCenteredWorkTabSwitchButton(Rect buttonRect)
        {
            if (!FluffyWorkTabGateway.BetterWorkTabOwnsWorkTab ||
                (!FluffyWorkTabGateway.IsPresent && !SleekWorkTabGateway.IsPresent))
            {
                return;
            }

            DrawOwnerButton(buttonRect);
        }

        private static void DrawOwnerButton(Rect buttonRect)
        {
            bool hasSleek = SleekWorkTabGateway.IsPresent;
            bool hasFluffy = FluffyWorkTabGateway.IsPresent;
            string label = hasSleek
                ? TranslateOrFallback("BWT_ChangeWorkTab_Button", "Change Work Tab")
                : "BWT_UseFluffyWorkTab_Button".Translate().ToString();
            if (Widgets.ButtonText(buttonRect, label))
            {
                if (hasSleek)
                {
                    OpenWorkTabOwnerMenu(hasFluffy);
                }
                else
                {
                    ShowFluffyConfirmation();
                }
            }

            TooltipHandler.TipRegion(
                buttonRect,
                hasSleek
                    ? TranslateOrFallback(
                        "BWT_ChangeWorkTab_Tooltip",
                        "Choose Better Work Tab, Fluffy Work Tab, or Sleek Work Priorities.")
                    : "BWT_UseFluffyWorkTab_Tooltip".Translate());
        }

        private static void OpenWorkTabOwnerMenu(bool hasFluffy)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    TranslateOrFallback("BWT_WorkTabOwner_Better", "Better Work Tab only"),
                    FluffyWorkTabGateway.SwitchToBetterWorkTab)
            };

            if (hasFluffy)
            {
                options.Add(new FloatMenuOption(
                    TranslateOrFallback("BWT_WorkTabOwner_Fluffy", "Fluffy Work Tab"),
                    ShowFluffyConfirmation));
            }

            if (SleekWorkTabGateway.IsPresent)
            {
                options.Add(new FloatMenuOption(
                    TranslateOrFallback(
                        "BWT_WorkTabOwner_Mixed",
                        "Better Work Tab + Sleek (recommended)"),
                    FluffyWorkTabGateway.SwitchToBetterWorkTabWithSleek));
                options.Add(new FloatMenuOption(
                    TranslateOrFallback("BWT_WorkTabOwner_Sleek", "Sleek Work Priorities"),
                    ShowSleekChoice));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ShowFluffyConfirmation()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "BWT_UseFluffyWorkTab_Confirm".Translate(),
                FluffyWorkTabGateway.SwitchToExternalWorkTab,
                destructive: false,
                title: "BWT_UseFluffyWorkTab_Title".Translate()));
        }

        private static void ShowSleekChoice()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !SleekWorkTabGateway.IsPresent)
            {
                return;
            }

            if (settings.sleekWorkTabChoicePromptDismissed)
            {
                ApplySleekChoice(settings.sleekWorkTabUseMixedByDefault);
                return;
            }

            Find.WindowStack.Add(new Dialog_WarningWithCheckbox(
                TranslateOrFallback(
                    "BWT_SleekChoice_Body",
                    "Sleek Work Priorities can run by itself, or Better Work Tab can keep its rows, headers, dividers, search, and Rule Builder while Sleek supplies the priority-cell visuals. Which Work tab do you want?"),
                TranslateOrFallback("BWT_SleekChoice_Title", "Choose Sleek Work tab mode"),
                () => ApplySleekChoice(useMixed: true),
                value => RememberSleekChoice(settings, value),
                confirmLabel: TranslateOrFallback("BWT_SleekChoice_Both", "Use both (BWT + Sleek)"),
                cancelLabel: TranslateOrFallback("BWT_SleekChoice_SleekOnly", "Use Sleek only"),
                onCancel: () => ApplySleekChoice(useMixed: false),
                rememberOnCancel: true));
        }

        private static void ApplySleekChoice(bool useMixed)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.sleekWorkTabUseMixedByDefault = useMixed;
            settings.Write();
            if (useMixed)
            {
                FluffyWorkTabGateway.SwitchToBetterWorkTabWithSleek();
            }
            else
            {
                FluffyWorkTabGateway.SwitchToSleekWorkPriorities();
            }
        }

        private static void RememberSleekChoice(BetterWorkTabSettings settings, bool remember)
        {
            if (!remember || settings == null)
            {
                return;
            }

            settings.sleekWorkTabChoicePromptDismissed = true;
            settings.Write();
        }

        internal static void DrawSettingsBannerIfNeeded(ref Rect inRect)
        {
            if (!FluffyWorkTabGateway.IsPresent ||
                !FluffyWorkTabGateway.FluffyOwnsWorkTab)
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
                FluffyWorkTabGateway.SwitchToBetterWorkTab();
            }

            TooltipHandler.TipRegion(buttonRect, "BWT_SwitchBackToBetterWorkTab_Tooltip".Translate());
            inRect.yMin += SettingsBannerHeight + SettingsBannerGap;
        }

        private static string TranslateOrFallback(string key, string fallback)
        {
            return key.CanTranslate() ? key.Translate().ToString() : fallback;
        }
    }
}
