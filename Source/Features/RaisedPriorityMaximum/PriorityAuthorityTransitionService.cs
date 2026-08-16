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

        internal static PriorityAuthorityOwner CurrentAuthority
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

        internal static bool IsBetterWorkTabAuthority =>
            CurrentAuthority == PriorityAuthorityOwner.BetterWorkTab;

        internal static bool IsFluffyWorkTabAuthority =>
            CurrentAuthority == PriorityAuthorityOwner.FluffyWorkTab;

        internal static bool HasExternalAuthority =>
            CurrentAuthority != PriorityAuthorityOwner.BetterWorkTab;

        internal static void NotifyPotentialAuthorityChanged(bool refreshRegistry = true)
        {
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
            PriorityAuthoritySnapshot next = hasPendingAuthorityTransition
                ? pendingNextAuthority
                : authority;
            hasPendingAuthorityTransition = false;
            pendingNextAuthority = default(PriorityAuthoritySnapshot);
            lastAuthoritySnapshot = next;
            RunHandoff(previous, next);
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

        private static bool SameAuthority(
            PriorityAuthoritySnapshot left,
            PriorityAuthoritySnapshot right)
        {
            return left.Owner == right.Owner &&
                   string.Equals(left.StoreId, right.StoreId, StringComparison.OrdinalIgnoreCase) &&
                   left.StoreRegistrationGeneration == right.StoreRegistrationGeneration &&
                   (left.AuthoritativeStore == null ||
                    ReferenceEquals(left.AuthoritativeStore, right.AuthoritativeStore));
        }
    }
}
