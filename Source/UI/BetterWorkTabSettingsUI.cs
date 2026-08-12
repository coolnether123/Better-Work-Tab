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
                    drawer.GetLabel = BWTSettingsTranslation.GetLabel;
                    drawer.GetTooltip = BWTSettingsTranslation.GetTooltip;
                    drawer.SimpleLabel = BWTSettingsTranslation.Simple;
                    drawer.AdvancedLabel = BWTSettingsTranslation.Advanced;
                    drawer.NoResultsLabel = BWTSettingsTranslation.NoResults;
                    drawer.EditColorLabel = BWTSettingsTranslation.Edit;
                    drawer.ColorPreviewTooltip = "Hover here or adjust the picker to preview this color live on the Work tab.";
                    drawer.ColorPreviewSink = WorkTabColorPreviewController.Instance;
                    drawer.ColorPreviewTransactionSink = WorkTabColorPreviewController.Instance;
                    drawer.OnSettingPreview = WorkTabColorPreviewController.Instance.PreviewSetting;
                    drawer.Filters = BWTSettingsFilters.Create();
                    drawer.FilterLabel = "Filter";
                    drawer.AllSettingsFilterLabel = "All Settings";
                    drawer.IndentPerLevel = 20f;
                    drawer.OnSettingTooltipViewed = MarkSettingViewed;
                    drawer.OnSettingInteracted = (definition, _) =>
                        BWTGeneralTutorial.NotifySettingsRowInteracted(definition?.Id);
                },
                PrepareDrawer = (drawer, settingsObject) =>
                {
                    var settings = (BetterWorkTabSettings)settingsObject;
                    drawer.ShowResetIcons = !settings.hideSettingResetIcons;
                    drawer.FocusHighlightColor = settings.Color_SettingFocusHighlight;
                    drawer.ImportExportActions = BWTSettingsImportExportActions.Create(
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
                    return rect;
                },
                ReadViewMode = settingsObject =>
                    ((BetterWorkTabSettings)settingsObject).settingsViewMode ==
                    BetterWorkTabSettings.SettingsViewMode.Simple
                        ? SettingsViewMode.Simple
                        : SettingsViewMode.Advanced,
                WriteViewMode = (settingsObject, viewMode) =>
                    ((BetterWorkTabSettings)settingsObject).settingsViewMode =
                        viewMode == SettingsViewMode.Simple
                            ? BetterWorkTabSettings.SettingsViewMode.Simple
                            : BetterWorkTabSettings.SettingsViewMode.Advanced
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
                settings.Write,
                PageOptions);
            return _page;
        }

        private static void MarkSettingViewed(SettingDefinition def, object settingsObject)
        {
            if (def == null || !(settingsObject is BetterWorkTabSettings settings))
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
