using System.Collections.Generic;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Application
{
    internal static class WorkTabActionability
    {
        internal static bool CanApplyParent(Pawn pawn, WorkTypeDef workType) =>
            CanApplyWorkType(pawn, workType) && CanApplyAnyWorkGiver(pawn, workType);

        internal static bool CanApplySpecific(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver) =>
            CanApplyWorkType(pawn, workType) && CanApplyWorkGiver(pawn, workGiver);

        internal static bool CanApplySchedule(TimePriorityTarget target)
        {
            if (target.Kind == TimePriorityTargetKind.WorkGiver)
            {
                WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(target.TargetDefName);
                WorkTypeDef workType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
                return target.IsGlobal
                    ? workGiver != null && workType != null
                    : CanApplySpecific(TimePriorityService.FindPawn(target.PawnId), workType, workGiver);
            }
            WorkTypeDef parent = DefDatabase<WorkTypeDef>.GetNamedSilentFail(target.WorkTypeDefName);
            return !target.IsGlobal && CanApplyParent(TimePriorityService.FindPawn(target.PawnId), parent);
        }

        private static bool CanApplyWorkType(Pawn pawn, WorkTypeDef workType) =>
            pawn?.workSettings != null && workType != null && !pawn.Dead && pawn.workSettings.EverWork &&
            !pawn.WorkTypeIsDisabled(workType) && !pawn.IsWorkTypeDisabledByAge(workType, out _);

        internal static bool CanApplyAnyWorkGiver(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.health?.capacities == null || workType == null) return false;
            IList<WorkGiverDef> workGivers = workType.workGiversByPriority;
            for (int i = 0; workGivers != null && i < workGivers.Count; i++)
                if (CanApplyWorkGiver(pawn, workGivers[i])) return true;
            return false;
        }

        private static bool CanApplyWorkGiver(Pawn pawn, WorkGiverDef workGiver)
        {
            if (pawn?.health?.capacities == null || workGiver == null) return false;
            IList<PawnCapacityDef> capacities = workGiver.requiredCapacities;
            for (int i = 0; capacities != null && i < capacities.Count; i++)
                if (!pawn.health.capacities.CapableOf(capacities[i])) return false;
            return true;
        }
    }
}
