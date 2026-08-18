using System;
using Better_Work_Tab.API;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal static class PriorityProviderSelector
    {
        private static readonly PriorityProviderSnapshot[] snapshotCache =
            new PriorityProviderSnapshot[PriorityConstants.ExtendedHardMax + 1];
        private static object snapshotSettings;
        private static PriorityMode snapshotMode;
        private static int snapshotConfiguredMax;
        private static int snapshotHighestLivePriority;
        private static bool snapshotDelegatesToExternal;
        private static string snapshotSelectedProviderId;
        private static long snapshotProviderAvailabilityGeneration = -1;
        private static bool snapshotCacheInitialized;

        internal static PriorityProviderSnapshot GetSnapshot()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            settings?.NormalizePrioritySettings();
            PriorityMode mode = settings?.priorityMode ?? DefaultSettings.priorityMode;
            return GetSnapshotForPriority(
                mode == PriorityMode.Auto ? GetRequiredPriorityFloor() : 0);
        }

        internal static PriorityProviderSnapshot GetSnapshotForPriority(int requestedPriority)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            settings?.NormalizePrioritySettings();

            PriorityMode mode = settings?.priorityMode ?? DefaultSettings.priorityMode;
            bool needsExternalProviders = mode == PriorityMode.ExternalProvider ||
                (mode == PriorityMode.Auto && (settings?.delegateToExternalPriorityMods ?? false));
            if (needsExternalProviders)
            {
                PriorityProviderRegistry.EnsureInitialized();
            }

            int highestLivePriority = mode == PriorityMode.Auto
                ? PriorityRangePolicy.GetHighestLivePriority()
                : 0;
            PrepareSnapshotCache(
                settings,
                mode,
                PriorityRangePolicy.GetAutoConfiguredMaxPriority(),
                highestLivePriority,
                settings?.delegateToExternalPriorityMods ?? false,
                settings?.selectedPriorityProviderId);
            int cacheIndex = requestedPriority < 0
                ? 0
                : requestedPriority > PriorityConstants.ExtendedHardMax
                    ? PriorityConstants.ExtendedHardMax
                    : requestedPriority;
            PriorityProviderSnapshot cached = snapshotCache[cacheIndex];
            if (cached != null)
            {
                return cached;
            }

            PriorityProviderSnapshot snapshot;
            switch (mode)
            {
                case PriorityMode.Vanilla:
                    snapshot = CreateVanillaSnapshot(false);
                    break;
                case PriorityMode.BetterWorkTab:
                    snapshot = CreateBetterWorkTabSnapshot(
                        false,
                        PriorityRangePolicy.GetAutoConfiguredMaxPriority());
                    break;
                case PriorityMode.ExternalProvider:
                    snapshot = ResolveSelectedExternalProvider(settings) ??
                               CreateVanillaSnapshot(true);
                    break;
                case PriorityMode.Auto:
                default:
                    snapshot = ResolveAutomaticProvider(settings, requestedPriority);
                    break;
            }

            snapshotCache[cacheIndex] = snapshot;
            return snapshot;
        }

        internal static void InvalidateCache()
        {
            Array.Clear(snapshotCache, 0, snapshotCache.Length);
            snapshotSettings = null;
            snapshotMode = default(PriorityMode);
            snapshotConfiguredMax = 0;
            snapshotHighestLivePriority = 0;
            snapshotDelegatesToExternal = false;
            snapshotSelectedProviderId = null;
            snapshotProviderAvailabilityGeneration = -1;
            snapshotCacheInitialized = false;
        }

        internal static int GetRuntimePriorityMaximum(int requestedPriority)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            PriorityMode mode = settings?.priorityMode ?? DefaultSettings.priorityMode;
            switch (mode)
            {
                case PriorityMode.Vanilla:
                    return PriorityConstants.VanillaMax;
                case PriorityMode.BetterWorkTab:
                    return PriorityRangePolicy.GetAutoConfiguredMaxPriority();
                case PriorityMode.ExternalProvider:
                    PriorityProviderRegistry.EnsureInitialized();
                    // A missing, built-in, unavailable, or replaced selection resolves to the
                    // vanilla provider in ResolveSelectedExternalProvider. The registry runtime
                    // policy exposes the same vanilla maximum for those cases without a snapshot
                    // allocation or a world scan.
                    if (settings == null ||
                        IsProviderId(settings.selectedPriorityProviderId, PriorityConstants.AutoProviderId) ||
                        IsProviderId(settings.selectedPriorityProviderId, PriorityConstants.VanillaProviderId) ||
                        IsProviderId(settings.selectedPriorityProviderId, PriorityConstants.BwtProviderId))
                    {
                        return PriorityConstants.VanillaMax;
                    }

                    // Auto selection must keep the requested-priority lookup in the registry so
                    // its provider tie-break ordering and configured maximum cap remain identical
                    // to snapshot resolution.
                    PriorityProviderRegistry.EnsureRuntimePolicy(
                        PriorityRangePolicy.GetAutoConfiguredMaxPriority(),
                        settings.selectedPriorityProviderId);
                    return PriorityProviderRegistry.RuntimeSelectedProviderMax;
                case PriorityMode.Auto:
                default:
                    if (settings == null || !settings.delegateToExternalPriorityMods)
                    {
                        return PriorityRangePolicy.GetAutoConfiguredMaxPriority();
                    }

                    PriorityProviderRegistry.EnsureInitialized();
                    if (PriorityProviderRegistry.ExternalProviderCount == 0)
                    {
                        return PriorityRangePolicy.GetAutoConfiguredMaxPriority();
                    }

                    PriorityProviderRegistry.EnsureRuntimePolicy(
                        PriorityRangePolicy.GetAutoConfiguredMaxPriority(),
                        settings.selectedPriorityProviderId);
                    return PriorityProviderRegistry.GetRuntimeAutoMax(requestedPriority);
            }
        }

        private static void PrepareSnapshotCache(
            BetterWorkTabSettings settings,
            PriorityMode mode,
            int configuredMax,
            int highestLivePriority,
            bool delegatesToExternal,
            string selectedProviderId)
        {
            long providerAvailabilityGeneration = PriorityProviderRegistry.AvailabilityGeneration;
            if (snapshotCacheInitialized &&
                ReferenceEquals(snapshotSettings, settings) &&
                snapshotMode == mode &&
                snapshotConfiguredMax == configuredMax &&
                snapshotHighestLivePriority == highestLivePriority &&
                snapshotDelegatesToExternal == delegatesToExternal &&
                string.Equals(snapshotSelectedProviderId, selectedProviderId, StringComparison.OrdinalIgnoreCase) &&
                snapshotProviderAvailabilityGeneration == providerAvailabilityGeneration)
            {
                return;
            }

            Array.Clear(snapshotCache, 0, snapshotCache.Length);
            snapshotSettings = settings;
            snapshotMode = mode;
            snapshotConfiguredMax = configuredMax;
            snapshotHighestLivePriority = highestLivePriority;
            snapshotDelegatesToExternal = delegatesToExternal;
            snapshotSelectedProviderId = selectedProviderId;
            snapshotProviderAvailabilityGeneration = providerAvailabilityGeneration;
            snapshotCacheInitialized = true;
        }

        private static PriorityProviderSnapshot ResolveAutomaticProvider(
            BetterWorkTabSettings settings,
            int requestedPriority)
        {
            int autoMaxPriority = PriorityRangePolicy.GetAutoConfiguredMaxPriority();
            int highestLivePriority = PriorityRangePolicy.GetHighestLivePriority();
            int requiredPriority = PriorityRangePolicy.ClampMaxPriority(Math.Max(
                PriorityConstants.VanillaMax,
                Math.Max(requestedPriority, highestLivePriority)));

            if (settings != null && settings.delegateToExternalPriorityMods)
            {
                PriorityProviderSnapshot external = FindBestExternalProvider(
                    requiredPriority,
                    autoMaxPriority);
                if (external != null)
                {
                    return external;
                }

                if (highestLivePriority > PriorityConstants.VanillaMax &&
                    highestLivePriority >= requestedPriority)
                {
                    return CreateObservedExternalSnapshot(
                        Math.Min(highestLivePriority, autoMaxPriority));
                }
            }

            if (requiredPriority <= PriorityConstants.VanillaMax)
            {
                return CreateVanillaSnapshot(false);
            }

            return CreateBetterWorkTabSnapshot(true, autoMaxPriority);
        }

        private static PriorityProviderSnapshot ResolveSelectedExternalProvider(
            BetterWorkTabSettings settings)
        {
            string providerId = settings?.selectedPriorityProviderId;
            if (string.IsNullOrWhiteSpace(providerId) ||
                IsProviderId(providerId, PriorityConstants.AutoProviderId) ||
                IsProviderId(providerId, PriorityConstants.VanillaProviderId) ||
                IsProviderId(providerId, PriorityConstants.BwtProviderId))
            {
                return null;
            }

            PriorityProviderRecord provider;
            if (!PriorityProviderRegistry.TryFindAvailableProviderRecord(providerId, out provider))
            {
                return null;
            }

            PriorityProviderSnapshot snapshot;
            return TryCreateProviderSnapshot(provider, false, out snapshot)
                ? snapshot
                : null;
        }

        private static PriorityProviderSnapshot FindBestExternalProvider(
            int requiredPriority,
            int autoMaxPriority)
        {
            PriorityProviderSnapshot best = null;
            PriorityProviderRecord[] providers = PriorityProviderRegistry.GetAvailableProviderRecords();
            for (int i = 0; i < providers.Length; i++)
            {
                PriorityProviderRecord provider = providers[i];
                if (IsBuiltInProvider(provider.Provider))
                {
                    continue;
                }

                PriorityProviderSnapshot snapshot;
                if (!TryCreateProviderSnapshot(provider, false, out snapshot))
                {
                    continue;
                }

                snapshot = LimitSnapshot(snapshot, autoMaxPriority);
                if (snapshot.MaxPriority >= requiredPriority)
                {
                    if (best == null ||
                        snapshot.MaxPriority < best.MaxPriority ||
                        (snapshot.MaxPriority == best.MaxPriority &&
                         (string.Compare(snapshot.DisplayName, best.DisplayName, StringComparison.OrdinalIgnoreCase) < 0 ||
                          (string.Equals(snapshot.DisplayName, best.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                           string.Compare(snapshot.ProviderId, best.ProviderId, StringComparison.OrdinalIgnoreCase) < 0))))
                    {
                        best = snapshot;
                    }
                }
            }

            return best;
        }

        private static bool TryCreateProviderSnapshot(
            PriorityProviderRecord provider,
            bool isFallback,
            out PriorityProviderSnapshot snapshot)
        {
            snapshot = null;
            PriorityAuthorityKind authorityKind = GetProviderAuthorityKind(provider.ProviderId);
            PriorityProviderCapabilities capabilities = GetDefaultCapabilities(authorityKind);
            snapshot = new PriorityProviderSnapshot(
                provider.ProviderId,
                provider.DisplayName,
                provider.MaxPriority,
                provider.DefaultEnabledPriority,
                authorityKind,
                isFallback,
                capabilities);
            return true;
        }

        private static PriorityProviderSnapshot LimitSnapshot(
            PriorityProviderSnapshot snapshot,
            int maxPriority)
        {
            if (snapshot == null)
            {
                return null;
            }

            int limitedMax = Math.Min(
                snapshot.MaxPriority,
                PriorityRangePolicy.ClampMaxPriority(maxPriority));
            return new PriorityProviderSnapshot(
                snapshot.ProviderId,
                snapshot.DisplayName,
                limitedMax,
                PriorityRangePolicy.ClampDefaultEnabledPriority(
                    snapshot.DefaultEnabledPriority,
                    limitedMax),
                snapshot.AuthorityKind,
                snapshot.IsFallback,
                snapshot.Capabilities);
        }

        private static PriorityProviderSnapshot CreateVanillaSnapshot(bool isFallback)
        {
            return new PriorityProviderSnapshot(
                PriorityConstants.VanillaProviderId,
                "Vanilla RimWorld",
                PriorityConstants.VanillaMax,
                PriorityConstants.VanillaDefaultEnabled,
                PriorityAuthorityKind.Vanilla,
                isFallback,
                PriorityProviderCapabilities.OwnsPriorityRange |
                PriorityProviderCapabilities.OwnsVanillaPriorityRange |
                PriorityProviderCapabilities.OwnsPriorityDisplay);
        }

        private static PriorityProviderSnapshot CreateBetterWorkTabSnapshot(
            bool isFallback,
            int maxPriority)
        {
            maxPriority = PriorityRangePolicy.ClampMaxPriority(Math.Max(
                PriorityConstants.VanillaMax,
                maxPriority));
            return new PriorityProviderSnapshot(
                PriorityConstants.BwtProviderId,
                "Better Work Tab",
                maxPriority,
                PriorityRangePolicy.ClampDefaultEnabledPriority(
                    PriorityConstants.VanillaDefaultEnabled,
                    maxPriority),
                PriorityAuthorityKind.BetterWorkTab,
                isFallback,
                PriorityProviderCapabilities.OwnsPriorityRange |
                PriorityProviderCapabilities.OwnsPriorityDisplay |
                PriorityProviderCapabilities.ProvidesPriorityColors |
                PriorityProviderCapabilities.ProvidesClickCycle);
        }

        private static PriorityProviderSnapshot CreateObservedExternalSnapshot(int maxPriority)
        {
            maxPriority = PriorityRangePolicy.ClampMaxPriority(Math.Max(
                PriorityConstants.VanillaMax,
                maxPriority));
            return new PriorityProviderSnapshot(
                PriorityConstants.ObservedExternalProviderId,
                "Observed external priority values",
                maxPriority,
                PriorityRangePolicy.ClampDefaultEnabledPriority(
                    PriorityConstants.VanillaDefaultEnabled,
                    maxPriority),
                PriorityAuthorityKind.External,
                true,
                PriorityProviderCapabilities.PreservesUnknownExternalPriorities);
        }

        private static int GetRequiredPriorityFloor()
        {
            return Math.Max(
                PriorityConstants.VanillaMax,
                PriorityRangePolicy.GetHighestLivePriority());
        }

        private static PriorityAuthorityKind GetProviderAuthorityKind(string providerId)
        {
            if (IsProviderId(providerId, PriorityConstants.VanillaProviderId))
            {
                return PriorityAuthorityKind.Vanilla;
            }

            if (IsProviderId(providerId, PriorityConstants.BwtProviderId))
            {
                return PriorityAuthorityKind.BetterWorkTab;
            }

            return PriorityAuthorityKind.External;
        }

        private static PriorityProviderCapabilities GetDefaultCapabilities(
            PriorityAuthorityKind authorityKind)
        {
            switch (authorityKind)
            {
                case PriorityAuthorityKind.Vanilla:
                    return PriorityProviderCapabilities.OwnsPriorityRange |
                           PriorityProviderCapabilities.OwnsVanillaPriorityRange |
                           PriorityProviderCapabilities.OwnsPriorityDisplay;
                case PriorityAuthorityKind.BetterWorkTab:
                    return PriorityProviderCapabilities.OwnsPriorityRange |
                           PriorityProviderCapabilities.OwnsPriorityDisplay |
                           PriorityProviderCapabilities.ProvidesPriorityColors;
                default:
                    return PriorityProviderCapabilities.OwnsPriorityRange;
            }
        }

        private static bool IsBuiltInProvider(IMaxPriorityProvider provider)
        {
            return provider == null ||
                   IsProviderId(provider.ProviderId, PriorityConstants.VanillaProviderId) ||
                   IsProviderId(provider.ProviderId, PriorityConstants.BwtProviderId) ||
                   IsProviderId(provider.ProviderId, PriorityConstants.AutoProviderId);
        }

        private static bool IsProviderId(string providerId, string expectedProviderId)
        {
            return string.Equals(
                providerId?.Trim(),
                expectedProviderId,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
