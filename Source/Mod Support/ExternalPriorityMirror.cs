using System;
using RimWorld;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// The mod-compatibility layer's outbound hook for priority changes.
    /// </summary>
    /// <remarks>
    /// Core services announce that they changed a priority; this class forwards the event to registered
    /// external work-tab stores. Core must never name a specific mod, so every mod-specific concern
    /// (cascade semantics, priority ceilings, tracker layout) stays behind that store's adapter.
    /// <para>
    /// Notifications are cheap when no external mod is loaded, so callers do not need to check first.
    /// </para>
    /// </remarks>
    internal static class ExternalPriorityMirror
    {
        /// <summary>
        /// Suppresses mirroring for the lifetime of the returned scope. Wrap any operation that imports
        /// <em>out of</em> an external mod, so a partially built local store cannot be echoed back over
        /// the data still being read.
        /// </summary>
        internal static IDisposable Suspend()
        {
            return ExternalWorkTabRegistry.SuspendAllMirroring();
        }

        /// <summary>
        /// True while <see cref="Suspend"/> is in effect.
        /// </summary>
        internal static bool IsSuspended => ExternalWorkTabRegistry.AnyStoreSuspended;

        internal static bool ShouldMirrorTimePrioritySchedules =>
            ExternalWorkTabRegistry.ShouldMirrorTimePrioritySchedules;

        /// <summary>
        /// One pawn's work-type priority changed, including any hourly schedule attached to it.
        /// </summary>
        internal static void NotifyWorkTypeChanged(Pawn pawn, WorkTypeDef workType)
        {
            ExternalWorkTabRegistry.PushWorkType(pawn, workType);
        }

        /// <summary>
        /// One pawn's work-giver priority changed, including any hourly schedule attached to it.
        /// </summary>
        internal static void NotifyWorkGiverChanged(Pawn pawn, WorkGiverDef workGiver)
        {
            ExternalWorkTabRegistry.PushWorkGiver(pawn, workGiver);
        }

        /// <summary>
        /// A work type changed for every pawn, e.g. a global hourly schedule was edited.
        /// </summary>
        internal static void NotifyWorkTypeChangedForAllPawns(WorkTypeDef workType)
        {
            ExternalWorkTabRegistry.PushWorkTypeForAllPawns(workType);
        }

        /// <summary>
        /// A work giver changed for every pawn, e.g. a global hourly schedule was edited.
        /// </summary>
        internal static void NotifyWorkGiverChangedForAllPawns(WorkGiverDef workGiver)
        {
            ExternalWorkTabRegistry.PushWorkGiverForAllPawns(workGiver);
        }

        /// <summary>
        /// Priority data changed broadly enough that every pawn must be re-mirrored.
        /// Returns the number of pawns pushed.
        /// </summary>
        internal static int NotifyAllChanged()
        {
            return ExternalWorkTabRegistry.PushAllPawns();
        }
    }
}
