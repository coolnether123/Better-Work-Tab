namespace Spine.UI.SettingsFramework
{
    internal static class SettingsPresentationPolicy
    {
        internal const int MinimumSettingsForSearch = 5;
        internal const int MinimumSettingsForFilters = 11;
        internal const int MinimumAdditionalAdvancedSettings = 4;

        internal static bool ShowSearch(int settingCount)
        {
            return settingCount >= MinimumSettingsForSearch;
        }

        internal static bool ShowFilters(int settingCount)
        {
            return settingCount >= MinimumSettingsForFilters;
        }

        internal static bool ShowViewModes(
            int settingCount,
            int advancedOnlySettingCount)
        {
            return ShowFilters(settingCount) &&
                advancedOnlySettingCount >=
                    MinimumAdditionalAdvancedSettings;
        }
    }
}
