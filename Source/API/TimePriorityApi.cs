using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.API
{
    public readonly struct TimePriorityApiSnapshot
    {
        public TimePriorityApiSnapshot(
            int apiVersion,
            int hour,
            int basePriority,
            int effectivePriority,
            bool hasSchedule,
            int scheduledPriority,
            int scheduleVersion,
            string scheduleScope,
            bool disabledBySchedule,
            string disabledReason)
        {
            ApiVersion = apiVersion;
            Hour = hour;
            BasePriority = basePriority;
            EffectivePriority = effectivePriority;
            HasSchedule = hasSchedule;
            ScheduledPriority = scheduledPriority;
            ScheduleVersion = scheduleVersion;
            ScheduleScope = scheduleScope ?? "none";
            DisabledBySchedule = disabledBySchedule;
            DisabledReason = disabledReason;
        }

        public int ApiVersion { get; }
        public int Hour { get; }
        public int BasePriority { get; }
        public int EffectivePriority { get; }
        public bool HasSchedule { get; }
        public int ScheduledPriority { get; }
        public int ScheduleVersion { get; }
        public string ScheduleScope { get; }
        public bool IsAvailable => EffectivePriority > WorkPrioritySystem.DisabledPriority;
        public bool DisabledBySchedule { get; }
        public string DisabledReason { get; }
    }

    public static class TimePriorityApi
    {
        public const int ApiVersion = 1;
        public const string AssemblyName = "Better Work Tab";
        public const string TypeName = "Better_Work_Tab.API.TimePriorityApi";

        public static bool IsEnabled()
        {
            return TimePriorityService.IsRuntimeEnabled;
        }

        public static int GetScheduleVersion()
        {
            return TimePriorityService.CurrentVersion;
        }

        public static int GetCurrentHour(Pawn pawn = null)
        {
            return TimePriorityService.GetCurrentHour(pawn);
        }

        public static TimePriorityApiSnapshot GetWorkTypeSnapshot(Pawn pawn, WorkTypeDef workType)
        {
            int basePriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            return ToApiSnapshot(TimePriorityService.EvaluateWorkTypePriority(pawn, workType, basePriority));
        }

        public static TimePriorityApiSnapshot GetWorkGiverSnapshot(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            int parentPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
            int basePriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
            return ToApiSnapshot(TimePriorityService.EvaluateWorkGiverPriority(pawn, workType, workGiver, basePriority));
        }

        public static int GetEffectiveWorkTypePriority(Pawn pawn, WorkTypeDef workType)
        {
            return GetWorkTypeSnapshot(pawn, workType).EffectivePriority;
        }

        public static int GetEffectiveWorkGiverPriority(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            return GetWorkGiverSnapshot(pawn, workType, workGiver).EffectivePriority;
        }

        public static bool IsWorkTypeAvailableNow(Pawn pawn, WorkTypeDef workType)
        {
            return GetWorkTypeSnapshot(pawn, workType).IsAvailable;
        }

        public static bool IsWorkGiverAvailableNow(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            return GetWorkGiverSnapshot(pawn, workType, workGiver).IsAvailable;
        }

        public static bool TryGetDisabledReason(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver, out string reason)
        {
            reason = null;
            if (workGiver == null)
            {
                TimePriorityApiSnapshot workTypeSnapshot = GetWorkTypeSnapshot(pawn, workType);
                if (!workTypeSnapshot.DisabledBySchedule)
                {
                    return false;
                }

                reason = workTypeSnapshot.DisabledReason;
                return true;
            }

            if (!TimePriorityService.TryGetDisabledByTime(pawn, workType, workGiver, out reason, out _))
            {
                return false;
            }

            return true;
        }

        private static TimePriorityApiSnapshot ToApiSnapshot(TimePriorityEvaluation evaluation)
        {
            return new TimePriorityApiSnapshot(
                ApiVersion,
                evaluation.Hour,
                evaluation.BasePriority,
                evaluation.EffectivePriority,
                evaluation.HasSchedule,
                evaluation.ScheduledPriority,
                TimePriorityService.CurrentVersion,
                evaluation.ScheduleScope,
                evaluation.DisabledBySchedule,
                evaluation.DisabledReason);
        }
    }
}
