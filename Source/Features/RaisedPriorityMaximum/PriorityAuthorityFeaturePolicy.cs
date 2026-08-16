using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    /// <summary>
    /// Keeps presentation ownership separate from priority-data ownership.
    /// </summary>
    internal static class PriorityAuthorityFeaturePolicy
    {
        /// <summary>
        /// True while Better Work Tab draws the Work tab, i.e. any time another Work tab mod has not
        /// taken the window over. Independent of who stores the priority numbers.
        /// </summary>
        internal static bool BetterWorkTabRendersWorkTab =>
            !FluffyWorkTabGateway.ExternalWorkTabOwnsWorkTab;

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
            PriorityAuthorityTransitionService.IsBetterWorkTabAuthority &&
            (WorkGiverReassignmentManager.HasActiveData ||
             TimePriorityService.IsRuntimeActive ||
             WorkExecutionOrder.HasCustomExecutionOrder);
    }
}
