using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
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
            Type pawnTableType = AccessTools.TypeByName("RimWorld.PawnTable");
            return TargetType == null || pawnTableType == null
                ? null
                : AccessTools.Method(TargetType, "DoCell", new[] { typeof(Rect), typeof(Pawn), pawnTableType });
        }

        [HarmonyPrefix]
        private static bool Prefix(Rect rect, Pawn pawn, PawnTable table)
        {
            if (!PawnTableCompat.IsWorkTable(table) ||
                !MainTabCompat.TryGetOpenBetterWorkTab(out _))
            {
                return true;
            }

            if (TimePriorityScheduleEditor.TryDrawScheduleCopyPasteWorkPrioritiesCell(rect, pawn))
            {
                return false;
            }

            return true;
        }
    }
}
