using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Better_Work_Tab.API;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    public enum PriorityAuthorityOwner
    {
        BetterWorkTab,
        FluffyWorkTab,
        SleekWorkPriorities,
        ExternalWorkTab = FluffyWorkTab
    }

#if DEBUG
    /// <summary>
    /// Read-only counters for the priority-authority decision seam. These counters are intentionally
    /// aggregate only: they make the authority path measurable without logging from a hot call.
    /// </summary>
    public struct PriorityAuthorityDiagnosticsSnapshot
    {
        internal PriorityAuthorityDiagnosticsSnapshot(
            long authorityRequests,
            long authorityCacheHits,
            long authorityComputations,
            long authorityTransitions,
            long handoffs,
            long explicitInvalidations,
            long registryListBuilds,
            long storeProbes,
            long registryRefreshPasses,
            bool registryRefreshDeferred,
            long registryGeneration,
            long explicitAuthorityGeneration)
        {
            AuthorityRequests = authorityRequests;
            AuthorityCacheHits = authorityCacheHits;
            AuthorityComputations = authorityComputations;
            AuthorityTransitions = authorityTransitions;
            Handoffs = handoffs;
            ExplicitInvalidations = explicitInvalidations;
            RegistryListBuilds = registryListBuilds;
            StoreObservationCount = storeProbes;
            RegistryRefreshPasses = registryRefreshPasses;
            RegistryRefreshDeferred = registryRefreshDeferred;
            RegistryGeneration = registryGeneration;
            ExplicitAuthorityGeneration = explicitAuthorityGeneration;
        }

        public long AuthorityRequests { get; }
        public long AuthorityCacheHits { get; }
        public long AuthorityComputations { get; }
        public long AuthorityTransitions { get; }
        public long Handoffs { get; }
        public long ExplicitInvalidations { get; }
        public long RegistryListBuilds { get; }
        public long StoreObservationCount { get; }
        public long RegistryRefreshPasses { get; }
        public bool RegistryRefreshDeferred { get; }
        public long RegistryGeneration { get; }
        public long ExplicitAuthorityGeneration { get; }
    }
