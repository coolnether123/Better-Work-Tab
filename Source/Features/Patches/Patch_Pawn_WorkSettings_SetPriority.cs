using HarmonyLib;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Patches
{
    /// <summary>
    /// Prevents priority changes while a column header is being dragged.
    /// This ensures drag-and-drop column reordering doesn't accidentally trigger priority edits.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority))]
    public static class Patch_Pawn_WorkSettings_SetPriority
    {
        [HarmonyPrefix]
        [HarmonyBefore(new[] { "fluffy.worktab" })]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(int priority, ref int __state)
        {
            __state = priority;
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return true;
            }

            if (BetterWorkTabLocalState.IsHeaderDragging)
            {
                __state = int.MinValue;
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyAfter(new[] { "fluffy.worktab" })]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn_WorkSettings __instance, WorkTypeDef w, int __state)
        {
            if (__instance?.priorities == null ||
                w == null ||
                __state == int.MinValue)
            {
                return;
            }

            Pawn pawn = WorkPrioritySystem.GetPawn(__instance);
            if (pawn != null)
            {
                UI.WorkGrid.Invalidation.WorkTabInvalidationHub.InvalidatePriority(
                    pawn.thingIDNumber,
                    w.shortHash);
            }
            else
            {
                UI.WorkGrid.Invalidation.WorkTabInvalidationHub.InvalidateCategory(
                    UI.WorkGrid.Invalidation.WorkGridInvalidationCategory.Priority);
            }

            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return;
            }

            int priority = PriorityAuthorityBroker.ClampPriorityForRequest(__state);
            if (__instance.priorities[w] == priority)
            {
                return;
            }

            __instance.priorities[w] = priority;
            __instance.Notify_UseWorkPrioritiesChanged();
        }
    }
}
