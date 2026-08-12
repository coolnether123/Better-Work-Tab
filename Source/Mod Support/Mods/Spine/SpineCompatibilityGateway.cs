using System;
using Spine.Api;
using Spine.UI.SettingsFramework;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.Spine
{
    /// <summary>
    /// Owns BWT's optional standalone-Spine negotiation and diagnostics.
    /// Physical assembly selection remains the responsibility of LoadFolders.xml.
    /// </summary>
    internal static class SpineCompatibilityGateway
    {
        internal const string PackageId = "CoolNether123.Spine";
        internal const string ConsumerId = "CoolNether123.BetterWorkTab";

        private const string StandaloneAssemblyName = "Spine";
        private static readonly SemanticVersion MinimumVersion =
            new SemanticVersion(1, 1, 1);
        // BWT compiles its original fluent-transpiler implementation into both
        // assembly variants. Standalone Spine supplies only the shared runtime
        // contracts that the external variant removes from its own build.
        private static readonly SpineCapability RequiredCapabilities =
            SpineCapability.BoundedCaches |
            SpineCapability.Settings |
            SpineCapability.ContextualSettings |
            SpineCapability.ModSettingsPages |
            SpineCapability.SettingsSchema |
            SpineCapability.SettingsPreviewTransactions;

        private static bool initialized;
        private static SpineCompatibilityDecision decision;

        internal static SpineProviderKind Provider
        {
            get
            {
                EnsureInitialized();
                return decision.Provider;
            }
        }

        /// <summary>
        /// The single BWT settings integration point. LoadFolders selects the
        /// external-linked or embedded BWT assembly, and this property validates
        /// that selection before exposing the selected Spine settings host.
        /// </summary>
        internal static IModSettingsFacade Settings
        {
            get
            {
                EnsureInitialized();
                if (!decision.IsCompatible)
                {
                    throw new NotSupportedException(decision.Detail);
                }

                return SpineApi.Settings;
            }
        }

        internal static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            var apiAssembly = typeof(SpineApi).Assembly;
            bool standaloneAssemblyBound = string.Equals(
                apiAssembly.GetName().Name,
                StandaloneAssemblyName,
                StringComparison.Ordinal);
            bool standalonePackageActive = ModsConfig.IsActive(PackageId);
            SpineApiDescriptor descriptor = SpineApi.Runtime.Descriptor;
            var requirement = new SpineRequirement(
                ConsumerId,
                MinimumVersion,
                RequiredCapabilities);

            decision = SpineCompatibilityPolicy.Evaluate(
                standalonePackageActive,
                standaloneAssemblyBound,
                descriptor,
                requirement);

            string message = "[Better Work Tab][Spine] " + decision.Detail +
                " Provider=" + decision.Provider +
                ", API assembly=" + apiAssembly.GetName().Name +
                ", version=" + descriptor.Version +
                ", capabilities=" + descriptor.Capabilities + ".";
            if (decision.IsCompatible)
            {
                Log.Message(message);
            }
            else
            {
                Log.Error(message);
            }
        }

        private static void EnsureInitialized()
        {
            if (!initialized)
            {
                Initialize();
            }
        }
    }
}
