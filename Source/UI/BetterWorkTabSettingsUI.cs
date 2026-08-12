using System.Linq;
using Better_Work_Tab;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.UI.Settings;
using Spine.UI.SettingsFramework;
using SettingsListDrawer = Better_Work_Tab.UI.SettingsPresentation.SettingsListDrawer;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Entry point for rendering Better Work Tab mod settings using the shared drawer.
    /// </summary>
    public static class BetterWorkTabSettingsUI
    {
        // One list, not two pages.
        //
        // Settings and Controls used to be separate drawers behind a pair of tab
        // buttons. The split cost more than it bought: the search box, the view
        // toggle and every filter chip only ever saw one half, so searching for
        // a control from the Settings page found nothing, and a context filter
        // had to work out which page its target lived on before it could open.
        // The control rows already sit under their own header, so merging keeps
        // them grouped where they were while making the whole set searchable.
        private static SettingsListDrawer _drawer;
        private static SettingsViewMode _viewMode = SettingsViewMode.Simple;
        private static Vector2 _preservedScrollPosition = Vector2.zero;

        /// <summary>
        /// Renders the settings window contents.
        /// </summary>
        public static void DoSettingsWindowContents(Rect inRect, BetterWorkTabSettings settings)
        {
            EnsureDrawerInitialized();
            FluffyWorkTabGateway.DrawSettingsBannerIfNeeded(ref inRect);

            _viewMode = settings.settingsViewMode == BetterWorkTabSettings.SettingsViewMode.Simple
                ? SettingsViewMode.Simple
                : SettingsViewMode.Advanced;

            SettingsListDrawer drawer = _drawer;
            drawer.ShowResetIcons = !settings.hideSettingResetIcons;
            drawer.FocusHighlightColor = settings.Color_SettingFocusHighlight;
            drawer.ImportExportActions = BWTSettingsImportExportActions.Create(settings, NotifySettingsChanged);
            if (BWTSettingsContextFocus.TryConsume(out BWTSettingsFocusRequest focusRequest))
            {
                drawer.ApplyContextFilter(
                    BWTSettingsContextFocus.CreateFilter(focusRequest),
                    focusRequest.TargetSettingId);
            }

            drawer.Draw(inRect, settings, ref _viewMode, () => settings.Write());
            settings.settingsViewMode = _viewMode == SettingsViewMode.Simple
                ? BetterWorkTabSettings.SettingsViewMode.Simple
                : BetterWorkTabSettings.SettingsViewMode.Advanced;
        }

        public static void NotifySettingsChanged()
        {
            // Preserve scroll position before destroying drawer
            if (_drawer != null)
            {
                _preservedScrollPosition = _drawer.ScrollPosition;
            }

            _drawer = null;
        }

        /// <summary>
        /// Lazily builds the drawer with Better Work Tab specific translators.
        /// </summary>
        private static void EnsureDrawerInitialized()
        {
            if (_drawer != null)
            {
                return;
            }

            BWTSettingsRegistry.EnsureInitialized();
            _drawer = CreateDrawer(
                new SettingsHierarchy(BWTSettingsRegistry.Definitions),
                _preservedScrollPosition);
        }

        private static SettingsListDrawer CreateDrawer(
            SettingsHierarchy hierarchy,
            Vector2 scrollPosition)
        {
            return new SettingsListDrawer(hierarchy)
            {
                GetLabel = BWTSettingsTranslation.GetLabel,
                GetTooltip = BWTSettingsTranslation.GetTooltip,
                SimpleLabel = BWTSettingsTranslation.Simple,
                AdvancedLabel = BWTSettingsTranslation.Advanced,
                NoResultsLabel = BWTSettingsTranslation.NoResults,
                EditColorLabel = BWTSettingsTranslation.Edit,
                ColorPreviewTooltip = "Hover here or adjust the picker to preview this color live on the Work tab.",
                ColorPreviewSink = WorkTabColorPreviewController.Instance,
                Filters = BWTSettingsFilters.Create(),
                FilterLabel = "Filter",
                AllSettingsFilterLabel = "All Settings",
                IndentPerLevel = 20f,
                RowHeight = 32f,
                ScrollPosition = scrollPosition,
                OnSettingTooltipViewed = MarkSettingViewed,
                OnSettingInteracted = (definition, _) =>
                    BWTGeneralTutorial.NotifySettingsRowInteracted(definition?.Id)
            };
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
