using System;
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
        internal readonly string Label;

        private TimePriorityTarget(
            int pawnId,
            TimePriorityTargetKind kind,
            string workTypeDefName,
            string targetDefName,
            string label)
        {
            PawnId = pawnId;
            Kind = kind;
            WorkTypeDefName = workTypeDefName ?? string.Empty;
            TargetDefName = targetDefName ?? string.Empty;
            Label = label ?? "Work";
        }

        internal bool IsGlobal => PawnId == GlobalPawnId;

        internal string Key => TimePriorityService.BuildKey(PawnId, Kind, WorkTypeDefName, TargetDefName);

        internal TimePriorityCacheKey CacheKey =>
            new TimePriorityCacheKey(PawnId, Kind, WorkTypeDefName, TargetDefName);

        internal static TimePriorityTarget FromRaw(
            int pawnId,
            TimePriorityTargetKind kind,
            string workTypeDefName,
            string targetDefName,
            string label = null)
        {
            return new TimePriorityTarget(pawnId, kind, workTypeDefName, targetDefName, label);
        }

        internal static TimePriorityTarget ForWorkType(Pawn pawn, WorkTypeDef workType, string label = null)
        {
            string defName = workType?.defName ?? string.Empty;
            return new TimePriorityTarget(
                pawn?.thingIDNumber ?? GlobalPawnId,
                TimePriorityTargetKind.WorkType,
                defName,
                defName,
                label ?? workType?.labelShort?.CapitalizeFirst() ?? workType?.LabelCap.ToString() ?? "Work");
        }

        internal static TimePriorityTarget ForWorkGiver(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver, string label = null)
        {
            return new TimePriorityTarget(
                pawn?.thingIDNumber ?? GlobalPawnId,
                TimePriorityTargetKind.WorkGiver,
                workType?.defName ?? string.Empty,
                workGiver?.defName ?? string.Empty,
                label ?? workGiver?.LabelCap.ToString() ?? workType?.labelShort?.CapitalizeFirst() ?? "Sub-work");
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
