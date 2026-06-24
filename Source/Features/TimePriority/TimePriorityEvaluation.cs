using Better_Work_Tab.Features.RaisedPriorityMaximum;

namespace Better_Work_Tab.Features.TimePriority
{
    internal readonly struct TimePriorityEvaluation
    {
        internal TimePriorityEvaluation(
            TimePriorityTarget target,
            int hour,
            int basePriority,
            int effectivePriority,
            bool hasSchedule,
            int scheduledPriority,
            string scheduleScope)
        {
            Target = target;
            Hour = hour;
            BasePriority = WorkPrioritySystem.ClampPriority(basePriority);
            EffectivePriority = WorkPrioritySystem.ClampPriority(effectivePriority);
            HasSchedule = hasSchedule;
            ScheduledPriority = WorkPrioritySystem.ClampPriority(scheduledPriority);
            ScheduleScope = scheduleScope ?? TimePriorityScheduleScopes.None;
        }

        internal TimePriorityTarget Target { get; }
        internal int Hour { get; }
        internal int BasePriority { get; }
        internal int EffectivePriority { get; }
        internal bool HasSchedule { get; }
        internal int ScheduledPriority { get; }
        internal string ScheduleScope { get; }

        internal bool IsAvailable => EffectivePriority > WorkPrioritySystem.DisabledPriority;
        internal bool DisabledBySchedule =>
            HasSchedule &&
            BasePriority > WorkPrioritySystem.DisabledPriority &&
            EffectivePriority <= WorkPrioritySystem.DisabledPriority;

        internal string DisabledReason
        {
            get
            {
                if (!DisabledBySchedule)
                {
                    return null;
                }

                return ScheduleScope == TimePriorityScheduleScopes.Global
                    ? "disabled by global time priority for " + TimePriorityService.FormatHour(Hour)
                    : "disabled by time priority for " + TimePriorityService.FormatHour(Hour);
            }
        }
    }

    internal static class TimePriorityScheduleScopes
    {
        internal const string None = "none";
        internal const string Pawn = "pawn";
        internal const string Global = "global";
    }
}
