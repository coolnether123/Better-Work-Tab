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
            PriorityDataAuthorityPreference preference =
                BetterWorkTabMod.Settings?.priorityDataAuthority ??
                DefaultSettings.priorityDataAuthority;

            if (ExternalWorkTabRegistry.RegisteredStoreEntryCount == 0)
            {
                PriorityAuthoritySnapshot emptySnapshot = new PriorityAuthoritySnapshot(
                    game,
                    frame,
                    registryGeneration,
                    ExplicitAuthorityGeneration,
                    registeredStoreCount,
                    preference,
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
                cached.ExplicitAuthorityGeneration == ExplicitAuthorityGeneration &&
                cached.Preference == preference)
            {
                PriorityAuthorityDiagnostics.RecordAuthorityCacheHit();
                return cached;
            }

            if (preference == PriorityDataAuthorityPreference.BetterWorkTab)
            {
                return Cache(CreateBetterWorkTabSnapshot(
                    game,
                    frame,
                    registryGeneration,
                    registeredStoreCount,
                    preference));
            }

            if (preference == PriorityDataAuthorityPreference.FluffyWorkTab)
            {
                ExternalWorkTabRegistry.AuthoritativeStoreResult fluffySelection =
                    ExternalWorkTabRegistry.FindAuthoritativeStore();
                if (!fluffySelection.IsCoherent)
                {
                    PriorityAuthorityDiagnostics.RecordAuthorityComputation();
                    return new PriorityAuthoritySnapshot(
                        game,
                        frame,
                        fluffySelection.Generation,
                        ExplicitAuthorityGeneration,
                        registeredStoreCount,
                        preference,
                        PriorityAuthorityOwner.BetterWorkTab,
                        null,
                        null,
                        0,
                        false);
                }

                if (fluffySelection.Store == null)
                {
                    // Retain the selected preference while Fluffy is unavailable. BWT is a safe
                    // fallback only when no other external store currently claims authority.
                    return Cache(CreateBetterWorkTabSnapshot(
                        game,
                        frame,
                        fluffySelection.Generation,
                        registeredStoreCount,
                        preference));
                }

                if (!string.Equals(
                        fluffySelection.StoreId,
                        PriorityProviderIntegrationCatalog.FluffyWorkTabProviderId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    // An explicit Fluffy preference must not turn availability into ownership or
                    // hand authority away from an actually-claiming Sleek/other external store.
                    // Keep the snapshot non-coherent so transition and UI callers fail closed.
                    PriorityAuthorityDiagnostics.RecordAuthorityComputation();
                    return new PriorityAuthoritySnapshot(
                        game,
                        frame,
                        fluffySelection.Generation,
                        ExplicitAuthorityGeneration,
                        registeredStoreCount,
                        preference,
                        PriorityAuthorityOwner.BetterWorkTab,
                        null,
                        null,
                        0,
                        false);
                }

                // Availability is not authority. Revalidate the exact store object and
                // registration generation after the authoritative selection so an explicit
                // Fluffy preference cannot resurrect a replaced adapter or a stale handoff.
                if (!ExternalWorkTabRegistry.IsCurrentAuthoritativeStore(
                        fluffySelection.StoreId,
                        fluffySelection.Store,
                        fluffySelection.RegistrationGeneration))
                {
                    PriorityAuthorityDiagnostics.RecordAuthorityComputation();
                    return new PriorityAuthoritySnapshot(
                        game,
                        frame,
                        fluffySelection.Generation,
                        ExplicitAuthorityGeneration,
                        registeredStoreCount,
                        preference,
                        PriorityAuthorityOwner.BetterWorkTab,
                        null,
                        null,
                        0,
                        false);
                }

                return Cache(new PriorityAuthoritySnapshot(
                    game,
                    frame,
                    fluffySelection.Generation,
                    ExplicitAuthorityGeneration,
                    registeredStoreCount,
                    preference,
                    PriorityAuthorityOwner.FluffyWorkTab,
                    fluffySelection.Store,
                    fluffySelection.StoreId,
                    fluffySelection.RegistrationGeneration,
                    true));
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
                    preference,
                    PriorityAuthorityOwner.BetterWorkTab,
                    null,
                    null,
                    0,
                    false);
            }

            if (selection.Store != null &&
                !ExternalWorkTabRegistry.IsCurrentAuthoritativeStore(
                    selection.StoreId,
                    selection.Store,
                    selection.RegistrationGeneration))
            {
                PriorityAuthorityDiagnostics.RecordAuthorityComputation();
                return new PriorityAuthoritySnapshot(
                    game,
                    frame,
                    selection.Generation,
                    ExplicitAuthorityGeneration,
                    registeredStoreCount,
                    preference,
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
                preference,
                owner,
                store,
                selection.StoreId,
                selection.RegistrationGeneration,
                true);
            return Cache(snapshot);
        }

        private static PriorityAuthoritySnapshot CreateBetterWorkTabSnapshot(
            Game game,
            int frame,
            long registryGeneration,
            int registeredStoreCount,
            PriorityDataAuthorityPreference preference)
        {
            return new PriorityAuthoritySnapshot(
                game,
                frame,
                registryGeneration,
                ExplicitAuthorityGeneration,
                registeredStoreCount,
                preference,
                PriorityAuthorityOwner.BetterWorkTab,
                null,
                null,
                0,
                true);
        }

        private static PriorityAuthoritySnapshot Cache(PriorityAuthoritySnapshot snapshot)
        {
            cachedAuthoritySnapshot = snapshot;
            hasCachedAuthoritySnapshot = true;
            PriorityAuthorityDiagnostics.RecordAuthorityComputation();
            return snapshot;
        }

        /// <summary>
        /// Returns whether BWT priority data must be treated as read-only. This is deliberately
        /// separate from renderer ownership: BWT may continue drawing while another store owns the
        /// effective priority data.
        /// </summary>
        internal static bool ShouldBlockBetterWorkTabPriorityDataAccess
        {
            get
            {
                PriorityAuthoritySnapshot snapshot = Resolve();
                return !snapshot.IsCoherent ||
                    snapshot.Owner != PriorityAuthorityOwner.BetterWorkTab;
            }
        }

        /// <summary>
        /// Returns whether a Better Work Tab-owned input surface may write shared priority data.
        ///
        /// This is intentionally based on the resolver snapshot rather than on the rendering
        /// owner. BWT may render external priority data, but it must not turn a click, paint, or
        /// schedule gesture into an implicit authority handoff. A transiently incoherent registry
        /// is also read-only until the exact owner can be established again.
        /// </summary>
        internal static bool CanBetterWorkTabMutatePriorityData
        {
            get
            {
                PriorityAuthoritySnapshot snapshot = Resolve();
                return snapshot.IsCoherent &&
                    snapshot.Owner == PriorityAuthorityOwner.BetterWorkTab;
            }
        }

        /// <summary>
        /// Gets a stable authority revision for caches that must not retain BWT-derived child
        /// overrides across an external-authority transition.
        /// </summary>
        internal static long CurrentAuthorityRevision =>
            GetRevision(Resolve());

        /// <summary>
        /// Returns the exact current external store only when the resolver snapshot and the live
        /// registry selection still refer to the same registration. Readers may use this adapter;
        /// BWT has no write adapter and therefore remains read-only under the result.
        /// </summary>
        internal static bool TryGetVerifiedExternalStore(
            out IExternalWorkTabStore store,
            out long authorityRevision)
        {
            PriorityAuthoritySnapshot snapshot = Resolve();
            authorityRevision = GetRevision(snapshot);
            store = null;
            if (!snapshot.IsCoherent ||
                snapshot.Owner == PriorityAuthorityOwner.BetterWorkTab ||
                snapshot.AuthoritativeStore == null ||
                string.IsNullOrWhiteSpace(snapshot.StoreId) ||
                snapshot.StoreRegistrationGeneration == 0)
            {
                return false;
            }

            return ExternalWorkTabRegistry.IsCurrentAuthoritativeStore(
                       snapshot.StoreId,
                       snapshot.AuthoritativeStore,
                       snapshot.StoreRegistrationGeneration) &&
                   (store = snapshot.AuthoritativeStore) != null;
        }

        private static long GetRevision(PriorityAuthoritySnapshot snapshot)
        {
            unchecked
            {
                long revision = snapshot.RegistryGeneration;
                revision = (revision * 397) + snapshot.ExplicitAuthorityGeneration;
                revision = (revision * 397) + (long)snapshot.Preference;
                revision = (revision * 397) + (long)snapshot.Owner;
                revision = (revision * 397) + snapshot.StoreRegistrationGeneration;
                revision = (revision * 397) + (snapshot.IsCoherent ? 1 : 0);
                revision = (revision * 397) + StringComparer.OrdinalIgnoreCase.GetHashCode(
                    snapshot.StoreId ?? string.Empty);
                return revision;
            }
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
