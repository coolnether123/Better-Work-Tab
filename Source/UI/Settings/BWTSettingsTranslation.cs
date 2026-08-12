using System;
using Spine.UI.SettingsFramework;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Translation helpers for Better Work Tab settings UI.
    /// </summary>
    public static class BWTSettingsTranslation
    {
        /// <summary>
        /// Translated label for the Simple view toggle.
        /// </summary>
        public static string Simple => "BWT_Settings_UI_Simple".Translate();

        /// <summary>
        /// Translated label for the Advanced view toggle.
        /// </summary>
        public static string Advanced => "BWT_Settings_UI_Advanced".Translate();

        /// <summary>
        /// Translated text for empty search results.
        /// </summary>
        public static string NoResults => "BWT_Settings_UI_NoResults".Translate();

        /// <summary>
        /// Translated label for the color edit button.
        /// </summary>
        public static string Edit => "BWT_Settings_UI_Edit".Translate();

        /// <summary>
        /// Resolves a label for a setting using translation keys with fallbacks.
        /// </summary>
        public static string GetLabel(SettingDefinition def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            string key = GetLabelKey(def);
            if (!string.IsNullOrEmpty(key) && key.CanTranslate())
            {
                return key.Translate();
            }

            return def.Label ?? def.Id;
        }

        /// <summary>
        /// Resolves a tooltip for a setting using translation keys with fallbacks.
        /// </summary>
        public static string GetTooltip(SettingDefinition def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            string key = GetTooltipKey(def);
            if (!string.IsNullOrEmpty(key) && key.CanTranslate())
            {
                return key.Translate();
            }

            return def.Tooltip ?? string.Empty;
        }

        public static string GetLabelKey(SettingDefinition def)
        {
            return def == null
                ? string.Empty
                : def.LabelKey ?? $"BWT_Settings_{def.Id}";
        }

        public static string GetTooltipKey(SettingDefinition def)
        {
            return def == null
                ? string.Empty
                : def.TooltipKey ?? $"BWT_Settings_{def.Id}_Tooltip";
        }

        public static string GetEnumLabel(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            Type enumType = value.GetType();
            string key = $"BWT_Enum_{enumType.Name}_{value}";
            if (key.CanTranslate())
            {
                return key.Translate();
            }

            if (enumType == typeof(BetterWorkTabSettings.SkillViewHoverMode))
            {
                switch ((BetterWorkTabSettings.SkillViewHoverMode)value)
                {
                    case BetterWorkTabSettings.SkillViewHoverMode.Standard:
                        return "Priority";
                    case BetterWorkTabSettings.SkillViewHoverMode.SkillFocused:
                        return "Skill";
                }
            }

            return value.ToString();
        }

        public static string GetEnumDescription(object value)
        {
            if (value == null)
            {
                return null;
            }

            string key = $"BWT_Enum_{value.GetType().Name}_{value}_Description";
            return key.CanTranslate() ? key.Translate() : null;
        }
    }
}
