using System;
using System.Reflection;
using Spine.Api;
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
            new SemanticVersion(1, 0, 0);
        // BWT compiles its original fluent-transpiler implementation into both
        // assembly variants. Standalone Spine supplies only the shared runtime
        // contracts that the external variant removes from its own build.
        private static readonly SpineCapability RequiredCapabilities =
            SpineCapability.BoundedCaches;

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

        internal static bool IsCompatible
        {
            get
            {
                EnsureInitialized();
                return decision.IsCompatible;
            }
        }

        internal static string Status
        {
            get
            {
                EnsureInitialized();
                return decision.Detail;
            }
        }

        internal static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            Assembly apiAssembly = typeof(SpineApiDescriptor).Assembly;
            bool standaloneAssemblyBound = string.Equals(
                apiAssembly.GetName().Name,
                StandaloneAssemblyName,
                StringComparison.Ordinal);
            bool standalonePackageActive = ModsConfig.IsActive(PackageId);
            SpineApiDescriptor descriptor = standaloneAssemblyBound
                ? ReadStandaloneDescriptor(apiAssembly)
                : CreateEmbeddedDescriptor();
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

        private static SpineApiDescriptor ReadStandaloneDescriptor(
            Assembly apiAssembly)
        {
            try
            {
                Type apiType = apiAssembly.GetType(
                    "Spine.Api.SpineApi",
                    throwOnError: false);
                PropertyInfo runtimeProperty = apiType?.GetProperty(
                    "Runtime",
                    BindingFlags.Public | BindingFlags.Static);
                object runtime = runtimeProperty?.GetValue(null, null);
                PropertyInfo descriptorProperty = runtime?.GetType().GetProperty(
                    "Descriptor",
                    BindingFlags.Public | BindingFlags.Instance);
                object descriptor = descriptorProperty?.GetValue(runtime, null);
                if (descriptor is SpineApiDescriptor typedDescriptor)
                {
                    return typedDescriptor;
                }
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[Better Work Tab][Spine] Failed to read the standalone runtime descriptor: " +
                    exception.Message);
            }

            return new SpineApiDescriptor(
                PackageId,
                default(SemanticVersion),
                SpineCapability.None);
        }

        private static SpineApiDescriptor CreateEmbeddedDescriptor()
        {
            return new SpineApiDescriptor(
                ConsumerId + ".EmbeddedSpine",
                MinimumVersion,
                RequiredCapabilities);
        }
    }
}
