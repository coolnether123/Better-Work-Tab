using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
using Verse;
using Verse.AI;

namespace Better_Work_Tab.API
{
    public readonly struct WorkGiverApiSnapshot
    {
        public WorkGiverApiSnapshot(
            int apiVersion,
            int pawnThingId,
            string workTypeDefName,
            string workGiverDefName,
            string targetWorkTypeDefName,
            bool reassigned,
            int baseWorkTypePriority,
            int effectiveWorkTypePriority,
            int baseWorkGiverPriority,
            int effectiveWorkGiverPriority,
            bool hasPawnOverride,
            bool forced,
            int currentHour,
            int scheduleVersion,
            int subWorkVersion,
            string scheduleScope,
            int scheduledPriority,
            bool disabledBySchedule,
            string disabledReason)
        {
            ApiVersion = apiVersion;
            PawnThingId = pawnThingId;
            WorkTypeDefName = workTypeDefName ?? string.Empty;
            WorkGiverDefName = workGiverDefName ?? string.Empty;
            TargetWorkTypeDefName = targetWorkTypeDefName ?? string.Empty;
            Reassigned = reassigned;
            BaseWorkTypePriority = baseWorkTypePriority;
            EffectiveWorkTypePriority = effectiveWorkTypePriority;
            BaseWorkGiverPriority = baseWorkGiverPriority;
            EffectiveWorkGiverPriority = effectiveWorkGiverPriority;
            HasPawnOverride = hasPawnOverride;
            Forced = forced;
            CurrentHour = currentHour;
            ScheduleVersion = scheduleVersion;
            SubWorkVersion = subWorkVersion;
            ScheduleScope = scheduleScope ?? "none";
            ScheduledPriority = scheduledPriority;
            DisabledBySchedule = disabledBySchedule;
            DisabledReason = disabledReason;
        }

        public int ApiVersion { get; }
        public int PawnThingId { get; }
        public string WorkTypeDefName { get; }
        public string WorkGiverDefName { get; }
        public string TargetWorkTypeDefName { get; }
        public bool Reassigned { get; }
        public int BaseWorkTypePriority { get; }
        public int EffectiveWorkTypePriority { get; }
        public int BaseWorkGiverPriority { get; }
        public int EffectiveWorkGiverPriority { get; }
        public bool HasPawnOverride { get; }
        public bool Forced { get; }
        public int CurrentHour { get; }
        public int ScheduleVersion { get; }
        public int SubWorkVersion { get; }
        public string ScheduleScope { get; }
        public int ScheduledPriority { get; }
        public bool DisabledBySchedule { get; }
        public string DisabledReason { get; }
        public bool IsAvailableNow => Forced || EffectiveWorkGiverPriority > WorkPrioritySystem.DisabledPriority;
    }

    public static class WorkGiverApi
    {
        public const int ApiVersion = 2;
        public const string AssemblyName = "Better Work Tab";
        public const string TypeName = "Better_Work_Tab.API.WorkGiverApi";

        public static WorkTypeDef GetTargetWorkType(WorkGiverDef workGiver)
        {
            return WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
        }

        public static bool IsReassigned(WorkGiverDef workGiver)
        {
            return WorkGiverReassignmentManager.IsReassigned(workGiver);
        }

        public static string[] GetOrderedWorkGiverDefNamesForWorkType(string workTypeDefName)
        {
            WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (workType == null)
            {
                return new string[0];
            }

            return WorkGiverReassignmentManager.GetOrderedWorkGiversForWorkType(workType)
                .Where(wg => wg?.def != null)
                .Select(wg => wg.def.defName)
                .ToArray();
        }

        public static WorkGiverApiSnapshot GetSnapshot(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver, bool forced = false)
        {
            WorkTypeDef targetWorkType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver) ?? workType;
            int baseWorkTypePriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, targetWorkType);
            TimePriorityEvaluation workTypeEvaluation =
                TimePriorityService.EvaluateWorkTypePriority(pawn, targetWorkType, baseWorkTypePriority);
            int baseWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(
                pawn,
                workGiver,
                workTypeEvaluation.EffectivePriority);
            TimePriorityEvaluation workGiverEvaluation =
                TimePriorityService.EvaluateWorkGiverPriority(pawn, targetWorkType, workGiver, baseWorkGiverPriority);

