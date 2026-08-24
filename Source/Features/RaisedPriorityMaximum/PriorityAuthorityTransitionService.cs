using System;
using Better_Work_Tab.API;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    /// <summary>
    /// Applies authority changes and migrates priority data between stores.
    ///
    /// Authority resolution is intentionally delegated to <see cref="PriorityAuthorityResolver"/>;
    /// this service owns only transition state, bounded refresh, and handoff side effects.
    /// </summary>
    internal static class PriorityAuthorityTransitionService
    {
        private static bool authorityInitialized;
        private static PriorityAuthoritySnapshot lastAuthoritySnapshot;
        private static PriorityAuthoritySnapshot pendingPreviousAuthority;
        private static PriorityAuthoritySnapshot pendingNextAuthority;
        private static bool hasPendingAuthorityTransition;
        private static bool handoffInProgress;
        private static PriorityAuthorityOwner? handoffAuthorityOverride;
        private static readonly object authorityRefreshSyncRoot = new object();
        private static bool authorityRefreshInProgress;
        private static bool authorityRefreshPending;
        private static bool authorityRefreshDeferred;
        private const int MaxSynchronousAuthorityRefreshPasses = 8;

        // Authority resolution can observe a registry callback that asks BWT to refresh its
        // authority. Preview reads must be able to resolve the same snapshot without allowing
        // that callback to enter the transition/handoff owner. Keep this guard thread-local so
        // an observational read cannot suppress a normal authority transition on another thread.
        [ThreadStatic]
        private static int authorityObservationDepth;

        [ThreadStatic]
        private static bool hasObservationSnapshot;

        [ThreadStatic]
        private static PriorityAuthoritySnapshot observationSnapshot;

        internal static PriorityAuthorityOwner CurrentAuthority
        {
            get
            {
                if (handoffInProgress && handoffAuthorityOverride.HasValue)
                {
                    return handoffAuthorityOverride.Value;
                }

                if (authorityObservationDepth > 0)
                {
                    return GetObservationOwner();
                }

                if (!handoffInProgress && ExternalWorkTabRegistry.RegisteredStoreEntryCount == 0)
                {
                    return PriorityAuthorityOwner.BetterWorkTab;
                }

                DrainDeferredAuthorityRefresh();
                PriorityAuthoritySnapshot snapshot = PriorityAuthorityResolver.Resolve();
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

        /// <summary>
        /// Resolves a snapshot and its revision under one observational guard.
        /// This path never drains a pending transition or runs a handoff.
        /// </summary>
        internal static void CaptureObservationalAuthority(
            out PriorityAuthoritySnapshot snapshot,
            out long revision)
        {
            BeginAuthorityObservation();
            try
            {
                snapshot = ResolveObservationalSnapshot();
                revision = PriorityAuthorityResolver.CurrentAuthorityRevision;
            }
            finally
            {
                EndAuthorityObservation();
            }
        }

        /// <summary>
        /// Returns the resolver-owned cache revision without allowing a preview read to perform a
        /// transition or handoff. The resolver is intentionally asked for the revision while the
        /// observation guard is active because its registry probe can drain deferred refresh work.
        /// </summary>
        internal static long GetObservationalRevision()
        {
            CaptureObservationalAuthority(out _, out long revision);
            return revision;
        }

        internal static bool IsBetterWorkTabAuthority =>
            CurrentAuthority == PriorityAuthorityOwner.BetterWorkTab;

        internal static bool IsFluffyWorkTabAuthority =>
            CurrentAuthority == PriorityAuthorityOwner.FluffyWorkTab;

        internal static bool HasExternalAuthority =>
            CurrentAuthority != PriorityAuthorityOwner.BetterWorkTab;

        internal static void NotifyPotentialAuthorityChanged(bool refreshRegistry = true)
        {
            if (authorityObservationDepth > 0)
            {
                // A resolver callback may request a refresh while a preview is observing. The
                // observation must remain side-effect free; the normal owner will process the
                // registry change on its next non-preview authority path.
                return;
            }

            if (refreshRegistry)
            {
                ExternalWorkTabRegistry.NotifyAvailabilityChanged();
            }

            InvalidateAuthorityAndRefresh();
        }

        internal static void InvalidateCaches(bool refreshProviderRegistry = true)
        {
            PriorityRangePolicy.InvalidateCache();
            PriorityProviderSelector.InvalidateCache();
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

            if (authorityObservationDepth > 0)
            {
                store = GetObservationStore();
                return store != null;
            }

            // The empty immutable registry snapshot is the common path. Do not initialize or audit
            // optional integrations from the vanilla/BWT getter when there are no usable stores.
            if (ExternalWorkTabRegistry.RegisteredStoreEntryCount == 0)
            {
                store = null;
                return false;
            }

            DrainDeferredAuthorityRefresh();
            PriorityAuthoritySnapshot snapshot = PriorityAuthorityResolver.Resolve();
            EnsureTransitionApplied(snapshot);
            PriorityAuthoritySnapshot effective = hasPendingAuthorityTransition && !CanRunHandoffNow()
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
            if (authorityObservationDepth > 0)
            {
                // Explicit invalidation is safe to record while observing, but it must not start
                // the synchronous transition loop. The next normal authority read owns refresh.
                PriorityAuthorityResolver.Invalidate();
                return;
            }

            PriorityAuthorityResolver.Invalidate();
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

                    PriorityAuthoritySnapshot snapshot = PriorityAuthorityResolver.Resolve(forceRefresh: true);
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
                    PriorityAuthoritySnapshot snapshot = PriorityAuthorityResolver.Resolve(forceRefresh: true);
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

        private static void EnsureTransitionApplied(PriorityAuthoritySnapshot authority)
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

            if (!ReferenceEquals(lastAuthoritySnapshot.Game, authority.Game))
            {
                authorityInitialized = true;
                hasPendingAuthorityTransition = false;
                pendingPreviousAuthority = default(PriorityAuthoritySnapshot);
                pendingNextAuthority = default(PriorityAuthoritySnapshot);
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

            PriorityAuthorityDiagnostics.RecordAuthorityTransition();
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

            PriorityAuthoritySnapshot previous = hasPendingAuthorityTransition
                ? pendingPreviousAuthority
                : lastAuthoritySnapshot;
            pendingPreviousAuthority = previous;
            pendingNextAuthority = authority;
            hasPendingAuthorityTransition = true;

            PriorityAuthoritySnapshot next = pendingNextAuthority;
            RunHandoff(previous, next);

            lastAuthoritySnapshot = next;
            hasPendingAuthorityTransition = false;
            pendingPreviousAuthority = default(PriorityAuthoritySnapshot);
            pendingNextAuthority = default(PriorityAuthoritySnapshot);
        }

        private static bool CanRunHandoffNow()
        {
            return Current.Game != null && Current.ProgramState == ProgramState.Playing;
        }

        private static void RunHandoff(
            PriorityAuthoritySnapshot previous,
            PriorityAuthoritySnapshot next)
        {
            PriorityAuthorityDiagnostics.RecordHandoff();
            handoffInProgress = true;
            handoffAuthorityOverride = PriorityAuthorityOwner.BetterWorkTab;
            try
            {
                int changed = 0;
                bool explicitFluffyAuthority =
                    next.Preference == PriorityDataAuthorityPreference.FluffyWorkTab &&
                    next.Owner == PriorityAuthorityOwner.FluffyWorkTab;
                bool explicitBetterWorkTabAuthority =
                    next.Preference == PriorityDataAuthorityPreference.BetterWorkTab &&
                    next.Owner == PriorityAuthorityOwner.BetterWorkTab;
                if (explicitFluffyAuthority)
                {
                    // A selected Fluffy tracker is the source of truth, including after it
                    // becomes available again. Import it before BWT rebuilds its replica.
                    changed += ImportFromNextAuthority(next);
                }
                else if (next.Owner == PriorityAuthorityOwner.BetterWorkTab ||
                         previous.Owner != PriorityAuthorityOwner.BetterWorkTab)
                {
                    changed += ImportFromPreviousAuthority(previous, next);
                }

                if (explicitBetterWorkTabAuthority)
                {
                    // Explicit BWT authority keeps every compatible tracker synchronized.
                    changed += ExternalWorkTabRegistry.PushAllPawns();
                }
                else if (next.Owner != PriorityAuthorityOwner.BetterWorkTab)
                {
                    // Keep Automatic's historical handoff policy unchanged.
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

        private static int ImportFromPreviousAuthority(
            PriorityAuthoritySnapshot previous,
            PriorityAuthoritySnapshot next)
        {
            bool sameIdReplacement = !string.IsNullOrEmpty(next.StoreId) &&
                string.Equals(previous.StoreId, next.StoreId, StringComparison.OrdinalIgnoreCase) &&
                !ReferenceEquals(previous.AuthoritativeStore, next.AuthoritativeStore);
            PriorityAuthoritySnapshot source = sameIdReplacement || string.IsNullOrEmpty(previous.StoreId)
                ? next
                : previous;
            return string.IsNullOrEmpty(source.StoreId)
                ? 0
                : ExternalWorkTabRegistry.ImportFromStore(
                    source.StoreId,
                    source.AuthoritativeStore,
                    source.StoreRegistrationGeneration);
        }

        private static int PublishToNextAuthority(PriorityAuthoritySnapshot next)
        {
            return ExternalWorkTabRegistry.PushAllPawnsToStore(
                next.StoreId,
                next.AuthoritativeStore,
                next.StoreRegistrationGeneration);
        }

        private static int ImportFromNextAuthority(PriorityAuthoritySnapshot next)
        {
            return string.IsNullOrEmpty(next.StoreId)
                ? 0
                : ExternalWorkTabRegistry.ImportFromStore(
                    next.StoreId,
                    next.AuthoritativeStore,
                    next.StoreRegistrationGeneration);
        }

        private static bool SameAuthority(
            PriorityAuthoritySnapshot left,
            PriorityAuthoritySnapshot right)
        {
            return left.Owner == right.Owner &&
                   left.Preference == right.Preference &&
                   string.Equals(left.StoreId, right.StoreId, StringComparison.OrdinalIgnoreCase) &&
                   left.StoreRegistrationGeneration == right.StoreRegistrationGeneration &&
                   (left.AuthoritativeStore == null ||
                    ReferenceEquals(left.AuthoritativeStore, right.AuthoritativeStore));
        }

        private static PriorityAuthoritySnapshot ResolveObservationalSnapshot()
        {
            PriorityAuthoritySnapshot snapshot = PriorityAuthorityResolver.Resolve();
            observationSnapshot = snapshot;
            hasObservationSnapshot = true;
            return snapshot;
        }

        private static void BeginAuthorityObservation()
        {
            authorityObservationDepth++;
        }

        private static void EndAuthorityObservation()
        {
            if (authorityObservationDepth > 0)
            {
                authorityObservationDepth--;
            }

            if (authorityObservationDepth == 0)
            {
                hasObservationSnapshot = false;
                observationSnapshot = default(PriorityAuthoritySnapshot);
            }
        }

        private static PriorityAuthoritySnapshot GetObservationSnapshot()
        {
            return hasObservationSnapshot
                ? observationSnapshot
                : authorityInitialized
                    ? lastAuthoritySnapshot
                    : default(PriorityAuthoritySnapshot);
        }

        private static PriorityAuthorityOwner GetObservationOwner()
        {
            PriorityAuthoritySnapshot snapshot = GetObservationSnapshot();
            return snapshot.IsCoherent &&
                   snapshot.RegistryGeneration == ExternalWorkTabRegistry.RegistryGeneration
                ? snapshot.Owner
                : PriorityAuthorityOwner.BetterWorkTab;
        }

        private static IExternalWorkTabStore GetObservationStore()
        {
            PriorityAuthoritySnapshot snapshot = GetObservationSnapshot();
            if (!snapshot.IsCoherent ||
                snapshot.RegistryGeneration != ExternalWorkTabRegistry.RegistryGeneration ||
                snapshot.Owner == PriorityAuthorityOwner.BetterWorkTab ||
                snapshot.AuthoritativeStore == null ||
                snapshot.StoreRegistrationGeneration == 0)
            {
                return null;
            }

            return snapshot.AuthoritativeStore;
        }
    }
}
