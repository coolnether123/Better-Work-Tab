using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Patches
{
    /// <summary>
    /// Ensure the Work tab honors manual row order by sorting via PlayerSettings.displayOrder.
    ///
    /// Why: PawnTable by default label-sorts pawns regardless of input sequence. Since our
    /// row drag/drop writes displayOrder on drop, we sort by displayOrder here to reflect
    /// the user’s manual ordering.
    /// </summary>
    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PrimarySortFunction))]
    public static class WorkTab_PrimarySort_UseDisplayOrder
    {
        // Postfix so we can replace the final sequence that PawnTable uses to draw rows.
        public static void Postfix(PawnTable __instance, IEnumerable<Pawn> input, ref IEnumerable<Pawn> __result)
        {
            // Only affect the Work tab (avoid Animals/Mechs/etc.).
            if (__instance?.def != PawnTableDefOf.Work)
                return;

            // If the user clicked a column to sort, respect that and do not override.
            if (__instance.SortingBy != null)
                return;

            BetterWorkTabMod.DebugLog("[BWT_SortPatch] Applying custom sort by playerSettings.displayOrder.", DebugFeature.DragDrop);

            // Order by player display order, as used by the ColonistBar and persisted by vanilla.
            __result = PlayerPawnsDisplayOrderUtility.InOrder(__result);
        }
    }
}

