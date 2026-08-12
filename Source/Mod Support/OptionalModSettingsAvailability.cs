using System;
using Better_Work_Tab.UI.SettingsFramework;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Builds consistent disabled-state explanations for settings supplied by optional mods.
    /// </summary>
    internal static class OptionalModSettingsAvailability
    {
        internal static SettingSuppression Require(
            Func<bool> isAvailable,
            string modName,
            int settingCount,
            string workshopUrl = null)
        {
            return new SettingSuppression
            {
                When = _ => isAvailable == null || !isAvailable(),
                Reason = _ =>
                    $"Requires {modName}.",
                ExternalActionUrl = workshopUrl,
                ExternalActionLabel = string.IsNullOrEmpty(workshopUrl) ? null : "Open Workshop",
                ExternalActionTooltip = string.IsNullOrEmpty(workshopUrl)
                    ? null
                    : $"Requires {modName}. Click to open its Steam Workshop page."
            };
        }
    }
}
