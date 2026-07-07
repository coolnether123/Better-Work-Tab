using HarmonyLib;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using RimWorld;

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
            if (!FluffyWorkTabCoexistence.ShouldRunBetterWorkTabFeatures)
            {
                return true;
            }

            if (BetterWorkTabLocalState.IsHeaderDragging)
            {
                return false;
            }
            return true;
        }
    }
}
