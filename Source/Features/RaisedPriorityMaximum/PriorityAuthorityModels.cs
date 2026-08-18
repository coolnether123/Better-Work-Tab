using Better_Work_Tab.API;
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

    /// <summary>
    /// Persisted choice of the shared-priority source. This is intentionally independent of the
    /// Work-tab window owner so BWT can render while Fluffy remains the data authority.
    /// </summary>
    public enum PriorityDataAuthorityPreference
    {
        Automatic,
        BetterWorkTab,
        FluffyWorkTab
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

    internal struct PriorityAuthoritySnapshot
    {
        internal PriorityAuthoritySnapshot(
            Game game,
            int frame,
            long registryGeneration,
            long explicitAuthorityGeneration,
            int registeredStoreCount,
            PriorityDataAuthorityPreference preference,
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
            Preference = preference;
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
        internal PriorityDataAuthorityPreference Preference { get; }
        internal PriorityAuthorityOwner Owner { get; }
        internal IExternalWorkTabStore AuthoritativeStore { get; }
        internal string StoreId { get; }
        internal long StoreRegistrationGeneration { get; }
        internal bool IsCoherent { get; }
    }
}
