using HarmonyLib;
using RimWorld;
using Verse;

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
            if (mappedWorkType == null || mappedWorkType == giver.def.workType)
            {
                return true; // not reassigned, run vanilla
            }

            int priority = pawn?.workSettings?.GetPriority(mappedWorkType) ?? 0;
            __result =
                (giver.def.nonColonistsCanDo || pawn.IsColonist || pawn.IsColonyMech || pawn.IsColonySubhuman) &&
                !pawn.WorkTagIsDisabled(giver.def.workTags) &&
                !pawn.WorkTypeIsDisabled(mappedWorkType) &&
                priority > 0 &&
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
            if (!WorkGiverScannerExtensions.ShouldAllowForPawn(__instance, pawn))
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
            if (!WorkGiverScannerExtensions.ShouldAllowForPawn(__instance, pawn))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    internal static class WorkGiverScannerExtensions
    {
        internal static bool ShouldAllowForPawn(WorkGiver_Scanner scanner, Pawn pawn)
        {
            if (scanner?.def == null)
            {
                return true;
            }

            var mappedWorkType = WorkGiverReassignmentManager.GetTargetWorkType(scanner.def);
            if (mappedWorkType == null || mappedWorkType == scanner.def.workType)
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

            return pawn.workSettings.GetPriority(mappedWorkType) > 0;
        }
    }
}