            return new WorkGiverApiSnapshot(
                ApiVersion,
                pawn?.thingIDNumber ?? -1,
                workType?.defName,
                workGiver?.defName,
                targetWorkType?.defName,
                WorkGiverReassignmentManager.IsReassigned(workGiver),
                baseWorkTypePriority,
                workTypeEvaluation.EffectivePriority,
                baseWorkGiverPriority,
                forced ? baseWorkGiverPriority : workGiverEvaluation.EffectivePriority,
                WorkGiverReassignmentManager.HasPawnWorkGiverOverride(pawn, workGiver),
                forced,
                workGiverEvaluation.Hour,
                TimePriorityService.CurrentVersion,
                WorkGiverReassignmentManager.CurrentSyncVersion,
                workGiverEvaluation.HasSchedule ? workGiverEvaluation.ScheduleScope : workTypeEvaluation.ScheduleScope,
                workGiverEvaluation.HasSchedule ? workGiverEvaluation.ScheduledPriority : workTypeEvaluation.ScheduledPriority,
                workTypeEvaluation.DisabledBySchedule || workGiverEvaluation.DisabledBySchedule,
                workTypeEvaluation.DisabledBySchedule ? workTypeEvaluation.DisabledReason : workGiverEvaluation.DisabledReason);
        }

        public static WorkGiverApiSnapshot GetSnapshotByDefName(
            int pawnThingId,
            string workTypeDefName,
            string workGiverDefName,
            bool forced = false)
        {
            Pawn pawn = FindPawnByThingId(pawnThingId);
            WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            return GetSnapshot(pawn, workType, workGiver, forced);
        }

        public static bool CanPawnUseWorkGiverNow(Pawn pawn, WorkGiverDef workGiver, bool forced = false)
        {
            if (forced)
            {
                return true;
            }

            WorkTypeDef targetWorkType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
            if (targetWorkType == null || pawn?.workSettings == null)
            {
                return true;
            }

            WorkGiver worker = workGiver?.Worker;
            if (worker == null)
            {
                return false;
            }

            WorkGiverApiSnapshot snapshot = GetSnapshot(pawn, targetWorkType, workGiver, false);
            return snapshot.IsAvailableNow &&
                   (workGiver.nonColonistsCanDo ||
                    pawn.IsColonist ||
                    PawnWorkControlCompatibility.IsColonyMech(pawn) ||
                    PawnWorkControlCompatibility.IsColonySubhuman(pawn)) &&
                   !pawn.WorkTagIsDisabled(workGiver.workTags) &&
                   !pawn.WorkTypeIsDisabled(targetWorkType) &&
                   !worker.ShouldSkip(pawn) &&
                   worker.MissingRequiredCapacity(pawn) == null &&
                   (!pawn.RaceProps.IsMechanoid || PawnWorkControlCompatibility.CanBeDoneByMechs(workGiver));
        }

        public static bool CanPawnUseWorkGiverNowByDefName(int pawnThingId, string workGiverDefName, bool forced = false)
        {
            return CanPawnUseWorkGiverNow(
                FindPawnByThingId(pawnThingId),
                DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName),
                forced);
        }

        public static bool TryGetDisabledReason(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver, out string reason)
        {
            return TimePriorityService.TryGetDisabledByTime(pawn, workType, workGiver, out reason, out _);
        }

        /// <summary>
        /// Invalidates cached presentation state after an external mod changes a pawn's
        /// disabled work types or capacity-based WorkGiver eligibility without routing
        /// through RimWorld's standard notification methods.
        /// </summary>
        public static void NotifyPawnPresentationStateChanged(Pawn pawn)
        {
            WorkGiverPresentationInvalidation.NotifyPawnDynamicStateChanged(pawn);
        }

        public static void NotifyPawnPresentationStateChangedByThingId(int pawnThingId)
        {
            NotifyPawnPresentationStateChanged(FindPawnByThingId(pawnThingId));
        }

        private static Pawn FindPawnByThingId(int pawnThingId)
        {
            if (pawnThingId < 0)
            {
                return null;
            }

            return PawnsFinder.AllMapsWorldAndTemporary_Alive.FirstOrDefault(pawn => pawn.thingIDNumber == pawnThingId) ??
                   PawnsFinder.All_AliveOrDead.FirstOrDefault(pawn => pawn.thingIDNumber == pawnThingId);
        }
    }
}
