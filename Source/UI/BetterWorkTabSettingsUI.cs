using Better_Work_Tab;
using Better_Work_Tab.UI.Settings;
using Spine.UI.SettingsFramework;
using UnityEngine;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Entry point for rendering Better Work Tab mod settings using the shared drawer.
    /// </summary>
    public static class BetterWorkTabSettingsUI
    {
        private static SettingsListDrawer _drawer;
        private static SettingsViewMode _viewMode = SettingsViewMode.Simple;
        private static Vector2 _preservedScrollPosition = Vector2.zero;

        /// <summary>
        /// Renders the settings window contents.
        /// </summary>
        public static void DoSettingsWindowContents(Rect inRect, BetterWorkTabSettings settings)
        {
            EnsureDrawerInitialized();
            FluffyWorkTabCoexistenceUI.DrawSettingsBannerIfNeeded(ref inRect);

            _viewMode = settings.settingsViewMode == BetterWorkTabSettings.SettingsViewMode.Simple
                ? SettingsViewMode.Simple
                : SettingsViewMode.Advanced;

            _drawer.ShowResetIcons = !settings.hideSettingResetIcons;
            _drawer.FocusHighlightColor = settings.Color_SettingFocusHighlight;
            _drawer.ImportExportActions = BWTSettingsImportExportActions.Create(settings, NotifySettingsChanged);
            if (BWTSettingsContextFocus.TryConsume(out BWTSettingsFocusRequest focusRequest))
            {
                _drawer.ApplyContextFilter(
                    BWTSettingsContextFocus.CreateFilter(focusRequest),
                    focusRequest.TargetSettingId);
            }

            _drawer.Draw(inRect, settings, ref _viewMode, () => settings.Write());

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
            _drawer = new SettingsListDrawer(BWTSettingsRegistry.Hierarchy)
            {
                GetLabel = BWTSettingsTranslation.GetLabel,
                GetTooltip = BWTSettingsTranslation.GetTooltip,
                SimpleLabel = BWTSettingsTranslation.Simple,
                AdvancedLabel = BWTSettingsTranslation.Advanced,
                NoResultsLabel = BWTSettingsTranslation.NoResults,
                EditColorLabel = BWTSettingsTranslation.Edit,
                Filters = BWTSettingsFilters.Create(),
                FilterLabel = "Filter",
                AllSettingsFilterLabel = "All Settings",
                IndentPerLevel = 20f,
                RowHeight = 32f,
                ScrollPosition = _preservedScrollPosition, // Restore scroll position
                OnSettingTooltipViewed = MarkSettingViewed
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
