using System;

namespace Spine.Api
{
    internal sealed class SpineRuntimeFacade : ISpineRuntimeFacade
    {
        internal static readonly SpineRuntimeFacade Instance =
            new SpineRuntimeFacade();

        private static readonly SpineApiDescriptor CurrentDescriptor =
            new SpineApiDescriptor(
                "CoolNether123.Spine",
                new SemanticVersion(1, 1, 0),
                SpineCapability.BoundedCaches |
                SpineCapability.Settings |
                SpineCapability.HarmonyPatching |
                (Type.GetType(
                    "Spine.Harmony.FluentTranspiler, Spine.Transpilers",
                    throwOnError: false) == null
                    ? SpineCapability.None
                    : SpineCapability.FluentTranspilers) |
                SpineCapability.TooltipSizing |
                SpineCapability.ContextualSettings |
                SpineCapability.ModSettingsPages |
                SpineCapability.SettingsSchema |
                SpineCapability.SettingsPreviewTransactions);

        private SpineRuntimeFacade()
        {
        }

        public SpineApiDescriptor Descriptor => CurrentDescriptor;

        public SpineCompatibilityResult Check(
            SpineRequirement requirement)
        {
            SpineApiDescriptor descriptor = CurrentDescriptor;
            var missing = requirement.RequiredCapabilities &
                ~descriptor.Capabilities;
            if (descriptor.Version < requirement.MinimumVersion)
            {
                return new SpineCompatibilityResult(
                    false,
                    missing,
                    requirement.ConsumerId + " requires Spine API " +
                    requirement.MinimumVersion + " or newer; loaded " +
                    descriptor.Version + ".");
            }

            if (missing != SpineCapability.None)
            {
                return new SpineCompatibilityResult(
                    false,
                    missing,
                    requirement.ConsumerId +
                    " requires unavailable Spine capabilities: " +
                    missing + ". Loaded Spine " +
                    descriptor.Version + " advertises " +
                    descriptor.Capabilities + ".");
            }

            return new SpineCompatibilityResult(
                true,
                SpineCapability.None,
                requirement.ConsumerId + " requirements are satisfied by " +
                "Spine " + descriptor.Version + ".");
        }

        public void Require(SpineRequirement requirement)
        {
            var result = Check(requirement);
            if (!result.IsCompatible)
            {
                throw new NotSupportedException(result.Detail);
            }
        }
    }
}
