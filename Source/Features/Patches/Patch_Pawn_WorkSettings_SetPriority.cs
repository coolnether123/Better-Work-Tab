using HarmonyLib;
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
        public static bool Prefix()
        {
            // Block priority changes if local player is currently dragging a header
            if (BetterWorkTabLocalState.IsHeaderDragging)
            {
                return false; // Skip the original method
            }
            
            return true; // Allow the original method to run
        }
    }
}
