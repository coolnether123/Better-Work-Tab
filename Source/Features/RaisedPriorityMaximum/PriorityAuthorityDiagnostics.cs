using System.Diagnostics;
using Better_Work_Tab.ModSupport;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal static class PriorityAuthorityDiagnostics
    {
        private static long explicitAuthorityGeneration;
#if DEBUG
        private static long authorityRequests;
        private static long authorityCacheHits;
        private static long authorityComputations;
        private static long authorityTransitions;
        private static long handoffs;
        private static long explicitInvalidations;
#endif

        internal static long ExplicitAuthorityGeneration => explicitAuthorityGeneration;

        [Conditional("DEBUG")]
        internal static void RecordAuthorityRequest()
        {
#if DEBUG
            authorityRequests++;
#endif
        }

        [Conditional("DEBUG")]
        internal static void RecordAuthorityCacheHit()
        {
#if DEBUG
            authorityCacheHits++;
#endif
        }

        [Conditional("DEBUG")]
        internal static void RecordAuthorityComputation()
        {
#if DEBUG
            authorityComputations++;
#endif
        }

        [Conditional("DEBUG")]
        internal static void RecordAuthorityTransition()
        {
#if DEBUG
            authorityTransitions++;
#endif
        }

        [Conditional("DEBUG")]
        internal static void RecordHandoff()
        {
#if DEBUG
            handoffs++;
#endif
        }

        internal static void RecordExplicitInvalidation()
        {
#if DEBUG
            explicitInvalidations++;
#endif
            explicitAuthorityGeneration++;
        }

#if DEBUG
        internal static PriorityAuthorityDiagnosticsSnapshot Snapshot =>
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
    }
}
