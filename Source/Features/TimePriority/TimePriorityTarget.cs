using System;
using System.Globalization;
using Better_Work_Tab.Features.Workloads.V2;
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
            string label,
            bool labelFree)
        {
            PawnId = pawnId;
            Kind = kind;
            WorkTypeDefName = workTypeDefName ?? string.Empty;
            TargetDefName = targetDefName ?? string.Empty;
            Label = labelFree ? null : label ?? "Work";
        }

        private TimePriorityTarget(
            int pawnId,
            TimePriorityTargetKind kind,
            string workTypeDefName,
            string targetDefName,
            string label)
            : this(pawnId, kind, workTypeDefName, targetDefName, label, false)
        {
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

        /// <summary>
        /// Creates only the stable identity used by AI, priority, and schedule
        /// evaluation. UI callers should use <see cref="ForWorkType"/> so the
        /// existing localization timing and label semantics remain unchanged.
        /// </summary>
        internal static TimePriorityTarget ForRuntimeWorkType(Pawn pawn, WorkTypeDef workType)
        {
            string defName = workType?.defName ?? string.Empty;
            return new TimePriorityTarget(
                pawn?.thingIDNumber ?? GlobalPawnId,
                TimePriorityTargetKind.WorkType,
                defName,
                defName,
                null,
                true);
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

        /// <summary>
        /// Creates only the stable identity used by AI, priority, and schedule
        /// evaluation. UI callers should use <see cref="ForWorkGiver"/> so the
        /// existing localization timing and label semantics remain unchanged.
        /// </summary>
        internal static TimePriorityTarget ForRuntimeWorkGiver(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver)
        {
            return new TimePriorityTarget(
                pawn?.thingIDNumber ?? GlobalPawnId,
                TimePriorityTargetKind.WorkGiver,
                workType?.defName ?? string.Empty,
                workGiver?.defName ?? string.Empty,
                null,
                true);
        }

        internal bool Matches(TimePriorityTarget other)
        {
            return PawnId == other.PawnId &&
                   Kind == other.Kind &&
                   string.Equals(WorkTypeDefName, other.WorkTypeDefName, StringComparison.Ordinal) &&
                   string.Equals(TargetDefName, other.TargetDefName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Converts the runtime identity to the typed workload schedule key.
        /// The workload contract deliberately has no global parent-work-type
        /// schedule, so that identity is rejected instead of being silently
        /// written into a pawn-local record.
        /// </summary>
        internal bool TryGetWorkloadScheduleTarget(
            out WorkloadScheduleTargetKey key,
            out string reason)
        {
            key = null;
            reason = null;
            if (string.IsNullOrEmpty(WorkTypeDefName))
            {
                reason = "The schedule target has no WorkType identity.";
                return false;
            }

            var workType = new WorkTypeKey(WorkTypeDefName);
            if (Kind == TimePriorityTargetKind.WorkType)
            {
                if (IsGlobal)
                {
                    reason = "Global parent WorkType schedules are not supported by Workloads 2.0.";
                    return false;
                }

                if (PawnId < 0)
                {
                    reason = "The schedule target has an invalid pawn identity.";
                    return false;
                }

                key = WorkloadScheduleTargetKey.ForParent(
                    new PawnKey(PawnId.ToString(CultureInfo.InvariantCulture)),
                    workType);
                return key.IsValid;
            }

            if (Kind != TimePriorityTargetKind.WorkGiver ||
                string.IsNullOrEmpty(TargetDefName))
            {
                reason = "The schedule target has no WorkGiver identity.";
                return false;
            }

            var workGiver = new WorkGiverKey(TargetDefName);
            key = IsGlobal
                ? WorkloadScheduleTargetKey.GlobalWorkGiver(workType, workGiver)
                : WorkloadScheduleTargetKey.ForWorkGiver(
                    new PawnKey(PawnId.ToString(CultureInfo.InvariantCulture)),
                    workType,
                    workGiver);
            if (!key.IsValid)
            {
                reason = "The schedule target identity is not valid for the workload contract.";
                key = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Rehydrates the runtime schedule identity from the persisted typed
        /// key. This is intentionally the only conversion path used by the
        /// schedule commit adapter; callers do not poke workload key fields or
        /// the live schedule collection directly.
        /// </summary>
        internal static bool TryFromWorkloadScheduleTarget(
            WorkloadScheduleTargetKey key,
            out TimePriorityTarget target,
            out string reason)
        {
            target = default;
            reason = null;
            if (key == null || !key.IsValid)
            {
                reason = "The workload schedule target is invalid.";
                return false;
            }

            if (key.TargetKind == WorkloadScheduleTargetKind.ParentWorkType && key.IsGlobal)
            {
                reason = "Global parent WorkType schedules are not supported by Workloads 2.0.";
                return false;
            }

            int pawnId = GlobalPawnId;
            if (!key.IsGlobal &&
                (!int.TryParse(
                    key.Pawn.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out pawnId) || pawnId < 0))
            {
                reason = "The workload schedule key has an invalid pawn identity.";
                return false;
            }

            TimePriorityTargetKind kind = key.TargetKind == WorkloadScheduleTargetKind.ParentWorkType
                ? TimePriorityTargetKind.WorkType
                : TimePriorityTargetKind.WorkGiver;
            target = FromRaw(
                pawnId,
                kind,
                key.WorkType.Value,
                kind == TimePriorityTargetKind.WorkGiver ? key.WorkGiver.Value : key.WorkType.Value);
            return true;
        }
    }
}
