using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Spine.Harmony.Transpilers.VNext;
using Better_Work_Tab.Features.Patches.Profiles;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.ColorOfPriority))]
    internal static class Patch_WidgetsWork_ColorOfPriority
    {
        /// <summary>
        /// Extends RimWorld's priority color calculation beyond the vanilla 1..4 range.
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix(ref Color __result, int prio)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return;
            }

            __result = WorkPrioritySystem.GetPriorityColor(prio);
        }
    }

    [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.TipForPawnWorker))]
    internal static class Patch_WidgetsWork_TipForPawnWorker
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            return BwtCallRedirectConsumers.ApplyTipPriorityLookup(instructions, original);
        }
    }

    [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor))]
    internal static class Patch_WidgetsWork_DrawWorkBoxFor
    {
        /// <summary>
        /// Replaces the two wraparound constants used by the work-cell click handlers.
        /// </summary>
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            return BwtPriorityConsumers.ApplyDrawWorkBox(instructions, original);
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.HeaderClicked))]
    internal static class Patch_PawnColumnWorker_WorkPriority_HeaderClicked
    {
        /// <summary>
        /// Replaces the two wraparound constants used by the header bulk-edit controls.
        /// </summary>
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            return BwtPriorityConsumers.ApplyHeaderClicked(instructions, original);
        }
    }

    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority))]
    internal static class Patch_Pawn_WorkSettings_SetPriority
    {
        /// <summary>
        /// Replaces RimWorld's validation ceiling so values above 4 remain valid.
        /// </summary>
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            return BwtPriorityConsumers.ApplySetPriority(instructions, original);
        }
    }
}
