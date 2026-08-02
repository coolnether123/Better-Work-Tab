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
            int settingCount)
        {
            return new SettingSuppression
            {
                When = _ => isAvailable == null || !isAvailable(),
                Reason = _ =>
                    $"Install {modName} to enable these {settingCount} settings."
            };
        }
    }
}
