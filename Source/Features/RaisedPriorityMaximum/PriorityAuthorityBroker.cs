using Better_Work_Tab.API;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    /// <summary>
    /// Compatibility façade for priority authority, provider, and range decisions.
    ///
    /// Existing BWT call sites intentionally continue to use this stable surface while the
    /// implementation is separated into authority resolution, provider selection, range policy,
    /// transition/handoff, value resolution, and feature-gating services.
    /// </summary>
    public static class PriorityAuthorityBroker
    {
        public static PriorityAuthorityOwner CurrentAuthority =>
            PriorityAuthorityTransitionService.CurrentAuthority;

#if DEBUG
        public static PriorityAuthorityDiagnosticsSnapshot Diagnostics =>
            PriorityAuthorityDiagnostics.Snapshot;
#endif

        public static bool BetterWorkTabHasPriorityAuthority =>
            PriorityAuthorityTransitionService.IsBetterWorkTabAuthority;

        public static bool FluffyWorkTabHasPriorityAuthority =>
            PriorityAuthorityTransitionService.IsFluffyWorkTabAuthority;

        public static bool ExternalWorkTabHasPriorityAuthority =>
            PriorityAuthorityTransitionService.HasExternalAuthority;

        public static bool BetterWorkTabRendersWorkTab =>
            PriorityAuthorityFeaturePolicy.BetterWorkTabRendersWorkTab;

        internal static bool ShouldRunBetterWorkTabPriorityFeatures =>
            PriorityAuthorityFeaturePolicy.ShouldRunBetterWorkTabPriorityFeatures;

        internal static bool ShouldRunBetterWorkTabOrdering =>
            PriorityAuthorityFeaturePolicy.ShouldRunBetterWorkTabOrdering;

        internal static void NotifyPotentialAuthorityChanged(bool refreshRegistry = true)
        {
            PriorityAuthorityTransitionService.NotifyPotentialAuthorityChanged(refreshRegistry);
        }

        internal static int GetEffectivePriority(Pawn pawn, WorkTypeDef workType)
        {
            return PriorityValueResolver.GetEffectivePriority(pawn, workType);
        }

        internal static int GetEffectivePriorityAtHour(Pawn pawn, WorkTypeDef workType, int hour)
        {
            return PriorityValueResolver.GetEffectivePriorityAtHour(pawn, workType, hour);
        }

        internal static int GetBetterWorkTabStoredPriority(
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType)
        {
            return PriorityValueResolver.GetBetterWorkTabStoredPriority(workSettings, workType);
        }

        internal static int GetVanillaCompatibleStoredPriority(
            Pawn pawn,
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType)
        {
            return PriorityValueResolver.GetVanillaCompatibleStoredPriority(
                pawn,
                workSettings,
                workType);
        }

        internal static int GetBetterWorkTabEffectiveWorkGiverPriorityAtHour(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int hour)
        {
            return PriorityValueResolver.GetBetterWorkTabEffectiveWorkGiverPriorityAtHour(
                pawn,
                workType,
                workGiver,
                hour);
        }

        public static PriorityProviderSnapshot GetSnapshot()
        {
            return PriorityProviderSelector.GetSnapshot();
        }

        public static PriorityProviderSnapshot GetSnapshotForPriority(int requestedPriority)
        {
            return PriorityProviderSelector.GetSnapshotForPriority(requestedPriority);
        }

        public static int GetEffectiveMaxPriority()
        {
            return PriorityRangePolicy.GetEffectiveMaxPriority();
        }

        public static int GetRequestableMaxPriority()
        {
            return PriorityRangePolicy.GetRequestableMaxPriority();
        }

        public static int GetDefaultEnabledPriority()
        {
            return PriorityRangePolicy.GetDefaultEnabledPriority();
        }

        internal static int GetBetterWorkTabConfiguredMaxPriority()
        {
            return PriorityRangePolicy.GetBetterWorkTabConfiguredMaxPriority();
        }

        internal static int GetAutoConfiguredMaxPriority()
        {
            return PriorityRangePolicy.GetAutoConfiguredMaxPriority();
        }

        internal static int ClampMaxPriority(int value)
        {
            return PriorityRangePolicy.ClampMaxPriority(value);
        }

        internal static int ClampDefaultEnabledPriority(int value, int maxPriority)
        {
            return PriorityRangePolicy.ClampDefaultEnabledPriority(value, maxPriority);
        }

        public static int ClampPriority(int priority)
        {
            return PriorityRangePolicy.ClampPriorityForRequest(priority);
        }

        public static int ClampPriorityForRequest(int priority)
        {
            return PriorityRangePolicy.ClampPriorityForRequest(priority);
        }

        internal static int ClampStoredPriorityForRuntime(int priority)
        {
            return PriorityRangePolicy.ClampStoredPriorityForRuntime(priority);
        }

        public static int GetDefaultManualPriorityForDisabledWork()
        {
            return PriorityRangePolicy.GetDefaultManualPriorityForDisabledWork();
        }

        public static int GetPriorityAfterClick(int currentPriority, int delta)
        {
            return PriorityRangePolicy.GetPriorityAfterClick(currentPriority, delta);
        }

        public static int GetNextManualPriority(int currentPriority, int direction)
        {
            return PriorityRangePolicy.GetNextManualPriority(currentPriority, direction);
        }

        internal static bool AutoProviderSelectionEnabled()
        {
            return PriorityRangePolicy.AutoProviderSelectionEnabled();
        }

        internal static void InvalidateCaches(bool refreshProviderRegistry = true)
        {
            PriorityAuthorityTransitionService.InvalidateCaches(refreshProviderRegistry);
        }

        internal static IExternalWorkTabStore GetAuthoritativeStore()
        {
            return PriorityAuthorityTransitionService.GetAuthoritativeStore();
        }
    }
}
