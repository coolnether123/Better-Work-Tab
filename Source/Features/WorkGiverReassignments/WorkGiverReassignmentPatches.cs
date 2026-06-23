using System;
using HarmonyLib;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    [HarmonyPatch(typeof(JobGiver_Work), "PawnCanUseWorkGiver")]
    internal static class Patch_JobGiver_Work_PawnCanUseWorkGiver
    {
        public static bool Prefix(Pawn pawn, WorkGiver giver, ref bool __result)
        {
            if (giver?.def == null)
            {
                return true;
            }

            var mappedWorkType = WorkGiverReassignmentManager.GetTargetWorkType(giver.def);
            if (mappedWorkType == null)
            {
                return true;
            }

            int workTypePriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, mappedWorkType);
            workTypePriority = TimePriorityService.GetEffectiveWorkTypePriority(pawn, mappedWorkType, workTypePriority);
            int wgPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, giver.def, workTypePriority);
            wgPriority = TimePriorityService.GetEffectiveWorkGiverPriority(pawn, mappedWorkType, giver.def, wgPriority);
            bool parentWorkActive = workTypePriority > WorkPrioritySystem.DisabledPriority;
            bool lockedSubWorkCanRun = !parentWorkActive &&
                                       WorkGiverReassignmentManager.LockedPawnOverrideCanRunWhenParentDisabled(pawn, giver.def, mappedWorkType);

            __result =
                (giver.def.nonColonistsCanDo || pawn.IsColonist || pawn.IsColonyMech || pawn.IsColonySubhuman) &&
                !pawn.WorkTagIsDisabled(giver.def.workTags) &&
                !pawn.WorkTypeIsDisabled(mappedWorkType) &&
                (parentWorkActive || lockedSubWorkCanRun) &&
                wgPriority > 0 &&
                !giver.ShouldSkip(pawn) &&
                giver.MissingRequiredCapacity(pawn) == null &&
                (!pawn.RaceProps.IsMechanoid || giver.def.canBeDoneByMechs);

            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnThing))]
    internal static class Patch_WorkGiver_Scanner_HasJobOnThing
    {
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            if (!WorkGiverScannerExtensions.ShouldAllowForPawn(__instance, pawn, forced))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnCell))]
    internal static class Patch_WorkGiver_Scanner_HasJobOnCell
    {
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
        {
            if (!WorkGiverScannerExtensions.ShouldAllowForPawn(__instance, pawn, forced))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.Compare))]
    internal static class Patch_PawnColumnWorker_WorkPriority_Compare_SubWorkDrilldown
    {
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Pawn a, Pawn b, ref int __result)
        {
            if (!SubWorkDrilldownState.IsActive)
            {
                return true;
            }

            if (!SubWorkDrilldownState.TryGetWorkGiverForColumn(__instance.def, out _, out _))
            {
                return true;
            }

            __result = SubWorkDrilldownState.ComparePawnsForColumn(__instance.def, a, b);
            return false;
        }
    }

    internal static class WorkGiverScannerExtensions
    {
        internal static bool ShouldAllowForPawn(WorkGiver_Scanner scanner, Pawn pawn, bool forced = false)
        {
            if (forced) return true;

            if (scanner?.def == null)
            {
                return true;
            }

            var mappedWorkType = WorkGiverReassignmentManager.GetTargetWorkType(scanner.def);
            if (mappedWorkType == null)
            {
                return true;
            }

            if (pawn?.workSettings == null)
            {
                return true;
            }

            if (pawn.WorkTypeIsDisabled(mappedWorkType))
            {
                return false;
            }

            int wtPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, mappedWorkType);
            wtPriority = TimePriorityService.GetEffectiveWorkTypePriority(pawn, mappedWorkType, wtPriority);
            int wgPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, scanner.def, wtPriority);
            wgPriority = TimePriorityService.GetEffectiveWorkGiverPriority(pawn, mappedWorkType, scanner.def, wgPriority);
            bool parentWorkActive = wtPriority > WorkPrioritySystem.DisabledPriority;
            bool lockedSubWorkCanRun = !parentWorkActive &&
                                       WorkGiverReassignmentManager.LockedPawnOverrideCanRunWhenParentDisabled(pawn, scanner.def, mappedWorkType);

            return (parentWorkActive || lockedSubWorkCanRun) && wgPriority > 0;
        }
    }
}
