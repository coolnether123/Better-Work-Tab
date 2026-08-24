using Better_Work_Tab.API;
using Better_Work_Tab.Features.TimePriority;
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

        /// <summary>
        /// Captures one coherent read-only authority pair. Unlike
        /// <see cref="CurrentAuthority"/>, this seam cannot apply a transition
        /// or handoff between the owner and revision observations.
        /// </summary>
        internal static void CaptureObservationalAuthority(
            out PriorityAuthoritySnapshot snapshot,
            out long revision)
        {
            PriorityAuthorityTransitionService.CaptureObservationalAuthority(
                out snapshot,
                out revision);
        }

        /// <summary>
        /// Gets the authority revision used by projection/cache readers. Revision observation is
        /// deliberately separate from the normal transition-owning authority getter.
        /// </summary>
        internal static long GetObservationalAuthorityRevision()
        {
            return PriorityAuthorityTransitionService.GetObservationalRevision();
        }

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
            if (pawn?.workSettings == null || workType == null)
                return PriorityRangePolicy.GetDefaultEnabledPriority();
            IExternalWorkTabStore store = PriorityAuthorityTransitionService.GetAuthoritativeStore();
            return store != null && ExternalWorkTabRegistry.TryGetWorkTypePriority(
                store, pawn, workType, TimePriorityService.GetCurrentHour(pawn), out int priority)
                ? ClampRuntimePriority(priority) : GetBetterWorkTabEffectiveStoredPriority(pawn, workType);
        }

        internal static int GetBetterWorkTabStoredPriority(
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType)
        {
            return workSettings?.priorities == null || workType == null
                ? PriorityRangePolicy.GetDefaultEnabledPriority()
                : PriorityRangePolicy.ClampStoredPriorityForRuntime(workSettings.priorities[workType]);
        }

        internal static int GetVanillaCompatibleStoredPriority(
            Pawn pawn,
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType)
        {
            return GetVanillaCompatibleStoredPriority(
                pawn,
                workSettings,
                workType,
                Find.PlaySettings == null ? (bool?)null : Find.PlaySettings.useWorkPriorities);
        }

        // A captured null means PlaySettings was absent; it must not consult live state.
        internal static int GetVanillaCompatibleStoredPriority(
            Pawn pawn,
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType,
            bool? manualMode)
        {
            if (pawn?.RaceProps == null || workSettings?.priorities == null || workType == null)
                return PriorityRangePolicy.GetDefaultEnabledPriority();
            int priority = workSettings.priorities[workType];
            return pawn.RaceProps.Humanlike && priority > PriorityConstants.Disabled &&
                manualMode == false
                ? PriorityConstants.VanillaDefaultEnabled : priority;
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

        private static int GetBetterWorkTabEffectiveStoredPriority(Pawn pawn, WorkTypeDef workType)
        {
            return pawn?.workSettings?.priorities == null || workType == null
                ? PriorityRangePolicy.GetDefaultEnabledPriority()
                : PriorityRangePolicy.ClampStoredPriorityForRuntime(
                    GetVanillaCompatibleStoredPriority(pawn, pawn.workSettings, workType));
        }

        private static int ClampRuntimePriority(int priority)
        {
            return priority < PriorityConstants.Disabled ? PriorityConstants.Disabled :
                priority > PriorityConstants.ExtendedHardMax ? PriorityConstants.ExtendedHardMax : priority;
        }
    }
}
