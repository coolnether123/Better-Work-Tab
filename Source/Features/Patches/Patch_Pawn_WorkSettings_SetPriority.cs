using HarmonyLib;
using RimWorld;
#if vAlpha4
using Pawn_WorkSettings = Verse.AI.Pawn_WorkSettings;
#endif

namespace Better_Work_Tab.Features.Patches
{
    /// <summary>
    /// Prevents priority changes while a column header is being dragged.
    /// This ensures drag-and-drop column reordering doesn't accidentally trigger priority edits.
    /// </summary>
#if vAlpha4
    [HarmonyPatch(typeof(Pawn_WorkSettings), "SetWorkToPriority")]
#else
    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority))]
#endif
    public static class Patch_Pawn_WorkSettings_SetPriority
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (BetterWorkTabLocalState.IsHeaderDragging)
            {
                return false;
            }
            return true;
        }
    }
}
