using System;
using System.Globalization;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.Multiplayer;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    /// <summary>
    /// Workload-owned translation and transaction adapter for hourly schedule
    /// state.  It translates workload keys and payloads at this boundary while
    /// the schedule service retains identity-independent state and validation.
    /// </summary>
    internal static class WorkloadTimePriorityAdapter
    {
        internal static bool TryGetScheduleTarget(
            TimePriorityTarget target,
            out WorkloadScheduleTargetKey key,
            out string reason)
        {
            key = null;
            reason = null;
            if (string.IsNullOrEmpty(target.WorkTypeDefName))
                return Fail("The schedule target has no WorkType identity.", out reason);

            var workType = new WorkTypeKey(target.WorkTypeDefName);
            if (target.Kind == TimePriorityTargetKind.WorkType)
            {
                if (target.IsGlobal)
                    return Fail("Global parent WorkType schedules are not supported by Workloads 2.0.", out reason);

                if (target.PawnId < 0)
                    return Fail("The schedule target has an invalid pawn identity.", out reason);

                key = WorkloadScheduleTargetKey.ForParent(
                    new PawnKey(target.PawnId.ToString(CultureInfo.InvariantCulture)),
                    workType);
                if (key.IsValid) return true;
                key = null;
                return Fail("The schedule target identity is not valid for the workload contract.", out reason);
            }

            if (target.Kind != TimePriorityTargetKind.WorkGiver ||
                string.IsNullOrEmpty(target.TargetDefName))
                return Fail("The schedule target has no WorkGiver identity.", out reason);

            var workGiver = new WorkGiverKey(target.TargetDefName);
            key = target.IsGlobal
                ? WorkloadScheduleTargetKey.GlobalWorkGiver(workType, workGiver)
                : WorkloadScheduleTargetKey.ForWorkGiver(
                    new PawnKey(target.PawnId.ToString(CultureInfo.InvariantCulture)),
                    workType,
                    workGiver);
            if (!key.IsValid)
            {
                key = null;
                return Fail("The schedule target identity is not valid for the workload contract.", out reason);
            }

            return true;
        }

        internal static bool TryGetTimePriorityTarget(
            WorkloadScheduleTargetKey key,
            out TimePriorityTarget target,
            out string reason)
        {
            target = default;
            reason = null;
            if (key == null || !key.IsValid)
                return Fail("The workload schedule target is invalid.", out reason);

            if (key.TargetKind == WorkloadScheduleTargetKind.ParentWorkType && key.IsGlobal)
                return Fail("Global parent WorkType schedules are not supported by Workloads 2.0.", out reason);

            int pawnId = TimePriorityTarget.GlobalPawnId;
            if (!key.IsGlobal &&
                (!int.TryParse(
                    key.Pawn.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out pawnId) || pawnId < 0))
                return Fail("The workload schedule key has an invalid pawn identity.", out reason);

            if (key.TargetKind == WorkloadScheduleTargetKind.ParentWorkType)
            {
                target = TimePriorityTarget.FromRaw(
                    pawnId,
                    TimePriorityTargetKind.WorkType,
                    key.WorkType.Value,
                    key.WorkType.Value);
                return true;
            }

            WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(key.WorkGiver.Value);
            if (workGiver == null || WorkGiverReassignmentManager.GetTargetWorkType(workGiver) == null)
                return Fail("The workload schedule WorkGiver no longer resolves.", out reason);

            target = TimePriorityTarget.ForWorkGiver(pawnId, workGiver);
            return true;
        }

        internal static bool TryCaptureLiveScheduleSnapshot(
            WorkloadScheduleTargetKey key,
            int fallbackPriority,
            out TimePriorityLiveScheduleSnapshot snapshot,
            out string reason)
        {
            if (!TryGetTimePriorityTarget(key, out TimePriorityTarget target, out reason))
            {
                snapshot = null;
                return false;
            }

            return TimePriorityService.TryCaptureLiveScheduleSnapshot(
                target,
                fallbackPriority,
                out snapshot,
                out reason);
        }

        internal static bool Matches(
            TimePriorityLiveScheduleSnapshot snapshot,
            WorkloadSchedulePayload payload)
        {
            return snapshot != null &&
                   snapshot.Schedule != null &&
                   TryGetScheduleValue(payload, out TimePriorityScheduleValue value, out _) &&
                   snapshot.Schedule.Equals(value);
        }

        internal static WorkloadSchedulePayload ToPayload(TimePriorityScheduleValue value)
        {
            return value == null
                ? null
                : new WorkloadSchedulePayload(value.CopyPriorities(), value.PinnedHourMask);
        }

        internal static bool TryGetScheduleValue(
            WorkloadSchedulePayload payload,
            out TimePriorityScheduleValue value,
            out string reason)
        {
            value = null;
            reason = null;
            if (payload == null)
                return Fail("The schedule payload is missing.", out reason);

            value = new TimePriorityScheduleValue(payload.Priorities, payload.PinnedHourMask);
            if (value.IsValid) return true;

            value = null;
            return Fail("The schedule payload is not a complete 24-hour value.", out reason);
        }

        private static bool Fail(string message, out string reason)
        {
            reason = message;
            return false;
        }
    }
}
