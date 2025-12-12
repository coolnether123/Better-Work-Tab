using Verse;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Centralized translation helper for settings UI strings.
    /// Falls back to raw keys if translations are missing.
    /// </summary>
    public static class SettingsTranslation
    {
        // UI Chrome
        public static string Simple => "BWT_Settings_UI_Simple".Translate();
        public static string Advanced => "BWT_Settings_UI_Advanced".Translate();
        public static string Edit => "BWT_Settings_UI_Edit".Translate();
        public static string Close => "BWT_Settings_UI_Close".Translate();
        public static string NoResults => "BWT_Settings_UI_NoResults".Translate();
        public static string PinnedSettings => "BWT_Settings_UI_PinnedSettings".Translate();

        /// <summary>
        /// Gets the translated label for a setting.
        /// Falls back to the raw Label field if no translation exists.
        /// </summary>
        public static string GetSettingLabel(SettingDefinition def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            string key = $"BWT_Settings_{def.Id}";
            if (key.CanTranslate())
            {
                return key.Translate();
            }

            return def.Label ?? def.Id;
        }

        /// <summary>
        /// Gets the translated tooltip for a setting.
        /// Falls back to the raw Tooltip field if no translation exists.
        /// </summary>
        public static string GetSettingTooltip(SettingDefinition def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            string key = $"BWT_Settings_{def.Id}_Tooltip";
            if (key.CanTranslate())
            {
                return key.Translate();
            }

            return def.Tooltip ?? string.Empty;
        }

        /// <summary>
        /// Gets the translated label for a category.
        /// </summary>
        public static string GetCategoryLabel(SettingsCategoryDefinition cat)
        {
            if (cat == null)
            {
                return string.Empty;
            }

            string key = $"BWT_Settings_{cat.Id}";
            if (key.CanTranslate())
            {
                return key.Translate();
            }

            return cat.Label ?? cat.Id;
        }

        /// <summary>
        /// Gets the translated description for a category.
        /// </summary>
        public static string GetCategoryDescription(SettingsCategoryDefinition cat)
        {
            if (cat == null)
            {
                return string.Empty;
            }

            string key = $"BWT_Settings_{cat.Id}_Desc";
            if (key.CanTranslate())
            {
                return key.Translate();
            }

            return cat.Description ?? string.Empty;
        }

        /// <summary>
        /// Checks if a setting matches a search query using translated strings.
        /// </summary>
        public static bool MatchesSearch(SettingDefinition def, string query)
        {
            if (def == null || string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            query = query.ToLowerInvariant();

            string label = GetSettingLabel(def).ToLowerInvariant();
            if (label.Contains(query))
            {
                return true;
            }

            string tooltip = GetSettingTooltip(def).ToLowerInvariant();
            if (tooltip.Contains(query))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(def.Id) && def.Id.ToLowerInvariant().Contains(query))
            {
                return true;
            }

            return false;
        }
    }
}
