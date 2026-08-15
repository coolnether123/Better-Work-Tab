using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Better_Work_Tab.Transpilers.BwtExactProfile;
using Better_Work_Tab.Features.Patches.Profiles;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    // These exact-profile transpilers remain attribute-driven under one Harmony
    // PatchAll call. Harmony does not expose the composed, live CodeInstruction
    // inputs or a transaction boundary before installing attribute patches, so a
    // feature-level preflight cannot safely prepare and verify all affected methods
    // here (ColorOfPriority, TipForPawnWorker, DrawWorkBoxFor, HeaderClicked, and
    // SetPriority). A future gate must be a central BWT installer that composes
    // those exact inputs first, validates every max-priority profile, and only then
    // installs the feature's patches; static method-body guesses or per-patch
    // Prepare methods are not equivalent and must not be used as a gate.
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
