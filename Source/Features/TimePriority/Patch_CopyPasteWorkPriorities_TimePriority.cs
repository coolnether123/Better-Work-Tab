using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.TimePriority
{
    [HarmonyPatch(typeof(PawnColumnWorker_CopyPasteWorkPriorities), nameof(PawnColumnWorker_CopyPasteWorkPriorities.DoCell))]
    internal static class Patch_CopyPasteWorkPriorities_TimePriority
    {
        [HarmonyPrefix]
        private static bool Prefix(Rect rect, Pawn pawn)
        {
            if (TimePriorityPlannerPrototype.TryDrawScheduleCopyPasteWorkPrioritiesCell(rect, pawn))
            {
                return false;
            }

            return true;
        }
    }
}
