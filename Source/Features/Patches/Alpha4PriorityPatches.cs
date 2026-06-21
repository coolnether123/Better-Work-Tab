#if vAlpha4
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace Better_Work_Tab.Features.Patches
{
    [HarmonyPatch(typeof(Pawn_WorkSettings), "SetWorkToPriority")]
    internal static class Alpha4Patch_Pawn_WorkSettings_SetWorkToPriority
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_WorkSettings), "pawn");
        private static readonly FieldInfo PrioritiesField = AccessTools.Field(typeof(Pawn_WorkSettings), "workPriorities");

        [HarmonyPrefix]
        private static bool Prefix(Pawn_WorkSettings __instance, WorkTypeDef w, ref int priority)
        {
            if (BetterWorkTabLocalState.IsHeaderDragging)
            {
                return false;
            }

            if (__instance == null || w == null)
            {
                return false;
            }

            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (priority != WorkPrioritySystem.DisabledPriority && pawn?.story?.WorkTypeIsDisabled(w) == true)
            {
                Log.Error("Tried to change priority on disabled worktype " + w + " for pawn " + pawn);
                return false;
            }

            var priorities = PrioritiesField?.GetValue(__instance) as Dictionary<WorkTypeDef, int>;
            if (priorities == null)
            {
                return true;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);
            priorities[w] = priority;
            if (priority == WorkPrioritySystem.DisabledPriority && pawn != null)
            {
                pawn.MindState.Notify_WorkPriorityDisabled(w);
            }

            return false;
        }
    }
}
#endif
