using Better_Work_Tab;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.ModSupport.Mods.Spine;
using Better_Work_Tab.UI.Settings;
using Spine.UI.ContextualSettings;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Entry point for rendering Better Work Tab mod settings using the shared drawer.
    /// </summary>
    public static class BetterWorkTabSettingsUI
    {
        private static IModSettingsPage _page;

        private static readonly ModSettingsPageOptions PageOptions =
            new ModSettingsPageOptions
            {
                RowHeight = 32f,
                ConfigureDrawer = (drawer, _) =>
                {
                    drawer.GetLabel = definition => BWTWorkloadSettingsOwnershipPolicy.DecorateLabel(
                        definition,
                        BWTSettingsTranslation.GetLabel(definition));
                    drawer.GetTooltip = definition => BWTWorkloadSettingsOwnershipPolicy.DecorateTooltip(
                        definition,
                        BWTSettingsTranslation.GetTooltip(definition));
                    drawer.SimpleLabel = BWTSettingsTranslation.Simple;
                    drawer.AdvancedLabel = BWTSettingsTranslation.Advanced;
                    drawer.NoResultsLabel = BWTSettingsTranslation.NoResults;
                    drawer.EditColorLabel = BWTSettingsTranslation.Edit;
                    drawer.ColorPreviewTooltip = "BWT_Settings_UI_ColorPreviewTooltip".Translate();
                    drawer.ColorPreviewSink = WorkTabColorPreviewController.Instance;
                    drawer.OnSettingPreview = WorkTabColorPreviewController.Instance.PreviewSetting;
                    drawer.Filters = BWTSettingsFilters.Create();
                    drawer.FilterLabel = "BWT_Settings_UI_Filter".Translate();
                    drawer.AllSettingsFilterLabel = "BWT_Settings_UI_AllSettings".Translate();
                    drawer.IndentPerLevel = 20f;
                    drawer.OnSettingTooltipViewed = MarkSettingViewed;
                    drawer.OnSettingInteracted = (definition, settingsObject) =>
                    {
                        BWTWorkloadSettingsOwnershipPolicy.CaptureBeforeSettingInteraction(
                            definition,
                            settingsObject);
                        if (!BWTWorkloadSettingsOwnershipPolicy.IsPreviewActive)
                        {
                            // Search-alias and tutorial progress are persisted
                            // global UI state too. Do not advance either side
                            // channel from an otherwise non-destructive
                            // workload-preview interaction.
                            BWTSettingsAdaptiveSearchAliases.ConfirmInteraction(
                                drawer,
                                definition);
                            BWTGeneralTutorial.NotifySettingsRowInteracted(
                                definition?.Id);
                        }
                    };
                },
                PrepareDrawer = (drawer, settingsObject) =>
                {
                    var settings = (BetterWorkTabSettings)settingsObject;
                    if (!BWTWorkloadSettingsOwnershipPolicy.IsPreviewActive)
                    {
                        BWTSettingsAdaptiveSearchAliases.Observe(drawer, settings);
                    }
                    BWTWorkloadSettingsOwnershipPolicy.EnsureFresh();
                    drawer.ShowResetIcons = !settings.hideSettingResetIcons;
                    drawer.FocusHighlightColor = settings.Color_SettingFocusHighlight;
                    bool bulkOperationsBlocked =
                        BWTWorkloadSettingsOwnershipPolicy.IsBulkSettingsOperationBlocked(
                            out string unusedBulkBlockReason);
                    drawer.ImportExportActions = bulkOperationsBlocked
                        ? null
                        : BWTSettingsImportExportActions.Create(
                            settings,
                            NotifySettingsChanged);
                    if (BWTSettingsContextFocus.TryConsume(out BWTSettingsFocusRequest request))
                    {
                        drawer.ApplyContextFilter(
                            BWTSettingsContextFocus.CreateFilter(request),
                            request.TargetSettingId);
                    }
                },
                PrepareContentRect = (rect, _) =>
                {
                    FluffyWorkTabGateway.DrawSettingsBannerIfNeeded(ref rect);
                    BWTWorkloadSettingsOwnershipPolicy.DrawPreviewBannerIfNeeded(ref rect);
                    return rect;
                },
                ReadViewMode = settingsObject =>
                    ((BetterWorkTabSettings)settingsObject).settingsViewMode ==
                    BetterWorkTabSettings.SettingsViewMode.Simple
                        ? SettingsViewMode.Simple
                        : SettingsViewMode.Advanced,
                WriteViewMode = (settingsObject, viewMode) =>
                {
                    // The view-mode preference is global UI state. Keep it
                    // stable while a workload preview is active so the shared
                    // drawer cannot persist an unrelated global mutation.
                    if (!BWTWorkloadSettingsOwnershipPolicy.IsPreviewActive)
                    {
                        ((BetterWorkTabSettings)settingsObject).settingsViewMode =
                            viewMode == SettingsViewMode.Simple
                                ? BetterWorkTabSettings.SettingsViewMode.Simple
                                : BetterWorkTabSettings.SettingsViewMode.Advanced;
                    }
                }
            };

        internal static IContextualSettingsLease ContextualSettings =>
            GetPage().ContextualSettings;

        /// <summary>
        /// Renders the settings window contents.
        /// </summary>
        public static void DoSettingsWindowContents(Rect inRect) =>
            GetPage().Draw(inRect);

        public static void NotifySettingsChanged()
        {
            _page?.Dispose();
            _page = null;
        }

        /// <summary>
        /// Lazily builds the drawer with Better Work Tab specific translators.
        /// </summary>
        private static IModSettingsPage GetPage()
        {
            if (_page != null) return _page;

            BWTSettingsRegistry.EnsureInitialized();
            BetterWorkTabMod host = LoadedModManager.GetMod<BetterWorkTabMod>();
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (host == null || settings == null)
            {
                throw new System.InvalidOperationException(
                    "Better Work Tab settings were requested before the mod host initialized.");
            }

            _page = SpineCompatibilityGateway.Settings.Acquire(
                SpineCompatibilityGateway.ConsumerId,
                host,
                settings,
                BWTSettingsRegistry.Definitions,
                () => BWTWorkloadSettingsOwnershipPolicy.WriteSettings(settings),
                PageOptions);
            return _page;
        }

        private static void MarkSettingViewed(SettingDefinition def, object settingsObject)
        {
            if (def == null || !(settingsObject is BetterWorkTabSettings settings))
            {
                return;
            }

            // Viewing a row normally records a global settings preference. Do
            // not mutate that global object while a workload-owned settings
            // preview is active; the preview's custom rows are the only place
            // allowed to stage presentation values during this pass.
            if (BWTWorkloadSettingsOwnershipPolicy.IsPreviewActive)
            {
                return;
            }

            if (settings.RecordViewedSetting(def.Id))
            {
                settings.Write();
            }
        }
    }
}
