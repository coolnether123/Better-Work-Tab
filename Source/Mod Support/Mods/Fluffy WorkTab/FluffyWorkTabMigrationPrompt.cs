using System;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    /// <summary>
    /// Presents a one-time, non-destructive compatibility choice after a Fluffy-aware
    /// colony finishes loading.
    /// </summary>
    internal static class FluffyWorkTabMigrationPrompt
    {
        private static bool _promptQueued;

        internal static void QueueIfNeeded(
            GameComponent_BWTWorldSettings component,
            FluffyWorkTabMigrationResult result)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (_promptQueued ||
                settings == null ||
                component == null ||
                !FluffyWorkTabPromptPolicy.ShouldPrompt(
                    result.IsCurrentlyActive,
                    result.HasSaveEvidence,
                    settings.fluffyWorkTabActivePromptVersion,
                    component.FluffyWorkTabCompatibilityPromptVersion))
            {
                return;
            }

            _promptQueued = true;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                _promptQueued = false;
                if (Find.WindowStack == null ||
                    Current.Game?.GetComponent<GameComponent_BWTWorldSettings>() != component ||
                    !FluffyWorkTabPromptPolicy.ShouldPrompt(
                        result.IsCurrentlyActive,
                        result.HasSaveEvidence,
                        settings.fluffyWorkTabActivePromptVersion,
                        component.FluffyWorkTabCompatibilityPromptVersion))
                {
                    return;
                }

                string promptBodyKey = result.IsCurrentlyActive && result.HasSaveEvidence
                    ? "BWT_FluffyMigration_ActiveAndHistoricalBody"
                    : result.IsCurrentlyActive
                        ? "BWT_FluffyMigration_ActiveBody"
                        : "BWT_FluffyMigration_HistoricalBody";
                Action reviewSettings = () => Resolve(component, result, openSettings: true);
                Action keepCurrentSetup = () => Resolve(component, result, openSettings: false);
                Find.WindowStack.Add(new Dialog_MessageBox(
                    promptBodyKey.Translate(),
                    "BWT_FluffyMigration_ReviewSettings".Translate(),
                    reviewSettings,
                    "BWT_FluffyMigration_KeepSetup".Translate(),
                    keepCurrentSetup,
                    title: "BWT_FluffyMigration_Title".Translate(),
                    acceptAction: reviewSettings,
                    cancelAction: keepCurrentSetup));
            });
        }

        private static void Resolve(
            GameComponent_BWTWorldSettings component,
            FluffyWorkTabMigrationResult result,
            bool openSettings)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            if (result.IsCurrentlyActive)
            {
                settings.fluffyWorkTabActivePromptVersion =
                    FluffyWorkTabPromptPolicy.CurrentPromptVersion;
                settings.Write();
            }

            if (result.HasSaveEvidence)
            {
                component.FluffyWorkTabCompatibilityPromptVersion =
                    FluffyWorkTabPromptPolicy.CurrentPromptVersion;
            }

            if (!openSettings)
            {
                return;
            }

            settings.settingsViewMode = BetterWorkTabSettings.SettingsViewMode.Advanced;
            BWTSettingsContextFocus.Request(new BWTSettingsFocusRequest(
                "BWT_FluffyMigration_SettingsContext".Translate(),
                "BWT_FluffyMigration_SettingsContextTooltip".Translate(),
                SettingIDs.CompatFluffyWorkTabHeader,
                preferAdvancedView: true,
                settingIds: new[]
                {
                    SettingIDs.CompatFluffyWorkTabHeader,
                    SettingIDs.FluffyStyleFeatures,
                    SettingIDs.FluffyStyleTopButtons,
                    SettingIDs.FluffyStyleStandaloneTopButtons,
                    SettingIDs.FluffyStyleScheduleAssigner,
                    SettingIDs.CompatFluffyWorkTabOwnership,
                    SettingIDs.CompatFluffyWorkTabOwner,
                    SettingIDs.CompatExternalWorkTabColumns,
                    SettingIDs.SubWorkDrilldownStyle,
                    SettingIDs.UiTimePrioritySchedules,
                    SettingIDs.UiFluffyTimePriorityMirroring
                }));
            MainTabWindow_BetterWork.OpenBetterWorkTabSettings(toggleExisting: false);
        }
    }
}
