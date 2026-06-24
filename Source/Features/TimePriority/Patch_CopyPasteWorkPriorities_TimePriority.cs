using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.TimePriority
{
    [HarmonyPatch]
    internal static class Patch_CopyPasteWorkPriorities_TimePriority
    {
        private static Type TargetType => AccessTools.TypeByName("RimWorld.PawnColumnWorker_CopyPasteWorkPriorities");

        private static bool Prepare()
        {
            return TargetType != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(TargetType, "DoCell", new[] { typeof(Rect), typeof(Pawn), AccessTools.TypeByName("RimWorld.PawnTable") });
        }

        [HarmonyPrefix]
        private static bool Prefix(Rect rect, Pawn pawn)
        {
            return !TimePriorityPlannerPrototype.TryDrawScheduleCopyPasteWorkPrioritiesCell(rect, pawn);
        }
    }
}
