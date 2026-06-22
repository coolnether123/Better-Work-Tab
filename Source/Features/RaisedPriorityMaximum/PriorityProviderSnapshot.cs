using System;
using Better_Work_Tab.API;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    public enum PriorityAuthorityKind
    {
        Vanilla,
        BetterWorkTab,
        External
    }

    public sealed class PriorityProviderSnapshot
    {
        public readonly string ProviderId;
        public readonly string DisplayName;
        public readonly int MaxPriority;
        public readonly int DefaultEnabledPriority;
        public readonly PriorityAuthorityKind AuthorityKind;
        public readonly bool IsFallback;
        public readonly PriorityProviderCapabilities Capabilities;

        public PriorityProviderSnapshot(
            string providerId,
            string displayName,
            int maxPriority,
            int defaultEnabledPriority,
            PriorityAuthorityKind authorityKind,
            bool isFallback,
            PriorityProviderCapabilities capabilities)
        {
            ProviderId = string.IsNullOrEmpty(providerId) ? PriorityConstants.VanillaProviderId : providerId;
            DisplayName = string.IsNullOrEmpty(displayName) ? ProviderId : displayName;
            MaxPriority = Clamp(maxPriority, 1, PriorityConstants.ExtendedHardMax);
            DefaultEnabledPriority = Clamp(defaultEnabledPriority, 1, MaxPriority);
            AuthorityKind = Enum.IsDefined(typeof(PriorityAuthorityKind), authorityKind)
                ? authorityKind
                : PriorityAuthorityKind.Vanilla;
            IsFallback = isFallback;
            Capabilities = capabilities;
        }

        public bool UsesExtendedPriorityRange
        {
            get { return MaxPriority > PriorityConstants.VanillaMax; }
        }

        public bool VanillaOwnsPriorityRange
        {
            get { return AuthorityKind == PriorityAuthorityKind.Vanilla; }
        }

        public bool BetterWorkTabOwnsPriorityRange
        {
            get { return AuthorityKind == PriorityAuthorityKind.BetterWorkTab; }
        }

        public bool ExternalProviderOwnsPriorityRange
        {
            get { return AuthorityKind == PriorityAuthorityKind.External; }
        }

        public bool ProviderOwnsPriorityRange
        {
            get { return (Capabilities & PriorityProviderCapabilities.OwnsPriorityRange) != 0; }
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
}
