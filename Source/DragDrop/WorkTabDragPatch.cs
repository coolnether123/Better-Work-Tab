using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Entry point: processes custom drag-and-drop overlays for Work tab after the table draws.
    /// We intercept mouse events and draw ghosts/lines without relying on ReorderableWidget.
    /// </summary>
    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI))]
    internal static class WorkTabDragPatch
    {
        // Prefix handles MouseDown/Drag/Up BEFORE vanilla uses the event.
        public static void Prefix(PawnTable __instance, Vector2 position)
        {
            // Reset state if the table context changed.
            if (WorkTabDragState.ActiveTable != null && WorkTabDragState.ActiveTable != __instance)
                WorkTabDragState.Reset();

            // Columns first; if a column drag begins, consume the event to avoid starting a row drag.
            ColumnDragController.OnGUI(__instance, position);
            RowDragController.OnGUI(__instance, position);
        }

        // Postfix handles drawing during Repaint after vanilla draws the table (ghosts/lines overlay nicely).
        public static void Postfix(PawnTable __instance, Vector2 position)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            ColumnDragController.OnGUI(__instance, position);
            RowDragController.OnGUI(__instance, position);
        }
    }
}
