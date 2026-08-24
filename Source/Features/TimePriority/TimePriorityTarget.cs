using System;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.TimePriority
{
    internal readonly struct TimePriorityTarget
    {
        internal const int GlobalPawnId = -1;

        internal readonly int PawnId;
        internal readonly TimePriorityTargetKind Kind;
        internal readonly string WorkTypeDefName;
        internal readonly string TargetDefName;
        private TimePriorityTarget(
            int pawnId,
            TimePriorityTargetKind kind,
            string workTypeDefName,
            string targetDefName)
        {
            PawnId = pawnId;
            Kind = kind;
            WorkTypeDefName = workTypeDefName ?? string.Empty;
            TargetDefName = targetDefName ?? string.Empty;
        }

        internal bool IsGlobal => PawnId == GlobalPawnId;

        internal string Key => TimePriorityService.BuildKey(PawnId, Kind, WorkTypeDefName, TargetDefName);

        internal TimePriorityCacheKey CacheKey =>
            new TimePriorityCacheKey(PawnId, Kind, WorkTypeDefName, TargetDefName);

        internal static TimePriorityTarget FromRaw(
            int pawnId,
            TimePriorityTargetKind kind,
            string workTypeDefName,
            string targetDefName)
        {
            return new TimePriorityTarget(pawnId, kind, workTypeDefName, targetDefName);
        }

        internal static TimePriorityTarget ForWorkType(Pawn pawn, WorkTypeDef workType)
        {
            string defName = workType?.defName ?? string.Empty;
            return new TimePriorityTarget(
                pawn?.thingIDNumber ?? GlobalPawnId,
                TimePriorityTargetKind.WorkType,
                defName,
                defName);
        }

        /// <summary>
        /// Specific-job schedule identity is pawn scope plus WorkGiver. The
        /// current parent work type is resolved here solely for the persisted
        /// compatibility projection, never supplied by a caller.
        /// </summary>
        internal static TimePriorityTarget ForWorkGiver(Pawn pawn, WorkGiverDef workGiver) =>
            ForWorkGiver(pawn?.thingIDNumber ?? GlobalPawnId, workGiver);

        internal static TimePriorityTarget ForWorkGiver(int pawnId, WorkGiverDef workGiver)
        {
            WorkTypeDef workType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
            return new TimePriorityTarget(
                pawnId,
                TimePriorityTargetKind.WorkGiver,
                workType?.defName ?? string.Empty,
                workGiver?.defName ?? string.Empty);
        }

        internal bool Matches(TimePriorityTarget other)
        {
            return PawnId == other.PawnId &&
                   Kind == other.Kind &&
                   string.Equals(WorkTypeDefName, other.WorkTypeDefName, StringComparison.Ordinal) &&
                   string.Equals(TargetDefName, other.TargetDefName, StringComparison.Ordinal);
        }

    }
}
