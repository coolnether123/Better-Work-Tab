using Spine.Api;

namespace Better_Work_Tab.ModSupport.Mods.Spine
{
    internal enum SpineProviderKind
    {
        Embedded,
        Standalone
    }

    internal readonly struct SpineCompatibilityDecision
    {
        internal SpineCompatibilityDecision(
            SpineProviderKind provider,
            bool isCompatible,
            SpineCapability missingCapabilities,
            string detail)
        {
            Provider = provider;
            IsCompatible = isCompatible;
            MissingCapabilities = missingCapabilities;
            Detail = detail ?? string.Empty;
        }

        internal SpineProviderKind Provider { get; }
        internal bool IsCompatible { get; }
        internal SpineCapability MissingCapabilities { get; }
        internal string Detail { get; }
    }

    internal static class SpineCompatibilityPolicy
    {
        internal static SpineCompatibilityDecision Evaluate(
            bool standalonePackageActive,
            bool standaloneAssemblyBound,
            SpineApiDescriptor descriptor,
            SpineRequirement requirement)
        {
            if (!standalonePackageActive && standaloneAssemblyBound)
            {
                return new SpineCompatibilityDecision(
                    SpineProviderKind.Standalone,
                    false,
                    SpineCapability.None,
                    "Spine.dll is bound even though CoolNether123.Spine is not active. " +
                    "BWT will not treat an unmanaged assembly copy as its shared runtime.");
            }

            if (standalonePackageActive && !standaloneAssemblyBound)
            {
                return new SpineCompatibilityDecision(
                    SpineProviderKind.Embedded,
                    false,
                    SpineCapability.None,
                    "CoolNether123.Spine is active, but this BWT assembly is bound to its embedded Spine copy. " +
                    "The external-Spine BWT assembly was not selected by LoadFolders.xml.");
            }

            if (!standaloneAssemblyBound)
            {
                return new SpineCompatibilityDecision(
                    SpineProviderKind.Embedded,
                    true,
                    SpineCapability.None,
                    "Using BWT's embedded Spine runtime.");
            }

            SpineCapability missing = requirement.RequiredCapabilities &
                ~descriptor.Capabilities;
            if (descriptor.Version < requirement.MinimumVersion)
            {
                return new SpineCompatibilityDecision(
                    SpineProviderKind.Standalone,
                    false,
                    missing,
                    requirement.ConsumerId + " requires Spine " +
                    requirement.MinimumVersion + " or newer; loaded " +
                    descriptor.Version + ".");
            }

            if (missing != SpineCapability.None)
            {
                return new SpineCompatibilityDecision(
                    SpineProviderKind.Standalone,
                    false,
                    missing,
                    requirement.ConsumerId +
                    " requires unavailable Spine capabilities: " + missing + ".");
            }

            return new SpineCompatibilityDecision(
                SpineProviderKind.Standalone,
                true,
                SpineCapability.None,
                "Using standalone Spine " + descriptor.Version + ".");
        }
    }
}
