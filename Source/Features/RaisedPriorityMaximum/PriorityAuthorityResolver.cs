using System;
using Better_Work_Tab.API;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    /// <summary>
    /// Resolves which registered work-tab store owns priority data.
    ///
    /// This class deliberately knows nothing about drawing, provider ranges, or handoff migration.
    /// It observes the registry and returns a coherent, generation-stamped authority snapshot.
    /// </summary>
    internal static class PriorityAuthorityResolver
    {
        private static PriorityAuthoritySnapshot cachedAuthoritySnapshot;
        private static bool hasCachedAuthoritySnapshot;

        internal static long ExplicitAuthorityGeneration =>
            PriorityAuthorityDiagnostics.ExplicitAuthorityGeneration;

        internal static PriorityAuthoritySnapshot Resolve(bool forceRefresh = false)
        {
            PriorityAuthorityDiagnostics.RecordAuthorityRequest();

            ExternalWorkTabRegistry.RefreshDynamicAuthorityStateIfNeeded();
            Game game = Current.Game;
            int registeredStoreCount = ExternalWorkTabRegistry.RegisteredStoreCount;
            int frame = Time.frameCount;
            long registryGeneration = ExternalWorkTabRegistry.RegistryGeneration;

            if (ExternalWorkTabRegistry.RegisteredStoreEntryCount == 0)
            {
                PriorityAuthoritySnapshot emptySnapshot = new PriorityAuthoritySnapshot(
                    game,
                    frame,
                    registryGeneration,
                    ExplicitAuthorityGeneration,
                    registeredStoreCount,
                    PriorityAuthorityOwner.BetterWorkTab,
                    null,
                    null,
                    0,
                    true);
                cachedAuthoritySnapshot = emptySnapshot;
                hasCachedAuthoritySnapshot = true;
                PriorityAuthorityDiagnostics.RecordAuthorityComputation();
                return emptySnapshot;
            }

            PriorityAuthoritySnapshot cached = cachedAuthoritySnapshot;
            if (!forceRefresh &&
                hasCachedAuthoritySnapshot &&
                ReferenceEquals(cached.Game, game) &&
                cached.Frame == frame &&
                cached.IsCoherent &&
                cached.RegisteredStoreCount == registeredStoreCount &&
                cached.RegistryGeneration == registryGeneration &&
                cached.ExplicitAuthorityGeneration == ExplicitAuthorityGeneration)
            {
                PriorityAuthorityDiagnostics.RecordAuthorityCacheHit();
                return cached;
            }

            ExternalWorkTabRegistry.AuthoritativeStoreResult selection =
                ExternalWorkTabRegistry.FindAuthoritativeStore();
            if (!selection.IsCoherent)
            {
                PriorityAuthorityDiagnostics.RecordAuthorityComputation();
                // Registry callbacks were unstable for the bounded probe window. Return a
                // non-coherent BWT fallback with the current generation; never let an older
                // owner/store pair be reported as current or trigger a handoff.
                return new PriorityAuthoritySnapshot(
                    game,
                    frame,
                    selection.Generation,
                    ExplicitAuthorityGeneration,
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
            PriorityAuthoritySnapshot snapshot = new PriorityAuthoritySnapshot(
                game,
                frame,
                registryGeneration,
                ExplicitAuthorityGeneration,
                registeredStoreCount,
                owner,
                store,
                selection.StoreId,
                selection.RegistrationGeneration,
                true);
            cachedAuthoritySnapshot = snapshot;
            hasCachedAuthoritySnapshot = true;
            PriorityAuthorityDiagnostics.RecordAuthorityComputation();
            return snapshot;
        }

        internal static void Invalidate()
        {
            hasCachedAuthoritySnapshot = false;
            PriorityAuthorityDiagnostics.RecordExplicitInvalidation();
        }

        private static PriorityAuthorityOwner GetOwner(
            IExternalWorkTabStore store,
            string storeId)
        {
            if (store != null &&
                string.Equals(
                    storeId,
                    SleekWorkTabIdentity.ProviderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return PriorityAuthorityOwner.SleekWorkPriorities;
            }

            return store != null
                ? PriorityAuthorityOwner.FluffyWorkTab
                : PriorityAuthorityOwner.BetterWorkTab;
        }
    }
}
