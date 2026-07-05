using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    public static class PriorityAuthorityBroker
    {
        private static int cachedFrame = -1;
#if !(v0_13 || vAlpha4)
        private static Game cachedGame;
#endif
        private static int cachedHighestLivePriority;

        public static PriorityProviderSnapshot GetSnapshot()
        {
            return GetSnapshotForPriority(GetRequiredPriorityFloor());
        }

        public static PriorityProviderSnapshot GetSnapshotForPriority(int requestedPriority)
        {
            PriorityProviderRegistry.EnsureInitialized();
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            settings?.NormalizePrioritySettings();

            PriorityMode mode = settings?.priorityMode ?? DefaultSettings.priorityMode;
            switch (mode)
            {
                case PriorityMode.Vanilla:
                    return CreateVanillaSnapshot(false);
                case PriorityMode.BetterWorkTab:
                    return CreateBetterWorkTabSnapshot(false, GetBetterWorkTabConfiguredMaxPriority());
                case PriorityMode.ExternalProvider:
                    return ResolveSelectedExternalProvider(settings, requestedPriority) ??
                           CreateVanillaSnapshot(true);
                case PriorityMode.Auto:
                default:
                    return ResolveAutomaticProvider(settings, requestedPriority);
            }
        }

        public static int GetEffectiveMaxPriority()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            settings?.NormalizePrioritySettings();
            if ((settings?.priorityMode ?? DefaultSettings.priorityMode) == PriorityMode.Auto)
            {
                return GetAutoConfiguredMaxPriority();
            }

            return GetSnapshot().MaxPriority;
        }

        public static int GetRequestableMaxPriority()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            settings?.NormalizePrioritySettings();
            switch (settings?.priorityMode ?? DefaultSettings.priorityMode)
            {
                case PriorityMode.BetterWorkTab:
                    return GetBetterWorkTabConfiguredMaxPriority();
                case PriorityMode.Auto:
                    return GetAutoConfiguredMaxPriority();
                default:
                    return GetSnapshot().MaxPriority;
            }
        }

        public static int GetDefaultEnabledPriority()
        {
            return GetSnapshot().DefaultEnabledPriority;
        }

        internal static int GetBetterWorkTabConfiguredMaxPriority()
        {
            return ClampMaxPriority(BetterWorkTabMod.Settings?.maxPriorityInt ?? DefaultSettings.maxPriority);
        }

        internal static int GetAutoConfiguredMaxPriority()
        {
            return GetBetterWorkTabConfiguredMaxPriority();
        }

        internal static int ClampMaxPriority(int value)
        {
            return Clamp(value, 1, PriorityConstants.ExtendedHardMax);
        }

        internal static int ClampDefaultEnabledPriority(int value, int maxPriority)
        {
            return Clamp(value, 1, ClampMaxPriority(maxPriority));
        }

        public static int ClampPriority(int priority)
        {
            return ClampPriorityForRequest(priority);
        }

        public static int ClampPriorityForRequest(int priority)
        {
            return Clamp(
                priority,
                PriorityConstants.Disabled,
                GetSnapshotForPriority(priority).MaxPriority);
        }

        public static int GetDefaultManualPriorityForDisabledWork()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            int autoMaxPriority = GetAutoConfiguredMaxPriority();
            if (settings != null &&
                settings.autoDisabledPriorityMode == BetterWorkTabSettings.AutoDisabledPriorityMode.FixedPriority)
            {
                return Clamp(settings.autoDisabledPriorityFixedValue, 1, autoMaxPriority);
            }

            int priorityFloor = Math.Max(PriorityConstants.VanillaMax, GetHighestLivePriority());
            int priority = RoundUpToPriorityBand(priorityFloor);
            return Clamp(priority, PriorityConstants.VanillaMax, autoMaxPriority);
        }

        public static int GetPriorityAfterClick(int currentPriority, int delta)
        {
            PriorityProviderSnapshot currentSnapshot = GetSnapshotForPriority(currentPriority);
            int currentMax = currentSnapshot.MaxPriority;
            int normalized = Clamp(currentPriority, PriorityConstants.Disabled, currentMax);

            if (normalized == PriorityConstants.Disabled)
            {
                if (delta < 0)
                {
                    return AutoProviderSelectionEnabled()
                        ? GetDefaultManualPriorityForDisabledWork()
                        : currentMax;
                }

                return 1;
            }

            int requested = normalized + delta;
            if (requested <= PriorityConstants.Disabled)
            {
                return PriorityConstants.Disabled;
            }

            PriorityProviderSnapshot requestedSnapshot = GetSnapshotForPriority(requested);
            return requested <= requestedSnapshot.MaxPriority
                ? requested
                : PriorityConstants.Disabled;
        }

        public static int GetNextManualPriority(int currentPriority, int direction)
        {
            PriorityProviderSnapshot snapshot = GetSnapshotForPriority(currentPriority);
            int maxPriority = snapshot.MaxPriority;
            int normalized = Clamp(currentPriority, PriorityConstants.Disabled, maxPriority);

            if (direction > 0)
            {
                if (normalized == PriorityConstants.Disabled)
                {
                    return AutoProviderSelectionEnabled()
                        ? GetDefaultManualPriorityForDisabledWork()
                        : maxPriority;
                }

                if (normalized <= 1)
                {
                    return 1;
                }

                return normalized - 1;
            }

            if (direction < 0)
            {
                if (normalized == maxPriority)
                {
                    PriorityProviderSnapshot expanded = GetSnapshotForPriority(maxPriority + 1);
                    return expanded.MaxPriority > maxPriority
                        ? maxPriority + 1
                        : PriorityConstants.Disabled;
                }

                return normalized > PriorityConstants.Disabled ? normalized + 1 : normalized;
            }

            return normalized;
        }

        internal static bool AutoProviderSelectionEnabled()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return settings != null &&
                   settings.priorityMode == PriorityMode.Auto &&
                   settings.delegateToExternalPriorityMods;
        }

        internal static void InvalidateCaches()
        {
            cachedFrame = -1;
#if !(v0_13 || vAlpha4)
            cachedGame = null;
#endif
        }

        private static PriorityProviderSnapshot ResolveAutomaticProvider(
            BetterWorkTabSettings settings,
            int requestedPriority)
        {
            int autoMaxPriority = GetAutoConfiguredMaxPriority();
            int highestLivePriority = GetHighestLivePriority();
            int requiredPriority = ClampMaxPriority(Math.Max(
                PriorityConstants.VanillaMax,
                Math.Max(requestedPriority, highestLivePriority)));

            if (settings != null && settings.delegateToExternalPriorityMods)
            {
                PriorityProviderSnapshot external = FindBestExternalProvider(requiredPriority, autoMaxPriority);
                if (external != null)
                {
                    return external;
                }

                if (highestLivePriority > PriorityConstants.VanillaMax &&
                    highestLivePriority >= requestedPriority)
                {
                    return CreateObservedExternalSnapshot(Math.Min(highestLivePriority, autoMaxPriority));
                }
            }

            if (requiredPriority <= PriorityConstants.VanillaMax)
            {
                return CreateVanillaSnapshot(false);
            }

            return CreateBetterWorkTabSnapshot(true, autoMaxPriority);
        }

        private static PriorityProviderSnapshot ResolveSelectedExternalProvider(
            BetterWorkTabSettings settings,
            int requestedPriority)
        {
            string providerId = settings?.selectedPriorityProviderId;
            if (string.IsNullOrEmpty(providerId) ||
                IsProviderId(providerId, PriorityConstants.AutoProviderId) ||
                IsProviderId(providerId, PriorityConstants.VanillaProviderId) ||
                IsProviderId(providerId, PriorityConstants.BwtProviderId))
            {
                return null;
            }

            IMaxPriorityProvider provider;
            if (!PriorityProviderRegistry.TryFindByProviderId(providerId, out provider))
            {
                return null;
            }

            PriorityProviderSnapshot snapshot;
            return TryCreateProviderSnapshot(provider, false, out snapshot)
                ? LimitSnapshot(snapshot, Math.Max(requestedPriority, PriorityConstants.VanillaMax))
                : null;
        }

        private static PriorityProviderSnapshot FindBestExternalProvider(
            int requiredPriority,
            int autoMaxPriority)
        {
            List<PriorityProviderSnapshot> candidates = new List<PriorityProviderSnapshot>();
            foreach (IMaxPriorityProvider provider in PriorityProviderRegistry.GetAvailableProviders())
            {
                if (IsBuiltInProvider(provider))
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
                    candidates.Add(snapshot);
                }
            }

            return candidates
                .OrderBy(snapshot => snapshot.MaxPriority)
                .ThenBy(snapshot => snapshot.DisplayName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static bool TryCreateProviderSnapshot(
            IMaxPriorityProvider provider,
            bool isFallback,
            out PriorityProviderSnapshot snapshot)
        {
            snapshot = null;
            if (provider == null || !ProviderIsAvailable(provider))
            {
                return false;
            }

            int maxPriority;
            if (!provider.TryGetMaxPriority(out maxPriority))
            {
                return false;
            }

            maxPriority = ClampMaxPriority(maxPriority);

            int defaultEnabledPriority;
            if (!provider.TryGetDefaultEnabledPriority(out defaultEnabledPriority))
            {
                defaultEnabledPriority = PriorityConstants.VanillaDefaultEnabled;
            }

            PriorityAuthorityKind authorityKind = GetProviderAuthorityKind(provider.ProviderId);
            PriorityProviderCapabilities capabilities = GetDefaultCapabilities(authorityKind);
            snapshot = new PriorityProviderSnapshot(
                provider.ProviderId,
                provider.DisplayName,
                maxPriority,
                ClampDefaultEnabledPriority(defaultEnabledPriority, maxPriority),
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

            int limitedMax = Math.Min(snapshot.MaxPriority, ClampMaxPriority(maxPriority));
            return new PriorityProviderSnapshot(
                snapshot.ProviderId,
                snapshot.DisplayName,
                limitedMax,
                ClampDefaultEnabledPriority(snapshot.DefaultEnabledPriority, limitedMax),
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
            maxPriority = ClampMaxPriority(Math.Max(PriorityConstants.VanillaMax, maxPriority));
            return new PriorityProviderSnapshot(
                PriorityConstants.BwtProviderId,
                "Better Work Tab",
                maxPriority,
                ClampDefaultEnabledPriority(PriorityConstants.VanillaDefaultEnabled, maxPriority),
                PriorityAuthorityKind.BetterWorkTab,
                isFallback,
                PriorityProviderCapabilities.OwnsPriorityRange |
                PriorityProviderCapabilities.OwnsPriorityDisplay |
                PriorityProviderCapabilities.ProvidesPriorityColors |
                PriorityProviderCapabilities.ProvidesClickCycle);
        }

        private static PriorityProviderSnapshot CreateObservedExternalSnapshot(int maxPriority)
        {
            maxPriority = ClampMaxPriority(Math.Max(PriorityConstants.VanillaMax, maxPriority));
            return new PriorityProviderSnapshot(
                PriorityConstants.ObservedExternalProviderId,
                "Observed external priority values",
                maxPriority,
                ClampDefaultEnabledPriority(PriorityConstants.VanillaDefaultEnabled, maxPriority),
                PriorityAuthorityKind.External,
                true,
                PriorityProviderCapabilities.PreservesUnknownExternalPriorities);
        }

        private static int GetRequiredPriorityFloor()
        {
            return Math.Max(PriorityConstants.VanillaMax, GetHighestLivePriority());
        }

        private static int GetHighestLivePriority()
        {
            EnsureCachedPriorityScan();
            return cachedHighestLivePriority;
        }

        private static void EnsureCachedPriorityScan()
        {
#if v0_13 || vAlpha4
            int frame = Time.frameCount;
            if (cachedFrame == frame)
            {
                return;
            }

            cachedFrame = frame;
            cachedHighestLivePriority = ScanHighestLivePriority();
#else
            Game game = Verse.Current.Game;
            int frame = Time.frameCount;
            if (cachedFrame == frame && ReferenceEquals(cachedGame, game))
            {
                return;
            }

            cachedGame = game;
            cachedFrame = frame;
            cachedHighestLivePriority = ScanHighestLivePriority(game);
#endif
        }

#if v0_13 || vAlpha4
        private static int ScanHighestLivePriority()
#else
        private static int ScanHighestLivePriority(Game game)
#endif
        {
            int maxPriority = 0;
#if !(v0_13 || vAlpha4)
            if (game == null)
            {
                return maxPriority;
            }
#endif

            try
            {
                foreach (Pawn pawn in PawnsFinderCompat.AllMapsWorldAndTemporaryAlive)
                {
                    if (pawn?.workSettings == null)
                    {
                        continue;
                    }

                    foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if (workType == null)
                        {
                            continue;
                        }

                        int priority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
                        if (priority > maxPriority)
                        {
                            maxPriority = priority;
                        }
                    }
                }
            }
            catch
            {
                return maxPriority;
            }

            return ClampMaxPriority(maxPriority);
        }

        private static int RoundUpToPriorityBand(int priority)
        {
            const int bandSize = PriorityConstants.VanillaMax;
            if (priority <= bandSize)
            {
                return bandSize;
            }

            int remainder = priority % bandSize;
            return remainder == 0 ? priority : priority + (bandSize - remainder);
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

        private static PriorityProviderCapabilities GetDefaultCapabilities(PriorityAuthorityKind authorityKind)
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

        private static bool ProviderIsAvailable(IMaxPriorityProvider provider)
        {
            try
            {
                return provider != null && provider.IsAvailable;
            }
            catch
            {
                return false;
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
