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

        /// <summary>
        /// Renders the settings window contents.
        /// </summary>
        public static void DoSettingsWindowContents(Rect inRect, BetterWorkTabSettings settings)
        {
            EnsureDrawerInitialized();

            _viewMode = settings.settingsViewMode == BetterWorkTabSettings.SettingsViewMode.Simple
                ? SettingsViewMode.Simple
                : SettingsViewMode.Advanced;

            _drawer.Draw(inRect, settings, ref _viewMode, () => settings.Write());

            settings.settingsViewMode = _viewMode == SettingsViewMode.Simple
                ? BetterWorkTabSettings.SettingsViewMode.Simple
                : BetterWorkTabSettings.SettingsViewMode.Advanced;
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
                IndentPerLevel = 20f,
                RowHeight = 32f
            };
        }
    }
}
