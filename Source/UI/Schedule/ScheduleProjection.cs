using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Commands;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.Schedule
{
    /// <summary>UI boundary for preview schedule overlays; live state stays in TimePriorityService.</summary>
    internal static class ScheduleProjection
    {
        internal static bool IsPreviewActive => WorkTabEffectiveStateRuntime.IsPreviewActive;

        internal static void ReportScheduleBlocked(string reason)
        {
            WorkTabEffectiveStateRuntime.ReportBlocked(
                WorkTabEffectiveStateDimension.Schedule,
                reason);
        }

        internal static bool CanEditSchedule(TimePriorityTarget target, out string reason)
        {
            reason = null;
            if (target.Kind == TimePriorityTargetKind.WorkType && target.IsGlobal)
            {
                reason = "Global parent WorkType schedules are not supported.";
                return false;
            }
            if (!IsPreviewActive)
            {
                return true;
            }
            if (!WorkTabEffectiveStateRuntime.IsPreviewDimensionOwned(
                    WorkTabEffectiveStateDimension.Schedule))
            {
                reason = "The active preview does not own hourly schedules.";
                return false;
            }

            if (!(WorkTabEffectiveStateRuntime.CurrentProvider is IWorkTabPreviewStateEditor))
            {
                reason = "The active preview does not support typed schedule edits.";
                return false;
            }

            return true;
        }

        internal static TimePriorityScheduleValue ReadSchedule(
            TimePriorityTarget target,
            int fallbackPriority)
        {
            if (!TryGetPreviewSchedule(target, out WorkTabEffectiveStateResolution<TimePriorityScheduleValue> value))
            {
                return TimePriorityService.ReadLiveSchedule(target, fallbackPriority);
            }

            if (value.IsSet && value.Value != null && value.Value.IsValid)
            {
                return TimePriorityService.ResolveScheduleValue(value.Value, fallbackPriority);
            }

            return value.IsClear
                ? TimePriorityService.ResolveScheduleValue(TimePriorityScheduleValue.AllLinked, fallbackPriority)
                : TimePriorityService.ReadLiveSchedule(target, fallbackPriority);
        }

        internal static bool TryWriteSchedule(
            TimePriorityTarget target,
            TimePriorityScheduleValue value,
            int fallbackPriority,
            out string reason)
        {
            reason = null;
            if (value == null || !value.IsValid)
            {
                reason = "The schedule is not a complete 24-hour value.";
                return false;
            }

            if (!IsPreviewActive)
            {
                WorkTabApplicationResult commandResult = TimePriorityService.SubmitSchedule(
                    target,
                    value,
                    fallbackPriority);
                reason = commandResult.Reason;
                return commandResult.Accepted;
            }

            if (!CanEditSchedule(target, out reason))
            {
                return false;
            }

            WorkTabEffectiveStateResolution<TimePriorityScheduleValue> intent = value.HasPinnedHours
                ? WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.Set(value)
                : WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.Clear;
            bool accepted = WorkTabEffectiveStateRuntime.TrySetPreviewScheduleIntent(
                target,
                intent,
                out WorkTabEffectiveStateMutationResult mutationResult);
            reason = accepted ? null : mutationResult.Reason;
            return accepted;
        }

        internal static WorkTabApplicationResult ApplyFullDayPreviewParent(
            Pawn pawn,
            WorkTypeDef workType,
            int priority)
        {
            TimePriorityTarget target = TimePriorityTarget.ForWorkType(pawn, workType);
            return ApplyFullDayPreview(target,
                () => WorkPriorityCommandGateway.TrySetPreviewParentPriority(pawn, workType, priority),
                WorkTabApplicationDimensions.Schedule | WorkTabApplicationDimensions.ParentPriority);
        }

        internal static WorkTabApplicationResult ApplyFullDayPreviewSpecific(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int priority)
        {
            TimePriorityTarget target = TimePriorityTarget.ForWorkGiver(pawn, workGiver);
            WorkTabSpecificJobTarget specificTarget =
                WorkTabSpecificJobTarget.For(pawn, workType, workGiver);
            return ApplyFullDayPreview(target,
                () => WorkTabEffectiveStateRuntime.TrySetPreviewSpecificJobPriority(
                    specificTarget,
                    priority,
                    out _),
                WorkTabApplicationDimensions.Schedule | WorkTabApplicationDimensions.SpecificPriority);
        }

        private static WorkTabApplicationResult ApplyFullDayPreview(
            TimePriorityTarget target,
            System.Func<bool> apply,
            WorkTabApplicationDimensions dimensions)
        {
            WorkTabApplicationRevision revision = WorkTabApplication.Current?.Revision ?? default;
            if (!CanEditSchedule(target, out string reason))
            {
                return WorkTabApplicationResult.Rejected(reason, revision);
            }

            WorkTabEffectiveStateResolution<TimePriorityScheduleValue> previous =
                WorkTabEffectiveStateRuntime.ResolvePreviewScheduleIntent(target);
            if (!WorkTabEffectiveStateRuntime.TrySetPreviewScheduleIntent(
                    target,
                    WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.Clear,
                    out WorkTabEffectiveStateMutationResult clearResult))
                return WorkTabApplicationResult.Rejected(clearResult.Reason, revision);
            if (apply())
                return new WorkTabApplicationResult(WorkTabApplicationOutcome.Applied, null,
                    new WorkTabApplicationChange(target, dimensions, false), revision);

            bool restored = WorkTabEffectiveStateRuntime.TrySetPreviewScheduleIntent(
                target, previous, out WorkTabEffectiveStateMutationResult result);
            return restored
                ? WorkTabApplicationResult.Rejected("The active preview rejected the priority.", revision)
                : new WorkTabApplicationResult(WorkTabApplicationOutcome.Partial, result.Reason,
                    new WorkTabApplicationChange(target, dimensions, false), revision);
        }

        private static bool TryGetPreviewSchedule(
            TimePriorityTarget target,
            out WorkTabEffectiveStateResolution<TimePriorityScheduleValue> value)
        {
            value = WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.NoOpinion;
            if (!IsPreviewActive || !WorkTabEffectiveStateRuntime.IsPreviewDimensionOwned(
                    WorkTabEffectiveStateDimension.Schedule))
            {
                return false;
            }

            value = WorkTabEffectiveStateRuntime.ResolvePreviewSchedule(target);
            return true;
        }

    }
}
