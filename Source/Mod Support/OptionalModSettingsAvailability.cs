using System;
using Spine.UI.SettingsFramework;
using Verse;

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
            string workshopUrl = null)
        {
            return new SettingSuppression
            {
                When = _ => isAvailable == null || !isAvailable(),
                Reason = _ =>
                    "BWT_Settings_OptionalMod_Required".Translate(modName),
                ExternalActionUrl = workshopUrl,
                ExternalActionLabel = string.IsNullOrEmpty(workshopUrl)
                    ? null
                    : "BWT_Settings_OptionalMod_OpenWorkshop".Translate(),
                ExternalActionTooltip = string.IsNullOrEmpty(workshopUrl)
                    ? null
                    : "BWT_Settings_OptionalMod_WorkshopTooltip".Translate(modName)
            };
        }
    }
}
