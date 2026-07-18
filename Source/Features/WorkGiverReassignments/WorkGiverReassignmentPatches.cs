using System;
using System.Reflection;
using HarmonyLib;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    [HarmonyPatch(typeof(JobGiver_Work), "GiverCanGiveJobToPawn")]
    internal static class Patch_JobGiver_Work_GiverCanGiveJobToPawn
    {
        public static bool Prefix(Pawn pawn, WorkGiver giver, ref bool __result)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabOrdering)
            {
                return true;
            }

            if (WorkGiverAvailability.ShouldForceAllowBeforeVanilla(pawn, giver, out bool canUse))
            {
                __result = canUse;
                return false;
            }

            return true;
        }

        public static void Postfix(Pawn pawn, WorkGiver giver, ref bool __result)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabOrdering)
            {
                return;
            }

            if (__result && !WorkGiverAvailability.ShouldAllowForPawn(giver?.def, pawn))
            {
                __result = false;
            }
        }
    }

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

    [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnThing))]
    internal static class Patch_WorkGiver_Scanner_HasJobOnThing
    {
        public static void Postfix(WorkGiver_Scanner __instance, Pawn pawn, Thing t, ref bool __result)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabOrdering)
            {
                return;
            }

            if (__result && !WorkGiverScannerExtensions.ShouldAllowForPawn(__instance, pawn))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.HasJobOnCell))]
    internal static class Patch_WorkGiver_Scanner_HasJobOnCell
    {
        public static void Postfix(WorkGiver_Scanner __instance, Pawn pawn, IntVec3 c, ref bool __result)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabOrdering)
            {
                return;
            }

            if (__result && !WorkGiverScannerExtensions.ShouldAllowForPawn(__instance, pawn))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.Compare))]
    internal static class Patch_PawnColumnWorker_WorkPriority_Compare_SubWorkDrilldown
    {
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Pawn left, Pawn right, ref int __result)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return true;
            }

            if (!SubWorkDrilldownState.IsActive)
            {
                return true;
            }

            if (!SubWorkDrilldownState.TryGetWorkGiverForColumn(__instance.def, out _, out _))
            {
                return true;
            }

            __result = SubWorkDrilldownState.ComparePawnsForColumn(__instance.def, left, right);
            return false;
        }
    }

    internal static class WorkGiverScannerExtensions
    {
        internal static bool ShouldAllowForPawn(WorkGiver_Scanner scanner, Pawn pawn, bool forced = false)
        {
            return WorkGiverAvailability.ShouldAllowForPawn(scanner?.def, pawn, forced);
        }
    }

    internal static class WorkGiverAvailability
    {
        internal static bool ShouldForceAllowBeforeVanilla(Pawn pawn, WorkGiver giver, out bool canUse)
        {
            canUse = false;
            if (giver?.def == null || pawn?.workSettings == null)
            {
                return false;
            }

            WorkTypeDef mappedWorkType = WorkGiverReassignmentManager.GetTargetWorkType(giver.def);
            if (mappedWorkType == null)
            {
                return false;
            }

            bool reassigned = WorkGiverReassignmentManager.IsReassigned(giver.def);
            bool lockedDisabledParent =
                WorkGiverReassignmentManager.LockedPawnOverrideCanRunWhenParentDisabled(pawn, giver.def, mappedWorkType);
            if (!reassigned && !lockedDisabledParent)
            {
                return false;
            }

            canUse = PassesVanillaStaticEligibilityForMappedWorkType(pawn, giver, mappedWorkType) &&
                     ShouldAllowForPawn(giver.def, pawn);
            return canUse;
        }

        internal static bool ShouldAllowForPawn(WorkGiverDef workGiver, Pawn pawn, bool forced = false)
        {
            if (forced) return true;

            if (workGiver == null)
            {
                return true;
            }

            WorkTypeDef mappedWorkType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
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

            int wtPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, mappedWorkType);
            int wgPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, wtPriority);
            wgPriority = TimePriorityService.GetEffectiveWorkGiverPriority(pawn, mappedWorkType, workGiver, wgPriority);
            bool parentWorkActive = wtPriority > WorkPrioritySystem.DisabledPriority;
            bool lockedSubWorkCanRun = !parentWorkActive &&
                                       WorkGiverReassignmentManager.LockedPawnOverrideCanRunWhenParentDisabled(pawn, workGiver, mappedWorkType);

            return (parentWorkActive || lockedSubWorkCanRun) && wgPriority > 0;
        }

        private static bool PassesVanillaStaticEligibilityForMappedWorkType(Pawn pawn, WorkGiver giver, WorkTypeDef mappedWorkType)
        {
            WorkGiverDef def = giver?.def;
            if (pawn == null || def == null)
            {
                return false;
            }

            if (!PawnWorkControlCompatibility.NonColonistsCanDo(def) &&
                !pawn.IsColonist &&
                !PawnWorkControlCompatibility.IsColonyMech(pawn) &&
                !PawnWorkControlCompatibility.IsColonySubhuman(pawn))
            {
                return false;
            }

            if (PawnWorkControlCompatibility.WorkTagsDisabled(pawn, def))
            {
                return false;
            }

            if (mappedWorkType != null && pawn.WorkTypeIsDisabled(mappedWorkType))
            {
                return false;
            }

            if (giver.ShouldSkip(pawn))
            {
                return false;
            }

            if (giver.MissingRequiredCapacity(pawn) != null)
            {
                return false;
            }

            return !pawn.RaceProps.IsMechanoid || PawnWorkControlCompatibility.CanBeDoneByMechs(def);
        }
    }
}
