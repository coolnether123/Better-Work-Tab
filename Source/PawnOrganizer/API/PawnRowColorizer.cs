using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Spine.UI.ColourPicker;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.API
{
    /// <summary>
    /// Adds a right-click menu to pawn table rows to set a custom background color.
    /// </summary>
    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI))]
    public static class PawnTableRightClickMenu_Patch
    {
        public static void Postfix(PawnTable __instance, Vector2 position)
        {
            // Only trigger on right-click
            if (Event.current.type != EventType.MouseDown || Event.current.button != 1)
            {
                return;
            }

            // The area of the scroll view
            Rect scrollViewRect = new Rect(position.x, position.y + __instance.HeaderHeight, __instance.Size.x, __instance.Size.y - __instance.HeaderHeight);
            if (!scrollViewRect.Contains(Event.current.mousePosition))
            {
                return;
            }

            // Use reflection to get private fields from the PawnTable instance
            var pawns = __instance.PawnsListForReading;
            var rowHeights = (List<float>)AccessTools.Field(typeof(PawnTable), "cachedRowHeights").GetValue(__instance);
            var scrollPosition = (Vector2)AccessTools.Field(typeof(PawnTable), "scrollPosition").GetValue(__instance);

            // Mouse position relative to the inside of the scroll view content
            Vector2 mouseInScrollView = Event.current.mousePosition - new Vector2(scrollViewRect.x, scrollViewRect.y) + scrollPosition;

            Pawn clickedPawn = null;
            float currentY = 0f;
            for (int i = 0; i < pawns.Count; i++)
            {
                float rowHeight = rowHeights[i];
                Rect rowRect = new Rect(0, currentY, scrollViewRect.width, rowHeight);

                if (rowRect.Contains(mouseInScrollView))
                {
                    clickedPawn = pawns[i];
                    break;
                }
                currentY += rowHeight;
            }

            if (clickedPawn == null)
            {
                return;
            }

            // Create and show the float menu
            var options = new List<FloatMenuOption>();

            // Option to set color
            options.Add(new FloatMenuOption("Set row color", () =>
            {
                PawnColorDatabase.TryGetColor(clickedPawn, out Color currentColor);
                Find.WindowStack.Add(new Dialog_ColourPicker(currentColor, (newColor, closing) =>
                {
                    PawnColorDatabase.SetColor(clickedPawn, newColor);
                }));
            }));

            // Option to clear color
            if (PawnColorDatabase.TryGetColor(clickedPawn, out _))
            {
                options.Add(new FloatMenuOption("Clear row color", () =>
                {
                    PawnColorDatabase.ClearColor(clickedPawn);
                }));
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
                Event.current.Use();
            }
        }
    }

    /// <summary>
    /// Patches the pawn label column to draw a custom background color for the entire row.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_Label), nameof(PawnColumnWorker_Label.DoCell))]
    public static class PawnRowColor_Patch
    {
        public static void Prefix(Rect rect, Pawn pawn, PawnTable table)
        {
            if (PawnColorDatabase.TryGetColor(pawn, out Color color))
            {
                // The rect for the entire row, not just the cell
                Rect fullRowRect = new Rect(0, rect.y, table.Size.x - 16f, rect.height);
                Widgets.DrawBoxSolid(fullRowRect, color);
            }
        }
    }
}
