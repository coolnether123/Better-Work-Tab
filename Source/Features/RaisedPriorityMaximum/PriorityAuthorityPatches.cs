using HarmonyLib;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.GetPriority))]
    internal static class Patch_Pawn_WorkSettings_GetPriority_PriorityAuthority
    {
        [HarmonyPostfix]
        [HarmonyAfter(new[] { "fluffy.worktab" })]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Pawn_WorkSettings __instance, WorkTypeDef w, ref int __result)
        {
            if (!PriorityAuthorityBroker.BetterWorkTabHasPriorityAuthority)
            {
                return;
            }

            __result = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(__instance, w);
        }
    }
}
