using Spine.UI.Tooltips;
using Spine.UI.ContextualSettings;
using Spine.UI.SettingsFramework;
using Spine.Harmony;

namespace Spine.Api
{
    /// <summary>
    /// Stable entry point for Spine runtime negotiation and opt-in services.
    /// </summary>
    public static class SpineApi
    {
        public static ISpineRuntimeFacade Runtime =>
            SpineRuntimeFacade.Instance;

        public static ITooltipSizingFacade Tooltips =>
            StableTooltipSizing.Instance;

        public static IModSettingsFacade Settings =>
            ModSettingsFacade.Instance;

        public static IContextualSettingsFacade ContextualSettings =>
            ContextualSettingsService.Instance;

        public static IHarmonyPatchingFacade Patching =>
            HarmonyPatchingFacade.Instance;
    }
}
