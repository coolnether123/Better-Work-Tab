using System;
using Better_Work_Tab.Features.RaisedPriorityMaximum;

namespace Better_Work_Tab.API
{
    public readonly struct PriorityApiSnapshot
    {
        public readonly int ApiVersion;
        public readonly int MaxPriority;
        public readonly int DefaultEnabledPriority;
        public readonly string ProviderId;
        public readonly string ProviderName;
        public readonly bool BetterWorkTabOwnsPriority;
        public readonly bool ExternalProviderActive;
        public readonly bool ExtendedPrioritiesEnabled;

        public PriorityApiSnapshot(
            int apiVersion,
            int maxPriority,
            int defaultEnabledPriority,
            string providerId,
            string providerName,
            bool betterWorkTabOwnsPriority,
            bool externalProviderActive,
            bool extendedPrioritiesEnabled)
        {
            ApiVersion = apiVersion;
            MaxPriority = Math.Max(1, maxPriority);
            DefaultEnabledPriority = Clamp(defaultEnabledPriority, 1, MaxPriority);
            ProviderId = string.IsNullOrEmpty(providerId) ? PriorityConstants.VanillaProviderId : providerId;
            ProviderName = string.IsNullOrEmpty(providerName) ? ProviderId : providerName;
            BetterWorkTabOwnsPriority = betterWorkTabOwnsPriority;
            ExternalProviderActive = externalProviderActive;
            ExtendedPrioritiesEnabled = extendedPrioritiesEnabled;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }

    public static class PriorityApi
    {
        public const int ApiVersion = 1;
        public const string AssemblyName = "Better Work Tab";
        public const string TypeName = "Better_Work_Tab.API.PriorityApi";

        public static int GetMaxPriority()
        {
            return PriorityAuthorityBroker.GetEffectiveMaxPriority();
        }

        public static int GetDefaultEnabledPriority()
        {
            return PriorityAuthorityBroker.GetDefaultEnabledPriority();
        }

        public static PriorityApiSnapshot GetSnapshot()
        {
            return BuildSnapshot(PriorityAuthorityBroker.GetSnapshot());
        }

        public static PriorityApiSnapshot GetSnapshotForPriority(int requestedPriority)
        {
            return BuildSnapshot(PriorityAuthorityBroker.GetSnapshotForPriority(requestedPriority));
        }

        public static bool TryGetSnapshot(out PriorityApiSnapshot snapshot)
        {
            snapshot = GetSnapshot();
            return snapshot.MaxPriority > 0;
        }

        public static int ClampPriority(int priority)
        {
            return PriorityAuthorityBroker.ClampPriorityForRequest(priority);
        }

        public static bool RegisterProvider(
            string providerId,
            string displayName,
            int maxPriority,
            int defaultEnabledPriority = PriorityConstants.VanillaDefaultEnabled,
            int sortOrder = 100)
        {
            return PriorityProviderRegistry.RegisterProvider(
                new RegisteredPriorityProvider(
                    providerId,
                    displayName,
                    maxPriority,
                    defaultEnabledPriority,
                    sortOrder));
        }

        public static bool RegisterProviderObject(IMaxPriorityProvider provider)
        {
            return PriorityProviderRegistry.RegisterProvider(provider);
        }

        public static bool UnregisterProvider(string providerId)
        {
            return PriorityProviderRegistry.UnregisterProvider(providerId);
        }

        public static void NotifyProviderChanged(string providerId)
        {
            PriorityAuthorityBroker.InvalidateCaches();
        }

        private static PriorityApiSnapshot BuildSnapshot(PriorityProviderSnapshot snapshot)
        {
            return new PriorityApiSnapshot(
                ApiVersion,
                snapshot.MaxPriority,
                snapshot.DefaultEnabledPriority,
                snapshot.ProviderId,
                snapshot.DisplayName,
                snapshot.BetterWorkTabOwnsPriorityRange,
                snapshot.ExternalProviderOwnsPriorityRange,
                snapshot.UsesExtendedPriorityRange);
        }
    }
}
