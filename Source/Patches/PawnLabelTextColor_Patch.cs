using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
    /// <summary>
    /// Applies custom text color to pawn labels in the work tab.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_Label), nameof(PawnColumnWorker_Label.DoCell))]
    public static class PawnLabelTextColor_Patch
    {
        public static void Prefix(Rect rect, Pawn pawn, PawnTable table)
        {
            if (PawnOrganizer.API.PawnTextColorDatabase.TryGetColor(pawn, out Color textColor))
            {
                GUI.color = textColor;
            }
        }

        public static void Postfix()
        {
            GUI.color = Color.white; // Reset
        }
    }
}
