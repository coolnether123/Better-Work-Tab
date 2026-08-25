using System;
using Better_Work_Tab.Foundation.GameState;
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
        private static readonly OneTimePromptPresence Presence = new OneTimePromptPresence();

        internal static bool BlocksTutorialPresentation => Presence.IsPending;

        internal static void QueueIfNeeded(
            WorkTabGameRoot root,
            FluffyWorkTabMigrationResult result)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            // Guarding on the whole pending state, not just the queued flag, also
            // stops a second dialog stacking behind one already on screen: the
            // prompt version is only bumped once the player answers.
            if (Presence.IsPending ||
                settings == null ||
                root?.State?.CompatibilityMigrations == null ||
                !FluffyWorkTabPromptPolicy.ShouldPrompt(
                    result.IsCurrentlyActive,
                    result.HasSaveEvidence,
                    settings.fluffyWorkTabActivePromptVersion,
                    root.State.CompatibilityMigrations.CompatibilityPromptVersion))
            {
                return;
            }

            Presence.Queued = true;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Presence.Queued = false;
                if (Find.WindowStack == null ||
                    !ReferenceEquals(WorkTabGameRoots.For(Current.Game), root) ||
                    !FluffyWorkTabPromptPolicy.ShouldPrompt(
                        result.IsCurrentlyActive,
                        result.HasSaveEvidence,
                        settings.fluffyWorkTabActivePromptVersion,
                        root.State.CompatibilityMigrations.CompatibilityPromptVersion))
                {
                    return;
                }

                string promptBodyKey = result.IsCurrentlyActive && result.HasSaveEvidence
                    ? "BWT_FluffyMigration_ActiveAndHistoricalBody"
                    : result.IsCurrentlyActive
                        ? "BWT_FluffyMigration_ActiveBody"
                        : "BWT_FluffyMigration_HistoricalBody";
                Action reviewSettings = () => Resolve(root, result, openSettings: true);
                Action keepCurrentSetup = () => Resolve(root, result, openSettings: false);
#if v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
                Dialog_MessageBox dialog = new Dialog_MessageBox(
                    promptBodyKey.Translate(),
                    "BWT_FluffyMigration_ReviewSettings".Translate(),
                    reviewSettings,
                    "BWT_FluffyMigration_KeepSetup".Translate(),
                    keepCurrentSetup,
                    title: "BWT_FluffyMigration_Title".Translate());
#else
                Dialog_MessageBox dialog = new Dialog_MessageBox(
                    promptBodyKey.Translate(),
                    "BWT_FluffyMigration_ReviewSettings".Translate(),
                    reviewSettings,
                    "BWT_FluffyMigration_KeepSetup".Translate(),
                    keepCurrentSetup,
                    title: "BWT_FluffyMigration_Title".Translate(),
                    acceptAction: reviewSettings,
                    cancelAction: keepCurrentSetup);
#endif
                Find.WindowStack.Add(dialog);
                Presence.Track(dialog);
            });
        }

        private static void Resolve(
            WorkTabGameRoot root,
            FluffyWorkTabMigrationResult result,
            bool openSettings)
        {
            Presence.Clear();
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
                BWTWorkloadSettingsOwnershipPolicy.NotifyGlobalSettingsChanged();
            }

            if (result.HasSaveEvidence)
            {
                root.State.CompatibilityMigrations.CompatibilityPromptVersion =
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
            BetterWorkTabSettingsWindowService.Open(toggleExisting: false);
        }
    }
}
