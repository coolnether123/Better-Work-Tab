using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>Identifies the store that owns one pawn-specific job priority.</summary>
    internal enum SpecificJobPriorityStorageKind : byte
    {
        ReassignmentManager = 0,
        ExternalAuthority = 1
    }

    /// <summary>Exact captured state for one authority-selected priority store.</summary>
    internal readonly struct SpecificJobPriorityStorageSnapshot
    {
        internal SpecificJobPriorityStorageSnapshot(
            SpecificJobPriorityStorageKind storageKind,
            bool hadOverride,
            int priority)
        {
            StorageKind = storageKind;
            HadOverride = hadOverride;
            Priority = priority;
        }

        internal SpecificJobPriorityStorageKind StorageKind { get; }
        internal bool HadOverride { get; }
        internal int Priority { get; }
    }

    /// <summary>
    /// Selects and accesses the active pawn-specific job priority store without
    /// exposing an optional external mod to transaction planning code.
    /// </summary>
    internal static class SpecificJobPriorityAuthorityAdapter
    {
        internal static SpecificJobPriorityStorageSnapshot Capture(Pawn pawn, WorkGiverDef workGiver)
        {
            SpecificJobPriorityStorageKind storageKind = SleekWorkTabGateway.SleekCodeRuns
                ? SpecificJobPriorityStorageKind.ExternalAuthority
                : SpecificJobPriorityStorageKind.ReassignmentManager;
            bool hadOverride = TryRead(storageKind, pawn, workGiver, out int priority);
            return new SpecificJobPriorityStorageSnapshot(storageKind, hadOverride, priority);
        }

        internal static bool TryRead(
            SpecificJobPriorityStorageKind storageKind,
            Pawn pawn,
            WorkGiverDef workGiver,
            out int priority)
        {
            priority = WorkPrioritySystem.DisabledPriority;
            if (pawn == null || workGiver == null)
            {
                return false;
            }

            if (storageKind == SpecificJobPriorityStorageKind.ExternalAuthority)
            {
                return SleekWorkTabGateway.TryGetSleekWorkGiverOverride(
                           pawn,
                           workGiver,
                           out priority) &&
                       priority >= 0;
            }

            return WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                pawn,
                workGiver,
                out priority);
        }

        /// <summary>
        /// Writes the direct external store. Manager-owned priorities continue
        /// through its atomic batch API and are deliberately rejected here.
        /// </summary>
        internal static bool TryWriteExternal(
            SpecificJobPriorityStorageKind storageKind,
            Pawn pawn,
            WorkGiverDef workGiver,
            int? priority)
        {
            if (storageKind != SpecificJobPriorityStorageKind.ExternalAuthority ||
                pawn == null || workGiver == null)
            {
                return false;
            }

            return priority.HasValue
                ? SleekWorkTabGateway.TrySetSleekWorkGiverOverride(
                    pawn,
                    workGiver,
                    WorkPrioritySystem.ClampPriority(priority.Value))
                : SleekWorkTabGateway.TryClearSleekWorkGiverOverride(pawn, workGiver);
        }

        internal static bool TryRestoreExternal(
            SpecificJobPriorityStorageSnapshot snapshot,
            Pawn pawn,
            WorkGiverDef workGiver)
        {
            return TryWriteExternal(
                snapshot.StorageKind,
                pawn,
                workGiver,
                snapshot.HadOverride ? snapshot.Priority : (int?)null);
        }
    }
}
