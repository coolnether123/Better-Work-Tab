using System;
using System.Reflection;
using HarmonyLib;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
#if !vAlpha4
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
            int wgPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, giver.def, workTypePriority);

            __result =
                (PawnWorkControlCompatibility.NonColonistsCanDo(giver.def) || pawn.IsColonist || PawnWorkControlCompatibility.IsColonyMech(pawn) || PawnWorkControlCompatibility.IsColonySubhuman(pawn)) &&
                !PawnWorkControlCompatibility.WorkTagsDisabled(pawn, giver.def) &&
                !pawn.WorkTypeIsDisabled(mappedWorkType) &&
                workTypePriority > 0 &&
                wgPriority > 0 &&
                !giver.ShouldSkip(pawn) &&
                giver.MissingRequiredCapacity(pawn) == null &&
                (!pawn.RaceProps.IsMechanoid || PawnWorkControlCompatibility.CanBeDoneByMechs(giver.def));

            return false;
        }
    }
#endif

    internal static class PawnWorkControlCompatibility
    {
        private static readonly MethodInfo IsColonyMechGetter = AccessTools.PropertyGetter(typeof(Pawn), "IsColonyMech");
        private static readonly MethodInfo IsColonySubhumanGetter = AccessTools.PropertyGetter(typeof(Pawn), "IsColonySubhuman");
        private static readonly FieldInfo CanBeDoneByMechsField = AccessTools.Field(typeof(WorkGiverDef), "canBeDoneByMechs");
        private static readonly FieldInfo WorkTagsField = AccessTools.Field(typeof(WorkGiverDef), "workTags");
        private static readonly FieldInfo NonColonistsCanDoField =
            AccessTools.Field(typeof(WorkGiverDef), "nonColonistsCanDo") ??
            AccessTools.Field(typeof(WorkGiverDef), "canBeDoneByNonColonists");

        internal static bool IsColonyMech(Pawn pawn)
        {
            return GetOptionalPawnBool(pawn, IsColonyMechGetter);
        }

        internal static bool IsColonySubhuman(Pawn pawn)
        {
            return GetOptionalPawnBool(pawn, IsColonySubhumanGetter);
        }

        internal static bool CanBeDoneByMechs(WorkGiverDef def)
        {
            if (def == null || CanBeDoneByMechsField == null)
            {
                return true;
            }

            try
            {
                return (bool)CanBeDoneByMechsField.GetValue(def);
            }
            catch
            {
                return true;
            }
        }

        internal static bool NonColonistsCanDo(WorkGiverDef def)
        {
            if (def == null || NonColonistsCanDoField == null)
            {
                return false;
            }

            try
            {
                return (bool)NonColonistsCanDoField.GetValue(def);
            }
            catch
            {
                return false;
            }
        }

        internal static bool WorkTagsDisabled(Pawn pawn, WorkGiverDef def)
        {
            if (pawn == null || def == null || WorkTagsField == null)
            {
                return false;
            }

            try
            {
                object value = WorkTagsField.GetValue(def);
                return value is WorkTags tags && pawn.WorkTagIsDisabled(tags);
            }
            catch
            {
                return false;
            }
        }

        private static bool GetOptionalPawnBool(Pawn pawn, MethodInfo getter)
        {
            if (pawn == null || getter == null)
            {
                return false;
            }

            try
            {
                return (bool)getter.Invoke(pawn, null);
            }
            catch
            {
                return false;
            }
        }
    }

#if !vAlpha4
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
#endif

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

            if (PawnCompat.WorkSettings(pawn) == null)
            {
                return true;
            }

            if (pawn.WorkTypeIsDisabled(mappedWorkType))
            {
                return false;
            }

            int wtPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, mappedWorkType);
            int wgPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, scanner.def, wtPriority);

            return wtPriority > 0 && wgPriority > 0;
        }
    }
}
