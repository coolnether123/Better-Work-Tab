using System;
using Spine.Api;

namespace Better_Work_Tab.ModSupport.Mods.Spine
{
    internal enum SpineProviderKind
    {
        Embedded,
        Standalone
    }

    [Flags]
    internal enum SpinePublicSurface
    {
        None = 0,
        RuntimeDescriptor = 1,
        SettingsSurface = 2,
        SettingsPage = 4
    }

    internal readonly struct SpineCompatibilityDecision
    {
        internal SpineCompatibilityDecision(
            SpineProviderKind provider,
            bool isCompatible,
            SpineCapability missingCapabilities,
            string detail)
            : this(provider, isCompatible, missingCapabilities,
                SpinePublicSurface.None, detail)
        {
        }

        internal SpineCompatibilityDecision(
            SpineProviderKind provider,
            bool isCompatible,
            SpineCapability missingCapabilities,
            SpinePublicSurface missingPublicSurface,
            string detail)
        {
            Provider = provider;
            IsCompatible = isCompatible;
            MissingCapabilities = missingCapabilities;
            MissingPublicSurface = missingPublicSurface;
            Detail = detail ?? string.Empty;
        }

        internal SpineProviderKind Provider { get; }
        internal bool IsCompatible { get; }
        internal SpineCapability MissingCapabilities { get; }
        internal SpinePublicSurface MissingPublicSurface { get; }
        internal string Detail { get; }
    }

    internal static class SpineCompatibilityPolicy
    {
        internal const SpineCapability RequiredCapabilities =
            SpineCapability.BoundedCaches |
            SpineCapability.Settings |
            SpineCapability.ContextualSettings |
            SpineCapability.ModSettingsPages |
            SpineCapability.SettingsSchema |
            SpineCapability.SettingsPreviewTransactions;

        internal const SpinePublicSurface RequiredPublicSurface =
            SpinePublicSurface.RuntimeDescriptor |
            SpinePublicSurface.SettingsSurface |
            SpinePublicSurface.SettingsPage;

        internal static SpineCompatibilityDecision Evaluate(
            bool standalonePackageActive,
            bool standaloneAssemblyBound,
            SpineApiDescriptor descriptor,
            SpineRequirement requirement,
            SpinePublicSurface publicSurface = RequiredPublicSurface)
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
            SpinePublicSurface missingSurface = RequiredPublicSurface & ~publicSurface;
            if (missing != SpineCapability.None || missingSurface != SpinePublicSurface.None)
            {
                return new SpineCompatibilityDecision(
                    SpineProviderKind.Standalone,
                    false,
                    missing,
                    missingSurface,
                    requirement.ConsumerId +
                    " requires the advertised Spine capabilities and public surface. " +
                    "Missing capabilities=" + missing + ", surface=" + missingSurface + ".");
            }

            return new SpineCompatibilityDecision(
                SpineProviderKind.Standalone,
                true,
                SpineCapability.None,
                SpinePublicSurface.None,
                "Using standalone Spine with the required capability and public-surface contract.");
        }
    }
}