#endif

    public static class PriorityAuthorityBroker
    {
        private static int cachedFrame = -1;
        private static Game cachedGame;
        private static int cachedHighestLivePriority;
        private static AuthoritySnapshot cachedAuthoritySnapshot;
        private static bool hasCachedAuthoritySnapshot;
        private static long explicitAuthorityGeneration;
#if DEBUG
        private static long authorityRequests;
        private static long authorityCacheHits;
        private static long authorityComputations;
        private static long authorityTransitions;
        private static long handoffs;
        private static long explicitInvalidations;
#endif
        private static bool authorityInitialized;
        private static AuthoritySnapshot lastAuthoritySnapshot;
        private static AuthoritySnapshot pendingPreviousAuthority;
        private static AuthoritySnapshot pendingNextAuthority;
        private static bool hasPendingAuthorityTransition;
        private static bool handoffInProgress;
        private static PriorityAuthorityOwner? handoffAuthorityOverride;
        private static readonly object authorityRefreshSyncRoot = new object();
        private static bool authorityRefreshInProgress;
        private static bool authorityRefreshPending;
        private static bool authorityRefreshDeferred;
        private const int MaxSynchronousAuthorityRefreshPasses = 8;
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

        public static PriorityAuthorityOwner CurrentAuthority
        {
            get
            {
                if (handoffInProgress && handoffAuthorityOverride.HasValue)
                {
                    return handoffAuthorityOverride.Value;
                }

                if (!handoffInProgress && ExternalWorkTabRegistry.RegisteredStoreEntryCount == 0)
                {
                    return PriorityAuthorityOwner.BetterWorkTab;
                }

                DrainDeferredAuthorityRefresh();
                AuthoritySnapshot snapshot = GetAuthoritySnapshot();
                EnsureTransitionApplied(snapshot);
                if (hasPendingAuthorityTransition && !CanRunHandoffNow())
                {
                    // A last authority is allowed to remain visible only while an explicit handoff
                    // is pending. It is never treated as the current store selection by the store
                    // getter below.
                    return lastAuthoritySnapshot.Owner;
                }

                return snapshot.IsCoherent &&
                       snapshot.RegistryGeneration == ExternalWorkTabRegistry.RegistryGeneration
                    ? snapshot.Owner
                    : PriorityAuthorityOwner.BetterWorkTab;
            }
        }

#if DEBUG
        public static PriorityAuthorityDiagnosticsSnapshot Diagnostics =>
            new PriorityAuthorityDiagnosticsSnapshot(
                authorityRequests,
                authorityCacheHits,
                authorityComputations,
                authorityTransitions,
                handoffs,
                explicitInvalidations,
                ExternalWorkTabRegistry.RegistryListBuilds,
                ExternalWorkTabRegistry.StoreObservationCount,
                ExternalWorkTabRegistry.AuthorityRefreshPasses,
                ExternalWorkTabRegistry.AuthorityRefreshDeferred,
                ExternalWorkTabRegistry.RegistryGeneration,
                explicitAuthorityGeneration);
#endif

        public static bool BetterWorkTabHasPriorityAuthority => CurrentAuthority == PriorityAuthorityOwner.BetterWorkTab;

        public static bool FluffyWorkTabHasPriorityAuthority => CurrentAuthority == PriorityAuthorityOwner.FluffyWorkTab;

        public static bool ExternalWorkTabHasPriorityAuthority => CurrentAuthority != PriorityAuthorityOwner.BetterWorkTab;

        /// <summary>
        /// True while Better Work Tab draws the Work tab, i.e. any time another Work tab mod has not
        /// taken the window over. Independent of who stores the priority numbers.
        /// </summary>
        public static bool BetterWorkTabRendersWorkTab => !FluffyWorkTabGateway.ExternalWorkTabOwnsWorkTab;

        /// <summary>
        /// Gates presentation: headers, cells, priority colors, tooltips, float menus and the usable
        /// priority range. These belong to whoever draws the tab, not to whoever owns the data, so a
        /// Fluffy-backed priority store must not switch them off.
        /// </summary>
        internal static bool ShouldRunBetterWorkTabPriorityFeatures =>
            BwtRaisedPriorityFeatureInstaller.IsFeatureActive && BetterWorkTabRendersWorkTab;

        /// <summary>
        /// Gates behavior: work-giver overrides and work execution order. These must yield when Fluffy
        /// owns the priority data, because Fluffy then drives work-giver order through its own patches.
        /// </summary>
        internal static bool ShouldRunBetterWorkTabOrdering =>
            BetterWorkTabHasPriorityAuthority &&
            (WorkGiverReassignmentManager.HasActiveData ||
             TimePriorityService.IsRuntimeActive ||
             WorkExecutionOrder.HasCustomExecutionOrder);

        internal static void NotifyPotentialAuthorityChanged(bool refreshRegistry = true)
        {
            if (refreshRegistry)
            {
                ExternalWorkTabRegistry.NotifyAvailabilityChanged();
            }

            InvalidateAuthorityAndRefresh();
        }

        internal static int GetEffectivePriority(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null || workType == null)
            {
                return GetDefaultEnabledPriority();
            }

            IExternalWorkTabStore authoritativeStore;
            if (TryGetAuthoritativeStore(out authoritativeStore) &&
                ExternalWorkTabRegistry.TryGetWorkTypePriority(
                    authoritativeStore,
                    pawn,
                    workType,
                    TimePriorityService.GetCurrentHour(pawn),
                    out int externalPriority))
            {
                return ClampRuntimePriority(externalPriority);
            }

            return GetBetterWorkTabEffectiveStoredPriority(pawn, workType);
        }

        internal static int GetEffectivePriorityAtHour(Pawn pawn, WorkTypeDef workType, int hour)
        {
            if (pawn?.workSettings == null || workType == null)
            {
                return GetDefaultEnabledPriority();
            }

            IExternalWorkTabStore authoritativeStore;
            if (TryGetAuthoritativeStore(out authoritativeStore) &&
                ExternalWorkTabRegistry.TryGetWorkTypePriority(
                    authoritativeStore,
                    pawn,
                    workType,
                    hour,
                    out int externalPriority))
            {
                return ClampRuntimePriority(externalPriority);
            }

            int basePriority = GetBetterWorkTabEffectiveStoredPriority(pawn, workType);
            if (!TimePriorityService.IsRuntimeActive)
            {
                return basePriority;
            }

            return TimePriorityService.GetPriorityAtHour(
                TimePriorityTarget.ForRuntimeWorkType(pawn, workType),
                basePriority,
                hour);
        }

        internal static int GetBetterWorkTabStoredPriority(Pawn_WorkSettings workSettings, WorkTypeDef workType)
        {
            if (workSettings?.priorities == null || workType == null)
            {
                return GetDefaultEnabledPriority();
            }

            return ClampStoredPriorityForRuntime(workSettings.priorities[workType]);
        }

        private static int GetBetterWorkTabEffectiveStoredPriority(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings?.priorities == null || workType == null)
            {
                return GetDefaultEnabledPriority();
            }

            int storedPriority = GetVanillaCompatibleStoredPriority(pawn, pawn.workSettings, workType);
            return ClampStoredPriorityForRuntime(storedPriority);
        }

        internal static int GetVanillaCompatibleStoredPriority(
            Pawn pawn,
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType)
        {
            if (pawn?.RaceProps == null || workSettings?.priorities == null || workType == null)
            {
                return GetDefaultEnabledPriority();
            }

            int storedPriority = workSettings.priorities[workType];
            return pawn.RaceProps.Humanlike &&
                   storedPriority > PriorityConstants.Disabled &&
                   Find.PlaySettings != null &&
                   !Find.PlaySettings.useWorkPriorities
                ? PriorityConstants.VanillaDefaultEnabled
                : storedPriority;
        }

        internal static int GetBetterWorkTabEffectiveWorkGiverPriorityAtHour(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int hour)
        {
            int parentPriority = GetEffectivePriorityAtHour(pawn, workType, hour);
            int fallback = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
            if (!TimePriorityService.IsRuntimeActive)
            {
                return fallback;
            }

            return TimePriorityService.GetPriorityAtHour(
                TimePriorityTarget.ForRuntimeWorkGiver(pawn, workType, workGiver),
                fallback,
                hour);
        }

        public static PriorityProviderSnapshot GetSnapshot()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            settings?.NormalizePrioritySettings();
            PriorityMode mode = settings?.priorityMode ?? DefaultSettings.priorityMode;
            return GetSnapshotForPriority(mode == PriorityMode.Auto ? GetRequiredPriorityFloor() : 0);
        }

        public static PriorityProviderSnapshot GetSnapshotForPriority(int requestedPriority)
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

            int highestLivePriority = mode == PriorityMode.Auto ? GetHighestLivePriority() : 0;
            PrepareSnapshotCache(
                settings,
                mode,
                GetBetterWorkTabConfiguredMaxPriority(),
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
                    snapshot = CreateBetterWorkTabSnapshot(false, GetBetterWorkTabConfiguredMaxPriority());
                    break;
                case PriorityMode.ExternalProvider:
                    snapshot = ResolveSelectedExternalProvider(settings, requestedPriority) ??
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
            return Math.Max(
                PriorityConstants.VanillaMax,
                ClampMaxPriority(BetterWorkTabMod.Settings?.maxPriorityInt ?? DefaultSettings.maxPriority));
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
            // Snapshot resolution also observes the highest value already present in the world so
            // its metadata can describe preserved external values. That observation cannot lower
            // the result of clamping this request: the request itself is part of the snapshot's
            // required floor. The runtime policy is therefore the equivalent value-only seam and
            // avoids a world scan on every SetPriority call. Settings are normalized at the settings
            // load/write seams; the runtime getters still clamp malformed numeric values safely.
            return Clamp(
                priority,
                PriorityConstants.Disabled,
                GetRuntimePriorityMaximum(priority));
        }

        internal static int ClampStoredPriorityForRuntime(int priority)
        {
            return Clamp(priority, PriorityConstants.Disabled, GetRuntimePriorityMaximum(priority));
        }

        private static int GetRuntimePriorityMaximum(int requestedPriority)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            PriorityMode mode = settings?.priorityMode ?? DefaultSettings.priorityMode;
            switch (mode)
            {
                case PriorityMode.Vanilla:
                    return PriorityConstants.VanillaMax;
                case PriorityMode.BetterWorkTab:
                    return GetBetterWorkTabConfiguredMaxPriority();
                case PriorityMode.ExternalProvider:
                    PriorityProviderRegistry.EnsureInitialized();
                    // ResolveSelectedExternalProvider falls back to the vanilla
                    // provider for a missing, built-in, unavailable, or replaced
                    // selection; otherwise it returns an available provider with
                    // max M. Since its final snapshot is capped at the requested
                    // priority's vanilla floor, clamping directly to M is
                    // equivalent and keeps this setter path world-size O(1).
                    if (settings == null ||
                        IsProviderId(settings.selectedPriorityProviderId, PriorityConstants.AutoProviderId) ||
                        IsProviderId(settings.selectedPriorityProviderId, PriorityConstants.VanillaProviderId) ||
                        IsProviderId(settings.selectedPriorityProviderId, PriorityConstants.BwtProviderId))
                    {
                        return PriorityConstants.VanillaMax;
                    }

                    PriorityProviderRegistry.EnsureRuntimePolicy(
                        GetAutoConfiguredMaxPriority(),
                        settings?.selectedPriorityProviderId);
                    return PriorityProviderRegistry.RuntimeSelectedProviderMax;
                case PriorityMode.Auto:
                default:
                    if (settings == null || !settings.delegateToExternalPriorityMods)
                    {
                        return GetAutoConfiguredMaxPriority();
                    }

                    PriorityProviderRegistry.EnsureInitialized();
                    if (PriorityProviderRegistry.ExternalProviderCount == 0)
                    {
                        return GetAutoConfiguredMaxPriority();
                    }

                    PriorityProviderRegistry.EnsureRuntimePolicy(
                        GetAutoConfiguredMaxPriority(),
                        settings?.selectedPriorityProviderId);
                    return PriorityProviderRegistry.GetRuntimeAutoMax(requestedPriority);
            }
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

        internal static void InvalidateCaches(bool refreshProviderRegistry = true)
        {
            cachedFrame = -1;
            cachedGame = null;
            hasCachedAuthoritySnapshot = false;
            if (refreshProviderRegistry)
            {
                PriorityProviderRegistry.NotifyAvailabilityChanged();
            }

            PriorityProviderRegistry.InvalidateRuntimePolicy();
            InvalidateAuthorityAndRefresh();
        }

        internal static IExternalWorkTabStore GetAuthoritativeStore()
        {
            IExternalWorkTabStore store;
            return TryGetAuthoritativeStore(out store) ? store : null;
        }

        private static bool TryGetAuthoritativeStore(out IExternalWorkTabStore store)
        {
            if (handoffInProgress && handoffAuthorityOverride.HasValue)
            {
                store = null;
                return false;
            }

            // The empty immutable registry snapshot is the common path. Do not initialize or audit
            // optional integrations from the vanilla/BWT getter when there are no usable stores.
            if (ExternalWorkTabRegistry.RegisteredStoreEntryCount == 0)
            {
                store = null;
                return false;
            }

            DrainDeferredAuthorityRefresh();
            AuthoritySnapshot snapshot = GetAuthoritySnapshot();
            EnsureTransitionApplied(snapshot);
            AuthoritySnapshot effective = hasPendingAuthorityTransition && !CanRunHandoffNow()
                ? lastAuthoritySnapshot
                : snapshot;
            store = effective.AuthoritativeStore;
            return effective.IsCoherent &&
                   effective.RegistryGeneration == ExternalWorkTabRegistry.RegistryGeneration &&
                   effective.Owner != PriorityAuthorityOwner.BetterWorkTab &&
                   store != null;
        }

        private static void InvalidateAuthorityAndRefresh()
        {
#if DEBUG
            explicitInvalidations++;
#endif
            explicitAuthorityGeneration++;
            bool shouldRefresh = false;
            lock (authorityRefreshSyncRoot)
            {
                if (authorityRefreshInProgress)
                {
                    authorityRefreshPending = true;
                    return;
                }

                authorityRefreshInProgress = true;
                authorityRefreshDeferred = false;
                shouldRefresh = true;
            }

            if (!shouldRefresh)
            {
                return;
            }

            bool completed = false;
            try
            {
                for (int pass = 0; pass < MaxSynchronousAuthorityRefreshPasses; pass++)
                {
                    lock (authorityRefreshSyncRoot)
                    {
                        authorityRefreshPending = false;
                    }

                    AuthoritySnapshot snapshot = GetAuthoritySnapshot(forceRefresh: true);
                    EnsureTransitionApplied(snapshot);
                    lock (authorityRefreshSyncRoot)
                    {
                        if (!authorityRefreshPending)
                        {
                            completed = true;
                            return;
                        }
                    }
                }
            }
            finally
            {
                lock (authorityRefreshSyncRoot)
                {
                    authorityRefreshInProgress = false;
                    if (!completed && authorityRefreshPending)
                    {
                        authorityRefreshPending = false;
                        authorityRefreshDeferred = true;
                    }
                }
            }
        }

        private static void DrainDeferredAuthorityRefresh()
        {
            bool shouldRefresh = false;
            lock (authorityRefreshSyncRoot)
            {
                if (authorityRefreshDeferred && !authorityRefreshInProgress)
                {
                    authorityRefreshDeferred = false;
                    authorityRefreshInProgress = true;
                    shouldRefresh = true;
                }
            }

            if (shouldRefresh)
            {
                bool completed = false;
                try
                {
                    AuthoritySnapshot snapshot = GetAuthoritySnapshot(forceRefresh: true);
                    EnsureTransitionApplied(snapshot);
                    completed = true;
                }
                finally
                {
                    lock (authorityRefreshSyncRoot)
                    {
                        authorityRefreshInProgress = false;
                        if (!completed && authorityRefreshPending)
                        {
                            authorityRefreshPending = false;
                            authorityRefreshDeferred = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Decides who <em>stores</em> work priorities. This is a data question only.
        /// </summary>
        /// <remarks>
        /// Registered external work-tab stores can claim data authority through
        /// <see cref="IExternalWorkTabStore.PriorityAuthority"/>. The handoff in
        /// <see cref="RunHandoff"/> keeps the stores in step when that claim changes.
        /// <para>
        /// It does <em>not</em> follow that the external store should draw the tab. Use
        /// <see cref="BetterWorkTabRendersWorkTab"/> for that. Conflating the two is what silently
        /// disables Better Work Tab's headers, cells and priority colors while an external store owns
        /// data.
        /// </para>
        /// </remarks>
        private static AuthoritySnapshot GetAuthoritySnapshot(bool forceRefresh = false)
        {
            RecordAuthorityRequest();
            Game game = Current.Game;
            int registeredStoreCount = ExternalWorkTabRegistry.RegisteredStoreCount;
            int frame = -1;
            long registryGeneration = ExternalWorkTabRegistry.RegistryGeneration;

            if (ExternalWorkTabRegistry.RegisteredStoreEntryCount == 0)
            {
                AuthoritySnapshot emptySnapshot = new AuthoritySnapshot(
                    game,
                    frame,
                    registryGeneration,
                    explicitAuthorityGeneration,
                    registeredStoreCount,
                    PriorityAuthorityOwner.BetterWorkTab,
                    null,
                    null,
                    0,
                    true);
                cachedAuthoritySnapshot = emptySnapshot;
                hasCachedAuthoritySnapshot = true;
                RecordAuthorityComputation();
                return emptySnapshot;
            }

            AuthoritySnapshot cached = cachedAuthoritySnapshot;
            if (!forceRefresh &&
                hasCachedAuthoritySnapshot &&
                ReferenceEquals(cached.Game, game) &&
                cached.IsCoherent &&
                cached.RegisteredStoreCount == registeredStoreCount &&
                 cached.RegistryGeneration == registryGeneration &&
                cached.ExplicitAuthorityGeneration == explicitAuthorityGeneration)
            {
                RecordAuthorityCacheHit();
                return cached;
            }

            if (hasCachedAuthoritySnapshot && !ReferenceEquals(cached.Game, game))
            {
                authorityInitialized = false;
                hasPendingAuthorityTransition = false;
                pendingPreviousAuthority = default(AuthoritySnapshot);
                pendingNextAuthority = default(AuthoritySnapshot);
            }

            ExternalWorkTabRegistry.AuthoritativeStoreResult selection =
                ExternalWorkTabRegistry.FindAuthoritativeStore();
            if (!selection.IsCoherent)
            {
                RecordAuthorityComputation();
                // Registry callbacks were unstable for the bounded probe window. Return a
                // non-coherent BWT fallback with the current generation; never let an older
                // owner/store pair be reported as current or trigger a handoff.

                return new AuthoritySnapshot(
                    game,
                    frame,
                    selection.Generation,
                    explicitAuthorityGeneration,
                    registeredStoreCount,
                    PriorityAuthorityOwner.BetterWorkTab,
                    null,
                    null,
                    0,
                    false);
            }

            registryGeneration = selection.Generation;
            IExternalWorkTabStore store = selection.Store;
            PriorityAuthorityOwner owner = GetOwner(store, selection.StoreId);
            AuthoritySnapshot snapshot = new AuthoritySnapshot(
                game,
                frame,
                registryGeneration,
                explicitAuthorityGeneration,
                registeredStoreCount,
                owner,
                store,
                selection.StoreId,
                selection.RegistrationGeneration,
                true);
            cachedAuthoritySnapshot = snapshot;
            hasCachedAuthoritySnapshot = true;
            RecordAuthorityComputation();
            return snapshot;
        }

        private static PriorityAuthorityOwner GetOwner(IExternalWorkTabStore store, string storeId)
        {
            if (store != null &&
                string.Equals(storeId, SleekWorkTabIdentity.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return PriorityAuthorityOwner.SleekWorkPriorities;
            }

            return store != null
                ? PriorityAuthorityOwner.FluffyWorkTab
                : PriorityAuthorityOwner.BetterWorkTab;
        }

        private static void EnsureTransitionApplied(AuthoritySnapshot authority)
        {
            if (!authority.IsCoherent)
            {
                return;
            }

            if (!authorityInitialized)
            {
                authorityInitialized = true;
                lastAuthoritySnapshot = authority;
                return;
            }

            if (handoffInProgress)
            {
                return;
            }

            if (SameAuthority(lastAuthoritySnapshot, authority))
            {
                hasPendingAuthorityTransition = false;
                return;
            }

#if DEBUG
            authorityTransitions++;
#endif
            if (!CanRunHandoffNow())
            {
                if (!hasPendingAuthorityTransition)
                {
                    pendingPreviousAuthority = lastAuthoritySnapshot;
                    hasPendingAuthorityTransition = true;
                }

                pendingNextAuthority = authority;
                return;
            }

            AuthoritySnapshot previous = hasPendingAuthorityTransition
                ? pendingPreviousAuthority
                : lastAuthoritySnapshot;
            AuthoritySnapshot next = hasPendingAuthorityTransition
                ? pendingNextAuthority
                : authority;
            hasPendingAuthorityTransition = false;
            pendingNextAuthority = default(AuthoritySnapshot);
            lastAuthoritySnapshot = next;
            RunHandoff(previous, next);
        }

        private static bool CanRunHandoffNow()
        {
            return Current.Game != null && Current.ProgramState == ProgramState.Playing;
        }

        private static void RunHandoff(AuthoritySnapshot previous, AuthoritySnapshot next)
        {
#if DEBUG
            handoffs++;
#endif
            handoffInProgress = true;
            handoffAuthorityOverride = PriorityAuthorityOwner.BetterWorkTab;
            try
            {
                int changed = 0;
                if (next.Owner == PriorityAuthorityOwner.BetterWorkTab ||
                    previous.Owner != PriorityAuthorityOwner.BetterWorkTab)
                {
                    changed += ImportFromPreviousAuthority(previous, next);
                }

                if (next.Owner != PriorityAuthorityOwner.BetterWorkTab)
                {
                    changed += PublishToNextAuthority(next);
                }

                WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                BetterWorkTabMod.DebugLog(
                    "Priority authority changed " + previous.Owner + "/" +
                    (previous.StoreId ?? "<bwt>") + " -> " + next.Owner + "/" +
                    (next.StoreId ?? "<bwt>") + "; synced entries=" + changed + ".",
                    DebugFeature.ModSupport);
            }
            finally
            {
                handoffAuthorityOverride = null;
                handoffInProgress = false;
            }
        }

        /// <summary>
        /// Rebuilds the external work-tab mod's priority store from Better Work Tab's stores.
        /// </summary>
        private static int ImportFromPreviousAuthority(
            AuthoritySnapshot previous,
            AuthoritySnapshot next)
        {
            bool sameIdReplacement = !string.IsNullOrEmpty(next.StoreId) &&
                string.Equals(previous.StoreId, next.StoreId, StringComparison.OrdinalIgnoreCase) &&
                !ReferenceEquals(previous.AuthoritativeStore, next.AuthoritativeStore);
            AuthoritySnapshot source = sameIdReplacement || string.IsNullOrEmpty(previous.StoreId)
                ? next
                : previous;
            return string.IsNullOrEmpty(source.StoreId)
                ? 0
                : ExternalWorkTabRegistry.ImportFromStore(
                     source.StoreId,
                     source.AuthoritativeStore,
                    source.StoreRegistrationGeneration);
        }

        private static int PublishToNextAuthority(AuthoritySnapshot next)
        {
            return ExternalWorkTabRegistry.PushAllPawnsToStore(
                next.StoreId,
                next.AuthoritativeStore,
                next.StoreRegistrationGeneration);
        }

        private static bool SameAuthority(AuthoritySnapshot left, AuthoritySnapshot right)
        {
            return left.Owner == right.Owner &&
                   string.Equals(left.StoreId, right.StoreId, StringComparison.OrdinalIgnoreCase) &&
                   left.StoreRegistrationGeneration == right.StoreRegistrationGeneration &&
                   (left.AuthoritativeStore == null ||
                    ReferenceEquals(left.AuthoritativeStore, right.AuthoritativeStore));
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
                ? LimitSnapshot(snapshot, Math.Max(requestedPriority, PriorityConstants.VanillaMax))
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
            Game game = Current.Game;
            int frame = Time.frameCount;
            if (cachedFrame == frame && ReferenceEquals(cachedGame, game))
            {
                return;
            }

            cachedGame = game;
            cachedFrame = frame;
            cachedHighestLivePriority = ScanHighestLivePriority(game);
        }

        private static int ScanHighestLivePriority(Game game)
        {
            int maxPriority = 0;
            if (game == null)
            {
                return maxPriority;
            }

            try
            {
                foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                {
                    if (pawn?.workSettings?.priorities == null)
                    {
                        continue;
                    }

                    foreach (KeyValuePair<WorkTypeDef, int> entry in pawn.workSettings.priorities)
                    {
                        if (entry.Key != null && entry.Value > maxPriority)
                        {
                            maxPriority = entry.Value;
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

        private static int ClampRuntimePriority(int priority)
        {
            return Clamp(priority, PriorityConstants.Disabled, PriorityConstants.ExtendedHardMax);
        }

        [Conditional("DEBUG")]
        private static void RecordAuthorityRequest()
        {
#if DEBUG
            authorityRequests++;
#endif
        }

        [Conditional("DEBUG")]
        private static void RecordAuthorityCacheHit()
        {
#if DEBUG
            authorityCacheHits++;
#endif
        }

        [Conditional("DEBUG")]
        private static void RecordAuthorityComputation()
        {
#if DEBUG
            authorityComputations++;
#endif
        }

        private struct AuthoritySnapshot
        {
            internal AuthoritySnapshot(
                Game game,
                int frame,
                long registryGeneration,
                long explicitAuthorityGeneration,
                int registeredStoreCount,
                PriorityAuthorityOwner owner,
                IExternalWorkTabStore authoritativeStore,
                string storeId,
                long storeRegistrationGeneration,
                bool isCoherent)
            {
                Game = game;
                Frame = frame;
                RegistryGeneration = registryGeneration;
                ExplicitAuthorityGeneration = explicitAuthorityGeneration;
                RegisteredStoreCount = registeredStoreCount;
                Owner = owner;
                AuthoritativeStore = authoritativeStore;
                StoreId = storeId;
                StoreRegistrationGeneration = storeRegistrationGeneration;
                IsCoherent = isCoherent;
            }

            internal Game Game { get; }
            internal int Frame { get; }
            internal long RegistryGeneration { get; }
            internal long ExplicitAuthorityGeneration { get; }
            internal int RegisteredStoreCount { get; }
            internal PriorityAuthorityOwner Owner { get; }
            internal IExternalWorkTabStore AuthoritativeStore { get; }
            internal string StoreId { get; }
            internal long StoreRegistrationGeneration { get; }
            internal bool IsCoherent { get; }
        }
    }
}
